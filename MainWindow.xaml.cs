using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ColdStart.Models;
using ColdStart.Services;

namespace ColdStart;

public partial class MainWindow : Window
{
    // ── Theme Mode ─────────────────────────────────────────
    enum ThemeMode { Dark, Light, System }
    ThemeMode _theme = ThemeMode.Dark;

    // ── Theme Colors (mutable for theme switching) ─────────
    Brush Bg, Surface, Surface2, Bdr, Text, Dim, Accent, AccentBg;
    Brush Green, GreenBg, Yellow, YellowBg, Red, RedBg, Orange;

    static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    Brush B2(string hex) => B(hex); // instance alias for lambdas

    // ── Services ────────────────────────────────────────────
    readonly SystemInfoService _sysInfo = new();
    readonly StartupAnalyzerService _startup = new();
    readonly PerformanceService _perf = new();
    readonly AppUsageService _appUsage = new();
    StartupAnalysis? _startupData;
    AppUsageData? _lastAppUsageData;
    bool _perfLoaded;
    DispatcherTimer? _autoTimer;
    DispatcherTimer? _startupSearchDebounce;
    DispatcherTimer? _appSearchDebounce;
    string _searchQuery = "";
    string _sortBy = "Impact";
    string _appSortBy = "Memory";   // Memory, CPU, CPUTime, Duration, Name
    bool _appSortAsc = false;
    string _appSearch = "";
    string _appFilterMem = "All";    // All, >1GB, >500MB, >100MB
    string _appFilterCpu = "All";    // All, >10%, >5%, >1%
    string _appFilterType = "All";   // All, Startup, Non-Startup
    string _filterImpact = "All";    // All, High, Medium, Low
    string _filterStatus = "All";    // All, Enabled, Disabled
    string _filterTime = "All";      // All, >5s, >2s, >1s

    DispatcherTimer? _timelineResizeDebounce;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        KeyDown += OnGlobalKeyDown;
        SizeChanged += (_, _) =>
        {
            // Debounced re-render of timeline on resize
            if (TimelinePanel.Visibility != Visibility.Visible || _startupData == null) return;
            _timelineResizeDebounce?.Stop();
            _timelineResizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timelineResizeDebounce.Tick += (_, _) =>
            {
                _timelineResizeDebounce.Stop();
                RenderTimeline(_startupData);
            };
            _timelineResizeDebounce.Start();
        };
        Loaded += async (_, _) =>
        {
            _ = Task.Run(() => _sysInfo.GetSystemInfo()).ContinueWith(t =>
                Dispatcher.Invoke(() =>
                {
                    var s = t.Result;
                    SystemInfoText.Text = $"{s.Hostname}  ·  {s.Os}\n{s.Cpu}  ·  {s.Cores} cores  ·  {s.RamTotalGb} GB RAM\nBoot: {s.BootTime}  ·  Up {s.Uptime}";
                }));
            await LoadStartup();
        };
    }

    // ── Keyboard Shortcuts ──────────────────────────────────
    void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+1 = Startup tab, Ctrl+2 = Performance tab, Ctrl+3 = Timeline tab
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1:
                    StartupTab_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                case Key.D2:
                    PerfTab_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                case Key.D3:
                    TimelineTab_Click(this, new RoutedEventArgs());
                    e.Handled = true; return;
                case Key.F:
                    // Focus search box (find the TextBox inside StartupContent)
                    FocusSearchBox();
                    e.Handled = true; return;
                case Key.R:
                    _ = LoadStartup();
                    e.Handled = true; return;
                case Key.T:
                    CycleTheme();
                    e.Handled = true; return;
            }
        }

        // Escape clears search if focused
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                _searchQuery = "";
                if (_startupData != null) RenderStartup(_startupData);
                e.Handled = true;
            }
        }
    }

    void FocusSearchBox()
    {
        // Walk the visual tree of StartupContent to find the search TextBox
        static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
        var tb = FindChild<TextBox>(StartupContent);
        tb?.Focus();
    }

    // ── Theme ────────────────────────────────────────────────
    void ApplyTheme()
    {
        var effectiveTheme = _theme;
        if (effectiveTheme == ThemeMode.System)
        {
            // Detect Windows dark/light mode from registry
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                effectiveTheme = (val is int v && v == 0) ? ThemeMode.Dark : ThemeMode.Light;
            }
            catch { effectiveTheme = ThemeMode.Dark; }
        }

        if (effectiveTheme == ThemeMode.Light)
        {
            Bg = B("#f5f6fa"); Surface = B("#ffffff"); Surface2 = B("#ecedf3");
            Bdr = B("#cdd0dc"); Text = B("#1a1d27"); Dim = B("#555972");
            Accent = B("#3b5de7"); AccentBg = B("#e0e6ff");
            Green = B("#15803d"); GreenBg = B("#d1fae5");
            Yellow = B("#a16207"); YellowBg = B("#fef3c7");
            Red = B("#b91c1c"); RedBg = B("#fee2e2");
            Orange = B("#c2410c");
        }
        else
        {
            Bg = B("#0f1117"); Surface = B("#1a1d27"); Surface2 = B("#242835");
            Bdr = B("#2e3347"); Text = B("#e4e6f0"); Dim = B("#a0a4b8");
            Accent = B("#7d9bff"); AccentBg = B("#1c2540");
            Green = B("#4ade80"); GreenBg = B("#163a28");
            Yellow = B("#fcd34d"); YellowBg = B("#3a3318");
            Red = B("#f87171"); RedBg = B("#3a1818");
            Orange = B("#fb923c");
        }

        // Update window and XAML-bound resources
        Background = Bg;
        var res = Application.Current.Resources;
        res["BgBrush"] = Bg; res["SurfaceBrush"] = Surface;
        res["Surface2Brush"] = Surface2; res["BorderBrush"] = Bdr;
        res["TextBrush"] = Text; res["TextDimBrush"] = Dim;
        res["AccentBrush"] = Accent; res["GreenBrush"] = Green;
        res["YellowBrush"] = Yellow; res["RedBrush"] = Red;
        res["OrangeBrush"] = Orange;

        // Re-style header & tabs
        TitleIcon.Foreground = Accent;
        TitleText.Foreground = Text;
        SystemInfoText.Foreground = Dim;
        DeviceInfoIcon.Foreground = Dim;
        StartupTabBtn.Foreground = StartupPanel.Visibility == Visibility.Visible ? Accent : Dim;
        StartupTabBtn.Background = StartupPanel.Visibility == Visibility.Visible ? AccentBg : Brushes.Transparent;
        PerfTabBtn.Foreground = PerfPanel.Visibility == Visibility.Visible ? Accent : Dim;
        PerfTabBtn.Background = PerfPanel.Visibility == Visibility.Visible ? AccentBg : Brushes.Transparent;

        // Theme toggle pill
        ThemeBorder.Background = Surface2;
        ThemeBorder.BorderBrush = Bdr;
        ThemeBtn.Foreground = Text;
        ThemeBtn.Text = _theme switch { ThemeMode.Light => "Light", ThemeMode.Dark => "Dark", _ => "System" };
        ThemeIcon.Text = _theme switch { ThemeMode.Light => "☀️", ThemeMode.Dark => "🌙", _ => "💻" };
        ThemeIcon.Foreground = Text;

        // Device info card
        DeviceInfoCard.Background = Surface;
        DeviceInfoCard.BorderBrush = Bdr;

        // Tab bar
        TabBarBorder.Background = Surface;
        bool startupActive = StartupPanel.Visibility == Visibility.Visible;
        StartupTabBtn.Foreground = startupActive ? Accent : Dim;
        StartupTabBtn.Background = startupActive ? AccentBg : Brushes.Transparent;
        PerfTabBtn.Foreground = !startupActive ? Accent : Dim;
        PerfTabBtn.Background = !startupActive ? AccentBg : Brushes.Transparent;
    }

    void CycleTheme()
    {
        _theme = _theme switch
        {
            ThemeMode.Dark => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.System,
            _ => ThemeMode.Dark,
        };
        ApplyTheme();
        // Refresh tab button colors for the new theme
        RefreshTabButtonColors();
        // Re-render all tabs with new theme colors
        if (_startupData != null)
            RenderStartup(_startupData);
        if (_startupData != null && TimelinePanel.Visibility == Visibility.Visible)
            RenderTimeline(_startupData);
        if (_perfLoaded)
            _ = LoadAppUsage();
    }

    void RefreshTabButtonColors()
    {
        // Reset all tabs to inactive
        StartupTabBtn.Background = Brushes.Transparent; StartupTabBtn.Foreground = Dim;
        TimelineTabBtn.Background = Brushes.Transparent; TimelineTabBtn.Foreground = Dim;
        PerfTabBtn.Background = Brushes.Transparent; PerfTabBtn.Foreground = Dim;
        // Highlight active tab
        if (StartupPanel.Visibility == Visibility.Visible)
        { StartupTabBtn.Background = AccentBg; StartupTabBtn.Foreground = Accent; }
        else if (TimelinePanel.Visibility == Visibility.Visible)
        { TimelineTabBtn.Background = AccentBg; TimelineTabBtn.Foreground = Accent; }
        else if (PerfPanel.Visibility == Visibility.Visible)
        { PerfTabBtn.Background = AccentBg; PerfTabBtn.Foreground = Accent; }
    }

    // ── Tab Switching ───────────────────────────────────────
    void ThemeToggle_Click(object s, System.Windows.Input.MouseButtonEventArgs e) => CycleTheme();

    void StartupTab_Click(object s, RoutedEventArgs e)
    {
        StartupPanel.Visibility = Visibility.Visible;
        TimelinePanel.Visibility = Visibility.Collapsed;
        PerfPanel.Visibility = Visibility.Collapsed;
        StartupTabBtn.Background = AccentBg; StartupTabBtn.Foreground = Accent;
        TimelineTabBtn.Background = Brushes.Transparent; TimelineTabBtn.Foreground = Dim;
        PerfTabBtn.Background = Brushes.Transparent; PerfTabBtn.Foreground = Dim;
    }

    void TimelineTab_Click(object s, RoutedEventArgs e)
    {
        StartupPanel.Visibility = Visibility.Collapsed;
        TimelinePanel.Visibility = Visibility.Visible;
        PerfPanel.Visibility = Visibility.Collapsed;
        TimelineTabBtn.Background = AccentBg; TimelineTabBtn.Foreground = Accent;
        StartupTabBtn.Background = Brushes.Transparent; StartupTabBtn.Foreground = Dim;
        PerfTabBtn.Background = Brushes.Transparent; PerfTabBtn.Foreground = Dim;
        if (_startupData != null) RenderTimeline(_startupData);
    }

    void PerfTab_Click(object s, RoutedEventArgs e)
    {
        StartupPanel.Visibility = Visibility.Collapsed;
        TimelinePanel.Visibility = Visibility.Collapsed;
        PerfPanel.Visibility = Visibility.Visible;
        PerfTabBtn.Background = AccentBg; PerfTabBtn.Foreground = Accent;
        StartupTabBtn.Background = Brushes.Transparent; StartupTabBtn.Foreground = Dim;
        TimelineTabBtn.Background = Brushes.Transparent; TimelineTabBtn.Foreground = Dim;
        if (!_perfLoaded) _ = LoadAppUsage();
    }

    // ── Startup Loading ─────────────────────────────────────
    async Task LoadStartup()
    {
        StartupContent.Children.Clear();
        StartupContent.Children.Add(Loader("Scanning startup items — this may take a moment..."));
        _startupData = await Task.Run(() => _startup.Analyze());
        RenderStartup(_startupData);
    }

    void RenderStartup(StartupAnalysis data)
    {
        StartupContent.Children.Clear();

        // ── Controls row: Rescan, Search, Sort ──
        var controlsRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rescanBtn = MakeButton("↻  Re-scan", async (_, _) => await LoadStartup());
        rescanBtn.ToolTip = "Re-scan startup items (Ctrl+R)";
        Grid.SetColumn(rescanBtn, 0);

        // Search box
        var searchBorder = new Border
        {
            Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(10, 0, 10, 0), MinWidth = 220,
            ToolTip = "Search apps (Ctrl+F) · Clear with Escape",
        };
        var searchBox = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            CaretBrush = Text, Text = _searchQuery,
        };
        // Placeholder
        var placeholder = Txt("🔎  Search apps...", 13, Dim);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new Thickness(2, 0, 0, 0);
        placeholder.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
        var searchStack = new Grid();
        searchStack.Children.Add(placeholder);
        searchStack.Children.Add(searchBox);
        searchBorder.Child = searchStack;
        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            _searchQuery = searchBox.Text;
            _startupSearchDebounce?.Stop();
            _startupSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _startupSearchDebounce.Tick += (_, _) =>
            {
                _startupSearchDebounce.Stop();
                if (_startupData != null) RenderStartup(_startupData);
            };
            _startupSearchDebounce.Start();
        };
        searchBox.GotFocus += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        searchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(searchBox.Text)) placeholder.Visibility = Visibility.Visible;
        };
        Grid.SetColumn(searchBorder, 1);

        // Sort dropdown — custom dark-themed
        var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sortPanel.Children.Add(Txt("Sort by: ", 12, Dim));

        var sortOptions = new[] { "Impact", "Startup Time", "Name", "Status" };
        var sortBorder = new Border
        {
            Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
            Cursor = Cursors.Hand, MinWidth = 120,
        };
        var sortInner = new StackPanel { Orientation = Orientation.Horizontal };
        var sortLabel = Txt(_sortBy, 13, Text);
        var sortArrow = Txt(" ▾", 12, Dim);
        sortInner.Children.Add(sortLabel);
        sortInner.Children.Add(sortArrow);
        sortBorder.Child = sortInner;

        // Popup for sort options
        var sortPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = sortBorder,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        var popupBorder = new Border
        {
            Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
            Margin = new Thickness(0, 4, 0, 0), MinWidth = 140,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 12, Opacity = 0.5, ShadowDepth = 4,
            },
        };
        var popupStack = new StackPanel();
        foreach (var opt in sortOptions)
        {
            var optBorder = new Border
            {
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7), Cursor = Cursors.Hand,
            };
            var optText = Txt(opt, 13, opt == _sortBy ? Accent : Text);
            if (opt == _sortBy) optText.FontWeight = FontWeights.SemiBold;
            optBorder.Child = optText;
            optBorder.MouseEnter += (_, _) => optBorder.Background = B("#2e3347");
            optBorder.MouseLeave += (_, _) => optBorder.Background = Brushes.Transparent;
            var capturedOpt = opt;
            optBorder.MouseLeftButtonUp += (_, _) =>
            {
                _sortBy = capturedOpt;
                sortPopup.IsOpen = false;
                if (_startupData != null) RenderStartup(_startupData);
            };
            popupStack.Children.Add(optBorder);
        }
        popupBorder.Child = popupStack;
        sortPopup.Child = popupBorder;
        sortBorder.MouseLeftButtonUp += (_, _) => sortPopup.IsOpen = !sortPopup.IsOpen;
        sortPanel.Children.Add(sortBorder);
        sortPanel.Children.Add(sortPopup);
        Grid.SetColumn(sortPanel, 2);

        controlsRow.Children.Add(rescanBtn);
        controlsRow.Children.Add(searchBorder);
        controlsRow.Children.Add(sortPanel);
        StartupContent.Children.Add(controlsRow);

        // ── Filter chips row ──
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        filterRow.Children.Add(Txt("Filter: ", 12, Dim, margin: new Thickness(0, 0, 6, 0)));

        // Impact filter
        var impactOptions = new[] { "All", "High", "Medium", "Low" };
        foreach (var opt in impactOptions)
            filterRow.Children.Add(FilterChip(opt, _filterImpact, opt == "All" ? null : opt.ToLower() switch
            {
                "high" => Red, "medium" => Yellow, "low" => Green, _ => null
            }, v => { _filterImpact = v; if (_startupData != null) RenderStartup(_startupData); }));

        filterRow.Children.Add(new Border { Width = 1, Height = 18, Background = Bdr, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });

        // Status filter
        var statusOptions = new[] { "All", "Enabled", "Disabled" };
        foreach (var opt in statusOptions)
            filterRow.Children.Add(FilterChip(opt, _filterStatus, null,
                v => { _filterStatus = v; if (_startupData != null) RenderStartup(_startupData); }));

        filterRow.Children.Add(new Border { Width = 1, Height = 18, Background = Bdr, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });

        // Time filter
        var timeOptions = new[] { "All", "> 5s", "> 2s", "> 1s" };
        foreach (var opt in timeOptions)
            filterRow.Children.Add(FilterChip(opt, _filterTime, null,
                v => { _filterTime = v; if (_startupData != null) RenderStartup(_startupData); }));

        // Reset filters
        bool hasStartupFilters = _filterImpact != "All" || _filterStatus != "All" || _filterTime != "All" || !string.IsNullOrEmpty(_searchQuery);
        if (hasStartupFilters)
        {
            filterRow.Children.Add(new Border { Width = 16 });
            var resetBtn = new Border
            {
                Background = Brushes.Transparent, BorderBrush = Red, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            };
            resetBtn.Child = Txt("✕  Reset All", 12, Red, FontWeights.SemiBold);
            resetBtn.MouseEnter += (_, _) => { resetBtn.Background = Red; ((TextBlock)((Border)resetBtn).Child).Foreground = Bg; };
            resetBtn.MouseLeave += (_, _) => { resetBtn.Background = Brushes.Transparent; ((TextBlock)((Border)resetBtn).Child).Foreground = Red; };
            resetBtn.MouseLeftButtonUp += (_, _) =>
            {
                _filterImpact = "All"; _filterStatus = "All"; _filterTime = "All"; _searchQuery = "";
                if (_startupData != null) RenderStartup(_startupData);
            };
            filterRow.Children.Add(resetBtn);
        }

        StartupContent.Children.Add(filterRow);

        // Restore focus to search box if user was typing
        if (!string.IsNullOrEmpty(_searchQuery))
            searchBox.Dispatcher.BeginInvoke(() => { searchBox.Focus(); searchBox.CaretIndex = searchBox.Text.Length; });

        // ── Filter items ──
        var filteredItems = data.Items.AsEnumerable();
        if (!string.IsNullOrEmpty(_searchQuery))
        {
            var q = _searchQuery.ToLowerInvariant();
            filteredItems = filteredItems.Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        if (_filterImpact != "All")
            filteredItems = filteredItems.Where(i => i.Impact.Equals(_filterImpact, StringComparison.OrdinalIgnoreCase));
        if (_filterStatus == "Enabled")
            filteredItems = filteredItems.Where(i => i.IsEnabled);
        else if (_filterStatus == "Disabled")
            filteredItems = filteredItems.Where(i => !i.IsEnabled);
        if (_filterTime != "All")
        {
            long thresholdMs = _filterTime switch { "> 5s" => 5000, "> 2s" => 2000, _ => 1000 };
            filteredItems = filteredItems.Where(i => i.StartupTimeMs >= thresholdMs);
        }
        var itemsList = filteredItems.ToList();

        // ── Summary stats row ──
        var statsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        var s = data.Summary;
        statsGrid.Children.Add(StatCard("Total Items", s.Total.ToString(), "", Accent));
        statsGrid.Children.Add(StatCard("High Impact", s.High.ToString(), "Slowing boot significantly", s.High > 0 ? Red : Green));
        statsGrid.Children.Add(StatCard("Can Disable", s.CanDisable.ToString(), "Safe to turn off", s.CanDisable > 0 ? Yellow : Green));
        statsGrid.Children.Add(StatCard("Est. Savings", $"{s.EstimatedSavingsSec}s", "If you disable all suggestions", s.EstimatedSavingsSec > 5 ? Green : Accent));
        StartupContent.Children.Add(statsGrid);

        // ── Boot timeline ──
        var d = data.Diagnostics;
        if (d.Available && d.BootDurationMs > 0)
        {
            var card = Card();
            var stack = new StackPanel();
            stack.Children.Add(Txt("LAST BOOT TIMELINE", 11, Dim, FontWeights.SemiBold));
            stack.Children.Add(Txt($"{d.BootDurationMs / 1000.0:F1}s total boot time", 22, Text, FontWeights.Bold,
                new Thickness(0, 6, 0, 10)));

            var total = Math.Max(d.BootDurationMs, 1);
            var mainPct = Math.Clamp(d.MainPathMs * 100.0 / total, 5, 95);
            var barGrid = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mainPct, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - mainPct, GridUnitType.Star) });
            var mainSeg = new Border
            {
                Background = Accent, CornerRadius = new CornerRadius(4, 0, 0, 4),
                Child = Txt(Fmt(d.MainPathMs), 11, Brushes.White, FontWeights.SemiBold,
                    default, HorizontalAlignment.Center),
            };
            var postSeg = new Border
            {
                Background = Orange, CornerRadius = new CornerRadius(0, 4, 4, 0),
                Child = Txt(Fmt(d.PostBootMs), 11, Brushes.White, FontWeights.SemiBold,
                    default, HorizontalAlignment.Center),
            };
            Grid.SetColumn(mainSeg, 0); Grid.SetColumn(postSeg, 1);
            barGrid.Children.Add(mainSeg); barGrid.Children.Add(postSeg);
            stack.Children.Add(barGrid);

            var legend = new StackPanel { Orientation = Orientation.Horizontal };
            legend.Children.Add(LegendDot(Accent, "Core OS boot"));
            legend.Children.Add(LegendDot(Orange, "Post-boot (apps loading)"));
            stack.Children.Add(legend);

            card.Child = stack;
            StartupContent.Children.Add(card);
        }

        // ── Degrading apps ──
        if (d.DegradingApps.Count > 0)
        {
            var card = Card();
            var stack = new StackPanel();
            stack.Children.Add(Txt("APPS THAT SLOWED YOUR LAST BOOT", 11, Dim, FontWeights.SemiBold,
                new Thickness(0, 0, 0, 10)));
            var maxMs = Math.Max(d.DegradingApps.Max(a => a.TotalMs), 1);
            foreach (var app in d.DegradingApps.Take(10))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                var nameTxt = Txt(app.Name, 13, Text); Grid.SetColumn(nameTxt, 0);
                var timeTxt = Txt(Fmt(app.TotalMs), 13, Dim, align: HorizontalAlignment.Right);
                Grid.SetColumn(timeTxt, 1);
                var barTrack = new Border
                {
                    Background = Surface2, CornerRadius = new CornerRadius(3), Height = 6,
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                var barFill = new Border
                {
                    Background = Red, CornerRadius = new CornerRadius(3), Height = 6,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(4, app.TotalMs * 100.0 / maxMs),
                };
                barTrack.Child = barFill;
                Grid.SetColumn(barTrack, 2);
                row.Children.Add(nameTxt); row.Children.Add(timeTxt); row.Children.Add(barTrack);
                stack.Children.Add(row);
            }
            card.Child = stack;
            StartupContent.Children.Add(card);
        }

        // ── Timeline teaser card with "View Timeline →" button ──
        {
            var teaserCard = Card();
            var teaserStack = new StackPanel();
            var diag = data.Diagnostics;
            if (diag.Available && diag.BootDurationMs > 0)
            {
                teaserStack.Children.Add(Txt($"📊  Last boot: {diag.BootDurationMs / 1000.0:F1}s total", 14, Text, FontWeights.SemiBold));
                teaserStack.Children.Add(Txt("See when each app started loading after sign-in", 12, Dim,
                    margin: new Thickness(0, 4, 0, 0)));
            }
            else
            {
                teaserStack.Children.Add(Txt("📊  Startup Timeline", 14, Text, FontWeights.SemiBold));
                teaserStack.Children.Add(Txt("Visualize when each app started loading", 12, Dim,
                    margin: new Thickness(0, 4, 0, 0)));
            }

            var viewBtn = new Button
            {
                Content = "View Timeline →",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Accent, Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(1),
                BorderBrush = Accent, Background = AccentBg,
            };
            viewBtn.Template = CreateButtonTemplate(AccentBg);
            viewBtn.Click += (_, _) => TimelineTab_Click(this, new RoutedEventArgs());
            teaserStack.Children.Add(viewBtn);

            teaserCard.Child = teaserStack;
            StartupContent.Children.Add(teaserCard);
        }

        // ── Grouped items ──
        bool hasActiveFilter = !string.IsNullOrEmpty(_searchQuery)
            || _filterImpact != "All" || _filterStatus != "All" || _filterTime != "All";

        var groups = new (string Key, string Title, string Desc, Brush Badge, bool Expanded, bool? enabledFilter)[]
        {
            ("safe_to_disable", "✅  Safe to Disable", "These have no impact on system functionality", GreenBg, true, true),
            ("can_disable", "⚡  Consider Disabling", "Safe to disable but you may lose auto-start convenience", YellowBg, true, true),
            ("review", "🔍  Review", "Evaluate based on your personal usage", Surface2, hasActiveFilter, null),
            ("keep", "🔒  System Essential", "Critical components — do not disable", RedBg, hasActiveFilter, null),
            ("_disabled", "⊘  Already Disabled", "These are already disabled and won't start at boot", Surface2, false, false),
        };

        foreach (var (key, title, desc, badge, expanded, enabledFilter) in groups)
        {
            List<StartupItem> groupItems;
            if (key == "_disabled")
            {
                // Collect all disabled items regardless of action category
                groupItems = itemsList.Where(i => !i.IsEnabled).ToList();
            }
            else
            {
                groupItems = itemsList.Where(i => i.Action == key).ToList();
                // For safe_to_disable and can_disable, filter out already-disabled items
                if (enabledFilter == true)
                    groupItems = groupItems.Where(i => i.IsEnabled).ToList();
            }

            // Sort items within each group
            groupItems = _sortBy switch
            {
                "Startup Time" => groupItems.OrderByDescending(i => i.StartupTimeMs).ThenBy(i => i.Name).ToList(),
                "Name" => groupItems.OrderBy(i => i.Name).ToList(),
                "Status" => groupItems.OrderBy(i => i.IsEnabled ? 0 : 1).ThenByDescending(i => i.IsRunning ? 0 : 1).ToList(),
                _ /* Impact */ => groupItems
                    .OrderByDescending(i => i.Impact == "high" ? 3 : i.Impact == "medium" ? 2 : 1)
                    .ThenByDescending(i => i.StartupTimeMs)
                    .ToList(),
            };
            if (groupItems.Count == 0) continue;

            var groupCard = Card();
            var groupStack = new StackPanel();

            // Group header (clickable to collapse)
            var headerGrid = new Grid { Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleTxt = Txt(title, 15, Text, FontWeights.SemiBold); Grid.SetColumn(titleTxt, 0);
            var countBadge = new Border
            {
                Background = badge, CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 8, 0),
                Child = Txt(groupItems.Count.ToString(), 12, Text, FontWeights.SemiBold),
            };
            Grid.SetColumn(countBadge, 1);
            var descTxt = Txt(desc, 12, Dim); Grid.SetColumn(descTxt, 2);
            var chevron = Txt(expanded ? "▾" : "▸", 16, Dim);
            Grid.SetColumn(chevron, 3);

            headerGrid.Children.Add(titleTxt); headerGrid.Children.Add(countBadge);
            headerGrid.Children.Add(descTxt); headerGrid.Children.Add(chevron);

            var itemsPanel = new StackPanel { Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };

            headerGrid.MouseLeftButtonUp += (_, _) =>
            {
                if (itemsPanel.Visibility == Visibility.Visible)
                { itemsPanel.Visibility = Visibility.Collapsed; ((TextBlock)chevron).Text = "▸"; }
                else
                { itemsPanel.Visibility = Visibility.Visible; ((TextBlock)chevron).Text = "▾"; }
            };

            // Render each item
            foreach (var item in groupItems)
                itemsPanel.Children.Add(CreateItemCard(item));

            groupStack.Children.Add(headerGrid);
            groupStack.Children.Add(itemsPanel);
            groupCard.Child = groupStack;
            StartupContent.Children.Add(groupCard);
        }

        if (itemsList.Count == 0)
        {
            var msg = string.IsNullOrEmpty(_searchQuery)
                ? "No startup items found. Try running as Administrator for complete results."
                : $"No items matching \"{_searchQuery}\".";
            StartupContent.Children.Add(Txt(msg, 14, Dim, margin: new Thickness(0, 30, 0, 0),
                align: HorizontalAlignment.Center));
        }

        // ── Keyboard shortcuts help bar ──
        var shortcutsBar = new Border
        {
            Background = Surface, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 10, 0, 0),
        };
        var shortcutsPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+F", "Search"));
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+R", "Re-scan"));
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+1", "Startup"));
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+3", "Timeline"));
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+2", "App Usage"));
        shortcutsPanel.Children.Add(ShortcutHint("Ctrl+T", "Theme"));
        shortcutsPanel.Children.Add(ShortcutHint("Esc", "Clear search"));
        shortcutsPanel.Children.Add(ShortcutHint("Enter", "Expand item"));
        shortcutsBar.Child = shortcutsPanel;
        StartupContent.Children.Add(shortcutsBar);
    }

    // ── Startup Timeline (full-tab view) ────────────────────────
    void RenderTimeline(StartupAnalysis data)
    {
        TimelineContent.Children.Clear();

        var entries = new List<(string Name, long StartMs, long DurationMs, string Impact, string Source)>();
        var bootTimeUtc = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var d = data.Diagnostics;

        // Include ALL items — use best available timing
        foreach (var item in data.Items)
        {
            if (!item.IsEnabled) continue; // Skip disabled items

            long startMs, durationMs;

            if (item.HasBootOffset)
            {
                startMs = item.BootOffsetMs;
                durationMs = item.TimingSource switch
                {
                    "Measured" => Math.Max(item.StartupTimeMs, 500),
                    "Process" => item.Impact switch { "high" => 4000, "medium" => 2000, _ => 800 },
                    _ => item.StartupTimeMs > 0 ? Math.Max(item.StartupTimeMs, 500) : 1000,
                };
                if (startMs >= 0 && startMs <= 180_000)
                {
                    entries.Add((item.Name, startMs, Math.Max(durationMs, 300), item.Impact, item.TimingSource));
                    continue;
                }
            }

            if (item.TimingSource == "Process" && item.StartupTimeMs > 0 && item.StartupTimeMs <= 180_000)
            {
                startMs = item.StartupTimeMs;
                durationMs = item.Impact switch { "high" => 4000, "medium" => 2000, _ => 800 };
                entries.Add((item.Name, startMs, Math.Max(durationMs, 300), item.Impact, "Process"));
                continue;
            }

            // Estimated or no-timing items get synthetic offsets (added in Phase 2)
        }

        // Phase 2: Items without real timing data get synthetic offsets
        {
            long nextOffset = entries.Count > 0 ? entries.Max(e => e.StartMs + e.DurationMs) + 1000 : 5000;
            foreach (var item in data.Items
                .Where(i => i.IsEnabled && !entries.Any(e => e.Name == i.Name))
                .OrderByDescending(i => i.Impact == "high" ? 3 : i.Impact == "medium" ? 2 : 1)
                .ThenByDescending(i => i.StartupTimeMs))
            {
                var durationMs = item.StartupTimeMs > 0 ? Math.Max(item.StartupTimeMs, 500) : (item.Impact switch { "high" => 4000, "medium" => 1500, _ => 500 });
                entries.Add((item.Name, nextOffset, durationMs, item.Impact, item.TimingSource == "Estimated" ? "Estimated" : "No Data"));
                nextOffset += durationMs + 200;
            }
        }

        if (entries.Count == 0)
        {
            TimelineContent.Children.Add(Txt("No startup timing data available. Try running as Administrator for detailed boot diagnostics.",
                14, Dim, margin: new Thickness(0, 30, 0, 0), align: HorizontalAlignment.Center));
            return;
        }

        entries = entries.OrderBy(e => e.StartMs).ToList();

        long maxEndMs = entries.Max(e => e.StartMs + e.DurationMs);
        long totalMs = Math.Max(10000, ((maxEndMs / 5000) + 1) * 5000);

        // Dynamically calculate tick interval to prevent label overlap
        // First label is a clock time (~90px), rest are relative (~45px)
        double availableWidth = Math.Max(300, ActualWidth - 170 - 70 - 100);
        int maxVisibleTicks = Math.Max(2, (int)((availableWidth - 50) / 50));  // relative labels are ~45px
        int totalSeconds = (int)(totalMs / 1000);
        int[] possibleIntervals = { 5, 10, 15, 20, 30, 60, 120 };
        int tickIntervalSec = possibleIntervals[^1];
        foreach (var iv in possibleIntervals)
        {
            if (totalSeconds / iv <= maxVisibleTicks)
            {
                tickIntervalSec = iv;
                break;
            }
        }
        int tickIntervalMs = tickIntervalSec * 1000;
        int tickCount = Math.Max(1, (int)(totalMs / tickIntervalMs));

        // ── Header section ──
        var headerCard = Card();
        var headerStack = new StackPanel();
        headerStack.Children.Add(Txt("📊  STARTUP TIMELINE", 18, Accent, FontWeights.Bold));
        headerStack.Children.Add(Txt("When each app started loading after you signed in", 13, Dim,
            margin: new Thickness(0, 4, 0, 0)));

        // Stats row
        var statsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var highCount = entries.Count(e => e.Impact == "high");
        var medCount = entries.Count(e => e.Impact == "medium");

        void AddStatBadge(string label, string value, Brush color)
        {
            var badge = new Border
            {
                Background = Surface2, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0),
            };
            var st = new StackPanel();
            st.Children.Add(Txt(value, 20, color, FontWeights.Bold));
            st.Children.Add(Txt(label, 11, Dim));
            badge.Child = st;
            statsRow.Children.Add(badge);
        }

        AddStatBadge("Total Apps", entries.Count.ToString(), Accent);
        AddStatBadge("Desktop Ready", $"~{Fmt(maxEndMs)}", Green);
        if (highCount > 0) AddStatBadge("High Impact", highCount.ToString(), Red);
        if (medCount > 0) AddStatBadge("Medium Impact", medCount.ToString(), Orange);
        if (d.Available && d.BootDurationMs > 0)
            AddStatBadge("Total Boot", Fmt(d.BootDurationMs), Accent);

        headerStack.Children.Add(statsRow);
        headerCard.Child = headerStack;
        TimelineContent.Children.Add(headerCard);

        // ── Boot phase markers ──
        if (d.Available && d.BootDurationMs > 0)
        {
            var phaseCard = Card();
            var phaseStack = new StackPanel();
            phaseStack.Children.Add(Txt("BOOT PHASES", 11, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));

            var phaseBar = new Grid { Height = 36, Margin = new Thickness(0, 0, 0, 8) };
            var totalBoot = Math.Max(d.BootDurationMs, 1);
            var mainPct = Math.Clamp(d.MainPathMs * 100.0 / totalBoot, 5, 70);
            var postPct = Math.Clamp(d.PostBootMs * 100.0 / totalBoot, 5, 95 - mainPct);
            var restPct = Math.Max(5, 100 - mainPct - postPct);

            phaseBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mainPct, GridUnitType.Star) });
            phaseBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(postPct, GridUnitType.Star) });
            phaseBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(restPct, GridUnitType.Star) });

            var corePhase = new Border
            {
                Background = Accent, CornerRadius = new CornerRadius(6, 0, 0, 6),
                Child = Txt($"Core OS  {Fmt(d.MainPathMs)}", 11, Brushes.White, FontWeights.SemiBold,
                    align: HorizontalAlignment.Center),
            };
            var postPhase = new Border
            {
                Background = Orange,
                Child = Txt($"Apps Loading  {Fmt(d.PostBootMs)}", 11, Brushes.White, FontWeights.SemiBold,
                    align: HorizontalAlignment.Center),
            };
            var readyPhase = new Border
            {
                Background = Green, CornerRadius = new CornerRadius(0, 6, 6, 0),
                Child = Txt("✓ Ready", 11, Brushes.White, FontWeights.SemiBold,
                    align: HorizontalAlignment.Center),
            };
            Grid.SetColumn(corePhase, 0); Grid.SetColumn(postPhase, 1); Grid.SetColumn(readyPhase, 2);
            phaseBar.Children.Add(corePhase); phaseBar.Children.Add(postPhase); phaseBar.Children.Add(readyPhase);
            phaseStack.Children.Add(phaseBar);

            // Phase time markers
            var markers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            markers.Children.Add(Txt($"🔵 Sign-in started: {bootTimeUtc:h:mm:ss tt}", 12, Dim));
            markers.Children.Add(new Border { Width = 20 });
            markers.Children.Add(Txt($"🟠 Apps loading: {bootTimeUtc.AddMilliseconds(d.MainPathMs):h:mm:ss tt}", 12, Dim));
            markers.Children.Add(new Border { Width = 20 });
            markers.Children.Add(Txt($"🟢 Desktop ready: {bootTimeUtc.AddMilliseconds(d.BootDurationMs):h:mm:ss tt}", 12, Dim));
            phaseStack.Children.Add(markers);

            phaseCard.Child = phaseStack;
            TimelineContent.Children.Add(phaseCard);
        }

        // ── Gantt chart ──
        var chartCard = Card();
        var chartStack = new StackPanel();
        chartStack.Children.Add(Txt($"APP LAUNCH SEQUENCE  ·  {entries.Count} apps", 11, Dim, FontWeights.SemiBold,
            new Thickness(0, 0, 0, 12)));

        double nameW = 170, durW = 70;

        // ── Time axis labels (actual clock times) ──
        var axisRow = new Grid { Height = 18 };
        axisRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
        axisRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        axisRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(durW) });

        var axisLabels = new Grid();
        for (int i = 0; i < tickCount; i++)
            axisLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i <= tickCount; i++)
        {
            var offsetMs = i * tickIntervalMs;
            var clockTime = bootTimeUtc.AddMilliseconds(offsetMs);
            string timeStr;
            if (i == 0)
                timeStr = clockTime.ToString("h:mm:ss tt");
            else
            {
                var secs = offsetMs / 1000;
                if (secs >= 60 && secs % 60 == 0)
                    timeStr = $"+{secs / 60}m";
                else if (secs >= 60)
                    timeStr = $"+{secs / 60}m{secs % 60}s";
                else
                    timeStr = $"+{secs}s";
            }
            var lbl = Txt(timeStr, 9, Dim);
            if (i == 0)
            {
                lbl.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(lbl, 0);
            }
            else if (i == tickCount)
            {
                lbl.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(lbl, tickCount - 1);
            }
            else
            {
                lbl.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(lbl, i - 1);
                Grid.SetColumnSpan(lbl, 2);
            }
            axisLabels.Children.Add(lbl);
        }

        Grid.SetColumn(axisLabels, 1);
        axisRow.Children.Add(axisLabels);
        chartStack.Children.Add(axisRow);

        // ── Tick marks + baseline ──
        var tickRow = new Grid { Height = 8, Margin = new Thickness(0, 0, 0, 6) };
        tickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
        tickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(durW) });

        var tickArea = new Grid();
        for (int i = 0; i < tickCount; i++)
            tickArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var hLine = new Border { Background = Bdr, Height = 1, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumnSpan(hLine, tickCount);
        tickArea.Children.Add(hLine);

        for (int i = 0; i <= tickCount; i++)
        {
            var tick = new Border { Width = 1, Background = Bdr };
            if (i < tickCount)
            {
                Grid.SetColumn(tick, i);
                tick.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                Grid.SetColumn(tick, tickCount - 1);
                tick.HorizontalAlignment = HorizontalAlignment.Right;
            }
            tickArea.Children.Add(tick);
        }

        Grid.SetColumn(tickArea, 1);
        tickRow.Children.Add(tickArea);
        chartStack.Children.Add(tickRow);

        // ── App bars ──
        for (int idx = 0; idx < entries.Count; idx++)
        {
            var (name, startMs, durationMs, impact, source) = entries[idx];

            if (startMs + durationMs > totalMs)
                durationMs = totalMs - startMs;

            var row = new Grid { MinHeight = 28, Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(durW) });

            if (idx % 2 == 0)
            {
                var bg = new Border { Background = Surface2, CornerRadius = new CornerRadius(3), Opacity = 0.5 };
                Grid.SetColumnSpan(bg, 3);
                row.Children.Add(bg);
            }

            // App name + impact dot
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var impactDot = new Ellipse
            {
                Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0),
                Fill = impact switch { "high" => Red, "medium" => Orange, _ => Green },
            };
            namePanel.Children.Add(impactDot);
            var displayName = name.Length > 22 ? name[..20] + "…" : name;
            var nameLabel = Txt(displayName, 11, Text);
            if (name.Length > 22) nameLabel.ToolTip = name;
            namePanel.Children.Add(nameLabel);
            if (source is "Estimated" or "No Data")
            {
                var estBadge = Txt("~approx", 8, Dim, margin: new Thickness(4, 0, 0, 0));
                estBadge.FontStyle = FontStyles.Italic;
                namePanel.Children.Add(estBadge);
            }
            namePanel.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(namePanel, 0);
            row.Children.Add(namePanel);

            // Bar
            double beforeProp = Math.Max(0, (double)startMs / totalMs);
            double barProp = Math.Max(0.015, (double)durationMs / totalMs);
            double afterProp = Math.Max(0, 1.0 - beforeProp - barProp);

            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(beforeProp, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barProp, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(afterProp, GridUnitType.Star) });

            var barColor = impact switch { "high" => Red, "medium" => Orange, _ => Green };
            var startTime = bootTimeUtc.AddMilliseconds(startMs);
            var endTime = bootTimeUtc.AddMilliseconds(startMs + durationMs);
            var sourceLabel = source switch
            {
                "Measured" => "⏱ Measured from Windows boot diagnostics",
                "Process" => "📊 Detected from running process",
                "Estimated" => "📐 Estimated based on app type",
                "No Data" => "📐 Estimated — no exact timing available",
                _ => source,
            };
            var bar = new Border
            {
                Background = barColor, CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 4, 0, 4), MinWidth = 4,
                Opacity = source is "Estimated" or "No Data" ? 0.6 : 1.0,
                ToolTip = $"{name}\n{startTime:h:mm:ss tt} → {endTime:h:mm:ss tt}\nDuration: ~{Fmt(durationMs)}  ·  {impact} impact\n{sourceLabel}",
            };
            Grid.SetColumn(bar, 1);
            barGrid.Children.Add(bar);

            Grid.SetColumn(barGrid, 1);
            row.Children.Add(barGrid);

            // Time label
            var durLabel = Txt($"{startTime:h:mm:ss}", 10, Dim, align: HorizontalAlignment.Right);
            durLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(durLabel, 2);
            row.Children.Add(durLabel);

            chartStack.Children.Add(row);
        }

        // Desktop ready footer
        var readyTime = bootTimeUtc.AddMilliseconds(maxEndMs);
        var footerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        footerRow.Children.Add(Txt("🏁", 14));
        footerRow.Children.Add(Txt($"  Desktop ready at ~{readyTime:h:mm:ss tt} ({Fmt(maxEndMs)} after sign-in)", 13, Accent, FontWeights.SemiBold));
        chartStack.Children.Add(footerRow);

        // Legend
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        legend.Children.Add(LegendDot(Red, "High impact"));
        legend.Children.Add(LegendDot(Orange, "Medium"));
        legend.Children.Add(LegendDot(Green, "Low"));
        legend.Children.Add(new Border { Width = 15 });
        legend.Children.Add(Txt("· Faded bars = approximate timing", 11, Dim));
        legend.Children.Add(new Border { Width = 15 });
        legend.Children.Add(Txt("Hover bars for time details", 11, Dim));
        chartStack.Children.Add(legend);

        chartCard.Child = chartStack;
        TimelineContent.Children.Add(chartCard);
    }

    UIElement CreateItemCard(StartupItem item)
    {
        var isDisabled = !item.IsEnabled;
        var cardOpacity = isDisabled ? 0.55 : 1.0;

        var card = new Border
        {
            Background = Surface2, BorderBrush = isDisabled ? Brushes.Transparent : Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand,
            Opacity = cardOpacity,
        };

        var outer = new StackPanel();

        // ── Header row (always visible) ──
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: dot
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: disabled badge
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: name
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: time
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: impact badge
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 5: arrow

        // Running dot
        var dot = new Ellipse
        {
            Width = 7, Height = 7, Fill = item.IsRunning ? Green : Dim,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(dot, 0);

        // Disabled badge (prominent)
        UIElement? disabledBadge = null;
        if (isDisabled)
        {
            disabledBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 139, 143, 165)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt("DISABLED", 10, Dim, FontWeights.Bold),
            };
            Grid.SetColumn(disabledBadge, 1);
        }

        // Name + meta
        var nameStack = new StackPanel();
        var nameTxt = Txt(item.Name, 14, isDisabled ? Dim : Text, FontWeights.SemiBold);
        if (isDisabled) nameTxt.TextDecorations = TextDecorations.Strikethrough;
        nameStack.Children.Add(nameTxt);
        var metaLine = $"{item.Category}  ·  {item.Publisher}  ·  via {item.Source}";
        nameStack.Children.Add(Txt(metaLine, 12, Dim));
        Grid.SetColumn(nameStack, 2);

        // Startup time — always visible, prominent
        var timeStack = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 80,
        };
        if (item.HasStartupTime)
        {
            var timeColor = item.StartupTimeMs > 5000 ? Red
                : item.StartupTimeMs > 2000 ? Orange
                : item.StartupTimeMs > 1000 ? Yellow : Green;
            var isEstimated = item.TimingSource == "Estimated";
            var timeLabel = item.TimingSource switch
            {
                "Measured" => "Boot impact",
                "Process"  => $"Signed in + {Fmt(item.StartupTimeMs)}",
                "Estimated"=> "Approx. impact",
                _          => "Boot impact",
            };
            var displayTime = isEstimated ? $"~{Fmt(item.StartupTimeMs)}" : Fmt(item.StartupTimeMs);
            timeStack.Children.Add(Txt(displayTime, 16, isEstimated ? Dim : timeColor, FontWeights.Bold,
                align: HorizontalAlignment.Right));
            timeStack.Children.Add(Txt(timeLabel, 10, Dim,
                align: HorizontalAlignment.Right));
        }
        else
        {
            timeStack.Children.Add(Txt("—", 16, Dim, FontWeights.Bold,
                align: HorizontalAlignment.Right));
            timeStack.Children.Add(Txt(item.IsRunning ? "Not measured" : "Not running", 10, Dim,
                align: HorizontalAlignment.Right));
        }
        Grid.SetColumn(timeStack, 3);

        // Impact badge
        var (impText, impFg, impBg) = item.Impact switch
        {
            "high" => ("High Impact", Red, RedBg),
            "medium" => ("Medium", Yellow, YellowBg),
            _ => ("Low", Green, GreenBg),
        };
        var impactBadge = new Border
        {
            Background = impBg, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(impText, 11, impFg, FontWeights.SemiBold),
        };
        Grid.SetColumn(impactBadge, 4);

        // Expand arrow
        var arrow = Txt("▸", 14, Dim, margin: new Thickness(10, 0, 0, 0));
        Grid.SetColumn(arrow, 5);

        header.Children.Add(dot);
        if (disabledBadge != null) header.Children.Add(disabledBadge);
        header.Children.Add(nameStack);
        header.Children.Add(timeStack);
        header.Children.Add(impactBadge); header.Children.Add(arrow);

        // ── Detail panel (hidden by default) ──
        var detail = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(15, 10, 0, 4),
        };

        if (!string.IsNullOrEmpty(item.Description))
            detail.Children.Add(DetailRow("Description", item.Description));

        // What this app does (layman explanation)
        if (!string.IsNullOrEmpty(item.WhatItDoes))
        {
            var whatBox = new Border
            {
                Background = Surface, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
            };
            var whatStack = new StackPanel();
            whatStack.Children.Add(Txt("💡 WHAT THIS APP DOES", 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            whatStack.Children.Add(Txt(item.WhatItDoes, 13, Text));
            whatBox.Child = whatStack;
            detail.Children.Add(whatBox);
        }

        // What happens if disabled
        if (!string.IsNullOrEmpty(item.IfDisabled))
        {
            var ifBox = new Border
            {
                Background = item.Essential ? RedBg : AccentBg,
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = item.Essential ? Red : Accent,
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
            };
            var ifStack = new StackPanel();
            ifStack.Children.Add(Txt("IF YOU DISABLE IT", 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            ifStack.Children.Add(Txt(item.IfDisabled, 13, Text));
            ifBox.Child = ifStack;
            detail.Children.Add(ifBox);
        }

        // Why it's slow
        if (!string.IsNullOrEmpty(item.WhySlow))
        {
            var whyBox = new Border
            {
                Background = Surface, CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Orange,
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
            };
            var whyStack = new StackPanel();
            whyStack.Children.Add(Txt("🐢 WHY IT'S SLOW", 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            whyStack.Children.Add(Txt(item.WhySlow, 13, Text));
            whyBox.Child = whyStack;
            detail.Children.Add(whyBox);
        }

        // How to speed it up
        if (!string.IsNullOrEmpty(item.HowToSpeedUp))
        {
            var speedBox = new Border
            {
                Background = GreenBg, CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Green,
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
            };
            var speedStack = new StackPanel();
            speedStack.Children.Add(Txt("⚡ HOW TO SPEED IT UP", 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            speedStack.Children.Add(Txt(item.HowToSpeedUp, 13, Text));
            speedBox.Child = speedStack;
            detail.Children.Add(speedBox);
        }

        detail.Children.Add(DetailRow("Source", $"{item.Source} ({item.Scope})"));
        if (item.SizeMb > 0) detail.Children.Add(DetailRow("File Size", $"{item.SizeMb} MB"));
        var statusText = isDisabled
            ? "⊘ Disabled — won't start at boot"
            : item.IsRunning ? "● Running now" : "○ Not running (but enabled at boot)";
        detail.Children.Add(DetailRow("Status", statusText));
        if (item.HasStartupTime)
        {
            var timeDesc = item.TimingSource switch
            {
                "Measured"  => $"{Fmt(item.StartupTimeMs)} — slowed your boot by this much (from Windows diagnostics)",
                "Process"   => $"Started {Fmt(item.StartupTimeMs)} after sign-in — this is how long after you logged in before this process appeared",
                "Estimated" => $"~{Fmt(item.StartupTimeMs)} — approximate impact based on app type (run as Admin for exact measurements)",
                _           => Fmt(item.StartupTimeMs),
            };
            detail.Children.Add(DetailRow("Boot Time", timeDesc));
        }

        // Manual how-to
        var howTo = new Border
        {
            Background = Surface, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 6, 0, 0),
        };
        var howStack = new StackPanel();
        howStack.Children.Add(Txt("MANUAL STEPS", 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        howStack.Children.Add(Txt(item.HowToDisable, 12, Dim));
        howTo.Child = howStack;
        detail.Children.Add(howTo);

        // ── Disable Button ──
        if (!item.Essential && item.Action != "keep" && item.DisableMethod != DisableMethod.Unknown)
        {
            var disableBtn = new Button
            {
                Content = "🚫  Disable from Startup",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, Cursor = Cursors.Hand,
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(0),
            };
            disableBtn.Template = CreateButtonTemplate(
                B(item.Action == "safe_to_disable" ? "#16a34a" : "#d97706"));

            var capturedItem = item;
            disableBtn.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    $"Disable \"{capturedItem.Name}\" from starting automatically?\n\n{capturedItem.IfDisabled}",
                    "Confirm Disable", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                var (success, msg) = DisableService.Disable(capturedItem);
                if (success)
                {
                    MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = LoadStartup(); // Refresh
                }
                else
                {
                    MessageBox.Show(msg, "Could Not Disable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            detail.Children.Add(disableBtn);
        }

        outer.Children.Add(header);
        outer.Children.Add(detail);

        // Make card focusable for keyboard nav
        card.Focusable = true;
        card.FocusVisualStyle = null; // we'll handle our own focus ring
        var focusBrush = Accent;
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = focusBrush;
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = isDisabled ? Brushes.Transparent : Bdr;

        // Toggle expand/collapse
        void ToggleExpand()
        {
            if (detail.Visibility == Visibility.Visible)
            { detail.Visibility = Visibility.Collapsed; ((TextBlock)arrow).Text = "▸"; }
            else
            { detail.Visibility = Visibility.Visible; ((TextBlock)arrow).Text = "▾"; }
        }

        card.MouseLeftButtonUp += (_, e) => { ToggleExpand(); e.Handled = true; };
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            { ToggleExpand(); e.Handled = true; }
        };

        card.Child = outer;

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(card,
            $"{item.Name}, {item.Category}, {item.Impact} impact{(isDisabled ? ", disabled" : "")}");

        return card;
    }

    // ── App Usage Loading ──────────────────────────────────
    async Task LoadAppUsage()
    {
        PerfContent.Children.Clear();
        PerfContent.Children.Add(Loader("Analyzing app usage — sampling CPU for ~1 second..."));
        var startupItems = _startupData?.Items;
        var data = await Task.Run(() => _appUsage.GetAppUsage(startupItems));
        _perfLoaded = true;
        _lastAppUsageData = data;
        RenderAppUsage(data);
    }

    void RenderAppUsage(AppUsageData data)
    {
        PerfContent.Children.Clear();

        // ── Filter groups ──
        IEnumerable<AppGroup> filtered = data.Groups;

        // Search
        if (!string.IsNullOrWhiteSpace(_appSearch))
        {
            var q = _appSearch.ToLowerInvariant();
            filtered = filtered.Where(g =>
                g.FriendlyName.ToLower().Contains(q) ||
                (g.Publisher ?? "").ToLower().Contains(q) ||
                g.Processes.Any(p => p.Name.ToLower().Contains(q) ||
                    (p.FriendlyName ?? "").ToLower().Contains(q)));
        }

        // Memory filter
        filtered = _appFilterMem switch
        {
            ">1GB" => filtered.Where(g => g.TotalMemoryMb > 1024),
            ">500MB" => filtered.Where(g => g.TotalMemoryMb > 500),
            ">100MB" => filtered.Where(g => g.TotalMemoryMb > 100),
            _ => filtered,
        };

        // CPU filter
        filtered = _appFilterCpu switch
        {
            ">10%" => filtered.Where(g => g.TotalCpuPercent > 10),
            ">5%" => filtered.Where(g => g.TotalCpuPercent > 5),
            ">1%" => filtered.Where(g => g.TotalCpuPercent > 1),
            _ => filtered,
        };

        // Type filter
        filtered = _appFilterType switch
        {
            "Startup" => filtered.Where(g => g.IsStartupApp),
            "Non-Startup" => filtered.Where(g => !g.IsStartupApp),
            _ => filtered,
        };

        // ── Sort ──
        var groups = _appSortBy switch
        {
            "Name" => _appSortAsc ? filtered.OrderBy(g => g.FriendlyName).ToList()
                                  : filtered.OrderByDescending(g => g.FriendlyName).ToList(),
            "CPU" => _appSortAsc ? filtered.OrderBy(g => g.TotalCpuPercent).ToList()
                                 : filtered.OrderByDescending(g => g.TotalCpuPercent).ToList(),
            "CPUTime" => _appSortAsc ? filtered.OrderBy(g => g.TotalCpuTime).ToList()
                                     : filtered.OrderByDescending(g => g.TotalCpuTime).ToList(),
            "Duration" => _appSortAsc ? filtered.OrderBy(g => g.SessionDuration).ToList()
                                      : filtered.OrderByDescending(g => g.SessionDuration).ToList(),
            _ => _appSortAsc ? filtered.OrderBy(g => g.TotalMemoryMb).ToList()
                             : filtered.OrderByDescending(g => g.TotalMemoryMb).ToList(),
        };

        // ── Controls row: Refresh + Search ──
        var controls = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var refreshBtn = MakeButton("↻  Refresh", async (_, _) => await LoadAppUsage());
        refreshBtn.ToolTip = "Refresh app usage data";
        Grid.SetColumn(refreshBtn, 0);
        controls.Children.Add(refreshBtn);

        var searchBox = new TextBox
        {
            Text = _appSearch,
            FontSize = 13, Padding = new Thickness(10, 6, 10, 6),
            Background = Surface, Foreground = Text, BorderBrush = Bdr,
            BorderThickness = new Thickness(1),
        };
        searchBox.Resources.Add(SystemColors.HighlightBrushKey, Accent);
        bool isPlaceholder = string.IsNullOrEmpty(_appSearch);
        searchBox.Tag = isPlaceholder ? "placeholder" : null;
        if (isPlaceholder)
        {
            searchBox.Text = "🔍  Search apps...";
            searchBox.Foreground = Dim;
        }
        searchBox.GotFocus += (_, _) =>
        {
            if (searchBox.Tag as string == "placeholder" && searchBox.Text.StartsWith("🔍"))
            { searchBox.Text = ""; searchBox.Foreground = Text; searchBox.Tag = null; }
        };
        searchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(searchBox.Text))
            { searchBox.Text = "🔍  Search apps..."; searchBox.Foreground = Dim; searchBox.Tag = "placeholder"; }
        };
        searchBox.TextChanged += (_, _) =>
        {
            if (searchBox.Tag as string == "placeholder") return;
            _appSearch = searchBox.Text;
            _appSearchDebounce?.Stop();
            _appSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _appSearchDebounce.Tick += (_, _) =>
            {
                _appSearchDebounce.Stop();
                RenderAppUsage(data);
            };
            _appSearchDebounce.Start();
        };
        Grid.SetColumn(searchBox, 2);
        controls.Children.Add(searchBox);
        PerfContent.Children.Add(controls);

        // ── Filter chips ──
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        // Memory filter
        filterRow.Children.Add(Txt("Memory:", 11, Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", ">1GB", ">500MB", ">100MB" })
        {
            Brush? dot = opt switch { ">1GB" => Red, ">500MB" => Orange, ">100MB" => Yellow, _ => null };
            filterRow.Children.Add(FilterChip(opt, _appFilterMem, dot, v => { _appFilterMem = v; RenderAppUsage(data); }));
        }

        filterRow.Children.Add(new Border { Width = 16 }); // spacer

        // CPU filter
        filterRow.Children.Add(Txt("CPU:", 11, Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", ">10%", ">5%", ">1%" })
        {
            Brush? dot = opt switch { ">10%" => Red, ">5%" => Orange, ">1%" => Yellow, _ => null };
            filterRow.Children.Add(FilterChip(opt, _appFilterCpu, dot, v => { _appFilterCpu = v; RenderAppUsage(data); }));
        }

        filterRow.Children.Add(new Border { Width = 16 }); // spacer

        // Type filter
        filterRow.Children.Add(Txt("Type:", 11, Dim, FontWeights.SemiBold, new Thickness(0, 0, 6, 0)));
        foreach (var opt in new[] { "All", "Startup", "Non-Startup" })
        {
            Brush? dot = opt == "Startup" ? Accent : null;
            filterRow.Children.Add(FilterChip(opt, _appFilterType, dot, v => { _appFilterType = v; RenderAppUsage(data); }));
        }

        // Reset filters
        bool hasActiveFilters = _appFilterMem != "All" || _appFilterCpu != "All" || _appFilterType != "All" || !string.IsNullOrEmpty(_appSearch);
        if (hasActiveFilters)
        {
            filterRow.Children.Add(new Border { Width = 16 });
            var resetBtn = new Border
            {
                Background = Brushes.Transparent, BorderBrush = Red, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            };
            resetBtn.Child = Txt("✕  Reset All", 12, Red, FontWeights.SemiBold);
            resetBtn.MouseEnter += (_, _) => { resetBtn.Background = Red; ((TextBlock)((Border)resetBtn).Child).Foreground = Bg; };
            resetBtn.MouseLeave += (_, _) => { resetBtn.Background = Brushes.Transparent; ((TextBlock)((Border)resetBtn).Child).Foreground = Red; };
            resetBtn.MouseLeftButtonUp += (_, _) =>
            {
                _appFilterMem = "All"; _appFilterCpu = "All"; _appFilterType = "All"; _appSearch = "";
                RenderAppUsage(data);
            };
            filterRow.Children.Add(resetBtn);
        }

        PerfContent.Children.Add(filterRow);

        // ── Summary stats ──
        var memPct = data.TotalMemoryGb > 0 ? data.UsedMemoryGb / data.TotalMemoryGb * 100 : 0;
        var startupApps = data.Groups.Count(g => g.IsStartupApp);
        var statsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        statsGrid.Children.Add(StatCard("Showing", $"{groups.Count}", $"of {data.Groups.Count} app groups", Accent));
        statsGrid.Children.Add(StatCard("Memory Used", $"{data.UsedMemoryGb:F1} GB",
            $"of {data.TotalMemoryGb:F1} GB ({memPct:F0}%)", memPct > 85 ? Red : memPct > 60 ? Yellow : Green));
        statsGrid.Children.Add(StatCard("Startup Apps", startupApps.ToString(), "Running from boot", Accent));
        var topGroup = data.Groups.Count > 0 ? data.Groups.OrderByDescending(g => g.TotalMemoryMb).First() : null;
        statsGrid.Children.Add(StatCard("Top Consumer", topGroup?.FriendlyName ?? "—",
            topGroup != null ? $"{FmtMem(topGroup.TotalMemoryMb)}" : "", Orange));
        PerfContent.Children.Add(statsGrid);

        // Grouped app list
        var listCard = Card();
        var listStack = new StackPanel();
        listStack.Children.Add(Txt("ALL RUNNING APPLICATIONS", 11, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 10)));

        // Clickable column header — matches card padding (12px each side) + arrow column
        var headerBorder = new Border
        {
            Padding = new Thickness(12, 0, 12, 0), // match card's internal padding
            Margin = new Thickness(0, 0, 0, 0),
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); // arrow spacer

        void AddSortHeader(string label, string sortKey, int col, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            var indicator = _appSortBy == sortKey ? (_appSortAsc ? " ▲" : " ▼") : "";
            var isActive = _appSortBy == sortKey;
            var tb = Txt(label + indicator, 10, isActive ? Accent : Dim, FontWeights.SemiBold, align: align);
            var btn = new Border
            {
                Child = tb, Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 4, 4, 4),
                CornerRadius = new CornerRadius(4),
            };
            btn.MouseEnter += (_, _) => btn.Background = Surface;
            btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
            btn.MouseLeftButtonDown += (_, _) =>
            {
                if (_appSortBy == sortKey) _appSortAsc = !_appSortAsc;
                else { _appSortBy = sortKey; _appSortAsc = false; }
                RenderAppUsage(data);
            };
            btn.ToolTip = $"Sort by {label.ToLower()}";
            Grid.SetColumn(btn, col);
            headerGrid.Children.Add(btn);
        }

        AddSortHeader("APP", "Name", 0, HorizontalAlignment.Left);
        AddSortHeader("MEMORY", "Memory", 1);
        AddSortHeader("CPU %", "CPU", 2);
        AddSortHeader("CPU TIME", "CPUTime", 3);
        AddSortHeader("RUNNING FOR", "Duration", 4);

        headerBorder.Child = headerGrid;
        listStack.Children.Add(headerBorder);
        listStack.Children.Add(new Border { Background = Bdr, Height = 1, Margin = new Thickness(0, 4, 0, 4) });

        double totalMemMb = data.TotalMemoryGb * 1024;
        foreach (var group in groups)
            listStack.Children.Add(CreateAppGroupCard(group, totalMemMb));

        listCard.Child = listStack;
        PerfContent.Children.Add(listCard);
    }

    UIElement CreateAppGroupCard(AppGroup group, double totalMemMb)
    {
        bool isSingleProcess = group.Processes.Count == 1 && group.Processes[0].InstanceCount == 1;

        var card = new Border
        {
            Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand,
            Focusable = true, FocusVisualStyle = null,
        };
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = Accent;
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = Bdr;

        var outer = new StackPanel();

        // ── Group header row ──
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // memory
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // cpu%
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // cpu time
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // running for
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // arrow

        // Friendly name + badges + start time subtitle
        var nameOuter = new StackPanel();
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
        namePanel.Children.Add(Txt(group.FriendlyName, 13, Text, FontWeights.SemiBold));
        if (group.TotalInstances > 1)
        {
            namePanel.Children.Add(new Border
            {
                Background = Surface, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt($"{group.Processes.Count} processes · {group.TotalInstances} instances", 10, Dim),
            });
        }
        if (group.IsStartupApp)
        {
            namePanel.Children.Add(new Border
            {
                Background = AccentBg, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt("⚡Startup", 10, Accent),
            });
        }
        nameOuter.Children.Add(namePanel);
        // Subtitle: started at time
        var startedLabel = $"Started at {group.EarliestStart:h:mm tt}  ·  running for {FmtDuration(group.SessionDuration)}";
        nameOuter.Children.Add(Txt(startedLabel, 10.5, Dim, margin: new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(nameOuter, 0);

        // Memory
        var memStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var memVal = group.TotalMemoryMb;
        var memColor = memVal > 1000 ? Red : memVal > 300 ? Yellow : Text;
        memStack.Children.Add(Txt(FmtMem(memVal), 13, memColor, FontWeights.Medium,
            align: HorizontalAlignment.Right));
        var memPct = totalMemMb > 0 ? Math.Min(memVal / totalMemMb * 100, 100) : 0;
        var memBarTrack = new Border { Background = Surface, CornerRadius = new CornerRadius(2), Height = 3, Margin = new Thickness(0, 2, 0, 0) };
        memBarTrack.Child = new Border { Background = memColor, CornerRadius = new CornerRadius(2), Height = 3,
            HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, memPct * 0.8) };
        memStack.Children.Add(memBarTrack);
        Grid.SetColumn(memStack, 1);

        var cpuColor = group.TotalCpuPercent > 20 ? Red : group.TotalCpuPercent > 5 ? Yellow : Dim;
        var cpuTxt = Txt($"{group.TotalCpuPercent:F1}%", 13, cpuColor, align: HorizontalAlignment.Right);
        Grid.SetColumn(cpuTxt, 2);

        var cpuTimeTxt = Txt(FmtDuration(group.TotalCpuTime), 13, Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(cpuTimeTxt, 3);

        var durHours = group.SessionDuration.TotalHours;
        var durColor = durHours > 8 ? Orange : durHours > 2 ? Yellow : Green;
        var durationTxt = Txt(FmtDuration(group.SessionDuration), 13, durColor, FontWeights.Medium, align: HorizontalAlignment.Right);
        Grid.SetColumn(durationTxt, 4);

        var arrow = Txt("▸", 14, Dim, margin: new Thickness(8, 0, 0, 0));
        Grid.SetColumn(arrow, 5);

        header.Children.Add(nameOuter); header.Children.Add(memStack);
        header.Children.Add(cpuTxt); header.Children.Add(cpuTimeTxt);
        header.Children.Add(durationTxt); header.Children.Add(arrow);

        // ── Detail / child processes panel ──
        var detail = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 2) };

        // Group-level summary
        var summaryBox = new Border
        {
            Background = Surface, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 6),
        };
        var summaryStack = new StackPanel();
        if (!string.IsNullOrEmpty(group.Publisher))
            summaryStack.Children.Add(DetailRow("Publisher", group.Publisher));
        summaryStack.Children.Add(DetailRow("Total Memory", $"{FmtMem(group.TotalMemoryMb)} ({memPct:F1}% of system)"));
        summaryStack.Children.Add(DetailRow("CPU Usage", $"{group.TotalCpuPercent:F1}%"));
        summaryStack.Children.Add(DetailRow("Total CPU Time", FmtDuration(group.TotalCpuTime)));
        summaryStack.Children.Add(DetailRow("Running Since", group.EarliestStart.ToString("HH:mm:ss  ·  MMM dd")));
        summaryStack.Children.Add(DetailRow("Processes", $"{group.Processes.Count} processes, {group.TotalInstances} total instances"));
        summaryBox.Child = summaryStack;
        detail.Children.Add(summaryBox);

        // Individual processes
        if (group.Processes.Count > 1 || group.Processes[0].InstanceCount > 1)
        {
            detail.Children.Add(Txt("INDIVIDUAL PROCESSES", 10, Dim, FontWeights.SemiBold,
                new Thickness(0, 6, 0, 6)));

            foreach (var proc in group.Processes)
            {
                var procCard = new Border
                {
                    Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 3),
                };
                var procRow = new Grid();
                procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                procRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                var procNamePanel = new StackPanel { Orientation = Orientation.Horizontal };
                var displayName = !string.IsNullOrEmpty(proc.FriendlyName) && proc.FriendlyName != proc.Name
                    ? $"{proc.FriendlyName}  ({proc.Name})" : proc.Name;
                procNamePanel.Children.Add(Txt(displayName, 12, Text));
                if (proc.InstanceCount > 1)
                    procNamePanel.Children.Add(Txt($"  ×{proc.InstanceCount}", 10, Dim));
                Grid.SetColumn(procNamePanel, 0);

                var pMem = Txt(FmtMem(proc.MemoryMb), 12, Dim, align: HorizontalAlignment.Right);
                Grid.SetColumn(pMem, 1);
                var pCpu = Txt($"{proc.CpuPercent:F1}%", 12, Dim, align: HorizontalAlignment.Right);
                Grid.SetColumn(pCpu, 2);
                var pDur = Txt(FmtDuration(proc.SessionDuration), 12, Dim, align: HorizontalAlignment.Right);
                Grid.SetColumn(pDur, 3);

                procRow.Children.Add(procNamePanel); procRow.Children.Add(pMem);
                procRow.Children.Add(pCpu); procRow.Children.Add(pDur);
                procCard.Child = procRow;
                detail.Children.Add(procCard);
            }
        }
        else
        {
            // Single process — show path
            var proc = group.Processes[0];
            if (!string.IsNullOrEmpty(proc.ExePath))
                detail.Children.Add(DetailRow("Path", proc.ExePath));
        }

        outer.Children.Add(header);
        outer.Children.Add(detail);

        void ToggleExpand()
        {
            if (detail.Visibility == Visibility.Visible)
            { detail.Visibility = Visibility.Collapsed; ((TextBlock)arrow).Text = "▸"; }
            else
            { detail.Visibility = Visibility.Visible; ((TextBlock)arrow).Text = "▾"; }
        }

        card.MouseLeftButtonUp += (_, e) => { ToggleExpand(); e.Handled = true; };
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Space) { ToggleExpand(); e.Handled = true; }
        };

        card.Child = outer;
        System.Windows.Automation.AutomationProperties.SetName(card,
            $"{group.FriendlyName}, {FmtMem(group.TotalMemoryMb)} memory, {group.TotalCpuPercent:F1}% CPU, {group.Processes.Count} processes");
        return card;
    }

    static string FmtMem(double mb)
    {
        if (mb >= 1024) return $"{mb / 1024:F1} GB";
        if (mb >= 1) return $"{mb:F0} MB";
        return $"{mb * 1024:F0} KB";
    }

    static string FmtDuration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalSeconds}s";
    }

    // ══════════════════════════════════════════════════════════
    // ── UI Element Helpers ───────────────────────────────────
    // ══════════════════════════════════════════════════════════

    TextBlock Txt(string text, double size = 13, Brush? fg = null, FontWeight? weight = null,
        Thickness? margin = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg ?? Text,
            FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
        };
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }

    Border Card()
    {
        return new Border
        {
            Background = Surface, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(0, 0, 0, 14),
        };
    }

    Border StatCard(string label, string value, string sub, Brush color)
    {
        var card = new Border
        {
            Background = Surface, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 8, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(label.ToUpper(), 10, Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(value, 26, color, FontWeights.Bold));
        if (!string.IsNullOrEmpty(sub))
            stack.Children.Add(Txt(sub, 11, Dim, margin: new Thickness(0, 2, 0, 0)));
        card.Child = stack;
        return card;
    }

    UIElement DetailRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = Txt(label, 12, Dim); Grid.SetColumn(lbl, 0);
        var val = Txt(value, 12, Text); Grid.SetColumn(val, 1);
        grid.Children.Add(lbl); grid.Children.Add(val);
        return grid;
    }

    UIElement Loader(string msg)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 60),
        };
        stack.Children.Add(Txt("⏳", 28, align: HorizontalAlignment.Center));
        stack.Children.Add(Txt(msg, 14, Dim, margin: new Thickness(0, 10, 0, 0),
            align: HorizontalAlignment.Center));
        return stack;
    }

    UIElement LegendDot(Brush color, string label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(new Border
        {
            Background = color, Width = 10, Height = 10,
            CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(Txt(label, 12, Dim));
        return sp;
    }

    Border GaugeCard(string label, double pct, string sub)
    {
        pct = Math.Clamp(pct, 0, 100);
        var card = Card();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var color = pct < 60 ? Green : pct < 85 ? Yellow : Red;

        // Ring gauge using Arcs
        var size = 120.0;
        var strokeWidth = 10.0;
        var radius = (size - strokeWidth) / 2;
        var center = size / 2;

        var canvas = new Canvas { Width = size, Height = size, Margin = new Thickness(0, 0, 0, 8) };

        // Track circle
        var trackEllipse = new Ellipse
        {
            Width = size, Height = size,
            Stroke = Surface2, StrokeThickness = strokeWidth,
            Fill = Brushes.Transparent,
        };
        canvas.Children.Add(trackEllipse);

        // Fill arc
        if (pct > 0)
        {
            var angle = pct / 100.0 * 360;
            var isLargeArc = angle > 180;
            var radians = angle * Math.PI / 180;
            var endX = center + radius * Math.Sin(radians);
            var endY = center - radius * Math.Cos(radians);

            var pathFig = new PathFigure { StartPoint = new Point(center, center - radius) };
            pathFig.Segments.Add(new ArcSegment
            {
                Point = pct >= 99.9 ? new Point(center - 0.01, center - radius) : new Point(endX, endY),
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise,
            });
            var pathGeo = new PathGeometry();
            pathGeo.Figures.Add(pathFig);
            var path = new System.Windows.Shapes.Path
            {
                Data = pathGeo, Stroke = color, StrokeThickness = strokeWidth,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            canvas.Children.Add(path);
        }

        // Center text
        var pctText = Txt($"{Math.Round(pct)}%", 26, color, FontWeights.Bold);
        Canvas.SetLeft(pctText, center - 28);
        Canvas.SetTop(pctText, center - 16);
        canvas.Children.Add(pctText);

        stack.Children.Add(canvas);
        stack.Children.Add(Txt(label, 13, Dim, align: HorizontalAlignment.Center));
        stack.Children.Add(Txt(sub, 12, Dim, margin: new Thickness(0, 2, 0, 0),
            align: HorizontalAlignment.Center));

        card.Child = stack;
        return card;
    }

    Grid ProcRow(string name, string pid, string mem, string memPct, string status, bool isHeader)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var weight = isHeader ? FontWeights.SemiBold : FontWeights.Normal;
        var fg = isHeader ? Dim : Text;
        var sz = isHeader ? 10.0 : 13.0;

        var c0 = Txt(isHeader ? name.ToUpper() : name, sz, fg, weight); Grid.SetColumn(c0, 0);
        var c1 = Txt(pid, sz, Dim); Grid.SetColumn(c1, 1);
        var c2 = Txt(mem, sz, fg, align: HorizontalAlignment.Right); Grid.SetColumn(c2, 2);
        var c3 = Txt(memPct, sz, fg, align: HorizontalAlignment.Right); Grid.SetColumn(c3, 3);
        var statusColor = status.Contains("Running") ? Green : Red;
        var c4 = Txt(status, sz, statusColor); Grid.SetColumn(c4, 4); c4.Margin = new Thickness(10, 0, 0, 0);

        grid.Children.Add(c0); grid.Children.Add(c1);
        grid.Children.Add(c2); grid.Children.Add(c3); grid.Children.Add(c4);
        return grid;
    }

    Button MakeButton(string text, RoutedEventHandler? click = null)
    {
        var btn = new Button
        {
            Content = text, FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = Text, Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
        };
        btn.Template = CreateButtonTemplate(Surface2);
        if (click != null) btn.Click += click;
        return btn;
    }

    ControlTemplate CreateButtonTemplate(Brush bg)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, bg);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(16, 8, 16, 8));
        border.SetValue(Border.BorderBrushProperty, Bdr);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        return template;
    }

    string Fmt(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        var totalSec = ms / 1000.0;
        if (totalSec < 60) return $"{totalSec:F1}s";
        var min = (int)(totalSec / 60);
        var sec = (int)(totalSec % 60);
        return sec > 0 ? $"{min}m {sec}s" : $"{min}m";
    }

    UIElement FilterChip(string label, string currentValue, Brush? dotColor, Action<string> onSelect)
    {
        bool isActive = label == currentValue;
        var chip = new Border
        {
            Background = isActive ? AccentBg : Brushes.Transparent,
            BorderBrush = isActive ? Accent : Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (dotColor != null && label != "All")
        {
            inner.Children.Add(new Ellipse
            {
                Width = 7, Height = 7, Fill = dotColor,
                Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        }
        inner.Children.Add(Txt(label, 12, isActive ? Accent : Dim, isActive ? FontWeights.SemiBold : FontWeights.Normal));
        chip.Child = inner;
        chip.MouseEnter += (_, _) => { if (!isActive) chip.Background = Surface; };
        chip.MouseLeave += (_, _) => { if (!isActive) chip.Background = Brushes.Transparent; };
        chip.MouseLeftButtonUp += (_, _) => onSelect(label);
        return chip;
    }

    UIElement ShortcutHint(string key, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var keyBadge = new Border
        {
            Background = Surface2, BorderBrush = Bdr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 4, 0),
            Child = Txt(key, 10, Dim, FontWeights.SemiBold),
        };
        panel.Children.Add(keyBadge);
        panel.Children.Add(Txt(label, 11, Dim));
        return panel;
    }
}
