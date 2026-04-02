using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ColdStart.Helpers;
using ColdStart.Models;
using ColdStart.ViewModels;
using static ColdStart.Helpers.FormatHelper;
using static ColdStart.Helpers.UiHelper;

namespace ColdStart.Views.Controls;

/// <summary>
/// Displays startup analysis with filtering, sorting, and grouped item cards.
/// Subscribes to <see cref="StartupViewModel.DataChanged"/> and <see cref="MainViewModel.ThemeChanged"/>
/// to stay in sync with data and theme changes.
/// </summary>
public partial class StartupView : UserControl
{
    private StartupViewModel? _vm;
    private MainViewModel? _mainVm;
    private DispatcherTimer? _searchDebounce;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupView"/> control.
    /// </summary>
    public StartupView()
    {
        InitializeComponent();
    }

    private ThemeManager Theme => _mainVm!.Theme;

    /// <summary>Raised when the user clicks "View Timeline →".</summary>
    public event Action? TimelineRequested;

    /// <summary>Raised when the user clicks the Re-scan button.</summary>
    public event Action? RescanRequested;

    /// <summary>
    /// Wires the view to its view models and subscribes to data and theme change events.
    /// </summary>
    /// <param name="vm">The startup view model supplying analysis data.</param>
    /// <param name="mainVm">The main view model supplying the shared theme.</param>
    public void Initialize(StartupViewModel vm, MainViewModel mainVm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(mainVm);

        _vm = vm;
        _mainVm = mainVm;

        _vm.DataChanged += Render;
        _mainVm.ThemeChanged += Render;
    }

    /// <summary>
    /// Shows a loading spinner while startup data is being scanned.
    /// </summary>
    public void ShowLoading()
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(Loader(Theme, "Scanning startup items — this may take a moment..."));
    }

    /// <summary>
    /// Rebuilds the entire UI from the current view model state.
    /// </summary>
    public void Render()
    {
        if (_vm?.AnalysisData is not { } data) return;

        ContentPanel.Children.Clear();
        RenderControlsRow();
        RenderFilterChips();
        RestoreSearchFocus();
        RenderSummaryStats(data);
        RenderBootTimeline(data);
        RenderDegradingApps(data);
        RenderTimelineTeaser(data);
        RenderGroupedItems();
        RenderEmptyMessage();
        RenderTimingLegend();
        RenderShortcutsBar();
    }

    // ── Controls row: Re-scan, Search, Sort ──────────────────

    private void RenderControlsRow()
    {
        var controlsRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rescanBtn = MakeButton(Theme, "↻  Re-scan", (_, _) => RescanRequested?.Invoke());
        rescanBtn.ToolTip = "Re-scan startup items (Ctrl+R)";
        Grid.SetColumn(rescanBtn, 0);

        var searchBorder = BuildSearchBox();
        Grid.SetColumn(searchBorder, 1);

        var sortPanel = BuildSortDropdown();
        Grid.SetColumn(sortPanel, 2);

        controlsRow.Children.Add(rescanBtn);
        controlsRow.Children.Add(searchBorder);
        controlsRow.Children.Add(sortPanel);
        ContentPanel.Children.Add(controlsRow);
    }

    private Border BuildSearchBox()
    {
        var border = new Border
        {
            Background = Theme.Surface2, BorderBrush = Theme.Bdr,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(10, 0, 10, 0),
            MinWidth = 220,
            ToolTip = "Search apps (Ctrl+F) · Clear with Escape",
        };

        var searchBox = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Theme.Text, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            CaretBrush = Theme.Text, Text = _vm!.SearchQuery,
            Padding = new Thickness(0, 0, 18, 0),
        };

        var placeholder = Txt(Theme, "🔎  Search apps...", 13, Theme.Dim);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new Thickness(2, 0, 0, 0);
        placeholder.Visibility = string.IsNullOrEmpty(_vm.SearchQuery)
            ? Visibility.Visible : Visibility.Collapsed;

        var clearBtn = new TextBlock
        {
            Text = "✕", FontSize = 13, Foreground = Theme.Dim,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = string.IsNullOrEmpty(_vm.SearchQuery)
                ? Visibility.Collapsed : Visibility.Visible,
        };
        clearBtn.MouseEnter += (_, _) => clearBtn.Foreground = Theme.Text;
        clearBtn.MouseLeave += (_, _) => clearBtn.Foreground = Theme.Dim;
        clearBtn.MouseLeftButtonDown += (_, _) =>
        {
            _searchDebounce?.Stop();
            _vm!.SearchQuery = "";
            searchBox.Text = "";
            searchBox.Focus();
        };

        var grid = new Grid();
        grid.Children.Add(placeholder);
        grid.Children.Add(searchBox);
        grid.Children.Add(clearBtn);
        border.Child = grid;

        WireSearchEvents(searchBox, placeholder, clearBtn);
        return border;
    }

    private void WireSearchEvents(TextBox searchBox, TextBlock placeholder, TextBlock clearBtn)
    {
        searchBox.TextChanged += (_, _) =>
        {
            var hasText = !string.IsNullOrEmpty(searchBox.Text);
            placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            clearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

            _searchDebounce?.Stop();
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                _vm!.SearchQuery = searchBox.Text;
            };
            _searchDebounce.Start();
        };

        searchBox.GotFocus += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        searchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(searchBox.Text))
            {
                _searchDebounce?.Stop();
                _vm!.SearchQuery = "";
                placeholder.Visibility = Visibility.Visible;
                clearBtn.Visibility = Visibility.Collapsed;
            }
        };
    }

    private UIElement BuildSortDropdown()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Txt(Theme, "Sort by: ", 12, Theme.Dim));

        var sortBorder = BuildSortToggle();
        var sortPopup = BuildSortPopup(sortBorder);

        sortBorder.MouseLeftButtonUp += (_, _) => sortPopup.IsOpen = !sortPopup.IsOpen;
        panel.Children.Add(sortBorder);
        panel.Children.Add(sortPopup);
        return panel;
    }

    private Border BuildSortToggle()
    {
        var border = new Border
        {
            Background = Theme.Surface2, BorderBrush = Theme.Bdr,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6), Cursor = Cursors.Hand, MinWidth = 120,
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(Txt(Theme, _vm!.SortBy, 13, Theme.Text));
        inner.Children.Add(Txt(Theme, " ▾", 12, Theme.Dim));
        border.Child = inner;
        return border;
    }

    private Popup BuildSortPopup(Border placementTarget)
    {
        var popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
        };

        var border = new Border
        {
            Background = Theme.Surface2, BorderBrush = Theme.Bdr,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 0), MinWidth = 140,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 12, Opacity = 0.5, ShadowDepth = 4,
            },
        };

        var stack = new StackPanel();
        foreach (var opt in new[] { "Impact", "Startup Time", "Name", "Status" })
            stack.Children.Add(BuildSortOption(opt, popup));
        border.Child = stack;
        popup.Child = border;
        return popup;
    }

    private Border BuildSortOption(string option, Popup popup)
    {
        bool isSelected = option == _vm!.SortBy;
        var border = new Border
        {
            Background = Brushes.Transparent, CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7), Cursor = Cursors.Hand,
        };
        var text = Txt(Theme, option, 13, isSelected ? Theme.Accent : Theme.Text);
        if (isSelected) text.FontWeight = FontWeights.SemiBold;
        border.Child = text;

        border.MouseEnter += (_, _) => border.Background = ThemeManager.B("#2e3347");
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) =>
        {
            _vm!.SortBy = option;
            popup.IsOpen = false;
        };
        return border;
    }

    // ── Filter chips ─────────────────────────────────────────

    private void RenderFilterChips()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(Txt(Theme, "Filter: ", 12, Theme.Dim, margin: new Thickness(0, 0, 6, 0)));

        AddImpactChips(row);
        AddChipSeparator(row);
        AddStatusChips(row);
        AddChipSeparator(row);
        AddTimeChips(row);
        AddChipSeparator(row);
        AddSourceChips(row);
        AddResetButton(row);

        ContentPanel.Children.Add(row);
    }

    private void AddImpactChips(WrapPanel row)
    {
        foreach (var opt in new[] { "All", "High", "Medium", "Low" })
        {
            Brush? dot = opt.ToLowerInvariant() switch
            {
                "high" => Theme.Red, "medium" => Theme.Yellow, "low" => Theme.Green, _ => null,
            };
            row.Children.Add(FilterChip(Theme, opt, _vm!.FilterImpact, dot,
                v => _vm.FilterImpact = v));
        }
    }

    private void AddStatusChips(WrapPanel row)
    {
        foreach (var opt in new[] { "All", "Enabled", "Disabled" })
            row.Children.Add(FilterChip(Theme, opt, _vm!.FilterStatus, null,
                v => _vm.FilterStatus = v));
    }

    private void AddTimeChips(WrapPanel row)
    {
        foreach (var opt in new[] { "All", "> 5s", "> 2s", "> 1s" })
            row.Children.Add(FilterChip(Theme, opt, _vm!.FilterTime, null,
                v => _vm.FilterTime = v));
    }

    private void AddSourceChips(WrapPanel row)
    {
        foreach (var (opt, dot) in new (string, Brush?)[]
        {
            ("All", null),
            ("Measured", Theme.Green),
            ("Process", Theme.Accent),
            ("Estimated", Theme.Dim),
        })
        {
            row.Children.Add(FilterChip(Theme, opt, _vm!.FilterSource, dot,
                v => _vm.FilterSource = v));
        }
    }

    private void AddResetButton(WrapPanel row)
    {
        if (!_vm!.HasActiveFilters) return;

        row.Children.Add(new Border { Width = 16 });
        var btn = new Border
        {
            Background = Brushes.Transparent, BorderBrush = Theme.Red,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
        };
        btn.Child = Txt(Theme, "✕  Reset All", 12, Theme.Red, FontWeights.SemiBold);

        btn.MouseEnter += (_, _) =>
        {
            btn.Background = Theme.Red;
            ((TextBlock)btn.Child).Foreground = Theme.Bg;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = Brushes.Transparent;
            ((TextBlock)btn.Child).Foreground = Theme.Red;
        };
        btn.MouseLeftButtonUp += (_, _) => _vm.ResetFiltersCommand.Execute(null);
        row.Children.Add(btn);
    }

    private void AddChipSeparator(WrapPanel row)
    {
        row.Children.Add(new Border
        {
            Width = 1, Height = 18, Background = Theme.Bdr,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private void RestoreSearchFocus()
    {
        if (!string.IsNullOrEmpty(_vm!.SearchQuery))
        {
            Dispatcher.BeginInvoke(() =>
            {
                var tb = FindSearchTextBox();
                if (tb is null) return;
                tb.Focus();
                tb.CaretIndex = tb.Text.Length;
            });
        }
    }

    private TextBox? FindSearchTextBox()
    {
        if (ContentPanel.Children.Count == 0) return null;
        if (ContentPanel.Children[0] is not Grid controlsRow) return null;
        foreach (UIElement child in controlsRow.Children)
        {
            if (Grid.GetColumn(child) != 1 || child is not Border border) continue;
            if (border.Child is Grid g && g.Children.Count >= 2 && g.Children[1] is TextBox tb)
                return tb;
        }
        return null;
    }

    // ── Summary stats ────────────────────────────────────────

    private void RenderSummaryStats(StartupAnalysis data)
    {
        var s = data.Summary;
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        grid.Children.Add(StatCard(Theme, "Total Items", s.Total.ToString(), "", Theme.Accent));
        grid.Children.Add(StatCard(Theme, "High Impact", s.High.ToString(),
            "Slowing boot significantly", s.High > 0 ? Theme.Red : Theme.Green));
        grid.Children.Add(StatCard(Theme, "Can Disable", s.CanDisable.ToString(),
            "Safe to turn off", s.CanDisable > 0 ? Theme.Yellow : Theme.Green));
        grid.Children.Add(StatCard(Theme, "Est. Savings", Fmt((long)(s.EstimatedSavingsSec * 1000)),
            "If you disable all suggestions", s.EstimatedSavingsSec > 5 ? Theme.Green : Theme.Accent));
        ContentPanel.Children.Add(grid);
    }

    // ── Boot timeline bar ────────────────────────────────────

    private void RenderBootTimeline(StartupAnalysis data)
    {
        var d = data.Diagnostics;
        if (!d.Available || d.BootDurationMs <= 0) return;

        var card = Card(Theme);
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "LAST BOOT TIMELINE", 11, Theme.Dim, FontWeights.SemiBold));
        stack.Children.Add(Txt(Theme, $"{d.BootDurationMs / 1000.0:F1}s total boot time", 22,
            Theme.Text, FontWeights.Bold, new Thickness(0, 6, 0, 10)));

        stack.Children.Add(BuildBootBar(d));
        stack.Children.Add(BuildBootLegend());

        card.Child = stack;
        ContentPanel.Children.Add(card);
    }

    private Grid BuildBootBar(BootDiagnostics d)
    {
        var total = Math.Max(d.BootDurationMs, 1);
        var mainPct = Math.Clamp(d.MainPathMs * 100.0 / total, 5, 95);

        var bar = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mainPct, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - mainPct, GridUnitType.Star) });

        var mainSeg = new Border
        {
            Background = Theme.Accent, CornerRadius = new CornerRadius(4, 0, 0, 4),
            Child = Txt(Theme, Fmt(d.MainPathMs), 11, Brushes.White, FontWeights.SemiBold,
                align: HorizontalAlignment.Center),
        };
        var postSeg = new Border
        {
            Background = Theme.Orange, CornerRadius = new CornerRadius(0, 4, 4, 0),
            Child = Txt(Theme, Fmt(d.PostBootMs), 11, Brushes.White, FontWeights.SemiBold,
                align: HorizontalAlignment.Center),
        };

        Grid.SetColumn(mainSeg, 0);
        Grid.SetColumn(postSeg, 1);
        bar.Children.Add(mainSeg);
        bar.Children.Add(postSeg);
        return bar;
    }

    private StackPanel BuildBootLegend()
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(LegendDot(Theme, Theme.Accent, "Core OS boot"));
        legend.Children.Add(LegendDot(Theme, Theme.Orange, "Post-boot (apps loading)"));
        return legend;
    }

    // ── Degrading apps ───────────────────────────────────────

    private void RenderDegradingApps(StartupAnalysis data)
    {
        var apps = data.Diagnostics.DegradingApps;
        if (apps.Count == 0) return;

        var card = Card(Theme);
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "APPS THAT SLOWED YOUR LAST BOOT", 11,
            Theme.Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 10)));

        var maxMs = Math.Max(apps.Max(a => a.TotalMs), 1);
        foreach (var app in apps.Take(10))
            stack.Children.Add(BuildDegradingRow(app, maxMs));

        card.Child = stack;
        ContentPanel.Children.Add(card);
    }

    private Grid BuildDegradingRow(DegradingApp app, long maxMs)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var name = Txt(Theme, app.Name, 13, Theme.Text);
        Grid.SetColumn(name, 0);

        var time = Txt(Theme, Fmt(app.TotalMs), 13, Theme.Dim, align: HorizontalAlignment.Right);
        Grid.SetColumn(time, 1);

        var track = new Border
        {
            Background = Theme.Surface2, CornerRadius = new CornerRadius(3), Height = 6,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        track.Child = new Border
        {
            Background = Theme.Red, CornerRadius = new CornerRadius(3), Height = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(4, app.TotalMs * 100.0 / maxMs),
        };
        Grid.SetColumn(track, 2);

        row.Children.Add(name);
        row.Children.Add(time);
        row.Children.Add(track);
        return row;
    }

    // ── Timeline teaser ──────────────────────────────────────

    private void RenderTimelineTeaser(StartupAnalysis data)
    {
        var card = Card(Theme);
        var stack = new StackPanel();
        var diag = data.Diagnostics;

        if (diag.Available && diag.BootDurationMs > 0)
        {
            stack.Children.Add(Txt(Theme, $"📊  Last boot: {diag.BootDurationMs / 1000.0:F1}s total",
                14, Theme.Text, FontWeights.SemiBold));
            stack.Children.Add(Txt(Theme, "See when each app started loading after sign-in",
                12, Theme.Dim, margin: new Thickness(0, 4, 0, 0)));
        }
        else
        {
            stack.Children.Add(Txt(Theme, "📊  Startup Timeline", 14, Theme.Text, FontWeights.SemiBold));
            stack.Children.Add(Txt(Theme, "Visualize when each app started loading",
                12, Theme.Dim, margin: new Thickness(0, 4, 0, 0)));
        }

        stack.Children.Add(BuildTimelineButton());
        card.Child = stack;
        ContentPanel.Children.Add(card);
    }

    private Button BuildTimelineButton()
    {
        var btn = new Button
        {
            Content = "View Timeline →",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Accent, Cursor = Cursors.Hand,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Accent, Background = Theme.AccentBg,
        };
        btn.Template = CreateButtonTemplate(Theme, Theme.AccentBg);
        btn.Click += (_, _) => TimelineRequested?.Invoke();
        return btn;
    }

    // ── Grouped items ────────────────────────────────────────

    private void RenderGroupedItems()
    {
        foreach (var group in _vm!.FilteredGroups)
        {
            if (group.Items.Count == 0) continue;
            ContentPanel.Children.Add(BuildGroupCard(group));
        }
    }

    private Border BuildGroupCard(StartupItemGroup group)
    {
        var card = Card(Theme);
        var stack = new StackPanel();

        var itemsPanel = new StackPanel
        {
            Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        var chevron = Txt(Theme, group.IsExpanded ? "▾" : "▸", 16, Theme.Dim);

        stack.Children.Add(BuildGroupHeader(group, itemsPanel, chevron));
        foreach (var item in group.Items)
            itemsPanel.Children.Add(CreateItemCard(item));
        stack.Children.Add(itemsPanel);

        card.Child = stack;
        return card;
    }

    private Grid BuildGroupHeader(StartupItemGroup group, StackPanel itemsPanel, TextBlock chevron)
    {
        var header = new Grid { Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Txt(Theme, group.Title, 15, Theme.Text, FontWeights.SemiBold);
        Grid.SetColumn(title, 0);

        var badge = new Border
        {
            Background = ThemeManager.B(group.BadgeColor),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 8, 0),
            Child = Txt(Theme, group.Items.Count.ToString(), 12, Theme.Text, FontWeights.SemiBold),
        };
        Grid.SetColumn(badge, 1);

        var desc = Txt(Theme, group.Description, 12, Theme.Dim);
        Grid.SetColumn(desc, 2);
        Grid.SetColumn(chevron, 3);

        header.Children.Add(title);
        header.Children.Add(badge);
        header.Children.Add(desc);
        header.Children.Add(chevron);

        header.MouseLeftButtonUp += (_, _) => TogglePanel(itemsPanel, chevron);
        return header;
    }

    // ── Empty message ────────────────────────────────────────

    private void RenderEmptyMessage()
    {
        var allEmpty = _vm!.FilteredGroups.All(g => g.Items.Count == 0);
        if (!allEmpty) return;

        var msg = string.IsNullOrEmpty(_vm.SearchQuery)
            ? "No startup items found. Try running as Administrator for complete results."
            : $"No items matching \"{_vm.SearchQuery}\".";
        ContentPanel.Children.Add(Txt(Theme, msg, 14, Theme.Dim,
            margin: new Thickness(0, 30, 0, 0), align: HorizontalAlignment.Center));
    }

    // ── Timing source badge ─────────────────────────────────

    private Border BuildTimingSourceBadge(string source)
    {
        var (label, fg, bg) = source switch
        {
            "Measured"  => ("M", Theme.Green, Theme.GreenBg),
            "Process"   => ("P", Theme.Accent, Theme.AccentBg),
            "Estimated" => ("E", Theme.Dim, Theme.Surface),
            _           => ("?", Theme.Dim, Theme.Surface),
        };

        return new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(Theme, label, 9, fg, FontWeights.Bold),
        };
    }

    // ── Timing legend ────────────────────────────────────────

    private void RenderTimingLegend()
    {
        var card = Card(Theme);
        card.Margin = new Thickness(0, 14, 0, 0);
        var stack = new StackPanel();

        stack.Children.Add(Txt(Theme, "ℹ️  DATA SOURCE LEGEND", 12, Theme.Dim, FontWeights.SemiBold));

        var entries = new (string Badge, string Label, Brush Fg, Brush Bg, string Desc)[]
        {
            ("M", "Measured", Theme.Green, Theme.GreenBg,
                "Actual boot degradation time from Windows Diagnostics event log (most accurate — requires running as Administrator)."),
            ("P", "Process", Theme.Accent, Theme.AccentBg,
                "Time after sign-in when the process first appeared. Reflects startup delay, not duration. May change if the app restarts."),
            ("E", "Estimated", Theme.Dim, Theme.Surface,
                "Approximate impact based on app category (high ≈ 4s, medium ≈ 1.5s, low ≈ 0.5s). Shown when no measured data is available."),
        };

        foreach (var (badge, label, fg, bg, desc) in entries)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
            };

            row.Children.Add(new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = Txt(Theme, badge, 10, fg, FontWeights.Bold),
            });

            var textStack = new StackPanel();
            textStack.Children.Add(Txt(Theme, label, 12, Theme.Text, FontWeights.SemiBold));
            textStack.Children.Add(Txt(Theme, desc, 11, Theme.Dim));
            row.Children.Add(textStack);
            stack.Children.Add(row);
        }

        if (_vm?.AnalysisData?.Diagnostics is { Available: false })
        {
            var tip = new Border
            {
                Background = Theme.YellowBg, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 0),
            };
            tip.Child = Txt(Theme,
                "💡 Tip: Run ColdStart as Administrator to unlock Measured (M) timing from Windows event logs for the most accurate data.",
                11, Theme.Yellow);
            stack.Children.Add(tip);
        }

        card.Child = stack;
        ContentPanel.Children.Add(card);
    }

    // ── Keyboard shortcuts bar ───────────────────────────────

    private void RenderShortcutsBar()
    {
        var bar = new Border
        {
            Background = Theme.Surface, BorderBrush = Theme.Bdr,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 10, 0, 0),
        };
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+F", "Search"));
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+R", "Re-scan"));
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+1", "Startup"));
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+2", "Timeline"));
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+3", "App Usage"));
        panel.Children.Add(ShortcutHint(Theme, "Ctrl+T", "Theme"));
        panel.Children.Add(ShortcutHint(Theme, "Esc", "Clear search"));
        panel.Children.Add(ShortcutHint(Theme, "Enter", "Expand item"));
        bar.Child = panel;
        ContentPanel.Children.Add(bar);
    }

    // ── Item card ────────────────────────────────────────────

    private UIElement CreateItemCard(StartupItem item)
    {
        var isDisabled = !item.IsEnabled;
        var card = new Border
        {
            Background = Theme.Surface2,
            BorderBrush = isDisabled ? Brushes.Transparent : Theme.Bdr,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand,
            Opacity = isDisabled ? 0.55 : 1.0,
        };

        var outer = new StackPanel();
        var arrow = Txt(Theme, "▸", 14, Theme.Dim, margin: new Thickness(10, 0, 0, 0));
        outer.Children.Add(BuildItemHeader(item, isDisabled, arrow));

        var detail = BuildItemDetail(item, isDisabled);
        outer.Children.Add(detail);

        WireItemCardBehavior(card, detail, arrow, isDisabled);
        card.Child = outer;

        System.Windows.Automation.AutomationProperties.SetName(card,
            $"{item.Name}, {item.Category}, {item.Impact} impact{(isDisabled ? ", disabled" : "")}");
        return card;
    }

    private Grid BuildItemHeader(StartupItem item, bool isDisabled, TextBlock arrow)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddRunningDot(header, item);
        AddDisabledBadge(header, isDisabled);
        AddNameColumn(header, item, isDisabled);
        AddTimeColumn(header, item);
        AddImpactBadge(header, item);
        Grid.SetColumn(arrow, 5);
        header.Children.Add(arrow);
        return header;
    }

    private void AddRunningDot(Grid header, StartupItem item)
    {
        var dot = new Ellipse
        {
            Width = 7, Height = 7,
            Fill = item.IsRunning ? Theme.Green : Theme.Dim,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(dot, 0);
        header.Children.Add(dot);
    }

    private void AddDisabledBadge(Grid header, bool isDisabled)
    {
        if (!isDisabled) return;

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 139, 143, 165)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(Theme, "DISABLED", 10, Theme.Dim, FontWeights.Bold),
        };
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
    }

    private void AddNameColumn(Grid header, StartupItem item, bool isDisabled)
    {
        var stack = new StackPanel();
        var name = Txt(Theme, item.Name, 14, isDisabled ? Theme.Dim : Theme.Text, FontWeights.SemiBold);
        if (isDisabled) name.TextDecorations = TextDecorations.Strikethrough;
        stack.Children.Add(name);
        stack.Children.Add(Txt(Theme, $"{item.Category}  ·  {item.Publisher}  ·  via {item.Source}", 12, Theme.Dim));
        Grid.SetColumn(stack, 2);
        header.Children.Add(stack);
    }

    private void AddTimeColumn(Grid header, StartupItem item)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80,
        };

        if (item.HasStartupTime)
            AddMeasuredTime(stack, item);
        else
            AddUnmeasuredTime(stack, item);

        Grid.SetColumn(stack, 3);
        header.Children.Add(stack);
    }

    private void AddMeasuredTime(StackPanel stack, StartupItem item)
    {
        var color = item.StartupTimeMs > 5000 ? Theme.Red
            : item.StartupTimeMs > 2000 ? Theme.Orange
            : item.StartupTimeMs > 1000 ? Theme.Yellow : Theme.Green;

        var isEstimated = item.TimingSource == "Estimated";
        var displayTime = isEstimated ? $"~{Fmt(item.StartupTimeMs)}" : Fmt(item.StartupTimeMs);
        var timeLabel = item.TimingSource switch
        {
            "Measured" => "Boot impact",
            "Process" => $"Signed in + {Fmt(item.StartupTimeMs)}",
            "Estimated" => "Approx. impact",
            _ => "Boot impact",
        };

        stack.Children.Add(Txt(Theme, displayTime, 16, isEstimated ? Theme.Dim : color,
            FontWeights.Bold, align: HorizontalAlignment.Right));

        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        labelRow.Children.Add(BuildTimingSourceBadge(item.TimingSource));
        labelRow.Children.Add(Txt(Theme, timeLabel, 10, Theme.Dim));
        stack.Children.Add(labelRow);
    }

    private void AddUnmeasuredTime(StackPanel stack, StartupItem item)
    {
        stack.Children.Add(Txt(Theme, "—", 16, Theme.Dim, FontWeights.Bold,
            align: HorizontalAlignment.Right));
        stack.Children.Add(Txt(Theme, item.IsRunning ? "Not measured" : "Not running",
            10, Theme.Dim, align: HorizontalAlignment.Right));
    }

    private void AddImpactBadge(Grid header, StartupItem item)
    {
        var (text, fg, bg) = item.Impact switch
        {
            "high" => ("High Impact", Theme.Red, Theme.RedBg),
            "medium" => ("Medium", Theme.Yellow, Theme.YellowBg),
            _ => ("Low", Theme.Green, Theme.GreenBg),
        };
        var badge = new Border
        {
            Background = bg, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(Theme, text, 11, fg, FontWeights.SemiBold),
        };
        Grid.SetColumn(badge, 4);
        header.Children.Add(badge);
    }

    // ── Item detail panel ────────────────────────────────────

    private StackPanel BuildItemDetail(StartupItem item, bool isDisabled)
    {
        var detail = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(15, 10, 0, 4),
        };

        if (!string.IsNullOrEmpty(item.Description))
            detail.Children.Add(DetailRow(Theme, "Description", item.Description));

        AddWhatItDoesSection(detail, item);
        AddIfDisabledSection(detail, item);
        AddWhySlowSection(detail, item);
        AddSpeedUpSection(detail, item);
        AddDetailMetadata(detail, item, isDisabled);
        AddManualSteps(detail, item);
        AddDisableButton(detail, item);
        return detail;
    }

    private void AddWhatItDoesSection(StackPanel detail, StartupItem item)
    {
        if (string.IsNullOrEmpty(item.WhatItDoes)) return;

        var box = new Border
        {
            Background = Theme.Surface, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "💡 WHAT THIS APP DOES", 10, Theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(Theme, item.WhatItDoes, 13, Theme.Text));
        box.Child = stack;
        detail.Children.Add(box);
    }

    private void AddIfDisabledSection(StackPanel detail, StartupItem item)
    {
        if (string.IsNullOrEmpty(item.IfDisabled)) return;

        var box = new Border
        {
            Background = item.Essential ? Theme.RedBg : Theme.AccentBg,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = item.Essential ? Theme.Red : Theme.Accent,
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "IF YOU DISABLE IT", 10, Theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(Theme, item.IfDisabled, 13, Theme.Text));
        box.Child = stack;
        detail.Children.Add(box);
    }

    private void AddWhySlowSection(StackPanel detail, StartupItem item)
    {
        if (string.IsNullOrEmpty(item.WhySlow)) return;

        var box = new Border
        {
            Background = Theme.Surface, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Theme.Orange,
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "🐢 WHY IT'S SLOW", 10, Theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(Theme, item.WhySlow, 13, Theme.Text));
        box.Child = stack;
        detail.Children.Add(box);
    }

    private void AddSpeedUpSection(StackPanel detail, StartupItem item)
    {
        if (string.IsNullOrEmpty(item.HowToSpeedUp)) return;

        var box = new Border
        {
            Background = Theme.GreenBg, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Theme.Green,
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "⚡ HOW TO SPEED IT UP", 10, Theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(Theme, item.HowToSpeedUp, 13, Theme.Text));
        box.Child = stack;
        detail.Children.Add(box);
    }

    private void AddDetailMetadata(StackPanel detail, StartupItem item, bool isDisabled)
    {
        detail.Children.Add(DetailRow(Theme, "Source", $"{item.Source} ({item.Scope})"));

        if (item.SizeMb > 0)
            detail.Children.Add(DetailRow(Theme, "File Size", $"{item.SizeMb} MB"));

        var statusText = isDisabled
            ? "⊘ Disabled — won't start at boot"
            : item.IsRunning ? "● Running now" : "○ Not running (but enabled at boot)";
        detail.Children.Add(DetailRow(Theme, "Status", statusText));

        if (!item.HasStartupTime) return;
        var timeDesc = item.TimingSource switch
        {
            "Measured" => $"{Fmt(item.StartupTimeMs)} — slowed your boot by this much (from Windows diagnostics)",
            "Process" => $"Started {Fmt(item.StartupTimeMs)} after sign-in — this is how long after you logged in before this process appeared",
            "Estimated" => $"~{Fmt(item.StartupTimeMs)} — approximate impact based on app type (run as Admin for exact measurements)",
            _ => Fmt(item.StartupTimeMs),
        };
        detail.Children.Add(DetailRow(Theme, "Boot Time", timeDesc));
    }

    private void AddManualSteps(StackPanel detail, StartupItem item)
    {
        var box = new Border
        {
            Background = Theme.Surface, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(Txt(Theme, "MANUAL STEPS", 10, Theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(Theme, item.HowToDisable, 12, Theme.Dim));
        box.Child = stack;
        detail.Children.Add(box);
    }

    private void AddDisableButton(StackPanel detail, StartupItem item)
    {
        if (item.Essential || item.Action == "keep" || item.DisableMethod == DisableMethod.Unknown)
            return;

        var isAdmin = AdminHelper.IsAdmin();
        var btnColor = ThemeManager.B(item.Action == "safe_to_disable" ? "#16a34a" : "#d97706");
        var btn = new Button
        {
            Content = isAdmin ? "🚫  Disable from Startup" : "🛡️  Disable from Startup (requires Admin)",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Cursor = Cursors.Hand,
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            IsEnabled = isAdmin,
            Opacity = isAdmin ? 1.0 : 0.6,
            ToolTip = isAdmin ? null : "Restart ColdStart as Administrator to enable this feature",
        };
        btn.Template = CreateButtonTemplate(Theme, btnColor);

        btn.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                $"Disable \"{item.Name}\" from starting automatically?\n\n{item.IfDisabled}",
                "Confirm Disable", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                _vm!.DisableItemCommand.Execute(item);
        };
        detail.Children.Add(btn);
    }

    // ── Shared helpers ───────────────────────────────────────

    private static void TogglePanel(StackPanel panel, TextBlock chevron)
    {
        if (panel.Visibility == Visibility.Visible)
        {
            panel.Visibility = Visibility.Collapsed;
            chevron.Text = "▸";
        }
        else
        {
            panel.Visibility = Visibility.Visible;
            chevron.Text = "▾";
        }
    }

    private void WireItemCardBehavior(Border card, StackPanel detail, TextBlock arrow, bool isDisabled)
    {
        card.Focusable = true;
        card.FocusVisualStyle = null;

        card.GotKeyboardFocus += (_, _) => card.BorderBrush = Theme.Accent;
        card.LostKeyboardFocus += (_, _) =>
            card.BorderBrush = isDisabled ? Brushes.Transparent : Theme.Bdr;

        card.MouseLeftButtonUp += (_, e) =>
        {
            TogglePanel(detail, arrow);
            e.Handled = true;
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                TogglePanel(detail, arrow);
                e.Handled = true;
            }
        };
    }
}
