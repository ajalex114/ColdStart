using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ColdStart.Helpers;
using ColdStart.Models;
using ColdStart.ViewModels;
using static ColdStart.Helpers.FormatHelper;
using static ColdStart.Helpers.UiHelper;

namespace ColdStart.Views.Controls;

/// <summary>
/// Displays running application groups with filtering, sorting, and expandable detail panels.
/// Subscribes to <see cref="AppUsageViewModel.DataChanged"/> and <see cref="MainViewModel.ThemeChanged"/>
/// to stay in sync with data and theme changes.
/// </summary>
public partial class AppUsageView : UserControl
{
    private const int SearchDebounceMs = 250;

    private AppUsageViewModel? _viewModel;
    private MainViewModel? _mainVm;
    private DispatcherTimer? _searchDebounce;
    private bool _needsRender;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppUsageView"/> control.
    /// </summary>
    public AppUsageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the view to its view models and subscribes to data and theme change events.
    /// </summary>
    /// <param name="vm">The app usage view model supplying group data.</param>
    /// <param name="mainVm">The main view model supplying the shared theme.</param>
    public void Initialize(AppUsageViewModel vm, MainViewModel mainVm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(mainVm);

        _viewModel = vm;
        _mainVm = mainVm;

        _viewModel.DataChanged += OnDataChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _mainVm.ThemeChanged += OnThemeChanged;
        IsVisibleChanged += OnVisibilityChanged;
    }

    /// <summary>
    /// Shows a loading spinner while app usage data is being gathered.
    /// </summary>
    public void ShowLoading()
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(Loader(Theme, "Gathering app usage data — this may take a moment..."));
    }

    /// <summary>
    /// Renders the full app usage view. Call when the tab becomes visible or data changes.
    /// </summary>
    public void Render()
    {
        ContentPanel.Children.Clear();

        if (_viewModel?.IsLoading == true)
        {
            ContentPanel.Children.Add(Loader(Theme, "Gathering app usage data — this may take a moment..."));
            return;
        }

        if (_viewModel?.Data == null)
            return;

        RenderControls();
        RenderFilterChips();
        RenderSummaryStats();
        RenderAppList();
    }

    // ── Controls row ─────────────────────────────────────────

    /// <summary>
    /// Renders the refresh button and the search text box with debounced input.
    /// </summary>
    private void RenderControls()
    {
        var theme = Theme;
        var controls = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var refreshBtn = MakeButton(theme, "↻  Refresh", async (_, _) =>
            await _viewModel!.LoadCommand.ExecuteAsync(null));
        refreshBtn.ToolTip = "Refresh app usage data";
        Grid.SetColumn(refreshBtn, 0);
        controls.Children.Add(refreshBtn);

        var searchBox = BuildSearchBox(theme);
        Grid.SetColumn(searchBox, 2);
        controls.Children.Add(searchBox);

        ContentPanel.Children.Add(controls);
    }

    /// <summary>
    /// Creates the search text box with placeholder, debounced input, and a clear button.
    /// </summary>
    private UIElement BuildSearchBox(ThemeManager theme)
    {
        var searchBox = new TextBox
        {
            Text = _viewModel!.SearchQuery,
            FontSize = 13,
            Padding = new Thickness(10, 6, 26, 6),
            Background = theme.Surface,
            Foreground = theme.Text,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
        };
        searchBox.Resources.Add(SystemColors.HighlightBrushKey, theme.Accent);

        bool isPlaceholder = string.IsNullOrEmpty(_viewModel!.SearchQuery);
        searchBox.Tag = isPlaceholder ? "placeholder" : null;
        if (isPlaceholder)
        {
            searchBox.Text = "🔍  Search apps...";
            searchBox.Foreground = theme.Dim;
        }

        var clearBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 13,
            Foreground = theme.Dim,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = isPlaceholder ? Visibility.Collapsed : Visibility.Visible,
        };

        clearBtn.MouseEnter += (_, _) => clearBtn.Foreground = theme.Text;
        clearBtn.MouseLeave += (_, _) => clearBtn.Foreground = theme.Dim;
        clearBtn.MouseLeftButtonDown += (_, _) =>
        {
            _searchDebounce?.Stop();
            _viewModel!.SearchQuery = "";
            searchBox.Text = "";
            searchBox.Focus();
        };

        AttachSearchBoxEvents(searchBox, theme, clearBtn);

        var container = new Grid();
        container.Children.Add(searchBox);
        container.Children.Add(clearBtn);
        return container;
    }

    /// <summary>
    /// Attaches focus and text-changed handlers to the search box.
    /// </summary>
    private void AttachSearchBoxEvents(TextBox searchBox, ThemeManager theme, TextBlock clearBtn)
    {
        searchBox.GotFocus += (_, _) =>
        {
            if (searchBox.Tag as string == "placeholder" && searchBox.Text.StartsWith("🔍"))
            {
                searchBox.Text = "";
                searchBox.Foreground = theme.Text;
                searchBox.Tag = null;
            }
        };

        searchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(searchBox.Text))
            {
                _searchDebounce?.Stop();
                _viewModel!.SearchQuery = "";
                searchBox.Tag = "placeholder";
                searchBox.Foreground = theme.Dim;
                searchBox.Text = "🔍  Search apps...";
                clearBtn.Visibility = Visibility.Collapsed;
            }
        };

        searchBox.TextChanged += (_, _) =>
        {
            if (searchBox.Tag as string == "placeholder") return;

            var hasText = !string.IsNullOrEmpty(searchBox.Text);
            clearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

            var query = searchBox.Text;
            _searchDebounce?.Stop();
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                _viewModel!.SearchQuery = query;
            };
            _searchDebounce.Start();
        };
    }

    // ── Filter chips ─────────────────────────────────────────

    /// <summary>
    /// Renders memory, CPU, and type filter chips plus the reset button.
    /// </summary>
    private void RenderFilterChips()
    {
        var vm = _viewModel!;
        var theme = Theme;
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        AddMemoryFilters(filterRow, vm, theme);
        filterRow.Children.Add(new Border { Width = 16 });

        AddCpuFilters(filterRow, vm, theme);
        filterRow.Children.Add(new Border { Width = 16 });

        AddTypeFilters(filterRow, vm, theme);

        if (vm.HasActiveFilters)
            AddResetButton(filterRow, theme);

        ContentPanel.Children.Add(filterRow);
    }

    /// <summary>
    /// Adds memory filter chips to the filter row.
    /// </summary>
    private void AddMemoryFilters(WrapPanel row, AppUsageViewModel vm, ThemeManager theme)
    {
        row.Children.Add(Txt(theme, "Memory:", 11, theme.Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", ">1GB", ">500MB", ">100MB" })
        {
            Brush? dot = opt switch { ">1GB" => theme.Red, ">500MB" => theme.Orange, ">100MB" => theme.Yellow, _ => null };
            row.Children.Add(FilterChip(theme, opt, vm.FilterMemory, dot, v => vm.FilterMemory = v));
        }
    }

    /// <summary>
    /// Adds CPU filter chips to the filter row.
    /// </summary>
    private void AddCpuFilters(WrapPanel row, AppUsageViewModel vm, ThemeManager theme)
    {
        row.Children.Add(Txt(theme, "CPU:", 11, theme.Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", ">10%", ">5%", ">1%" })
        {
            Brush? dot = opt switch { ">10%" => theme.Red, ">5%" => theme.Orange, ">1%" => theme.Yellow, _ => null };
            row.Children.Add(FilterChip(theme, opt, vm.FilterCpu, dot, v => vm.FilterCpu = v));
        }
    }

    /// <summary>
    /// Adds type filter chips to the filter row.
    /// </summary>
    private void AddTypeFilters(WrapPanel row, AppUsageViewModel vm, ThemeManager theme)
    {
        row.Children.Add(Txt(theme, "Type:", 11, theme.Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", "Startup", "Non-Startup" })
        {
            Brush? dot = opt == "Startup" ? theme.Accent : null;
            row.Children.Add(FilterChip(theme, opt, vm.FilterType, dot, v => vm.FilterType = v));
        }
    }

    /// <summary>
    /// Adds the "Reset All" button when filters are active.
    /// </summary>
    private void AddResetButton(WrapPanel filterRow, ThemeManager theme)
    {
        filterRow.Children.Add(new Border { Width = 16 });

        var resetBtn = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = theme.Red,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var resetText = Txt(theme, "✕  Reset All", 12, theme.Red, FontWeights.SemiBold);
        resetBtn.Child = resetText;

        resetBtn.MouseEnter += (_, _) => { resetBtn.Background = theme.Red; resetText.Foreground = theme.Bg; };
        resetBtn.MouseLeave += (_, _) => { resetBtn.Background = Brushes.Transparent; resetText.Foreground = theme.Red; };
        resetBtn.MouseLeftButtonUp += (_, _) => _viewModel!.ResetFiltersCommand.Execute(null);

        filterRow.Children.Add(resetBtn);
    }

    // ── Summary stats ────────────────────────────────────────

    /// <summary>
    /// Renders four summary stat cards: Showing, Memory Used, Startup Apps, Top Consumer.
    /// </summary>
    private void RenderSummaryStats()
    {
        var vm = _viewModel!;
        var data = vm.Data!;
        var theme = Theme;

        var memPct = data.TotalMemoryGb > 0 ? data.UsedMemoryGb / data.TotalMemoryGb * 100 : 0;
        var startupApps = data.Groups.Count(g => g.IsStartupApp);
        var memColor = memPct > 85 ? theme.Red : memPct > 60 ? theme.Yellow : theme.Green;

        var statsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        statsGrid.Children.Add(StatCard(theme, "Showing", $"{vm.FilteredGroups.Count}",
            $"of {data.Groups.Count} app groups", theme.Accent));
        statsGrid.Children.Add(StatCard(theme, "Memory Used", $"{data.UsedMemoryGb:F1} GB",
            $"of {data.TotalMemoryGb:F1} GB ({memPct:F0}%)", memColor));
        statsGrid.Children.Add(StatCard(theme, "Startup Apps", startupApps.ToString(),
            "Running from boot", theme.Accent));
        statsGrid.Children.Add(BuildTopConsumerCard(data, theme));

        ContentPanel.Children.Add(statsGrid);
    }

    /// <summary>
    /// Builds the "Top Consumer" stat card from the data.
    /// </summary>
    private static Border BuildTopConsumerCard(AppUsageData data, ThemeManager theme)
    {
        var topGroup = data.Groups.Count > 0
            ? data.Groups.OrderByDescending(g => g.TotalMemoryMb).First()
            : null;

        return StatCard(theme, "Top Consumer",
            topGroup?.FriendlyName ?? "—",
            topGroup != null ? FmtMem(topGroup.TotalMemoryMb) : "",
            theme.Orange);
    }

    // ── App list ─────────────────────────────────────────────

    /// <summary>
    /// Renders the section header, column headers, separator, and grouped app cards.
    /// </summary>
    private void RenderAppList()
    {
        var vm = _viewModel!;
        var theme = Theme;
        double totalMemMb = vm.Data!.TotalMemoryGb * 1024;

        var listCard = Card(theme);
        var listStack = new StackPanel();

        listStack.Children.Add(Txt(theme, "ALL RUNNING APPLICATIONS", 11, theme.Dim, FontWeights.SemiBold,
            new Thickness(0, 0, 0, 10)));
        listStack.Children.Add(BuildColumnHeaders(vm, theme));
        listStack.Children.Add(new Border { Background = theme.Bdr, Height = 1, Margin = new Thickness(0, 4, 0, 4) });

        foreach (var group in vm.FilteredGroups)
            listStack.Children.Add(CreateAppGroupCard(group, totalMemMb));

        listCard.Child = listStack;
        ContentPanel.Children.Add(listCard);
    }

    /// <summary>
    /// Builds the clickable column header row with sort indicators.
    /// </summary>
    private static Border BuildColumnHeaders(AppUsageViewModel vm, ThemeManager theme)
    {
        var headerBorder = new Border { Padding = new Thickness(12, 0, 12, 0) };
        var headerGrid = CreateColumnHeaderGrid();

        AddSortHeader(headerGrid, vm, theme, "APP", "Name", 0, HorizontalAlignment.Left);
        AddSortHeader(headerGrid, vm, theme, "MEMORY", "Memory", 1);
        AddSortHeader(headerGrid, vm, theme, "CPU %", "CPU", 2);
        AddSortHeader(headerGrid, vm, theme, "CPU TIME", "CPUTime", 3);
        AddSortHeader(headerGrid, vm, theme, "RUNNING FOR", "Duration", 4);

        headerBorder.Child = headerGrid;
        return headerBorder;
    }

    /// <summary>
    /// Creates the grid layout for column headers matching card padding.
    /// </summary>
    private static Grid CreateColumnHeaderGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        return grid;
    }

    /// <summary>
    /// Adds a single clickable sort header to the header grid.
    /// </summary>
    private static void AddSortHeader(
        Grid headerGrid,
        AppUsageViewModel vm,
        ThemeManager theme,
        string label,
        string sortKey,
        int col,
        HorizontalAlignment align = HorizontalAlignment.Right)
    {
        var indicator = vm.SortBy == sortKey ? (vm.SortAscending ? " ▲" : " ▼") : "";
        var isActive = vm.SortBy == sortKey;
        var tb = Txt(theme, label + indicator, 10, isActive ? theme.Accent : theme.Dim, FontWeights.SemiBold, align: align);

        var btn = new Border
        {
            Child = tb,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
        };
        btn.MouseEnter += (_, _) => btn.Background = theme.Surface;
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        btn.MouseLeftButtonDown += (_, _) => vm.ToggleSortCommand.Execute(sortKey);
        btn.ToolTip = $"Sort by {label.ToLower()}";

        Grid.SetColumn(btn, col);
        headerGrid.Children.Add(btn);
    }

    // ── App group card ───────────────────────────────────────

    /// <summary>
    /// Creates an expandable card for an app group with header, detail panel, and process rows.
    /// </summary>
    private UIElement CreateAppGroupCard(AppGroup group, double totalMemMb)
    {
        var theme = Theme;
        var card = BuildCardBorder(theme);
        var outer = new StackPanel();

        var arrow = Txt(theme, "▸", 14, theme.Dim, margin: new Thickness(8, 0, 0, 0));
        var header = BuildGroupHeader(group, totalMemMb, arrow, theme);
        var detail = BuildGroupDetail(group, totalMemMb, theme);

        outer.Children.Add(header);
        outer.Children.Add(detail);
        card.Child = outer;

        AttachExpandBehavior(card, detail, arrow);
        SetAccessibilityName(card, group);

        return card;
    }

    /// <summary>
    /// Creates the outer card border for an app group.
    /// </summary>
    private static Border BuildCardBorder(ThemeManager theme)
    {
        var card = new Border
        {
            Background = theme.Surface2,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null,
        };
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = theme.Accent;
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = theme.Bdr;
        return card;
    }

    /// <summary>
    /// Builds the header row for an app group: name, badges, memory bar, CPU, duration, arrow.
    /// </summary>
    private Grid BuildGroupHeader(AppGroup group, double totalMemMb, TextBlock arrow, ThemeManager theme)
    {
        var header = CreateColumnHeaderGrid();

        Grid.SetColumn(BuildGroupNameColumn(group, theme), 0);
        header.Children.Add(BuildGroupNameColumn(group, theme));

        var memStack = BuildMemoryColumn(group, totalMemMb, theme);
        Grid.SetColumn(memStack, 1);
        header.Children.Add(memStack);

        var cpuColor = group.TotalCpuPercent > 20 ? theme.Red : group.TotalCpuPercent > 5 ? theme.Yellow : theme.Dim;
        var cpuTxt = Txt(theme, $"{group.TotalCpuPercent:F1}%", 13, cpuColor, align: HorizontalAlignment.Right);
        Grid.SetColumn(cpuTxt, 2);
        header.Children.Add(cpuTxt);

        var cpuTimeTxt = Txt(theme, FmtDuration(group.TotalCpuTime), 13, theme.Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(cpuTimeTxt, 3);
        header.Children.Add(cpuTimeTxt);

        var durHours = group.SessionDuration.TotalHours;
        var durColor = durHours > 8 ? theme.Orange : durHours > 2 ? theme.Yellow : theme.Green;
        var durationTxt = Txt(theme, FmtDuration(group.SessionDuration), 13, durColor, FontWeights.Medium,
            align: HorizontalAlignment.Right);
        Grid.SetColumn(durationTxt, 4);
        header.Children.Add(durationTxt);

        Grid.SetColumn(arrow, 5);
        header.Children.Add(arrow);

        return header;
    }

    /// <summary>
    /// Builds the name column with friendly name, process count badge, and startup badge.
    /// </summary>
    private static StackPanel BuildGroupNameColumn(AppGroup group, ThemeManager theme)
    {
        var nameOuter = new StackPanel();
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
        namePanel.Children.Add(Txt(theme, group.FriendlyName, 13, theme.Text, FontWeights.SemiBold));

        if (group.TotalInstances > 1)
        {
            namePanel.Children.Add(new Border
            {
                Background = theme.Surface,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt(theme, $"{group.Processes.Count} processes · {group.TotalInstances} instances", 10, theme.Dim),
            });
        }

        if (group.IsStartupApp)
        {
            namePanel.Children.Add(new Border
            {
                Background = theme.AccentBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt(theme, "⚡Startup", 10, theme.Accent),
            });
        }

        nameOuter.Children.Add(namePanel);
        var startedLabel = $"Started at {group.EarliestStart:h:mm tt}  ·  running for {FmtDuration(group.SessionDuration)}";
        nameOuter.Children.Add(Txt(theme, startedLabel, 10.5, theme.Dim, margin: new Thickness(0, 2, 0, 0)));

        return nameOuter;
    }

    /// <summary>
    /// Builds the memory column with value text and a proportional bar.
    /// </summary>
    private static StackPanel BuildMemoryColumn(AppGroup group, double totalMemMb, ThemeManager theme)
    {
        var memStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var memVal = group.TotalMemoryMb;
        var memColor = memVal > 1000 ? theme.Red : memVal > 300 ? theme.Yellow : theme.Text;

        memStack.Children.Add(Txt(theme, FmtMem(memVal), 13, memColor, FontWeights.Medium,
            align: HorizontalAlignment.Right));

        var memPct = totalMemMb > 0 ? Math.Min(memVal / totalMemMb * 100, 100) : 0;
        var memBarTrack = new Border
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(2),
            Height = 3,
            Margin = new Thickness(0, 2, 0, 0),
        };
        memBarTrack.Child = new Border
        {
            Background = memColor,
            CornerRadius = new CornerRadius(2),
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(2, memPct * 0.8),
        };
        memStack.Children.Add(memBarTrack);

        return memStack;
    }

    // ── Group detail panel ───────────────────────────────────

    /// <summary>
    /// Builds the collapsible detail panel for a group: summary box + individual processes.
    /// </summary>
    private static StackPanel BuildGroupDetail(AppGroup group, double totalMemMb, ThemeManager theme)
    {
        var detail = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 2) };

        detail.Children.Add(BuildGroupSummaryBox(group, totalMemMb, theme));

        if (group.Processes.Count > 1 || group.Processes[0].InstanceCount > 1)
            AddProcessRows(detail, group, theme);
        else
            AddSingleProcessPath(detail, group, theme);

        return detail;
    }

    /// <summary>
    /// Builds the summary box with publisher, memory, CPU, and running-since details.
    /// </summary>
    private static Border BuildGroupSummaryBox(AppGroup group, double totalMemMb, ThemeManager theme)
    {
        var summaryBox = new Border
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var memPct = totalMemMb > 0 ? Math.Min(group.TotalMemoryMb / totalMemMb * 100, 100) : 0;
        var stack = new StackPanel();

        if (!string.IsNullOrEmpty(group.Publisher))
            stack.Children.Add(DetailRow(theme, "Publisher", group.Publisher));
        stack.Children.Add(DetailRow(theme, "Total Memory", $"{FmtMem(group.TotalMemoryMb)} ({memPct:F1}% of system)"));
        stack.Children.Add(DetailRow(theme, "CPU Usage", $"{group.TotalCpuPercent:F1}%"));
        stack.Children.Add(DetailRow(theme, "Total CPU Time", FmtDuration(group.TotalCpuTime)));
        stack.Children.Add(DetailRow(theme, "Running Since", group.EarliestStart.ToString("HH:mm:ss  ·  MMM dd")));
        stack.Children.Add(DetailRow(theme, "Processes",
            $"{group.Processes.Count} processes, {group.TotalInstances} total instances"));

        summaryBox.Child = stack;
        return summaryBox;
    }

    /// <summary>
    /// Adds individual process rows to the detail panel.
    /// </summary>
    private static void AddProcessRows(StackPanel detail, AppGroup group, ThemeManager theme)
    {
        detail.Children.Add(Txt(theme, "INDIVIDUAL PROCESSES", 10, theme.Dim, FontWeights.SemiBold,
            new Thickness(0, 6, 0, 6)));

        foreach (var proc in group.Processes)
            detail.Children.Add(BuildProcessRow(proc, theme));
    }

    /// <summary>
    /// Builds a single process row card with name, memory, CPU, and duration.
    /// </summary>
    private static Border BuildProcessRow(AppUsageEntry proc, ThemeManager theme)
    {
        var procCard = new Border
        {
            Background = theme.Surface2,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 3),
        };

        var procRow = new Grid();
        procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var namePanel = BuildProcessNamePanel(proc, theme);
        Grid.SetColumn(namePanel, 0);
        procRow.Children.Add(namePanel);

        var pMem = Txt(theme, FmtMem(proc.MemoryMb), 12, theme.Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(pMem, 1);
        procRow.Children.Add(pMem);

        var pCpu = Txt(theme, $"{proc.CpuPercent:F1}%", 12, theme.Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(pCpu, 2);
        procRow.Children.Add(pCpu);

        var pDur = Txt(theme, FmtDuration(proc.SessionDuration), 12, theme.Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(pDur, 3);
        procRow.Children.Add(pDur);

        procCard.Child = procRow;
        return procCard;
    }

    /// <summary>
    /// Builds the name panel for a single process, including instance count badge.
    /// </summary>
    private static StackPanel BuildProcessNamePanel(AppUsageEntry proc, ThemeManager theme)
    {
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
        var displayName = !string.IsNullOrEmpty(proc.FriendlyName) && proc.FriendlyName != proc.Name
            ? $"{proc.FriendlyName}  ({proc.Name})"
            : proc.Name;
        namePanel.Children.Add(Txt(theme, displayName, 12, theme.Text));

        if (proc.InstanceCount > 1)
            namePanel.Children.Add(Txt(theme, $"  ×{proc.InstanceCount}", 10, theme.Dim));

        return namePanel;
    }

    /// <summary>
    /// Adds the executable path row for a single-process group.
    /// </summary>
    private static void AddSingleProcessPath(StackPanel detail, AppGroup group, ThemeManager theme)
    {
        var proc = group.Processes[0];
        if (!string.IsNullOrEmpty(proc.ExePath))
            detail.Children.Add(DetailRow(theme, "Path", proc.ExePath));
    }

    // ── Expand / collapse ────────────────────────────────────

    /// <summary>
    /// Attaches click and keyboard expand/collapse behavior to the card.
    /// </summary>
    private static void AttachExpandBehavior(Border card, StackPanel detail, TextBlock arrow)
    {
        void Toggle()
        {
            if (detail.Visibility == Visibility.Visible)
            {
                detail.Visibility = Visibility.Collapsed;
                arrow.Text = "▸";
            }
            else
            {
                detail.Visibility = Visibility.Visible;
                arrow.Text = "▾";
            }
        }

        card.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                Toggle();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Sets the automation name for accessibility on the group card.
    /// </summary>
    private static void SetAccessibilityName(Border card, AppGroup group)
    {
        System.Windows.Automation.AutomationProperties.SetName(card,
            $"{group.FriendlyName}, {FmtMem(group.TotalMemoryMb)} memory, " +
            $"{group.TotalCpuPercent:F1}% CPU, {group.Processes.Count} processes");
    }

    // ── Event handlers / helpers ─────────────────────────────

    /// <summary>
    /// Convenience accessor for the shared theme manager.
    /// </summary>
    private ThemeManager Theme => _mainVm!.Theme;

    /// <summary>
    /// Handles data changes from the view model by re-rendering if visible.
    /// </summary>
    private void OnDataChanged()
    {
        if (IsVisible)
            Render();
        else
            _needsRender = true;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppUsageViewModel.IsLoading) && IsVisible)
            Render();
    }

    /// <summary>
    /// Handles theme changes by re-rendering if visible, or deferring until visible.
    /// </summary>
    private void OnThemeChanged()
    {
        if (IsVisible)
            Render();
        else
            _needsRender = true;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _needsRender)
        {
            _needsRender = false;
            Render();
        }
    }
}
