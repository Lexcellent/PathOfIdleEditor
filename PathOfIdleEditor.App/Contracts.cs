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
}

public sealed class EditorResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public EditorSnapshot? Snapshot { get; set; }
    public EquipmentRules? EquipmentRules { get; set; }
    public InventorySnapshot? Inventory { get; set; }
}

public sealed class EditorSnapshot
{
    public List<EquipmentTemplate> EquipmentTemplates { get; set; } = new();
    public List<RuleOption> EquipmentQualities { get; set; } = new();
    public List<int> EquipmentLevels { get; set; } = new();
    public List<int> BlessingLevels { get; set; } = new();
    public List<HeroEdit> Heroes { get; set; } = new();
    public InventorySnapshot Inventory { get; set; } = new();
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
    public List<AffixEdit> Affixes { get; set; } = new();
}

public sealed class EquipmentRules
{
    public int MaximumAffixCount { get; set; }
    public int MaximumAffixLevel { get; set; }
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
    public string Display => $"{Id} · {Name}";
}

public sealed class AffixEdit
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string QualityName { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int? Value { get; set; }
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
    public float Strength { get; set; }
    public float Dexterity { get; set; }
    public float Intelligence { get; set; }
    public int RemainingSkillPoints { get; set; }
    public int MaximumAlienSkills { get; set; }
    public int AlienSkillCount { get; set; }
    public int MaximumInspiredTalents { get; set; }
    public int InspiredTalentCount { get; set; }
    public int GrowthRerollPrice { get; set; }
    public List<GrowthAttributeEdit> GrowthAttributes { get; set; } = new();
    public List<TalentSlotEdit> TalentSlots { get; set; } = new();
    public string Display => $"{Name}  ·  ID {UniqueId}";
}

public sealed class GrowthAttributeEdit
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public float Value { get; set; }
}

public sealed class TalentSlotEdit : INotifyPropertyChanged
{
    private int _talentId;
    private int _skillId;
    private string _name = "";
    private int _level;
    private int _minimumLevel;
    private int _maximumLevel;

    public int SlotId { get; set; }
    public bool IsAlien { get; set; }
    public bool IsInspired { get; set; }
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
    public List<SkillOption> SkillOptions { get; set; } = new();
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
    public int MaximumLevel { get; set; }
    public string Display => $"{Name}  ·  上限 {MaximumLevel}";
}
