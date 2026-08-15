using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PathOfIdleEditor.App;

public sealed class EditorRequest
{
    public string Action { get; set; } = "";
    public EquipmentEdit? Equipment { get; set; }
    public HeroEdit? Hero { get; set; }
    public InventoryItemEdit? InventoryItem { get; set; }
    public InventoryAddEdit? InventoryAdd { get; set; }
    public LordEdit? Lord { get; set; }
}

public sealed class EditorResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public EditorSnapshot? Snapshot { get; set; }
    public EquipmentRules? EquipmentRules { get; set; }
    public InventorySnapshot? Inventory { get; set; }
    public LordEdit? Lord { get; set; }
}

public sealed class EditorSnapshot
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

public sealed class LordEdit
{
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
    public List<LordJobEdit> Jobs { get; set; } = new();
    public List<LordJobLevelRule> JobLevelRules { get; set; } = new();
}

public sealed class LordJobLevelRule
{
    public int Level { get; set; }
    public int RequiredLordLevel { get; set; }
    public int TotalAttributePoints { get; set; }
    public int MaximumTalentBonusLevel { get; set; }
}

public sealed class LordJobEdit
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
    public string StrengthRange => $"{StrengthMinimum}-{StrengthMaximum}";
    public string DexterityRange => $"{DexterityMinimum}-{DexterityMaximum}";
    public string IntelligenceRange => $"{IntelligenceMinimum}-{IntelligenceMaximum}";
}

public sealed class LordJobAttributeRule
{
    public int Level { get; set; }
    public int TotalAttributePoints { get; set; }
    public int StrengthMinimum { get; set; }
    public int StrengthMaximum { get; set; }
    public int DexterityMinimum { get; set; }
    public int DexterityMaximum { get; set; }
    public int IntelligenceMinimum { get; set; }
    public int IntelligenceMaximum { get; set; }
}

public sealed class LordTalentBonusEdit
{
    public int TalentId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
}

public sealed class InventorySnapshot
{
    public List<InventoryTemplate> AvailableItems { get; set; } = new();
    public List<InventoryItemEdit> BagItems { get; set; } = new();
}

public sealed class InventoryTemplate
{
    public int Type { get; set; }
    public string TypeName { get; set; } = "";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quality { get; set; }
    public int Level { get; set; }
    public string LevelDescription { get; set; } = "";
    public string Display => string.IsNullOrWhiteSpace(LevelDescription)
        ? $"{Id} · {Name}"
        : $"{Id} · {Name} · {LevelDescription}";
}

public sealed class InventoryItemEdit
{
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

public sealed class InventoryAddEdit
{
    public int Type { get; set; }
    public int Id { get; set; }
    public int Quality { get; set; }
    public int Level { get; set; }
    public int Count { get; set; }
}

public sealed class RuleOption
{
    public int Value { get; set; }
    public string Name { get; set; } = "";
    public string Display => $"{Value} · {Name}";
}

public sealed class EquipmentTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Part { get; set; }
    public string PartName { get; set; } = "";
    public int BaseQuality { get; set; }
    public List<int> AllowedQualities { get; set; } = new();
    public string Display => $"{Id} · {Name}  /  {PartName}";
}

public sealed class EquipmentEdit
{
    public int TemplateId { get; set; }
    public int Quality { get; set; }
    public int Level { get; set; }
    public int ForgeLevel { get; set; }
    public List<AffixEdit> Affixes { get; set; } = new();
}

public sealed class EquipmentRules
{
    public int MaximumAffixCount { get; set; }
    public int MaximumAffixLevel { get; set; }
    public List<int> AllowedForgeLevels { get; set; } = new();
    public Dictionary<int, int> AffixQualityLimits { get; set; } = new();
    public Dictionary<int, string> AffixQualityNames { get; set; } = new();
    public List<AffixOption> AllowedAffixes { get; set; } = new();
    public List<AffixEdit> GeneratedAffixes { get; set; } = new();
}

public sealed class AffixOption
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string QualityName { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AffixValueRange> ValueRanges { get; set; } = new();
    public string Display => $"{Id} · {Name}";
}

public sealed class AffixEdit : INotifyPropertyChanged
{
    private int _level;
    private int? _value;
    public int Id { get; set; }
    public int Quality { get; set; }
    public string QualityName { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level
    {
        get => _level;
        set
        {
            if (_level == value) return;
            _level = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueRange)));
            var range = ValueRanges.FirstOrDefault(item => item.Level == value);
            if (range != null)
                Value = range.Maximum;
        }
    }
    public int? Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
    public List<AffixValueRange> ValueRanges { get; set; } = new();
    public string ValueRange
    {
        get
        {
            var range = ValueRanges.FirstOrDefault(item => item.Level == Level);
            return range == null ? "游戏特殊随机/未提供" : $"{range.Minimum}-{range.Maximum}";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AffixValueRange
{
    public int Level { get; set; }
    public int Minimum { get; set; }
    public int Maximum { get; set; }
}

public sealed class AffixCategoryOption
{
    public int Quality { get; set; }
    public string Name { get; set; } = "";
    public int Limit { get; set; }
    public string Display => $"{Name} · 最多 {Limit} 条";
}

public sealed class HeroEdit
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
    public string Display => $"{Name}  ·  ID {UniqueId}";
}

public sealed class GrowthAttributeEdit
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public float Value { get; set; }
    public int MinimumValue { get; set; }
    public int MaximumValue { get; set; }
    public string ValueRange => $"{MinimumValue}-{MaximumValue}";
}

public sealed class TalentSlotEdit : INotifyPropertyChanged
{
    private int _talentId;
    private int _skillId;
    private string _name = "";
    private int _level;
    private int _minimumLevel;
    private int _maximumLevel;
    private List<SkillOption> _skillOptions = new();

    public int SlotId { get; set; }
    public bool IsAlien { get; set; }
    public bool IsInspired { get; set; }
    public bool CanChangeTalent { get; set; }
    public int TalentId
    {
        get => _talentId;
        set => SetField(ref _talentId, value);
    }
    public int SkillId
    {
        get => _skillId;
        set => SetField(ref _skillId, value);
    }
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public int CategoryOrder { get; set; }
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }
    public int Level
    {
        get => _level;
        set => SetField(ref _level, value);
    }
    public int MinimumLevel
    {
        get => _minimumLevel;
        set
        {
            if (SetField(ref _minimumLevel, value))
                OnPropertyChanged(nameof(LevelRange));
        }
    }
    public int MaximumLevel
    {
        get => _maximumLevel;
        set
        {
            if (SetField(ref _maximumLevel, value))
                OnPropertyChanged(nameof(LevelRange));
        }
    }
    public List<SkillOption> SkillOptions
    {
        get => _skillOptions;
        set => SetField(ref _skillOptions, value);
    }
    public string LevelRange => $"{MinimumLevel} - {MaximumLevel}";

    public event PropertyChangedEventHandler? PropertyChanged;

    // 仅通知当前技能行发生变化，避免刷新整张 DataGrid 后递归创建 ComboBox。
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SkillOption
{
    public int TalentId { get; set; }
    public int SkillId { get; set; }
    public string Name { get; set; } = "";
    public string JobName { get; set; } = "";
    public int MaximumLevel { get; set; }
    public string Display => string.IsNullOrWhiteSpace(JobName)
        ? $"{Name}  ·  上限 {MaximumLevel}"
        : $"{Name}  ·  {JobName}  ·  上限 {MaximumLevel}";
}
