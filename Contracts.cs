using System.Collections.Generic;

namespace PathOfIdleEditor;

// 管道协议只传输普通 CLR 对象，不能直接序列化 IL2CPP 游戏对象。
internal sealed class EditorRequest
{
    public string Action { get; set; } = "";
    public EquipmentEdit? Equipment { get; set; }
    public HeroEdit? Hero { get; set; }
    public InventoryItemEdit? InventoryItem { get; set; }
    public InventoryAddEdit? InventoryAdd { get; set; }
    public LordEdit? Lord { get; set; }
}

internal sealed class EditorResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public EditorSnapshot? Snapshot { get; set; }
    public EquipmentRules? EquipmentRules { get; set; }
    public InventorySnapshot? Inventory { get; set; }
    public LordEdit? Lord { get; set; }
}

internal sealed class EditorSnapshot
{
    public List<EquipmentTemplate> EquipmentTemplates { get; set; } = new();
    public List<RuleOption> EquipmentQualities { get; set; } = new();
    public List<int> EquipmentLevels { get; set; } = new();
    public List<int> BlessingLevels { get; set; } = new();
    public List<RuleOption> HeroQualities { get; set; } = new();
    public List<HeroEdit> Heroes { get; set; } = new();
    public InventorySnapshot Inventory { get; set; } = new();
    public LordEdit Lord { get; set; } = new();
}

internal sealed class LordEdit
{
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
    public List<LordJobEdit> Jobs { get; set; } = new();
    public List<LordJobLevelRule> JobLevelRules { get; set; } = new();
}

internal sealed class LordJobLevelRule
{
    public int Level { get; set; }
    public int RequiredLordLevel { get; set; }
    public int TotalAttributePoints { get; set; }
    public int MaximumTalentBonusLevel { get; set; }
}

internal sealed class LordJobEdit
{
    public int JobId { get; set; }
    public string JobName { get; set; } = "";
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
    public int RequiredLordLevel { get; set; }
    public int TotalAttributePoints { get; set; }
    public int Strength { get; set; }
    public int StrengthMinimum { get; set; }
    public int StrengthMaximum { get; set; }
    public int Dexterity { get; set; }
    public int DexterityMinimum { get; set; }
    public int DexterityMaximum { get; set; }
    public int Intelligence { get; set; }
    public int IntelligenceMinimum { get; set; }
    public int IntelligenceMaximum { get; set; }
    public List<LordJobAttributeRule> AttributeRules { get; set; } = new();
    public List<LordTalentBonusEdit> TalentBonuses { get; set; } = new();
}

internal sealed class LordJobAttributeRule
{
    public int Level { get; set; }
    public int StrengthMinimum { get; set; }
    public int StrengthMaximum { get; set; }
    public int DexterityMinimum { get; set; }
    public int DexterityMaximum { get; set; }
    public int IntelligenceMinimum { get; set; }
    public int IntelligenceMaximum { get; set; }
}

internal sealed class LordTalentBonusEdit
{
    public int TalentId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
}

internal sealed class InventorySnapshot
{
    public List<InventoryTemplate> AvailableItems { get; set; } = new();
    public List<InventoryItemEdit> BagItems { get; set; } = new();
}

internal sealed class InventoryTemplate
{
    public int Type { get; set; }
    public string TypeName { get; set; } = "";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quality { get; set; }
    public int Level { get; set; }
    public string LevelDescription { get; set; } = "";
}

internal sealed class InventoryItemEdit
{
    // 0 为普通背包，1 为优先存放道具的 5x5 道具袋。
    public int Container { get; set; }
    public string ContainerName { get; set; } = "";
    public int FieldIndex { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = "";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quality { get; set; }
    public int Level { get; set; }
    public int Count { get; set; }
}

internal sealed class InventoryAddEdit
{
    public int Type { get; set; }
    public int Id { get; set; }
    public int Quality { get; set; }
    public int Level { get; set; }
    public int Count { get; set; }
}

internal sealed class RuleOption
{
    public int Value { get; set; }
    public string Name { get; set; } = "";
}

internal sealed class EquipmentTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Part { get; set; }
    public string PartName { get; set; } = "";
    public int BaseQuality { get; set; }
    public List<int> AllowedQualities { get; set; } = new();
}

internal sealed class EquipmentEdit
{
    public int TemplateId { get; set; }
    public int Quality { get; set; }
    public int Level { get; set; }
    public int ForgeLevel { get; set; }
    public List<AffixEdit> Affixes { get; set; } = new();
}

internal sealed class EquipmentRules
{
    // 这些限制由当前游戏表和原生方法实时计算，桌面端不维护规则副本。
    public int MaximumAffixCount { get; set; }
    public int MaximumAffixLevel { get; set; }
    public List<int> AllowedForgeLevels { get; set; } = new();
    public Dictionary<int, int> AffixQualityLimits { get; set; } = new();
    public Dictionary<int, string> AffixQualityNames { get; set; } = new();
    public List<AffixOption> AllowedAffixes { get; set; } = new();
    public List<AffixEdit> GeneratedAffixes { get; set; } = new();
}

internal sealed class AffixOption
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string QualityName { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class AffixEdit
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string QualityName { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    // null 表示沿用游戏原生随机值；非 null 时在创建完成后覆盖存档中的整数值。
    public int? Value { get; set; }
}

internal sealed class HeroEdit
{
    public int UniqueId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
    public int BlessingLevel { get; set; }
    public int Quality { get; set; }
    public float Strength { get; set; }
    public float Dexterity { get; set; }
    public float Intelligence { get; set; }
    public int RemainingSkillPoints { get; set; }
    public int MaximumAlienSkills { get; set; }
    public int AlienSkillCount { get; set; }
    public int MaximumInspiredTalents { get; set; }
    public int InspiredTalentCount { get; set; }
    public List<GrowthAttributeEdit> GrowthAttributes { get; set; } = new();
    public List<TalentSlotEdit> TalentSlots { get; set; } = new();
}

internal sealed class GrowthAttributeEdit
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public float Value { get; set; }
    public int MinimumValue { get; set; }
    public int MaximumValue { get; set; }
}

internal sealed class TalentSlotEdit
{
    // SlotId 是存档字典键，TalentId 是该位置当前选择的天赋/技能定义 ID。
    public int SlotId { get; set; }
    public int TalentId { get; set; }
    public int SkillId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MinimumLevel { get; set; }
    public int MaximumLevel { get; set; }
    public bool IsAlien { get; set; }
    public bool IsInspired { get; set; }
    public List<SkillOption> SkillOptions { get; set; } = new();
}

internal sealed class SkillOption
{
    public int TalentId { get; set; }
    public int SkillId { get; set; }
    public string Name { get; set; } = "";
    public int MaximumLevel { get; set; }
}
