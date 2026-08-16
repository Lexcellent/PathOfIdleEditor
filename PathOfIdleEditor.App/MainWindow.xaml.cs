using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace PathOfIdleEditor.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AffixEdit> _affixes = new();
    private readonly ICollectionView _affixView;
    private EditorSnapshot? _snapshot;
    private EquipmentRules? _equipmentRules;
    private ICollectionView? _equipmentView;
    private ICollectionView? _inventoryTemplateView;
    private ICollectionView? _inventoryView;
    private readonly Dictionary<int, List<SkillOption>> _talentSkillOptionCatalogs = new();
    private HeroEdit? _talentSkillCatalogHero;
    private bool _refreshingTalentSkillOptions;
    private bool _loadingControls;
    private int _rulesRequestVersion;

    public MainWindow()
    {
        InitializeComponent();
        _affixView = CollectionViewSource.GetDefaultView(_affixes);
        _affixView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AffixEdit.QualityName)));
        AffixesGrid.ItemsSource = _affixView;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            // 快照包含游戏当前版本的装备表、等级表、品级规则和角色技能树规则。
            var response = await BridgeClient.SendAsync(new EditorRequest { Action = "snapshot" });
            EnsureSuccess(response);
            _snapshot = response.Snapshot ?? throw new InvalidDataException("桥接没有返回游戏数据。");

            _loadingControls = true;
            _equipmentView = CollectionViewSource.GetDefaultView(_snapshot.EquipmentTemplates);
            EquipmentTemplateCombo.ItemsSource = _equipmentView;
            EquipmentLevelCombo.ItemsSource = _snapshot.EquipmentLevels;
            BlessingLevelCombo.ItemsSource = _snapshot.BlessingLevels;
            HeroQualityCombo.ItemsSource = _snapshot.HeroQualities;
            HeroCombo.ItemsSource = _snapshot.Heroes;
            BindInventory(_snapshot.Inventory);
            BindLord(_snapshot.Lord);
            EquipmentTemplateCombo.SelectedIndex = _snapshot.EquipmentTemplates.Count > 0 ? 0 : -1;
            EquipmentLevelCombo.SelectedIndex = _snapshot.EquipmentLevels.Count > 0 ? 0 : -1;
            HeroCombo.SelectedIndex = _snapshot.Heroes.Count > 0 ? 0 : -1;
            RefreshQualityOptions();
            _loadingControls = false;
            await LoadEquipmentRulesAsync();
            return $"已读取当前游戏：{_snapshot.EquipmentTemplates.Count} 个装备模板，{_snapshot.Inventory.BagItems.Count} 组背包物品，{_snapshot.Heroes.Count} 名角色。";
        });
    }

    private void BindInventory(InventorySnapshot inventory)
    {
        // 桥接返回的是全新快照；用稳定字段恢复选项，避免添加后跳回当前类型的第一项。
        var selectedTemplate = InventoryTemplateCombo.SelectedItem as InventoryTemplate;
        var selectedBagItem = InventoryGrid.SelectedItem as InventoryItemEdit;
        if (_snapshot != null)
            _snapshot.Inventory = inventory;
        _inventoryTemplateView = CollectionViewSource.GetDefaultView(inventory.AvailableItems);
        _inventoryView = CollectionViewSource.GetDefaultView(inventory.BagItems);
        InventoryTemplateCombo.ItemsSource = _inventoryTemplateView;
        InventoryGrid.ItemsSource = _inventoryView;
        RefreshInventoryFilters();

        if (selectedTemplate != null)
        {
            var restoredTemplate = inventory.AvailableItems.FirstOrDefault(item =>
                item.Type == selectedTemplate.Type && item.Id == selectedTemplate.Id &&
                item.Quality == selectedTemplate.Quality && item.Level == selectedTemplate.Level);
            if (restoredTemplate != null && _inventoryTemplateView.Contains(restoredTemplate))
                InventoryTemplateCombo.SelectedItem = restoredTemplate;
        }
        EnsureInventoryTemplateSelection();

        if (selectedBagItem != null)
        {
            var restoredBagItem = inventory.BagItems.FirstOrDefault(item =>
                item.Container == selectedBagItem.Container && item.Type == selectedBagItem.Type && item.FieldIndex == selectedBagItem.FieldIndex &&
                item.Id == selectedBagItem.Id && item.Quality == selectedBagItem.Quality && item.Level == selectedBagItem.Level);
            if (restoredBagItem != null && _inventoryView.Contains(restoredBagItem))
                InventoryGrid.SelectedItem = restoredBagItem;
        }
    }

    private void InventoryTemplateSearchText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_inventoryTemplateView == null)
            return;
        RefreshInventoryFilters();
        EnsureInventoryTemplateSelection();
    }

    private void BindLord(LordEdit lord)
    {
        if (_snapshot != null)
            _snapshot.Lord = lord;
        LordLevelText.Text = lord.Level.ToString(CultureInfo.InvariantCulture);
        LordLevelRuleText.Text = $"合法等级读取自当前游戏 TLordLevel 表：1-{lord.MaximumLevel}。";
        LordJobsGrid.ItemsSource = lord.Jobs;
        LordJobsGrid.SelectedIndex = lord.Jobs.Count > 0 ? 0 : -1;
    }

    private void LordJobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LordJobsGrid.SelectedItem is not LordJobEdit job)
        {
            LordTalentsGrid.ItemsSource = null;
            LordTalentRuleText.Text = "请选择一个职业魔偶。";
            return;
        }
        SyncLordJobRule(job);
        LordTalentsGrid.ItemsSource = job.TalentBonuses;
        LordTalentRuleText.Text = $"{job.JobName}：魔偶 {job.Level} 级要求崇拜者至少 {job.RequiredLordLevel} 级；" +
            $"力量总加成 {job.StrengthMinimum}-{job.StrengthMaximum}，敏捷总加成 {job.DexterityMinimum}-{job.DexterityMaximum}，" +
            $"智力总加成 {job.IntelligenceMinimum}-{job.IntelligenceMaximum}，三项累计总和必须为 {job.TotalAttributePoints}。";
    }

    private void SyncLordJobRule(LordJobEdit job)
    {
        var rule = _snapshot?.Lord.JobLevelRules.FirstOrDefault(item => item.Level == job.Level);
        if (rule == null)
            return;
        job.RequiredLordLevel = rule.RequiredLordLevel;
        job.TotalAttributePoints = rule.TotalAttributePoints;
        var attributeRule = job.AttributeRules.FirstOrDefault(item => item.Level == job.Level);
        if (attributeRule != null)
        {
            // 每个职业以自身当前存档总加成为锚点累计升级点数，不能使用单次升级奖励。
            job.TotalAttributePoints = attributeRule.TotalAttributePoints;
            job.StrengthMinimum = attributeRule.StrengthMinimum;
            job.StrengthMaximum = attributeRule.StrengthMaximum;
            job.DexterityMinimum = attributeRule.DexterityMinimum;
            job.DexterityMaximum = attributeRule.DexterityMaximum;
            job.IntelligenceMinimum = attributeRule.IntelligenceMinimum;
            job.IntelligenceMaximum = attributeRule.IntelligenceMaximum;
        }
        foreach (var talent in job.TalentBonuses)
        {
            talent.MaximumLevel = rule.MaximumTalentBonusLevel;
            if (talent.Level > talent.MaximumLevel)
                talent.Level = talent.MaximumLevel;
        }
    }

    private async void ApplyLordButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            CommitGrid(LordJobsGrid);
            CommitGrid(LordTalentsGrid);
            var lord = _snapshot?.Lord ?? throw new InvalidOperationException("请先连接并读取游戏数据。");
            var level = ParseInt(LordLevelText.Text, "崇拜者等级");
            if (level < 1 || level > lord.MaximumLevel)
                throw new InvalidOperationException($"当前游戏表允许的崇拜者等级为 1-{lord.MaximumLevel}。");
            lord.Level = level;
            // 桌面端不强制六个职业一起合法；桥接只对实际修改的魔偶按最新游戏规则校验。
            foreach (var job in lord.Jobs) SyncLordJobRule(job);
            var response = await BridgeClient.SendAsync(new EditorRequest { Action = "updateLord", Lord = lord });
            EnsureSuccess(response);
            BindLord(response.Lord ?? throw new InvalidDataException("桥接没有返回更新后的崇拜者数据。"));
            return response.Message;
        });
    }

    private void InventorySearchText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_inventoryView == null)
            return;
        RefreshInventoryFilters();
    }

    private void InventoryTypeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 子控件的 SelectionChanged 会冒泡到 TabControl，只处理类型 Tab 自己的切换事件。
        if (e.Source != InventoryTypeTabs || _inventoryTemplateView == null || _inventoryView == null)
            return;
        RefreshInventoryFilters();
        EnsureInventoryTemplateSelection();
        if (InventoryGrid.SelectedItem != null && !_inventoryView.Contains(InventoryGrid.SelectedItem))
            InventoryGrid.SelectedItem = null;
    }

    private void RefreshInventoryFilters()
    {
        if (_inventoryTemplateView == null || _inventoryView == null)
            return;
        var type = GetSelectedInventoryType();
        var templateKeyword = InventoryTemplateSearchText.Text.Trim();
        var bagKeyword = InventorySearchText.Text.Trim();
        _inventoryTemplateView.Filter = item => item is InventoryTemplate template && template.Type == type &&
            (string.IsNullOrWhiteSpace(templateKeyword) ||
             template.Name.Contains(templateKeyword, StringComparison.CurrentCultureIgnoreCase) ||
             template.Id.ToString(CultureInfo.InvariantCulture).Contains(templateKeyword, StringComparison.OrdinalIgnoreCase) ||
             template.LevelDescription.Contains(templateKeyword, StringComparison.CurrentCultureIgnoreCase));
        _inventoryView.Filter = item => item is InventoryItemEdit inventoryItem && inventoryItem.Type == type &&
            (string.IsNullOrWhiteSpace(bagKeyword) ||
             inventoryItem.Name.Contains(bagKeyword, StringComparison.CurrentCultureIgnoreCase) ||
             inventoryItem.Id.ToString(CultureInfo.InvariantCulture).Contains(bagKeyword, StringComparison.OrdinalIgnoreCase));
        _inventoryTemplateView.Refresh();
        _inventoryView.Refresh();
    }

    private void EnsureInventoryTemplateSelection()
    {
        if (_inventoryTemplateView == null)
            return;
        if (InventoryTemplateCombo.SelectedItem != null &&
            !_inventoryTemplateView.Contains(InventoryTemplateCombo.SelectedItem))
            InventoryTemplateCombo.SelectedItem = null;
        if (InventoryTemplateCombo.SelectedItem == null && !_inventoryTemplateView.IsEmpty)
            InventoryTemplateCombo.SelectedIndex = 0;
    }

    private int GetSelectedInventoryType()
    {
        if (InventoryTypeTabs.SelectedItem is TabItem tab &&
            int.TryParse(tab.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
            return type;
        return 1;
    }

    private async void AddInventoryButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var template = InventoryTemplateCombo.SelectedItem as InventoryTemplate
                ?? throw new InvalidOperationException("请选择要增加的物品。");
            var count = ParseInt(AddInventoryCountText.Text, "增加数量");
            if (count <= 0)
                throw new InvalidOperationException("增加数量必须大于 0。");
            var response = await BridgeClient.SendAsync(new EditorRequest
            {
                Action = "addInventoryItem",
                InventoryAdd = new InventoryAddEdit
                {
                    Type = template.Type, Id = template.Id, Quality = template.Quality,
                    Level = template.Level, Count = count
                }
            });
            EnsureSuccess(response);
            BindInventory(response.Inventory ?? throw new InvalidDataException("桥接没有返回最新背包数据。"));
            return response.Message;
        });
    }

    private async void ApplyInventoryCountButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            CommitGrid(InventoryGrid);
            if (HasValidationError(InventoryGrid))
                throw new InvalidOperationException("物品数量只能填写大于等于 0 的整数。");
            var item = InventoryGrid.SelectedItem as InventoryItemEdit
                ?? throw new InvalidOperationException("请先选择要修改的背包物品。");
            if (item.Count < 0)
                throw new InvalidOperationException("物品数量不能小于 0；填 0 可以删除该堆叠。");
            var response = await BridgeClient.SendAsync(new EditorRequest
            {
                Action = "updateInventoryItem",
                InventoryItem = item
            });
            EnsureSuccess(response);
            BindInventory(response.Inventory ?? throw new InvalidDataException("桥接没有返回最新背包数据。"));
            return response.Message;
        });
    }

    private void EquipmentSearchText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_equipmentView == null)
            return;
        var keyword = EquipmentSearchText.Text.Trim();
        // 使用 WPF 集合视图过滤，不复制装备集合，搜索时可以保留原始规则数据。
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

        // 连续切换下拉框时只接受最后一次响应，避免较慢的旧响应覆盖当前选择。
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
            var previousForgeLevel = EquipmentForgeLevelCombo.SelectedItem is int selectedForgeLevel
                ? selectedForgeLevel : 0;
            EquipmentForgeLevelCombo.ItemsSource = _equipmentRules.AllowedForgeLevels;
            EquipmentForgeLevelCombo.SelectedItem = _equipmentRules.AllowedForgeLevels.Contains(previousForgeLevel)
                ? previousForgeLevel
                : _equipmentRules.AllowedForgeLevels.FirstOrDefault();
            var affixCategories = _equipmentRules.AffixQualityLimits
                .OrderBy(pair => pair.Key)
                .Select(pair => new AffixCategoryOption
                {
                    Quality = pair.Key,
                    Name = _equipmentRules.AffixQualityNames.TryGetValue(pair.Key, out var name) ? name : $"词条档位 {pair.Key}",
                    Limit = pair.Value
                })
                .ToList();
            AffixQualityCombo.ItemsSource = affixCategories;
            AffixQualityCombo.SelectedIndex = affixCategories.Count > 0 ? 0 : -1;
            _affixes.Clear();
            foreach (var affix in _equipmentRules.GeneratedAffixes)
            {
                // 自动生成的初始词条也默认使用当前装备规则允许的最高等级。
                var editableAffix = CloneAffix(affix);
                editableAffix.Level = _equipmentRules.MaximumAffixLevel;
                _affixes.Add(editableAffix);
            }
            ResizeAffixColumnsToContent();
            var qualityLimits = string.Join("，", _equipmentRules.AffixQualityLimits
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{(_equipmentRules.AffixQualityNames.TryGetValue(pair.Key, out var name) ? name : $"档位 {pair.Key}")}最多 {pair.Value} 条"));
            var maximumForgeLevel = _equipmentRules.AllowedForgeLevels.Max();
            AffixRuleText.Text = $"游戏规则：锻造等级 0-{maximumForgeLevel}；总计最多 {_equipmentRules.MaximumAffixCount} 条；{qualityLimits}；词条等级 1-{_equipmentRules.MaximumAffixLevel}；合法候选 {_equipmentRules.AllowedAffixes.Count} 条；数值范围随词条等级联动但仅供参考，不限制输入，留空时由游戏随机。";
            SetStatus("已根据当前装备、品级和等级刷新游戏规则。", true);
        }
        catch (Exception exception)
        {
            if (requestVersion != _rulesRequestVersion)
                return;
            _equipmentRules = null;
            _affixes.Clear();
            AffixQualityCombo.ItemsSource = null;
            AllowedAffixCombo.ItemsSource = null;
            EquipmentForgeLevelCombo.ItemsSource = null;
            AffixRuleText.Text = $"无法读取规则：{exception.Message}";
            SetStatus($"读取装备规则失败：{exception.Message}", false);
        }
    }

    private void AffixQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_equipmentRules == null || AffixQualityCombo.SelectedItem is not AffixCategoryOption category)
        {
            AllowedAffixCombo.ItemsSource = null;
            return;
        }

        // 每次只显示当前词条档位的原生候选池，避免不同档位的数量规则混在一起。
        var options = _equipmentRules.AllowedAffixes
            .Where(option => option.Quality == category.Quality)
            .OrderBy(option => option.Id)
            .ToList();
        AllowedAffixCombo.ItemsSource = options;
        AllowedAffixCombo.SelectedIndex = options.Count > 0 ? 0 : -1;
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
        var currentQualityCount = _affixes.Count(affix => affix.Quality == option.Quality);
        if (!_equipmentRules.AffixQualityLimits.TryGetValue(option.Quality, out var qualityLimit) ||
            currentQualityCount >= qualityLimit)
        {
            SetStatus($"{option.QualityName}最多允许 {qualityLimit} 条词条。", false);
            return;
        }
        var defaultLevel = _equipmentRules.MaximumAffixLevel;
        var defaultRange = option.ValueRanges.FirstOrDefault(range => range.Level == defaultLevel);
        _affixes.Add(new AffixEdit
        {
            Id = option.Id,
            Quality = option.Quality,
            QualityName = option.QualityName,
            Name = option.Name,
            // 新增词条默认填入游戏当前规则允许的最高等级，用户仍可在表格中下调。
            Level = defaultLevel,
            ValueRanges = option.ValueRanges.Select(range => new AffixValueRange
            {
                Level = range.Level, Minimum = range.Minimum, Maximum = range.Maximum
            }).ToList(),
            // 有原生范围时默认最大值；特殊随机等无范围词条保持空值并交给游戏生成。
            Value = defaultRange?.Maximum
        });
        ResizeAffixColumnsToContent();
        SetStatus($"已添加词条 {option.Id}，提交前仍会由游戏规则复核。", true);
    }

    private void ResizeAffixColumnsToContent()
    {
        // 等待绑定文本生成单元格后再重置为 Auto，强制 WPF 使用最新内容重新测量列宽。
        Dispatcher.BeginInvoke(() =>
        {
            AffixesGrid.UpdateLayout();
            foreach (var column in AffixesGrid.Columns)
            {
                column.Width = new DataGridLength(column.ActualWidth, DataGridLengthUnitType.Pixel);
                column.Width = DataGridLength.Auto;
            }
            AffixesGrid.UpdateLayout();
        }, DispatcherPriority.Loaded);
    }

    private void ResizeTalentColumnsToContent()
    {
        // 分组和虚拟化会让模板列首次只按空单元格测量；内容生成后重新计算实际宽度。
        Dispatcher.BeginInvoke(() =>
        {
            TalentsGrid.UpdateLayout();
            foreach (var column in TalentsGrid.Columns)
            {
                column.Width = new DataGridLength(column.ActualWidth, DataGridLengthUnitType.Pixel);
                column.Width = column is DataGridTemplateColumn &&
                    column.Header?.ToString()?.StartsWith("技能 / 天赋", StringComparison.Ordinal) == true
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : DataGridLength.Auto;
            }
            TalentsGrid.UpdateLayout();
        }, DispatcherPriority.Loaded);
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
            if (HasValidationError(AffixesGrid))
                throw new InvalidOperationException("词条等级和数值只能填写整数；词条数值可以留空并由游戏随机生成。");
            var template = EquipmentTemplateCombo.SelectedItem as EquipmentTemplate
                ?? throw new InvalidOperationException("请选择装备模板。");
            var quality = QualityCombo.SelectedItem as RuleOption
                ?? throw new InvalidOperationException("该装备没有可用的合法品级。");
            var level = EquipmentLevelCombo.SelectedItem is int selectedLevel
                ? selectedLevel : throw new InvalidOperationException("请选择合法装备等级。");
            var forgeLevel = EquipmentForgeLevelCombo.SelectedItem is int selectedForgeLevel
                ? selectedForgeLevel : throw new InvalidOperationException("请选择合法锻造等级。");
            if (_equipmentRules == null)
                throw new InvalidOperationException("尚未读取当前组合的游戏规则。");
            // UI 先给出即时反馈；桥接层收到请求后仍会按最新游戏规则再次验证。
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
                    ForgeLevel = forgeLevel,
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
        BlessingLevelCombo.SelectedItem = hero.BlessingLevel;
        HeroQualityCombo.SelectedItem = _snapshot?.HeroQualities.FirstOrDefault(item => item.Value == hero.Quality);
        GrowthGrid.ItemsSource = hero.GrowthAttributes;
        PrepareTalentSkillOptions(hero);
        var talentView = CollectionViewSource.GetDefaultView(hero.TalentSlots);
        talentView.GroupDescriptions.Clear();
        talentView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TalentSlotEdit.Category)));
        TalentsGrid.ItemsSource = talentView;
        ResizeTalentColumnsToContent();
        var growthTotal = hero.GrowthAttributes.Sum(item => item.Value);
        GrowthRuleText.Text = $"可直接编辑且不消耗血肉结晶；单项范围与总成长读取自当前职业、品级的游戏规则。当前总和 {growthTotal:0.###}。";
        ExtraTalentRuleText.Text = $"异化技能 {hero.AlienSkillCount}/{hero.MaximumAlienSkills}，启迪天赋 {hero.InspiredTalentCount}/{hero.MaximumInspiredTalents}；现有项目可直接修改技能或天赋及其等级。";
        SetStatus($"已选择“{hero.Name}”；角色合法等级为 1-{hero.MaximumLevel}。", true);
    }

    private async void ChangeHeroQualityButton_Click(object sender, RoutedEventArgs e)
    {
        var hero = HeroCombo.SelectedItem as HeroEdit;
        var quality = HeroQualityCombo.SelectedItem as RuleOption;
        if (hero == null || quality == null)
        {
            SetStatus("操作失败：请选择角色和目标品级。", false);
            return;
        }
        hero.Quality = quality.Value;
        await RunHeroActionAsync("changeHeroQuality");
    }

    private async Task RunHeroActionAsync(string action)
    {
        await RunAsync(async () =>
        {
            var hero = HeroCombo.SelectedItem as HeroEdit
                ?? throw new InvalidOperationException("请选择角色。");
            var response = await BridgeClient.SendAsync(new EditorRequest { Action = action, Hero = hero });
            EnsureSuccess(response);
            var refreshed = response.Snapshot ?? throw new InvalidDataException("桥接没有返回更新后的角色数据。");
            _snapshot = refreshed;
            BlessingLevelCombo.ItemsSource = refreshed.BlessingLevels;
            HeroQualityCombo.ItemsSource = refreshed.HeroQualities;
            HeroCombo.ItemsSource = refreshed.Heroes;
            HeroCombo.SelectedItem = refreshed.Heroes.FirstOrDefault(item => item.UniqueId == hero.UniqueId);
            BindInventory(refreshed.Inventory);
            return response.Message;
        });
    }

    private void SkillOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingTalentSkillOptions)
            return;
        if (sender is not ComboBox combo || combo.DataContext is not TalentSlotEdit slot || combo.SelectedItem is not SkillOption option)
            return;

        // ComboBox 首次创建或被虚拟化回收时也会触发 SelectionChanged；这些不是用户操作。
        if (e.RemovedItems.Count == 0 || (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen))
            return;

        slot.TalentId = option.TalentId;
        slot.SkillId = option.SkillId;
        slot.Name = option.Name;
        // 不同技能的上限可能不同，切换技能时同步收紧当前编辑值。
        slot.MaximumLevel = option.MaximumLevel;
        if (slot.Level > slot.MaximumLevel)
            slot.Level = slot.MaximumLevel;
        if (HeroCombo.SelectedItem is HeroEdit hero && slot.Category == "天赋技能")
            RefreshTalentSkillOptions(hero);
        SetStatus($"位置 {slot.SlotId} 已选择“{option.Name}”，合法等级上限 {option.MaximumLevel}。", true);
    }

    private void PrepareTalentSkillOptions(HeroEdit hero)
    {
        // 新快照或切换角色时保存每个天赋技能槽的完整候选池；后续联动过滤不能覆盖原始列表。
        if (!ReferenceEquals(_talentSkillCatalogHero, hero))
        {
            _talentSkillCatalogHero = hero;
            _talentSkillOptionCatalogs.Clear();
            foreach (var slot in hero.TalentSlots)
                if (slot.Category == "天赋技能")
                    _talentSkillOptionCatalogs[slot.SlotId] = slot.SkillOptions.ToList();
        }
        RefreshTalentSkillOptions(hero);
    }

    private void RefreshTalentSkillOptions(HeroEdit hero)
    {
        var selectedSkillIds = hero.TalentSlots
            .Where(item => item.Category == "天赋技能")
            .Select(item => item.SkillId)
            .ToHashSet();
        _refreshingTalentSkillOptions = true;
        try
        {
            foreach (var slot in hero.TalentSlots)
            {
                if (slot.Category != "天赋技能" ||
                    !_talentSkillOptionCatalogs.TryGetValue(slot.SlotId, out var catalog))
                    continue;
                // 当前槽位保留自己的已选项；其他槽位选中的技能不会重复出现在此下拉框。
                slot.SkillOptions = catalog
                    .Where(option => option.TalentId == slot.TalentId || !selectedSkillIds.Contains(option.SkillId))
                    .ToList();
            }
        }
        finally
        {
            _refreshingTalentSkillOptions = false;
        }
    }

    private async void ApplyHeroButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            CommitGrid(GrowthGrid);
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
            foreach (var growth in hero.GrowthAttributes)
            {
                if (Math.Abs(growth.Value - MathF.Round(growth.Value)) > 0.0001f)
                    throw new InvalidOperationException($"“{growth.Name}”每级成长必须是整数。");
                if (growth.Value < growth.MinimumValue || growth.Value > growth.MaximumValue)
                    throw new InvalidOperationException($"“{growth.Name}”每级成长合法范围为 {growth.MinimumValue}-{growth.MaximumValue}。");
            }
            hero.Name = HeroNameText.Text;
            hero.Level = level;
            hero.BlessingLevel = BlessingLevelCombo.SelectedItem is int blessingLevel
                ? blessingLevel
                : throw new InvalidOperationException("请选择合法赐福等级。");
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
        Id = value.Id, Quality = value.Quality, QualityName = value.QualityName,
        Name = value.Name, Level = value.Level, Value = value.Value,
        ValueRanges = value.ValueRanges.Select(range => new AffixValueRange
        {
            Level = range.Level, Minimum = range.Minimum, Maximum = range.Maximum
        }).ToList()
    };

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static bool HasValidationError(DependencyObject element)
    {
        if (Validation.GetHasError(element))
            return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasValidationError(VisualTreeHelper.GetChild(element, index)))
                return true;
        return false;
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
