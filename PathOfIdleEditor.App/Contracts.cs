namespace PathOfIdleEditor.App;

public sealed class EditorRequest
{
    public string Action { get; set; } = "";
    public EquipmentEdit? Equipment { get; set; }
    public HeroEdit? Hero { get; set; }
}

public sealed class EditorResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public EditorSnapshot? Snapshot { get; set; }
    public EquipmentRules? EquipmentRules { get; set; }
}

public sealed class EditorSnapshot
{
    public List<EquipmentTemplate> EquipmentTemplates { get; set; } = new();
    public List<RuleOption> EquipmentQualities { get; set; } = new();
    public List<int> EquipmentLevels { get; set; } = new();
    public List<HeroEdit> Heroes { get; set; } = new();
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
    public List<AffixOption> AllowedAffixes { get; set; } = new();
    public List<AffixEdit> GeneratedAffixes { get; set; } = new();
}

public sealed class AffixOption
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string Name { get; set; } = "";
    public string Display => $"{Id} · {Name}";
}

public sealed class AffixEdit
{
    public int Id { get; set; }
    public int Quality { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
}

public sealed class HeroEdit
{
    public int UniqueId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaximumLevel { get; set; }
    public float Strength { get; set; }
    public float Dexterity { get; set; }
    public float Intelligence { get; set; }
    public int RemainingSkillPoints { get; set; }
    public List<TalentSlotEdit> TalentSlots { get; set; } = new();
    public string Display => $"{Name}  ·  ID {UniqueId}";
}

public sealed class TalentSlotEdit
{
    public int SlotId { get; set; }
    public int TalentId { get; set; }
    public int SkillId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MinimumLevel { get; set; }
    public int MaximumLevel { get; set; }
    public List<SkillOption> SkillOptions { get; set; } = new();
    public string LevelRange => $"{MinimumLevel} - {MaximumLevel}";
}

public sealed class SkillOption
{
    public int TalentId { get; set; }
    public int SkillId { get; set; }
    public string Name { get; set; } = "";
    public int MaximumLevel { get; set; }
    public string Display => $"{Name}  ·  上限 {MaximumLevel}";
}
