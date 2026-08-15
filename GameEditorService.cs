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

        // 0 表示未赐福，其余合法等级完全来自当前游戏的赐福等级表。
        snapshot.BlessingLevels.Add(0);
        foreach (var pair in THeroBlessLevel.create())
            if (!snapshot.BlessingLevels.Contains(pair.Key)) snapshot.BlessingLevels.Add(pair.Key);
        snapshot.BlessingLevels.Sort();

        var partTable = TEquipPart.create();
        var templates = TEquip.create();
        var maximumHeroLevel = GetMaximumHeroLevel();

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
                snapshot.Heroes.Add(CreateHeroEdit(hero, maximumHeroLevel));
        }
        snapshot.Inventory = GetInventorySnapshot(lord);
        return snapshot;
    }

    internal static InventorySnapshot GetInventorySnapshot() => GetInventorySnapshot(GetLord());

    private static InventorySnapshot GetInventorySnapshot(LordData lord)
    {
        var snapshot = new InventorySnapshot();

        // 可添加物品完全来自当前游戏表；装备由装备生成器处理，不在这里重复暴露。
        foreach (var pair in TRes.create())
            if (pair.Value != null) snapshot.AvailableItems.Add(new InventoryTemplate
            {
                Type = (int)EItemType.res, TypeName = GetItemTypeName(EItemType.res), Id = pair.Key,
                Name = pair.Value.name ?? $"资源 {pair.Key}", Quality = pair.Value.quality
            });
        foreach (var pair in TTool.create())
            if (pair.Value != null) snapshot.AvailableItems.Add(new InventoryTemplate
            {
                Type = (int)EItemType.tool, TypeName = GetItemTypeName(EItemType.tool), Id = pair.Key,
                Name = pair.Value.name ?? $"工具 {pair.Key}", Quality = pair.Value.quality, Level = 1
            });
        foreach (var pair in TRune.create())
            if (pair.Value != null) snapshot.AvailableItems.Add(new InventoryTemplate
            {
                Type = (int)EItemType.rune, TypeName = GetItemTypeName(EItemType.rune), Id = pair.Key,
                Name = pair.Value.name ?? $"符文 {pair.Key}", Quality = pair.Value.baseQuality
            });
        foreach (var pair in TCurio.create())
            if (pair.Value != null) snapshot.AvailableItems.Add(new InventoryTemplate
            {
                Type = (int)EItemType.curio, TypeName = GetItemTypeName(EItemType.curio), Id = pair.Key,
                Name = pair.Value.name ?? $"奇物 {pair.Key}", Quality = pair.Value.quality
            });
        snapshot.AvailableItems.Sort((a, b) =>
        {
            var typeCompare = a.Type.CompareTo(b.Type);
            return typeCompare != 0 ? typeCompare : a.Id.CompareTo(b.Id);
        });

        // 普通背包、符文背包和奇物背包是三套原生容器；分别读取可避免漏项。
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.res));
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.rune));
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.curio));
        snapshot.BagItems.Sort((a, b) =>
        {
            var typeCompare = a.Type.CompareTo(b.Type);
            return typeCompare != 0 ? typeCompare : a.FieldIndex.CompareTo(b.FieldIndex);
        });
        return snapshot;
    }

    internal static EditorResponse UpdateInventoryItem(InventoryItemEdit edit)
    {
        if (edit.Count < 0)
            throw new InvalidOperationException("物品数量不能小于 0。");
        var lord = GetLord();
        var type = (EItemType)edit.Type;
        if (type == EItemType.equip || !IsEditableItemType(type))
            throw new InvalidOperationException("该物品类型不能在物品编辑器中修改。");

        var fields = lord.lordBagData.GetFieldList(type);
        ItemFieldData? target = null;
        for (var i = 0; fields != null && i < fields.Count; i++)
        {
            var field = fields[i];
            var save = field?.itemData?.saveItemData;
            if (field?.saveItemFieldData?.index == edit.FieldIndex && save != null &&
                save.type == type && save.id == edit.Id && save.quality == edit.Quality && save.level == edit.Level)
            {
                target = field;
                break;
            }
        }
        if (target?.itemData?.saveItemData == null)
            throw new InvalidOperationException("背包物品已经发生变化，请刷新后重试。");

        var oldCount = target.itemData.saveItemData.count;
        if (edit.Count > oldCount)
            lord.lordBagData.StackItem(target, edit.Count - oldCount);
        else if (edit.Count < oldCount)
            lord.lordBagData.ReduceItem(target, oldCount - edit.Count);
        SaveNow();
        return new EditorResponse
        {
            Success = true,
            Message = edit.Count == 0 ? $"已从背包删除“{edit.Name}”。" : $"已把“{edit.Name}”的数量修改为 {edit.Count}。",
            Inventory = GetInventorySnapshot(lord)
        };
    }

    internal static EditorResponse AddInventoryItem(InventoryAddEdit edit)
    {
        if (edit.Count <= 0)
            throw new InvalidOperationException("增加数量必须大于 0。");
        var lord = GetLord();
        var type = (EItemType)edit.Type;
        SaveItemData? saveItem;
        string name;
        switch (type)
        {
            case EItemType.res:
            {
                var table = TRes.create();
                if (!table.ContainsKey(edit.Id)) throw new InvalidOperationException($"资源 {edit.Id} 不存在于当前游戏表中。");
                name = table[edit.Id].name;
                saveItem = SaveItemData.CreateRes(edit.Id, edit.Count);
                break;
            }
            case EItemType.tool:
            {
                var table = TTool.create();
                if (!table.ContainsKey(edit.Id)) throw new InvalidOperationException($"工具 {edit.Id} 不存在于当前游戏表中。");
                name = table[edit.Id].name;
                saveItem = SaveItemData.CreateTool(edit.Id, edit.Count, Math.Max(1, edit.Level));
                break;
            }
            case EItemType.rune:
            {
                var table = TRune.create();
                if (!table.ContainsKey(edit.Id)) throw new InvalidOperationException($"符文 {edit.Id} 不存在于当前游戏表中。");
                if (!TRuneQuality.create().ContainsKey(edit.Quality))
                    throw new InvalidOperationException($"符文品质 {edit.Quality} 不存在于当前游戏规则表中。");
                name = table[edit.Id].name;
                saveItem = SaveItemData.CreateRune(edit.Id, edit.Quality, edit.Count);
                break;
            }
            case EItemType.curio:
            {
                var table = TCurio.create();
                if (!table.ContainsKey(edit.Id)) throw new InvalidOperationException($"奇物 {edit.Id} 不存在于当前游戏表中。");
                name = table[edit.Id].name;
                saveItem = SaveItemData.CreateCurio(edit.Id, edit.Count);
                break;
            }
            default:
                throw new InvalidOperationException("该物品类型不能通过物品编辑器增加。");
        }
        if (saveItem == null)
            throw new InvalidOperationException("游戏原生物品生成器拒绝了当前物品。");
        var item = ItemData.Create(saveItem, EItemPosType.bag, 1)
            ?? throw new InvalidOperationException("游戏创建运行时物品数据失败。");
        if (!lord.lordBagData.addItemToBag(item))
            throw new InvalidOperationException("背包空间不足，物品没有加入存档。");
        SaveNow();
        return new EditorResponse
        {
            Success = true,
            Message = $"已增加“{name}”×{edit.Count}。",
            Inventory = GetInventorySnapshot(lord)
        };
    }

    private static void AddInventoryFields(
        InventorySnapshot snapshot,
        Il2CppSystem.Collections.Generic.List<ItemFieldData> fields)
    {
        for (var i = 0; fields != null && i < fields.Count; i++)
        {
            var field = fields[i];
            var save = field?.itemData?.saveItemData;
            if (save == null || save.count <= 0 || save.type == EItemType.equip || !IsEditableItemType(save.type))
                continue;
            snapshot.BagItems.Add(new InventoryItemEdit
            {
                FieldIndex = field.saveItemFieldData.index,
                Type = (int)save.type,
                TypeName = GetItemTypeName(save.type),
                Id = save.id,
                Name = field.itemData.GetName() ?? $"物品 {save.id}",
                Quality = save.quality,
                Level = save.level,
                Count = save.count
            });
        }
    }

    private static bool IsEditableItemType(EItemType type) =>
        type == EItemType.res || type == EItemType.tool || type == EItemType.rune || type == EItemType.curio;

    private static string GetItemTypeName(EItemType type) => type switch
    {
        EItemType.res => "资源",
        EItemType.tool => "工具",
        EItemType.rune => "符文",
        EItemType.curio => "奇物",
        _ => $"类型 {(int)type}"
    };

    internal static EquipmentRules GetEquipmentRules(EquipmentEdit edit)
    {
        var template = RequireEquipmentRequest(edit);
        var levelData = TEquipLevel.create()[edit.Level];

        // 创建未入包的预览装备，让游戏原生生成器决定该组合的默认词条和数量规则。
        var preview = SaveItemData.CreateEquip(edit.TemplateId, edit.Quality, edit.Level)
            ?? throw new InvalidOperationException("游戏原生装备生成器拒绝了当前组合。");
        var rules = new EquipmentRules
        {
            MaximumAffixLevel = Math.Max(1, levelData.affixMaxLevel)
        };
        var affixTable = TAffix.create();
        var affixQualityTable = TAffixQuality.create();
        var equipmentQuality = TEquipQuality.create()[edit.Quality];

        // 装备品级表明确记录精良/稀有词条数量。数量为 0 的类别不能出现在编辑器中，
        // 因此不能假设每件装备都有普通词条，也不能只依赖一次随机预览推断类别。
        AddAffixQualityLimit(rules, affixQualityTable, (int)EItemQualityType.fine,
            Math.Max(0, equipmentQuality.fineAffixCount));
        AddAffixQualityLimit(rules, affixQualityTable, (int)EItemQualityType.rare,
            Math.Max(0, equipmentQuality.rareAffixCount));

        var generatedQualityCounts = new Dictionary<int, int>();

        if (preview.affixList != null)
        {
            for (var i = 0; i < preview.affixList.Count; i++)
            {
                var affix = preview.affixList[i];
                if (affix == null)
                    continue;

                generatedQualityCounts.TryGetValue(affix.quality, out var qualityCount);
                generatedQualityCounts[affix.quality] = qualityCount + 1;
                var qualityName = affixQualityTable.ContainsKey(affix.quality)
                    ? affixQualityTable[affix.quality].name
                    : $"词条档位 {affix.quality}";
                rules.AffixQualityNames[affix.quality] = qualityName;
                rules.GeneratedAffixes.Add(new AffixEdit
                {
                    Id = affix.id,
                    Quality = affix.quality,
                    QualityName = qualityName,
                    Name = affixTable.ContainsKey(affix.id) ? GetAffixName(affixTable[affix.id]) : $"词条 {affix.id}",
                    Level = Math.Clamp(affix.level, 1, rules.MaximumAffixLevel),
                    // 编辑器默认不复用预览装备的随机值，提交时让游戏为最终装备重新随机。
                    Value = null
                });
            }
        }

        // 套装、传奇等装备可能带有品级表计数之外的固定词条；这些类别以原生结果为准保留。
        foreach (var pair in generatedQualityCounts)
        {
            rules.AffixQualityLimits.TryGetValue(pair.Key, out var configuredCount);
            AddAffixQualityLimit(rules, affixQualityTable, pair.Key, Math.Max(configuredCount, pair.Value));
        }
        foreach (var limit in rules.AffixQualityLimits.Values)
            rules.MaximumAffixCount += limit;

        // 只按实际数量大于 0 的词条档位读取原生池；稀有专属装备不会混入普通词条。
        var poolByKey = BuildAffixPoolMap(template, edit.Level, rules.AffixQualityLimits.Keys);
        var allowedAffixKeys = new HashSet<(int Id, int Quality)>();
        foreach (var pair in poolByKey)
        {
            if (!affixTable.ContainsKey(pair.Key.Id))
                continue;
            var affix = affixTable[pair.Key.Id];
            var qualityName = affixQualityTable.ContainsKey(pair.Key.Quality)
                ? affixQualityTable[pair.Key.Quality].name
                : $"词条档位 {pair.Key.Quality}";
            rules.AffixQualityNames[pair.Key.Quality] = qualityName;
            rules.AllowedAffixes.Add(new AffixOption
            {
                Id = affix.id,
                // 同一基础词条可由不同档位的原生池生成，类别必须使用查询池的档位。
                Quality = pair.Key.Quality,
                QualityName = qualityName,
                Name = GetAffixName(affix)
            });
            allowedAffixKeys.Add(pair.Key);
        }
        // 固定词条不一定属于随机池，仍应允许用户删除后重新添加。
        foreach (var generated in rules.GeneratedAffixes)
        {
            if (!allowedAffixKeys.Add((generated.Id, generated.Quality)))
                continue;
            rules.AllowedAffixes.Add(new AffixOption
            {
                Id = generated.Id,
                Quality = generated.Quality,
                QualityName = generated.QualityName,
                Name = generated.Name
            });
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

        var poolByKey = BuildAffixPoolMap(template, edit.Level, rules.AffixQualityLimits.Keys);
        var affixTable = TAffix.create();
        var seen = new HashSet<int>();
        var requestedQualityCounts = new Dictionary<int, int>();
        var nativeSpecialAffixKeys = new HashSet<(int Id, int Quality)>();
        foreach (var generated in rules.GeneratedAffixes)
        {
            var generatedKey = (generated.Id, generated.Quality);
            if (!poolByKey.ContainsKey(generatedKey)) nativeSpecialAffixKeys.Add(generatedKey);
        }

        // 先用原生流程建立完整装备结构，再只替换用户明确编辑过的词条列表。
        var saveItem = SaveItemData.CreateEquip(edit.TemplateId, edit.Quality, edit.Level)
            ?? throw new InvalidOperationException("游戏原生装备生成器创建失败。");
        saveItem.affixList.Clear();
        foreach (var requested in edit.Affixes)
        {
            // 即使桌面端已经过滤，桥接层仍需再次校验，防止旧客户端或手工请求绕过规则。
            if (!seen.Add(requested.Id))
                throw new InvalidOperationException($"词条 {requested.Id} 重复，游戏规则不允许重复添加同一词条。");
            var requestedKey = (requested.Id, requested.Quality);
            if (!affixTable.ContainsKey(requested.Id) ||
                (!poolByKey.ContainsKey(requestedKey) && !nativeSpecialAffixKeys.Contains(requestedKey)))
                throw new InvalidOperationException($"词条 {requested.Id} 不在当前装备、品级和等级的游戏词条池中。");
            if (requested.Level < 1 || requested.Level > rules.MaximumAffixLevel)
                throw new InvalidOperationException($"词条 {requested.Id} 的合法等级为 1-{rules.MaximumAffixLevel}。");

            // 套装装备的实例档位可能与 TAffix 中的基础档位不同，必须按用户所见的实际档位计数。
            requestedQualityCounts.TryGetValue(requested.Quality, out var requestedQualityCount);
            requestedQualityCount++;
            if (!rules.AffixQualityLimits.TryGetValue(requested.Quality, out var qualityLimit) ||
                requestedQualityCount > qualityLimit)
                throw new InvalidOperationException($"档位 {requested.Quality} 最多允许 {qualityLimit} 条词条。");
            requestedQualityCounts[requested.Quality] = requestedQualityCount;

            // 普通随机词条沿用游戏池的数值倍率；原生固定词条不在随机池时使用完整倍率。
            var rate = poolByKey.TryGetValue(requestedKey, out var poolInfo) ? poolInfo.Rate : 1f;
            var saveAffix = SaveAffixData.Create(requested.Id, requested.Quality, requested.Level,
                rate, EAffixValueType.random);
            if (saveAffix == null)
                throw new InvalidOperationException($"游戏拒绝创建词条 {requested.Id}。");
            // 游戏原生创建路径没有针对外部 value 的范围校验；仅在用户填写时覆盖随机结果。
            if (requested.Value.HasValue)
                saveAffix.value = requested.Value.Value;
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
        var heroLevelTable = THeroLevel.create();
        var maxHeroLevel = GetMaximumHeroLevel(heroLevelTable);
        if (!heroLevelTable.ContainsKey(edit.Level))
            throw new InvalidOperationException($"角色等级 {edit.Level} 不存在于当前游戏等级表中；表内上限为 {maxHeroLevel}。");
        var blessingTable = THeroBlessLevel.create();
        if (edit.BlessingLevel != 0 && !blessingTable.ContainsKey(edit.BlessingLevel))
            throw new InvalidOperationException($"赐福等级 {edit.BlessingLevel} 不存在于当前游戏规则表中。");

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

        var targetBlessingLevel = edit.BlessingLevel;
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

        // 赐福通过游戏原生升降流程修改，使赐福属性和赐福技能点同步更新。
        if (targetBlessingLevel < save.blessLevel && !hero.ClearAllBlessLevel())
            throw new InvalidOperationException("游戏拒绝清除当前角色的赐福等级。");
        while (save.blessLevel < targetBlessingLevel)
        {
            var previousLevel = save.blessLevel;
            hero.BlessLevelUp();
            if (save.blessLevel <= previousLevel)
                throw new InvalidOperationException($"游戏无法把赐福等级提升到 {targetBlessingLevel}。");
        }
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
        BlessingLevel = hero.saveHeroData.blessLevel,
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

    private static int GetMaximumHeroLevel(
        Il2CppSystem.Collections.Generic.Dictionary<int, THeroLevel>? levelTable = null)
    {
        levelTable ??= THeroLevel.create();
        var maximum = 0;
        foreach (var pair in levelTable)
            maximum = Math.Max(maximum, pair.Key);
        if (maximum < 1)
            throw new InvalidOperationException("当前游戏的角色等级表为空。");
        return maximum;
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

    private static Dictionary<(int Id, int Quality), SAffixInfo> BuildAffixPoolMap(
        TEquip template,
        int equipmentLevel,
        IEnumerable<int> affixQualities)
    {
        var result = new Dictionary<(int Id, int Quality), SAffixInfo>();
        foreach (var affixQuality in affixQualities)
        {
            var pool = EquipSys.GetEquipAffixPool(template.id, template.part, affixQuality, equipmentLevel);
            for (var i = 0; pool != null && i < pool.Count; i++)
                result[(pool[i].id, affixQuality)] = pool[i];
        }
        return result;
    }

    private static void AddAffixQualityLimit(
        EquipmentRules rules,
        Il2CppSystem.Collections.Generic.Dictionary<int, TAffixQuality> qualityTable,
        int quality,
        int count)
    {
        if (count <= 0)
            return;
        rules.AffixQualityLimits[quality] = count;
        rules.AffixQualityNames[quality] = qualityTable.ContainsKey(quality)
            ? qualityTable[quality].name
            : $"词条档位 {quality}";
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
