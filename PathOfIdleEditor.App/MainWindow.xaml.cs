using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PathOfIdleEditor.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AffixEdit> _affixes = new();
    private EditorSnapshot? _snapshot;
    private EquipmentRules? _equipmentRules;
    private ICollectionView? _equipmentView;
    private bool _loadingControls;
    private int _rulesRequestVersion;

    public MainWindow()
    {
        InitializeComponent();
        AffixesGrid.ItemsSource = _affixes;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var response = await BridgeClient.SendAsync(new EditorRequest { Action = "snapshot" });
            EnsureSuccess(response);
            _snapshot = response.Snapshot ?? throw new InvalidDataException("桥接没有返回游戏数据。");

            _loadingControls = true;
            _equipmentView = CollectionViewSource.GetDefaultView(_snapshot.EquipmentTemplates);
            EquipmentTemplateCombo.ItemsSource = _equipmentView;
            EquipmentLevelCombo.ItemsSource = _snapshot.EquipmentLevels;
            HeroCombo.ItemsSource = _snapshot.Heroes;
            EquipmentTemplateCombo.SelectedIndex = _snapshot.EquipmentTemplates.Count > 0 ? 0 : -1;
            EquipmentLevelCombo.SelectedIndex = _snapshot.EquipmentLevels.Count > 0 ? 0 : -1;
            HeroCombo.SelectedIndex = _snapshot.Heroes.Count > 0 ? 0 : -1;
            RefreshQualityOptions();
            _loadingControls = false;
            await LoadEquipmentRulesAsync();
            return $"已读取当前游戏：{_snapshot.EquipmentTemplates.Count} 个装备模板，{_snapshot.Heroes.Count} 名角色。";
        });
    }

    private void EquipmentSearchText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_equipmentView == null)
            return;
        var keyword = EquipmentSearchText.Text.Trim();
        _equipmentView.Filter = item =>
        {
            if (item is not EquipmentTemplate equipment || string.IsNullOrWhiteSpace(keyword))
                return true;
            return equipment.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                   equipment.Id.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase);
        };
        _equipmentView.Refresh();
        if (EquipmentTemplateCombo.SelectedItem == null && !_equipmentView.IsEmpty)
            EquipmentTemplateCombo.SelectedIndex = 0;
    }

    private async void EquipmentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls)
            return;
        if (sender == EquipmentTemplateCombo)
        {
            _loadingControls = true;
            RefreshQualityOptions();
            _loadingControls = false;
        }
        await LoadEquipmentRulesAsync();
    }

    private void RefreshQualityOptions()
    {
        if (_snapshot == null || EquipmentTemplateCombo.SelectedItem is not EquipmentTemplate template)
        {
            QualityCombo.ItemsSource = null;
            return;
        }
        var options = _snapshot.EquipmentQualities
            .Where(option => template.AllowedQualities.Contains(option.Value))
            .ToList();
        QualityCombo.ItemsSource = options;
        if (options.Count > 0)
        {
            var baseIndex = options.FindIndex(option => option.Value == template.BaseQuality);
            QualityCombo.SelectedIndex = baseIndex >= 0 ? baseIndex : 0;
        }
    }

    private async Task LoadEquipmentRulesAsync()
    {
        if (EquipmentTemplateCombo.SelectedItem is not EquipmentTemplate template ||
            QualityCombo.SelectedItem is not RuleOption quality ||
            EquipmentLevelCombo.SelectedItem is not int level)
            return;

        var requestVersion = ++_rulesRequestVersion;
        AffixRuleText.Text = "正在读取当前游戏的词条池和数量限制……";
        try
        {
            var response = await BridgeClient.SendAsync(new EditorRequest
            {
                Action = "equipmentRules",
                Equipment = new EquipmentEdit { TemplateId = template.Id, Quality = quality.Value, Level = level }
            });
            if (requestVersion != _rulesRequestVersion)
                return;
            EnsureSuccess(response);
            _equipmentRules = response.EquipmentRules ?? throw new InvalidDataException("桥接没有返回装备规则。");
            AllowedAffixCombo.ItemsSource = _equipmentRules.AllowedAffixes;
            AllowedAffixCombo.SelectedIndex = _equipmentRules.AllowedAffixes.Count > 0 ? 0 : -1;
            _affixes.Clear();
            foreach (var affix in _equipmentRules.GeneratedAffixes)
                _affixes.Add(affix);
            AffixRuleText.Text = $"游戏规则：最多 {_equipmentRules.MaximumAffixCount} 条词条；词条等级 1-{_equipmentRules.MaximumAffixLevel}；当前合法词条池 {_equipmentRules.AllowedAffixes.Count} 条。";
            SetStatus("已根据当前装备、品级和等级刷新游戏规则。", true);
        }
        catch (Exception exception)
        {
            if (requestVersion != _rulesRequestVersion)
                return;
            _equipmentRules = null;
            _affixes.Clear();
            AllowedAffixCombo.ItemsSource = null;
            AffixRuleText.Text = $"无法读取规则：{exception.Message}";
            SetStatus($"读取装备规则失败：{exception.Message}", false);
        }
    }

    private void AddAffixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equipmentRules == null)
        {
            SetStatus("请先选择合法的装备、品级和等级。", false);
            return;
        }
        if (_affixes.Count >= _equipmentRules.MaximumAffixCount)
        {
            SetStatus($"当前品级最多允许 {_equipmentRules.MaximumAffixCount} 条词条。", false);
            return;
        }
        if (AllowedAffixCombo.SelectedItem is not AffixOption option)
        {
            SetStatus("当前组合没有可添加的合法词条。", false);
            return;
        }
        if (_affixes.Any(affix => affix.Id == option.Id))
        {
            SetStatus("同一个词条不能重复添加。", false);
            return;
        }
        _affixes.Add(new AffixEdit { Id = option.Id, Quality = option.Quality, Name = option.Name, Level = 1 });
        SetStatus($"已添加词条 {option.Id}，提交前仍会由游戏规则复核。", true);
    }

    private void RemoveAffixButton_Click(object sender, RoutedEventArgs e)
    {
        if (AffixesGrid.SelectedItem is not AffixEdit affix)
        {
            SetStatus("请先在词条表格中选择要删除的一行。", false);
            return;
        }
        _affixes.Remove(affix);
        SetStatus($"已从待生成装备中删除词条 {affix.Id}。", true);
    }

    private async void GenerateEquipmentButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            CommitGrid(AffixesGrid);
            var template = EquipmentTemplateCombo.SelectedItem as EquipmentTemplate
                ?? throw new InvalidOperationException("请选择装备模板。");
            var quality = QualityCombo.SelectedItem as RuleOption
                ?? throw new InvalidOperationException("该装备没有可用的合法品级。");
            var level = EquipmentLevelCombo.SelectedItem is int selectedLevel
                ? selectedLevel : throw new InvalidOperationException("请选择合法装备等级。");
            if (_equipmentRules == null)
                throw new InvalidOperationException("尚未读取当前组合的游戏规则。");
            foreach (var affix in _affixes)
            {
                if (affix.Level < 1 || affix.Level > _equipmentRules.MaximumAffixLevel)
                    throw new InvalidOperationException($"词条 {affix.Id} 的合法等级为 1-{_equipmentRules.MaximumAffixLevel}。");
            }
            var response = await BridgeClient.SendAsync(new EditorRequest
            {
                Action = "generateEquipment",
                Equipment = new EquipmentEdit
                {
                    TemplateId = template.Id,
                    Quality = quality.Value,
                    Level = level,
                    Affixes = _affixes.Select(CloneAffix).ToList()
                }
            });
            EnsureSuccess(response);
            return response.Message;
        });
    }

    private void HeroCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeroCombo.SelectedItem is not HeroEdit hero)
            return;
        HeroNameText.Text = hero.Name;
        HeroLevelText.Text = hero.Level.ToString(CultureInfo.InvariantCulture);
        StrengthText.Text = hero.Strength.ToString(CultureInfo.InvariantCulture);
        DexterityText.Text = hero.Dexterity.ToString(CultureInfo.InvariantCulture);
        IntelligenceText.Text = hero.Intelligence.ToString(CultureInfo.InvariantCulture);
        RemainingPointsText.Text = hero.RemainingSkillPoints.ToString(CultureInfo.InvariantCulture);
        TalentsGrid.ItemsSource = hero.TalentSlots;
        SetStatus($"已选择“{hero.Name}”；角色合法等级为 1-{hero.MaximumLevel}。", true);
    }

    private void SkillOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.DataContext is not TalentSlotEdit slot || combo.SelectedItem is not SkillOption option)
            return;
        slot.TalentId = option.TalentId;
        slot.SkillId = option.SkillId;
        slot.Name = option.Name;
        slot.MaximumLevel = option.MaximumLevel;
        if (slot.Level > slot.MaximumLevel)
            slot.Level = slot.MaximumLevel;
        TalentsGrid.Items.Refresh();
        SetStatus($"位置 {slot.SlotId} 已选择“{option.Name}”，合法等级上限 {option.MaximumLevel}。", true);
    }

    private async void ApplyHeroButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            CommitGrid(TalentsGrid);
            var hero = HeroCombo.SelectedItem as HeroEdit
                ?? throw new InvalidOperationException("请选择角色。");
            var level = ParseInt(HeroLevelText.Text, "角色等级");
            if (level < 1 || level > hero.MaximumLevel)
                throw new InvalidOperationException($"当前游戏规则允许的角色等级为 1-{hero.MaximumLevel}。");
            foreach (var slot in hero.TalentSlots)
            {
                if (slot.Level < slot.MinimumLevel || slot.Level > slot.MaximumLevel)
                    throw new InvalidOperationException($"位置 {slot.SlotId}“{slot.Name}”的合法等级为 {slot.MinimumLevel}-{slot.MaximumLevel}。");
            }
            hero.Name = HeroNameText.Text;
            hero.Level = level;
            hero.Strength = ParseFloat(StrengthText.Text, "力量");
            hero.Dexterity = ParseFloat(DexterityText.Text, "敏捷");
            hero.Intelligence = ParseFloat(IntelligenceText.Text, "智力");
            hero.RemainingSkillPoints = ParseInt(RemainingPointsText.Text, "剩余技能点");
            var response = await BridgeClient.SendAsync(new EditorRequest { Action = "updateHero", Hero = hero });
            EnsureSuccess(response);
            return response.Message;
        });
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        RefreshButton.IsEnabled = false;
        SetStatus("正在处理，请稍候……", true);
        try
        {
            SetStatus(await action(), true);
        }
        catch (TimeoutException)
        {
            SetStatus("连接超时：请确认游戏已启动、桥接 Mod 已加载，并且已经进入游戏存档。", false);
        }
        catch (Exception exception)
        {
            SetStatus($"操作失败：{exception.Message}", false);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(success ? "#22C55E" : "#EF4444"));
    }

    private static AffixEdit CloneAffix(AffixEdit value) => new()
    {
        Id = value.Id, Quality = value.Quality, Name = value.Name, Level = value.Level
    };

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static void EnsureSuccess(EditorResponse response)
    {
        if (!response.Success) throw new InvalidOperationException(response.Message);
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : throw new FormatException($"{name}必须是整数。");

    private static float ParseFloat(string value, string name) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result : throw new FormatException($"{name}必须是数字（小数点使用 .）。");
}
