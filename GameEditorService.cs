using System;
using System.Collections.Generic;

namespace PathOfIdleEditor;

internal static class GameEditorService
{
    internal static EditorSnapshot GetSnapshot()
    {
        var lord = GetLord();
        var snapshot = new EditorSnapshot();

        // 每次连接都重新读取游戏表，游戏更新后无需同步维护桌面端的等级和品级常量。
        var qualityTable = TEquipQuality.create();
        foreach (var pair in qualityTable)
            snapshot.EquipmentQualities.Add(new RuleOption { Value = pair.Key, Name = pair.Value?.name ?? $"品级 {pair.Key}" });
        snapshot.EquipmentQualities.Sort((a, b) => a.Value.CompareTo(b.Value));

        var levelTable = TEquipLevel.create();
        foreach (var pair in levelTable)
            snapshot.EquipmentLevels.Add(pair.Key);
        snapshot.EquipmentLevels.Sort();

        var partTable = TEquipPart.create();
        var templates = TEquip.create();

        // 同类装备会共享游戏随机池参数；缓存池结果可显著减少主线程中的原生调用次数。
        var qualityPoolCache = new Dictionary<string, HashSet<int>>();
        foreach (var pair in templates)
        {
            var value = pair.Value;
            if (value == null)
                continue;
            snapshot.EquipmentTemplates.Add(new EquipmentTemplate
            {
                Id = value.id,
                Name = value.name ?? $"装备 {value.id}",
                Part = value.part,
                PartName = partTable.ContainsKey(value.part) ? partTable[value.part].name : $"部位 {value.part}",
                BaseQuality = value.baseQuality,
                AllowedQualities = GetAllowedQualities(value, snapshot.EquipmentQualities, qualityPoolCache)
            });
        }
        snapshot.EquipmentTemplates.Sort((a, b) => a.Id.CompareTo(b.Id));

        for (var i = 0; i < lord.heroFieldList.Count; i++)
        {
            var hero = lord.heroFieldList[i]?.heroData;
            if (hero?.saveHeroData != null)
                snapshot.Heroes.Add(CreateHeroEdit(hero, lord.GetMaxHeroLevel()));
        }
        return snapshot;
    }

    internal static EquipmentRules GetEquipmentRules(EquipmentEdit edit)
    {
        var template = RequireEquipmentRequest(edit);
        var levelData = TEquipLevel.create()[edit.Level];

        // 创建未入包的预览装备，让游戏原生生成器决定该组合的默认词条和数量规则。
        var preview = SaveItemData.CreateEquip(edit.TemplateId, edit.Quality, edit.Level)
            ?? throw new InvalidOperationException("游戏原生装备生成器拒绝了当前组合。");
        var rules = new EquipmentRules
        {
            MaximumAffixCount = Math.Max(Math.Max(0, preview.GetAffixCount(edit.Quality)), preview.affixList?.Count ?? 0),
            MaximumAffixLevel = Math.Max(1, levelData.affixMaxLevel)
        };

        // 词条候选直接来自游戏按装备、部位、品级和等级筛选后的原生池。
        var pool = EquipSys.GetEquipAffixPool(template.id, template.part, edit.Quality, edit.Level);
        var affixTable = TAffix.create();
        for (var i = 0; pool != null && i < pool.Count; i++)
        {
            var info = pool[i];
            if (!affixTable.ContainsKey(info.id))
                continue;
            var affix = affixTable[info.id];
            rules.AllowedAffixes.Add(new AffixOption
            {
                Id = affix.id,
                Quality = affix.quality,
                Name = GetAffixName(affix)
            });
        }
        rules.AllowedAffixes.Sort((a, b) => a.Id.CompareTo(b.Id));

        if (preview.affixList != null)
        {
            for (var i = 0; i < preview.affixList.Count; i++)
            {
                var affix = preview.affixList[i];
                if (affix == null)
                    continue;
                rules.GeneratedAffixes.Add(new AffixEdit
                {
                    Id = affix.id,
                    Quality = affix.quality,
                    Name = affixTable.ContainsKey(affix.id) ? GetAffixName(affixTable[affix.id]) : $"词条 {affix.id}",
                    Level = Math.Clamp(affix.level, 1, rules.MaximumAffixLevel)
                });
                if (!ContainsAffix(rules.AllowedAffixes, affix.id))
                {
                    rules.AllowedAffixes.Add(new AffixOption
                    {
                        Id = affix.id,
                        Quality = affix.quality,
                        Name = affixTable.ContainsKey(affix.id) ? GetAffixName(affixTable[affix.id]) : $"词条 {affix.id}"
                    });
                }
            }
        }
        rules.AllowedAffixes.Sort((a, b) => a.Id.CompareTo(b.Id));
        return rules;
    }

    internal static string GenerateEquipment(EquipmentEdit edit)
    {
        var lord = GetLord();
        var template = RequireEquipmentRequest(edit);
        var rules = GetEquipmentRules(edit);
        if (edit.Affixes.Count > rules.MaximumAffixCount)
            throw new InvalidOperationException($"当前品级最多允许 {rules.MaximumAffixCount} 条词条。");

        var pool = EquipSys.GetEquipAffixPool(template.id, template.part, edit.Quality, edit.Level);
        var poolById = new Dictionary<int, SAffixInfo>();
        for (var i = 0; pool != null && i < pool.Count; i++)
            poolById[pool[i].id] = pool[i];
        var affixTable = TAffix.create();
        var seen = new HashSet<int>();

        // 先用原生流程建立完整装备结构，再只替换用户明确编辑过的词条列表。
        var saveItem = SaveItemData.CreateEquip(edit.TemplateId, edit.Quality, edit.Level)
            ?? throw new InvalidOperationException("游戏原生装备生成器创建失败。");
        saveItem.affixList.Clear();
        foreach (var requested in edit.Affixes)
        {
            // 即使桌面端已经过滤，桥接层仍需再次校验，防止旧客户端或手工请求绕过规则。
            if (!seen.Add(requested.Id))
                throw new InvalidOperationException($"词条 {requested.Id} 重复，游戏规则不允许重复添加同一词条。");
            if (!affixTable.ContainsKey(requested.Id) || !ContainsAffix(rules.AllowedAffixes, requested.Id))
                throw new InvalidOperationException($"词条 {requested.Id} 不在当前装备、品级和等级的游戏词条池中。");
            if (requested.Level < 1 || requested.Level > rules.MaximumAffixLevel)
                throw new InvalidOperationException($"词条 {requested.Id} 的合法等级为 1-{rules.MaximumAffixLevel}。");

            var tableAffix = affixTable[requested.Id];
            // 普通随机词条沿用游戏池的数值倍率；原生固定词条不在随机池时使用完整倍率。
            var rate = poolById.TryGetValue(requested.Id, out var poolInfo) ? poolInfo.Rate : 1f;
            var saveAffix = SaveAffixData.Create(requested.Id, tableAffix.quality, requested.Level,
                rate, EAffixValueType.random);
            if (saveAffix == null)
                throw new InvalidOperationException($"游戏拒绝创建词条 {requested.Id}。");
            saveItem.affixList.Add(saveAffix);
        }

        var item = ItemData.Create(saveItem, EItemPosType.bag, 1)
            ?? throw new InvalidOperationException("游戏创建运行时装备数据失败。");
        if (!lord.lordBagData.addItemToBag(item))
            throw new InvalidOperationException("领主背包已满，装备没有加入存档。");
        SaveNow();
        return $"已生成“{template.name}”：品级 {edit.Quality}，等级 {edit.Level}，{edit.Affixes.Count} 条词条。";
    }

    internal static string UpdateHero(HeroEdit edit)
    {
        var lord = GetLord();
        var hero = FindHero(lord, edit.UniqueId);
        if (hero.IsAdventureBusy())
            throw new InvalidOperationException("该角色正在进行冒险，请返回城镇后再修改。");
        var save = hero.saveHeroData;
        var maxHeroLevel = Math.Max(1, lord.GetMaxHeroLevel());
        if (edit.Level < 1 || edit.Level > maxHeroLevel)
            throw new InvalidOperationException($"当前游戏规则允许的角色等级为 1-{maxHeroLevel}。");

        // 提交时重新构建技能树规则，不能信任桌面端连接时缓存的等级上限和候选项。
        var currentSlots = BuildTalentSlots(hero);
        var bySlot = new Dictionary<int, TalentSlotEdit>();
        foreach (var slot in currentSlots) bySlot[slot.SlotId] = slot;
        foreach (var requested in edit.TalentSlots)
        {
            if (!bySlot.TryGetValue(requested.SlotId, out var rule))
                throw new InvalidOperationException($"技能树位置 {requested.SlotId} 已不存在，请刷新数据。");
            SkillOption? selected = null;
            foreach (var option in rule.SkillOptions)
                if (option.TalentId == requested.TalentId) { selected = option; break; }
            if (selected == null)
                throw new InvalidOperationException($"技能树位置 {requested.SlotId} 不支持天赋/技能 {requested.TalentId}。");
            if (requested.Level < rule.MinimumLevel || requested.Level > selected.MaximumLevel)
                throw new InvalidOperationException($"“{selected.Name}”的合法等级为 {rule.MinimumLevel}-{selected.MaximumLevel}。");
        }

        save.level = edit.Level;
        save.exp = 0;
        save.name = edit.Name.Trim();
        save.mainAttrDic[EAttrType.STR] = Math.Max(0, edit.Strength);
        save.mainAttrDic[EAttrType.DEX] = Math.Max(0, edit.Dexterity);
        save.mainAttrDic[EAttrType.INT] = Math.Max(0, edit.Intelligence);
        foreach (var requested in edit.TalentSlots)
        {
            var target = save.talentDic[requested.SlotId];
            var oldTalent = TTalent.create()[target.id];
            var newTalent = TTalent.create()[requested.TalentId];
            if (oldTalent.skillId == save.baseSkillId && newTalent.skillId > 0)
                save.baseSkillId = newTalent.skillId;
            target.id = requested.TalentId;
            target.isAlien = newTalent.jobId != save.jobId;
            target.SetLevel(requested.Level);
        }

        // 重新初始化运行时角色，使属性派生值、装备效果和技能对象与存档字段保持一致。
        hero.Init();
        save.talentRemainPoint = Math.Max(0, edit.RemainingSkillPoints);
        hero.SetName(save.name);
        Game.eventMgr?.sendEvent(EEvent.talentChange, hero);
        Game.eventMgr?.sendEvent(EEvent.heroLevelUp, hero);
        SaveNow();
        return $"已保存角色“{GetHeroName(hero)}”，等级 {save.level}。";
    }

    private static HeroEdit CreateHeroEdit(HeroData hero, int maximumLevel) => new()
    {
        UniqueId = hero.saveHeroData.uniqueId,
        Name = GetHeroName(hero),
        Level = hero.saveHeroData.level,
        MaximumLevel = Math.Max(1, maximumLevel),
        Strength = ReadAttribute(hero.saveHeroData, EAttrType.STR),
        Dexterity = ReadAttribute(hero.saveHeroData, EAttrType.DEX),
        Intelligence = ReadAttribute(hero.saveHeroData, EAttrType.INT),
        RemainingSkillPoints = hero.saveHeroData.talentRemainPoint,
        TalentSlots = BuildTalentSlots(hero)
    };

    private static List<TalentSlotEdit> BuildTalentSlots(HeroData hero)
    {
        var result = new List<TalentSlotEdit>();
        var save = hero.saveHeroData;
        var runtime = hero.heroTalentData.talentDic;
        var talentTable = TTalent.create();
        // 由游戏自身返回该角色可使用的技能天赋池，再按当前位置类型和层级筛选。
        var legalSkillPool = save.GetSkillTalentList();

        foreach (var pair in save.talentDic)
        {
            var saved = pair.Value;
            if (saved == null || !talentTable.ContainsKey(saved.id))
                continue;
            var currentTable = talentTable[saved.id];
            var currentRuntime = runtime.ContainsKey(pair.Key) ? runtime[pair.Key] : null;
            var cap = currentRuntime?.GetTalentLevelCap() ?? Math.Max(0, saved.level);
            var minimum = currentRuntime == null ? 0 : Math.Max(0, hero.heroTalentData.GetTalentMinSaveLevel(currentRuntime));
            var slot = new TalentSlotEdit
            {
                SlotId = pair.Key,
                TalentId = saved.id,
                SkillId = currentTable.skillId,
                Kind = currentTable.skillId > 0 ? "技能" : "天赋/专精",
                Name = GetTalentDisplayName(currentTable),
                Level = saved.level,
                MinimumLevel = minimum,
                MaximumLevel = Math.Max(minimum, cap)
            };

            if (currentTable.skillId > 0)
            {
                for (var i = 0; legalSkillPool != null && i < legalSkillPool.Count; i++)
                {
                    var candidate = legalSkillPool[i];
                    if (candidate == null || candidate.type != currentTable.type || candidate.floor != currentTable.floor)
                        continue;
                    slot.SkillOptions.Add(new SkillOption
                    {
                        TalentId = candidate.id,
                        SkillId = candidate.skillId,
                        Name = GetTalentDisplayName(candidate),
                        MaximumLevel = GetCandidateTalentCap(hero, saved, candidate.id, cap)
                    });
                }
            }
            if (slot.SkillOptions.Count == 0)
            {
                slot.SkillOptions.Add(new SkillOption
                {
                    TalentId = currentTable.id,
                    SkillId = currentTable.skillId,
                    Name = GetTalentDisplayName(currentTable),
                    MaximumLevel = Math.Max(minimum, cap)
                });
            }
            if (!ContainsTalent(slot.SkillOptions, currentTable.id))
            {
                slot.SkillOptions.Add(new SkillOption
                {
                    TalentId = currentTable.id,
                    SkillId = currentTable.skillId,
                    Name = GetTalentDisplayName(currentTable),
                    MaximumLevel = Math.Max(minimum, cap)
                });
            }
            slot.SkillOptions.Sort((a, b) => a.TalentId.CompareTo(b.TalentId));
            result.Add(slot);
        }
        result.Sort((a, b) => a.SlotId.CompareTo(b.SlotId));
        return result;
    }

    private static int GetCandidateTalentCap(HeroData hero, SaveTalentData current, int candidateId, int fallback)
    {
        try
        {
            // 临时 TalentData 不写入存档，仅用于调用游戏的动态等级上限计算。
            var previewSave = SaveTalentData.Create(candidateId, 0, current.posId, current.isFixed);
            var preview = TalentData.Create(previewSave, hero);
            return Math.Max(0, preview.GetTalentLevelCap());
        }
        catch
        {
            return Math.Max(0, fallback);
        }
    }

    private static TEquip RequireEquipmentRequest(EquipmentEdit edit)
    {
        // 所有基础 ID 先通过当前游戏表验证，再调用原生 API，避免无效 ID 触发 IL2CPP 异常。
        var templates = TEquip.create();
        if (!templates.ContainsKey(edit.TemplateId))
            throw new InvalidOperationException($"装备模板 {edit.TemplateId} 不存在于当前游戏表中。");
        var template = templates[edit.TemplateId];
        var qualityTable = TEquipQuality.create();
        if (!qualityTable.ContainsKey(edit.Quality))
            throw new InvalidOperationException($"品级 {edit.Quality} 不存在于当前游戏表中。");
        var qualityOptions = new List<RuleOption>();
        foreach (var pair in qualityTable)
            qualityOptions.Add(new RuleOption { Value = pair.Key, Name = pair.Value.name });
        if (!GetAllowedQualities(template, qualityOptions).Contains(edit.Quality))
            throw new InvalidOperationException($"“{template.name}”不支持品级“{qualityTable[edit.Quality].name}”。");
        if (!TEquipLevel.create().ContainsKey(edit.Level))
            throw new InvalidOperationException($"装备等级 {edit.Level} 不存在于当前游戏规则表中。");
        return template;
    }

    private static List<int> GetAllowedQualities(
        TEquip template,
        List<RuleOption> qualities,
        Dictionary<string, HashSet<int>>? poolCache = null)
    {
        var result = new List<int>();
        foreach (var quality in qualities)
        {
            try
            {
                // CollectRandomEquipIds 是游戏生成装备时使用的筛选入口，比写死品级映射更耐更新。
                var cacheKey = $"{quality.Value}:{template.part}:{template.minType}:{template.specialGet}:{template.boxLevel}";
                HashSet<int>? cachedIds = null;
                if (poolCache == null || !poolCache.TryGetValue(cacheKey, out cachedIds))
                {
                    cachedIds = new HashSet<int>();
                    var ids = EquipSys.CollectRandomEquipIds(quality.Value, template.part, template.minType,
                        template.specialGet, template.boxLevel);
                    for (var i = 0; ids != null && i < ids.Count; i++) cachedIds.Add(ids[i]);
                    poolCache?.Add(cacheKey, cachedIds);
                }
                if (cachedIds.Contains(template.id) || quality.Value == template.baseQuality)
                    result.Add(quality.Value);
            }
            catch
            {
                if (quality.Value == template.baseQuality)
                    result.Add(quality.Value);
            }
        }
        result.Sort();
        return result;
    }

    private static HeroData FindHero(LordData lord, int uniqueId)
    {
        for (var i = 0; i < lord.heroFieldList.Count; i++)
        {
            var hero = lord.heroFieldList[i]?.heroData;
            if (hero?.saveHeroData?.uniqueId == uniqueId)
                return hero;
        }
        throw new InvalidOperationException($"角色 {uniqueId} 已不存在，请刷新游戏数据。");
    }

    private static bool ContainsTalent(List<SkillOption> options, int talentId)
    {
        foreach (var option in options) if (option.TalentId == talentId) return true;
        return false;
    }

    private static bool ContainsAffix(List<AffixOption> options, int affixId)
    {
        foreach (var option in options) if (option.Id == affixId) return true;
        return false;
    }

    private static string GetTalentDisplayName(TTalent talent)
    {
        if (talent.skillId > 0)
        {
            var skills = TSkill.create();
            if (skills.ContainsKey(talent.skillId))
                return $"{skills[talent.skillId].name}（技能 {talent.skillId}）";
        }
        return string.IsNullOrWhiteSpace(talent.name) ? $"天赋 {talent.id}" : talent.name;
    }

    private static string GetAffixName(TAffix affix) =>
        string.IsNullOrWhiteSpace(affix.des) ? $"词条 {affix.id}" : affix.des;

    private static LordData GetLord() => Game.dataMgr?.nowSeasonData?.lordData
        ?? throw new InvalidOperationException("请先启动游戏并进入一个游戏存档。");

    private static string GetHeroName(HeroData hero)
    {
        var name = hero.saveHeroData?.name;
        if (string.IsNullOrWhiteSpace(name)) name = hero.saveHeroData?.GetL10nName();
        return string.IsNullOrWhiteSpace(name) ? $"角色 #{hero.saveHeroData?.uniqueId}" : name;
    }

    private static float ReadAttribute(SaveHeroData save, EAttrType type) =>
        save.mainAttrDic != null && save.mainAttrDic.ContainsKey(type) ? save.mainAttrDic[type] : 0;

    private static void SaveNow() =>
        // 只在完整操作成功后调用原生保存，校验失败时不会产生半完成存档。
        (Game.dataMgr?.nowSeasonData?.nativeSeasonData
         ?? throw new InvalidOperationException("当前游戏存档不可用。"))
        .SaveData();
}
