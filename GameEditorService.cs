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

        // 角色品级选项直接来自当前游戏表，名称和可用品级会随版本更新。
        foreach (var pair in THeroQuality.create())
            if (pair.Value != null)
                snapshot.HeroQualities.Add(new RuleOption
                {
                    Value = pair.Key,
                    Name = pair.Value.name ?? $"品级 {pair.Key}"
                });
        snapshot.HeroQualities.Sort((a, b) => a.Value.CompareTo(b.Value));

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
        snapshot.Lord = CreateLordEdit(lord);
        return snapshot;
    }

    private static LordEdit CreateLordEdit(LordData lord)
    {
        var lordLevels = TLordLevel.create();
        var jobLevels = TJobLevel.create();
        var maximumLordLevel = GetMaximumTableKey(lordLevels, "崇拜者等级");
        var maximumJobLevel = GetMaximumTableKey(jobLevels, "魔偶等级");
        var result = new LordEdit
        {
            Level = lord.saveLordData.level,
            MaximumLevel = maximumLordLevel
        };
        foreach (var pair in jobLevels)
        {
            if (pair.Value == null)
                continue;
            result.JobLevelRules.Add(new LordJobLevelRule
            {
                Level = pair.Key,
                RequiredLordLevel = GetRequiredLordLevel(pair.Key, jobLevels),
                TotalAttributePoints = pair.Value.totalAttr,
                MaximumTalentBonusLevel = pair.Value.masteryMaxLevel
            });
        }
        result.JobLevelRules.Sort((a, b) => a.Level.CompareTo(b.Level));
        var talentTable = TTalent.create();
        foreach (var pair in lord.jobDic)
        {
            var runtime = pair.Value;
            var save = runtime?.saveLordJobData;
            if (runtime == null || save == null)
                continue;
            var levelRule = jobLevels.ContainsKey(save.level) ? jobLevels[save.level] : null;
            var edit = new LordJobEdit
            {
                JobId = save.jobId,
                JobName = runtime.tHeroJobData?.name ?? $"职业 {save.jobId}",
                Level = save.level,
                MaximumLevel = maximumJobLevel,
                RequiredLordLevel = GetRequiredLordLevel(save.level, jobLevels),
                TotalAttributePoints = levelRule?.totalAttr ?? 0,
                Strength = ReadLordJobAttribute(save, EAttrType.STR),
                Dexterity = ReadLordJobAttribute(save, EAttrType.DEX),
                Intelligence = ReadLordJobAttribute(save, EAttrType.INT)
            };
            foreach (var rule in result.JobLevelRules)
                edit.AttributeRules.Add(CreateLordJobAttributeRule(runtime, rule, jobLevels));
            ApplyLordJobAttributeRule(edit, edit.AttributeRules.Find(item => item.Level == edit.Level));
            foreach (var talentPair in save.talentDic)
            {
                if (!talentTable.ContainsKey(talentPair.Key))
                    continue;
                var talent = talentTable[talentPair.Key];
                edit.TalentBonuses.Add(new LordTalentBonusEdit
                {
                    TalentId = talentPair.Key,
                    Kind = talent.skillId > 0 ? "技能" : "天赋/专精",
                    Name = GetTalentDisplayName(talent),
                    Level = talentPair.Value,
                    MaximumLevel = Math.Max(1, save.GetMasteryMaxLevel())
                });
            }
            edit.TalentBonuses.Sort((a, b) => a.TalentId.CompareTo(b.TalentId));
            result.Jobs.Add(edit);
        }
        result.Jobs.Sort((a, b) => a.JobId.CompareTo(b.JobId));
        return result;
    }

    internal static EditorResponse UpdateLord(LordEdit edit)
    {
        var lord = GetLord();
        var lordLevels = TLordLevel.create();
        var jobLevels = TJobLevel.create();
        if (!lordLevels.ContainsKey(edit.Level))
            throw new InvalidOperationException($"崇拜者等级 {edit.Level} 不存在于当前游戏等级表中。");

        // 只校验和写入实际发生变化的职业；单独修改崇拜者等级时不能重写六个魔偶。
        var validated = new List<(LordJobData Runtime, LordJobEdit Request, TJobLevel Rule)>();
        var seenJobs = new HashSet<int>();
        foreach (var request in edit.Jobs)
        {
            if (!seenJobs.Add(request.JobId))
                throw new InvalidOperationException($"职业 {request.JobId} 重复出现，请刷新后重试。");
            if (!lord.jobDic.ContainsKey(request.JobId))
                throw new InvalidOperationException($"职业 {request.JobId} 已不存在，请刷新游戏数据。");
            var runtime = lord.jobDic[request.JobId];
            var currentSave = runtime.saveLordJobData;
            var jobChanged = request.Level != currentSave.level ||
                request.Strength != ReadLordJobAttribute(currentSave, EAttrType.STR) ||
                request.Dexterity != ReadLordJobAttribute(currentSave, EAttrType.DEX) ||
                request.Intelligence != ReadLordJobAttribute(currentSave, EAttrType.INT) ||
                HasLordTalentChanges(currentSave, request.TalentBonuses);
            if (!jobChanged)
                continue;
            if (!jobLevels.ContainsKey(request.Level))
                throw new InvalidOperationException($"“{request.JobName}”的魔偶等级 {request.Level} 不存在于当前游戏等级表中。");
            var requiredLordLevel = GetRequiredLordLevel(request.Level, jobLevels);
            if (edit.Level < requiredLordLevel)
                throw new InvalidOperationException($"“{request.JobName}”升到 {request.Level} 级要求崇拜者至少 {requiredLordLevel} 级。");
            var rule = jobLevels[request.Level];
            var attributeRule = CreateLordJobAttributeRule(runtime, new LordJobLevelRule
            {
                Level = request.Level,
                TotalAttributePoints = rule.totalAttr
            }, jobLevels);
            ValidateLordJobAttribute(request.JobName, "力量", request.Strength, attributeRule.StrengthMinimum, attributeRule.StrengthMaximum);
            ValidateLordJobAttribute(request.JobName, "敏捷", request.Dexterity, attributeRule.DexterityMinimum, attributeRule.DexterityMaximum);
            ValidateLordJobAttribute(request.JobName, "智力", request.Intelligence, attributeRule.IntelligenceMinimum, attributeRule.IntelligenceMaximum);
            var attributeTotal = request.Strength + request.Dexterity + request.Intelligence;
            if (attributeTotal != rule.totalAttr)
                throw new InvalidOperationException($"“{request.JobName}”{request.Level} 级的力量、敏捷、智力总和必须为 {rule.totalAttr}，当前为 {attributeTotal}。");

            var currentTalentIds = new HashSet<int>();
            foreach (var pair in runtime.saveLordJobData.talentDic) currentTalentIds.Add(pair.Key);
            if (request.TalentBonuses.Count != currentTalentIds.Count)
                throw new InvalidOperationException($"“{request.JobName}”的天赋加成列表已经变化，请刷新后重试。");
            var requestedTalentIds = new HashSet<int>();
            foreach (var talent in request.TalentBonuses)
            {
                if (!requestedTalentIds.Add(talent.TalentId) || !currentTalentIds.Contains(talent.TalentId))
                    throw new InvalidOperationException($"“{request.JobName}”包含无效的天赋加成 {talent.TalentId}。");
                if (talent.Level < 1 || talent.Level > rule.masteryMaxLevel)
                    throw new InvalidOperationException($"“{request.JobName}”的“{talent.Name}”等级加成合法范围为 1-{rule.masteryMaxLevel}。");
            }
            validated.Add((runtime, request, rule));
        }
        lord.saveLordData.level = edit.Level;
        lord.saveLordData.exp = 0;
        lord.tLordLevelData = lordLevels[edit.Level];
        lord.CreateOfflineResList();
        foreach (var entry in validated)
        {
            var runtime = entry.Runtime;
            var save = runtime.saveLordJobData;
            runtime.RemoveJobAttrUp();
            save.level = entry.Request.Level;
            runtime.tJobLevelData = entry.Rule;
            save.attrUpDic[(int)EAttrType.STR] = entry.Request.Strength;
            save.attrUpDic[(int)EAttrType.DEX] = entry.Request.Dexterity;
            save.attrUpDic[(int)EAttrType.INT] = entry.Request.Intelligence;
            foreach (var talent in entry.Request.TalentBonuses)
                save.talentDic[talent.TalentId] = talent.Level;
            // 原生初始化会重建显示列表、技能加成和等级锁，再把新属性加成应用到对应职业角色。
            runtime.Init();
            runtime.AddJobAttrUp();
        }
        SaveNow();
        return new EditorResponse
        {
            Success = true,
            Message = validated.Count == 0
                ? $"已保存崇拜者等级 {edit.Level}；六个职业魔偶未被改写。"
                : $"已保存崇拜者等级 {edit.Level}，并更新 {validated.Count} 个职业魔偶。",
            Lord = CreateLordEdit(lord)
        };
    }

    private static int GetRequiredLordLevel(
        int jobLevel,
        Il2CppSystem.Collections.Generic.Dictionary<int, TJobLevel> jobLevels)
    {
        if (jobLevel <= 1)
            return 1;
        return jobLevels.ContainsKey(jobLevel - 1) ? Math.Max(1, jobLevels[jobLevel - 1].lordLevel) : 1;
    }

    private static int GetMaximumTableKey<T>(Il2CppSystem.Collections.Generic.Dictionary<int, T> table, string tableName)
    {
        var maximum = 0;
        foreach (var pair in table) maximum = Math.Max(maximum, pair.Key);
        if (maximum <= 0)
            throw new InvalidOperationException($"当前游戏的{tableName}表为空。");
        return maximum;
    }

    private static int ReadLordJobAttribute(SaveLordJobData save, EAttrType type)
    {
        var key = (int)type;
        return save.attrUpDic != null && save.attrUpDic.ContainsKey(key) ? save.attrUpDic[key] : 0;
    }

    private static bool HasLordTalentChanges(SaveLordJobData save, List<LordTalentBonusEdit> requested)
    {
        if (save.talentDic.Count != requested.Count)
            return true;
        foreach (var talent in requested)
            if (!save.talentDic.ContainsKey(talent.TalentId) || save.talentDic[talent.TalentId] != talent.Level)
                return true;
        return false;
    }

    private static LordJobAttributeRule CreateLordJobAttributeRule(
        LordJobData runtime,
        LordJobLevelRule levelRule,
        Il2CppSystem.Collections.Generic.Dictionary<int, TJobLevel> jobLevels)
    {
        var result = new LordJobAttributeRule { Level = levelRule.Level };
        if (!jobLevels.ContainsKey(levelRule.Level))
            return result;
        var previousRule = runtime.tJobLevelData;
        try
        {
            // 让游戏自己的 CreateAttrRangeList 按目标等级和职业表计算，避免复制易变的百分比规则。
            runtime.tJobLevelData = jobLevels[levelRule.Level];
            runtime.CreateAttrRangeList();
            for (var index = 0; index < runtime.attrRangeList.Count; index++)
            {
                var range = runtime.attrRangeList[index];
                if (range == null) continue;
                if (range.type == EAttrType.STR) { result.StrengthMinimum = range.minValue; result.StrengthMaximum = range.maxValue; }
                if (range.type == EAttrType.DEX) { result.DexterityMinimum = range.minValue; result.DexterityMaximum = range.maxValue; }
                if (range.type == EAttrType.INT) { result.IntelligenceMinimum = range.minValue; result.IntelligenceMaximum = range.maxValue; }
            }
        }
        finally
        {
            runtime.tJobLevelData = previousRule;
            runtime.CreateAttrRangeList();
        }
        return result;
    }

    private static void ApplyLordJobAttributeRule(LordJobEdit edit, LordJobAttributeRule? rule)
    {
        if (rule == null)
            return;
        edit.StrengthMinimum = rule.StrengthMinimum;
        edit.StrengthMaximum = rule.StrengthMaximum;
        edit.DexterityMinimum = rule.DexterityMinimum;
        edit.DexterityMaximum = rule.DexterityMaximum;
        edit.IntelligenceMinimum = rule.IntelligenceMinimum;
        edit.IntelligenceMaximum = rule.IntelligenceMaximum;
    }

    private static void ValidateLordJobAttribute(string jobName, string attributeName, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
            throw new InvalidOperationException($"“{jobName}”的{attributeName}加成合法范围为 {minimum}-{maximum}，当前为 {value}。");
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
        var boxLevelTable = TBoxLevel.create();
        var maximumBoxLevel = Math.Max(0, Game.dataMgr?.nowVersionData?.equipBoxMaxLevel ?? 0);
        foreach (var pair in TTool.create())
        {
            var tool = pair.Value;
            if (tool == null)
                continue;
            if (tool.type == (int)EToolType.equipBox)
            {
                // 装备宝箱的 level 会直接决定开箱时使用的装备等级范围，不能统一写成 1。
                foreach (var boxLevelPair in boxLevelTable)
                {
                    if (boxLevelPair.Value == null || boxLevelPair.Key > maximumBoxLevel)
                        continue;
                    snapshot.AvailableItems.Add(new InventoryTemplate
                    {
                        Type = (int)EItemType.tool, TypeName = GetItemTypeName(EItemType.tool), Id = pair.Key,
                        Name = tool.name ?? $"工具 {pair.Key}", Quality = tool.quality, Level = boxLevelPair.Key,
                        LevelDescription = $"宝箱等级 {boxLevelPair.Key}（装备 {boxLevelPair.Value.minEquipLevel}-{boxLevelPair.Value.maxEquipLevel} 级）"
                    });
                }
            }
            else
            {
                snapshot.AvailableItems.Add(new InventoryTemplate
                {
                    Type = (int)EItemType.tool, TypeName = GetItemTypeName(EItemType.tool), Id = pair.Key,
                    Name = tool.name ?? $"工具 {pair.Key}", Quality = tool.quality, Level = 1
                });
            }
        }
        var runeQualityTable = TRuneQuality.create();
        foreach (var pair in TRune.create())
        {
            if (pair.Value == null)
                continue;
            // 原生 CreateRune 只校验品级是否存在于 TRuneQuality，并不把符文限制在 baseQuality。
            // 因此把当前游戏表中的全部合法品级交给用户选择，而不是在编辑器中硬编码范围。
            foreach (var qualityPair in runeQualityTable)
            {
                if (qualityPair.Value == null)
                    continue;
                snapshot.AvailableItems.Add(new InventoryTemplate
                {
                    Type = (int)EItemType.rune, TypeName = GetItemTypeName(EItemType.rune), Id = pair.Key,
                    Name = pair.Value.name ?? $"符文 {pair.Key}", Quality = qualityPair.Key,
                    LevelDescription = $"品级 {qualityPair.Key} · {qualityPair.Value.name}"
                });
            }
        }
        foreach (var pair in TCurio.create())
            if (pair.Value != null) snapshot.AvailableItems.Add(new InventoryTemplate
            {
                Type = (int)EItemType.curio, TypeName = GetItemTypeName(EItemType.curio), Id = pair.Key,
                Name = pair.Value.name ?? $"奇物 {pair.Key}", Quality = pair.Value.quality
            });
        snapshot.AvailableItems.Sort((a, b) =>
        {
            var typeCompare = a.Type.CompareTo(b.Type);
            if (typeCompare != 0) return typeCompare;
            var idCompare = a.Id.CompareTo(b.Id);
            if (idCompare != 0) return idCompare;
            var qualityCompare = a.Quality.CompareTo(b.Quality);
            return qualityCompare != 0 ? qualityCompare : a.Level.CompareTo(b.Level);
        });

        // 普通背包、符文背包、奇物背包以及 5x5 道具袋是相互独立的原生容器。
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.res), 0, "背包");
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.rune), 0, "背包");
        AddInventoryFields(snapshot, lord.lordBagData.GetFieldList(EItemType.curio), 0, "背包");
        AddInventoryFields(snapshot, lord.lordWalletData.fieldList, 1, "5x5 道具袋");
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
        if (edit.Container != 0 && edit.Container != 1)
            throw new InvalidOperationException("物品存放位置无效，请刷新游戏数据后重试。");

        var fields = edit.Container == 1 ? lord.lordWalletData.fieldList : lord.lordBagData.GetFieldList(type);
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
        {
            if (edit.Container == 1) lord.lordWalletData.StackItem(target, edit.Count - oldCount);
            else lord.lordBagData.StackItem(target, edit.Count - oldCount);
        }
        else if (edit.Count < oldCount)
        {
            if (edit.Container == 1) lord.lordWalletData.ReduceItem(target, oldCount - edit.Count);
            else lord.lordBagData.ReduceItem(target, oldCount - edit.Count);
        }
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
                var tool = table[edit.Id];
                var level = Math.Max(1, edit.Level);
                if (tool.type == (int)EToolType.equipBox)
                {
                    var boxLevels = TBoxLevel.create();
                    var maximumLevel = Math.Max(0, Game.dataMgr?.nowVersionData?.equipBoxMaxLevel ?? 0);
                    if (!boxLevels.ContainsKey(level) || level > maximumLevel)
                        throw new InvalidOperationException($"装备宝箱等级 {level} 不存在于当前游戏版本规则中。");
                }
                name = tool.name;
                saveItem = SaveItemData.CreateTool(edit.Id, edit.Count, level);
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
        // 背包物品没有“需求数量”；传 1 会让游戏把数量错误显示成“当前数量/1”。
        var item = ItemData.Create(saveItem, EItemPosType.bag, 0)
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
        Il2CppSystem.Collections.Generic.List<ItemFieldData> fields,
        int container,
        string containerName)
    {
        for (var i = 0; fields != null && i < fields.Count; i++)
        {
            var field = fields[i];
            var itemData = field?.itemData;
            var fieldSave = field?.saveItemFieldData;
            var save = itemData?.saveItemData;
            if (itemData == null || fieldSave == null || save == null || save.count <= 0 ||
                save.type == EItemType.equip || !IsEditableItemType(save.type))
                continue;
            snapshot.BagItems.Add(new InventoryItemEdit
            {
                Container = container,
                ContainerName = containerName,
                FieldIndex = fieldSave.index,
                Type = (int)save.type,
                TypeName = GetItemTypeName(save.type),
                Id = save.id,
                Name = itemData.GetName() ?? $"物品 {save.id}",
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

        // 第三个参数是 needCount，普通背包实例必须为 0，否则图标会显示“数量/1”。
        var item = ItemData.Create(saveItem, EItemPosType.bag, 0)
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
        if (edit.Quality != 0 && edit.Quality != save.quality)
            throw new InvalidOperationException("角色品级已在游戏中变化，请刷新后再编辑其他属性；品级请使用单独的“应用品级并重算”按钮。");
        var heroLevelTable = THeroLevel.create();
        var maxHeroLevel = GetMaximumHeroLevel(heroLevelTable);
        if (!heroLevelTable.ContainsKey(edit.Level))
            throw new InvalidOperationException($"角色等级 {edit.Level} 不存在于当前游戏等级表中；表内上限为 {maxHeroLevel}。");
        var blessingTable = THeroBlessLevel.create();
        if (edit.BlessingLevel != 0 && !blessingTable.ContainsKey(edit.BlessingLevel))
            throw new InvalidOperationException($"赐福等级 {edit.BlessingLevel} 不存在于当前游戏规则表中。");

        var growthRules = CreateGrowthAttributeRules(hero);
        var growthByType = new Dictionary<int, GrowthAttributeEdit>();
        foreach (var growth in edit.GrowthAttributes)
        {
            if (!growthByType.TryAdd(growth.Type, growth))
                throw new InvalidOperationException($"每级属性成长类型 {growth.Type} 重复出现，请刷新数据。");
            var rule = growthRules.Find(item => item.Type == growth.Type)
                ?? throw new InvalidOperationException($"每级属性成长类型 {growth.Type} 已不在当前游戏规则中。");
            if (Math.Abs(growth.Value - MathF.Round(growth.Value)) > 0.0001f)
                throw new InvalidOperationException($"“{growth.Name}”每级成长必须是整数。");
            if (growth.Value < rule.MinimumValue || growth.Value > rule.MaximumValue)
                throw new InvalidOperationException($"“{growth.Name}”每级成长合法范围为 {rule.MinimumValue}-{rule.MaximumValue}。");
        }
        if (growthByType.Count != growthRules.Count)
            throw new InvalidOperationException("每级属性成长列表不完整，请刷新游戏数据后重试。");
        var growthTotal = 0f;
        foreach (var growth in edit.GrowthAttributes) growthTotal += growth.Value;
        var requiredGrowthTotal = hero.tHeroQualityData?.baseAttrGrow ?? 0;
        if (Math.Abs(growthTotal - requiredGrowthTotal) > 0.0001f)
            throw new InvalidOperationException($"每级属性成长总和必须为 {requiredGrowthTotal}，当前为 {growthTotal:0.###}。");

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
        foreach (var growth in edit.GrowthAttributes)
            save.baseAttrUpDic[(EAttrType)growth.Type] = growth.Value;
        foreach (var requested in edit.TalentSlots)
        {
            var target = save.talentDic[requested.SlotId];
            var oldTalent = TTalent.create()[target.id];
            var newTalent = TTalent.create()[requested.TalentId];
            if (oldTalent.skillId == save.baseSkillId && newTalent.skillId > 0)
                save.baseSkillId = newTalent.skillId;
            target.id = requested.TalentId;
            // 启迪天赋本来就来自其他职业，但不属于异化技能，不能混用两个原生标记。
            if (!target.isInspired)
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

    internal static EditorResponse RerollHeroGrowth(int uniqueId)
    {
        var lord = GetLord();
        var hero = FindHero(lord, uniqueId);
        if (hero.IsAdventureBusy())
            throw new InvalidOperationException("该角色正在进行冒险，请返回城镇后再操作。");
        var house = Game.dataMgr.nowSeasonData.townData.GetHouse((EHouseType)102);
        var torture = house?.housePrisonData?.priTurtoreData
            ?? throw new InvalidOperationException("当前游戏尚未开放可重随成长的监牢设施。");
        var previous = house.selectHeroData;
        try
        {
            // 原生流程会校验血肉结晶、理智和设施规则，并正确扣除旧成长属性后重算。
            house.selectHeroData = hero;
            var price = torture.GetTorturePrice();
            var result = torture.TortureHero();
            if (result != 0)
                throw new InvalidOperationException(GetTortureError(result, price));
            SaveNow();
            return new EditorResponse
            {
                Success = true,
                Message = $"已按游戏规则消耗 {price} 个血肉结晶，重新随机“{GetHeroName(hero)}”的每级属性成长。",
                Snapshot = GetSnapshot()
            };
        }
        finally
        {
            house.selectHeroData = previous;
        }
    }

    internal static EditorResponse ChangeHeroQuality(int uniqueId, int targetQuality)
    {
        var lord = GetLord();
        var hero = FindHero(lord, uniqueId);
        if (hero.IsAdventureBusy())
            throw new InvalidOperationException("该角色正在进行冒险，请返回城镇后再修改品级。");
        var qualityTable = THeroQuality.create();
        if (!qualityTable.ContainsKey(targetQuality))
            throw new InvalidOperationException($"角色品级 {targetQuality} 不存在于当前游戏 THeroQuality 表中。");

        var previousQuality = hero.saveHeroData.quality;
        const int maximumSteps = 100;
        var steps = 0;
        while (hero.saveHeroData.quality != targetQuality)
        {
            if (++steps > maximumSteps)
                throw new InvalidOperationException("游戏原生品级修改没有到达目标品级，操作已中止。");
            var currentQuality = hero.saveHeroData.quality;
            var direction = currentQuality < targetQuality ? 1 : -1;
            var nextQuality = currentQuality + direction;
            if (!qualityTable.ContainsKey(nextQuality))
                throw new InvalidOperationException($"当前游戏品级表在 {currentQuality} 与目标品级 {targetQuality} 之间不连续。");

            // ChangeQuality 会原生重建主属性、基础属性和每级成长，并同步异化技能与技能点。
            // 100% 使用原生最高升品概率，0% 在品级大于 1 时固定降一级；循环会校验实际结果直至目标。
            hero.ChangeQuality(direction > 0 ? 100f : 0f);
            var changedQuality = hero.saveHeroData.quality;
            if (Math.Abs(changedQuality - currentQuality) != 1 || !qualityTable.ContainsKey(changedQuality))
                throw new InvalidOperationException($"游戏拒绝从品级 {currentQuality} 执行有效的逐级调整。");
        }

        SaveNow();
        var qualityName = qualityTable[targetQuality]?.name ?? $"品级 {targetQuality}";
        return new EditorResponse
        {
            Success = true,
            Message = previousQuality == targetQuality
                ? $"“{GetHeroName(hero)}”已经是“{qualityName}”，未修改属性。"
                : $"已使用游戏原生方法把“{GetHeroName(hero)}”从品级 {previousQuality} 调整为“{qualityName}”，并重算属性、成长与技能点。",
            Snapshot = GetSnapshot()
        };
    }

    internal static EditorResponse SyncAlienSkills(int uniqueId)
    {
        var lord = GetLord();
        var hero = FindHero(lord, uniqueId);
        if (hero.IsAdventureBusy())
            throw new InvalidOperationException("该角色正在进行冒险，请返回城镇后再操作。");
        var maximum = Math.Max(0, hero.saveHeroData.GetAlienSkillCount());
        hero.saveHeroData.SyncAlienSkillTalentDic();
        hero.Init();
        SaveNow();
        return new EditorResponse
        {
            Success = true,
            Message = $"已按游戏规则同步异化技能；当前角色上限为 {maximum} 个。",
            Snapshot = GetSnapshot()
        };
    }

    internal static EditorResponse InspireHero(int uniqueId)
    {
        var lord = GetLord();
        var hero = FindHero(lord, uniqueId);
        if (hero.IsAdventureBusy())
            throw new InvalidOperationException("该角色正在进行冒险，请返回城镇后再操作。");
        var house = Game.dataMgr.nowSeasonData.townData.GetHouse((EHouseType)101);
        var inspire = house?.houseShrineData?.shrineInspireData
            ?? throw new InvalidOperationException("当前游戏尚未开放启迪天赋的神殿设施。");
        var previous = house.selectHeroData;
        try
        {
            // 使用神殿原生接口，让数量上限、天赋池、等级和血肉结晶价格全部跟随当前版本。
            house.selectHeroData = hero;
            var price = inspire.GetInspirePrice();
            var result = inspire.InspireHero();
            if (result != 0)
                throw new InvalidOperationException(GetInspireError(result, price));
            SaveNow();
            return new EditorResponse
            {
                Success = true,
                Message = $"已消耗 {price} 个血肉结晶，为“{GetHeroName(hero)}”启迪一个天赋。",
                Snapshot = GetSnapshot()
            };
        }
        finally
        {
            house.selectHeroData = previous;
        }
    }

    private static int GetMaximumInspiredTalents()
    {
        var house = Game.dataMgr?.nowSeasonData?.townData?.GetHouse((EHouseType)101);
        return house?.houseAttrData == null
            ? 0
            : Math.Max(0, (int)Math.Round(house.houseAttrData.GetAttrValue((EHouseAttrType)10140, null)));
    }

    private static int GetGrowthRerollPrice(HeroData hero)
    {
        var house = Game.dataMgr?.nowSeasonData?.townData?.GetHouse((EHouseType)102);
        var torture = house?.housePrisonData?.priTurtoreData;
        if (house == null || torture == null)
            return 0;
        var previous = house.selectHeroData;
        try
        {
            house.selectHeroData = hero;
            return Math.Max(0, torture.GetTorturePrice());
        }
        finally
        {
            house.selectHeroData = previous;
        }
    }

    private static string GetTortureError(int result, int price) => result switch
    {
        1 => "游戏没有找到可操作的角色。",
        2 => $"血肉结晶不足，本次需要 {price} 个。",
        3 => "角色理智已经耗尽，不能继续重随成长。",
        4 => "角色理智为 100，当前游戏规则不允许执行该操作。",
        _ => $"游戏拒绝重新随机属性成长（错误码 {result}）。"
    };

    private static string GetInspireError(int result, int price) => result switch
    {
        1 => "游戏没有找到可操作的角色。",
        3 => "该角色的启迪天赋数量已经达到当前神殿上限。",
        4 => $"血肉结晶不足，本次需要 {price} 个。",
        5 => "当前神殿等级尚未开放启迪功能。",
        6 => "当前没有可启迪的其他职业天赋。",
        7 => "游戏没有找到可用的启迪天赋位置。",
        _ => $"游戏拒绝启迪天赋（错误码 {result}）。"
    };

    private static HeroEdit CreateHeroEdit(HeroData hero, int maximumLevel)
    {
        var save = hero.saveHeroData;
        var result = new HeroEdit
        {
            UniqueId = save.uniqueId,
            Name = GetHeroName(hero),
            Level = save.level,
            MaximumLevel = Math.Max(1, maximumLevel),
            BlessingLevel = save.blessLevel,
            Quality = save.quality,
            Strength = ReadAttribute(save, EAttrType.STR),
            Dexterity = ReadAttribute(save, EAttrType.DEX),
            Intelligence = ReadAttribute(save, EAttrType.INT),
            RemainingSkillPoints = save.talentRemainPoint,
            MaximumAlienSkills = Math.Max(0, save.GetAlienSkillCount()),
            MaximumInspiredTalents = GetMaximumInspiredTalents(),
            GrowthRerollPrice = GetGrowthRerollPrice(hero),
            TalentSlots = BuildTalentSlots(hero)
        };
        result.GrowthAttributes = CreateGrowthAttributeRules(hero);
        foreach (var pair in save.talentDic)
        {
            if (pair.Value?.isAlien == true) result.AlienSkillCount++;
            if (pair.Value?.isInspired == true) result.InspiredTalentCount++;
        }
        return result;
    }

    private static List<GrowthAttributeEdit> CreateGrowthAttributeRules(HeroData hero)
    {
        var result = new List<GrowthAttributeEdit>();
        var save = hero.saveHeroData;
        var scope = hero.tHeroJobData?.baseScopeArr;
        var total = hero.tHeroQualityData?.baseAttrGrow ?? 0;
        if (scope == null)
            return result;
        var orderedTypes = new[] { EAttrType.STR, EAttrType.DEX, EAttrType.INT };
        for (var row = 0; row < orderedTypes.Length; row++)
        {
            // 直接调用游戏新增的“可达整数范围”规则；它同时考虑单项比例和三项总和约束。
            DataTool.GetConstrainedIntRangeInScopeArrPartition(scope, total, row, out var minimum, out var maximum);
            var attrType = orderedTypes[row];
            result.Add(new GrowthAttributeEdit
            {
                Type = (int)attrType,
                Name = GetAttributeName(attrType),
                Value = save.baseAttrUpDic.ContainsKey(attrType) ? save.baseAttrUpDic[attrType] : 0,
                MinimumValue = minimum,
                MaximumValue = maximum
            });
        }
        result.Sort((a, b) => a.Type.CompareTo(b.Type));
        return result;
    }

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
                Kind = saved.isInspired ? "启迪天赋" : saved.isAlien ? "异化技能" :
                    currentTable.skillId > 0 ? "技能" : "天赋/专精",
                IsAlien = saved.isAlien,
                IsInspired = saved.isInspired,
                Name = GetTalentDisplayName(currentTable),
                Level = saved.level,
                MinimumLevel = minimum,
                MaximumLevel = Math.Max(minimum, cap)
            };

            // 额外技能以天赋 ID 作为存档字典键，不能像普通固定槽位那样原地换 ID；
            // 异化由原生同步流程生成，启迪由神殿原生流程生成，这里只编辑其合法等级。
            if (currentTable.skillId > 0 && !saved.isAlien && !saved.isInspired)
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

    private static string GetAttributeName(EAttrType type) => type switch
    {
        EAttrType.STR => "力量",
        EAttrType.DEX => "敏捷",
        EAttrType.INT => "智力",
        _ => type.ToString()
    };

    private static void SaveNow() =>
        // 只在完整操作成功后调用原生保存，校验失败时不会产生半完成存档。
        (Game.dataMgr?.nowSeasonData?.nativeSeasonData
         ?? throw new InvalidOperationException("当前游戏存档不可用。"))
        .SaveData();
}
