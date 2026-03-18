using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ColdStart.Helpers;
using ColdStart.Models;
using ColdStart.ViewModels;
using static ColdStart.Helpers.FormatHelper;
using static ColdStart.Helpers.UiHelper;

namespace ColdStart.Views.Controls;

/// <summary>
/// Renders a Gantt-style startup timeline showing when each app began loading after sign-in.
/// Subscribes to <see cref="TimelineViewModel.DataChanged"/> and <see cref="MainViewModel.ThemeChanged"/>
/// to stay in sync with data and theme changes.
/// </summary>
public partial class TimelineView : UserControl
{
    private const double NameColumnWidth = 170;
    private const double DurationColumnWidth = 70;
    private const int ResizeDebounceMs = 200;
    private const int MaxDisplayNameLength = 22;

    private TimelineViewModel? _viewModel;
    private MainViewModel? _mainVm;
    private DispatcherTimer? _resizeDebounce;
    private bool _isRendering;
    private bool _needsRender;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineView"/> control.
    /// </summary>
    public TimelineView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the view to its view models and subscribes to data and theme change events.
    /// </summary>
    /// <param name="vm">The timeline view model supplying entry data.</param>
    /// <param name="mainVm">The main view model supplying the shared theme.</param>
    public void Initialize(TimelineViewModel vm, MainViewModel mainVm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(mainVm);

        _viewModel = vm;
        _mainVm = mainVm;

        _viewModel.DataChanged += OnDataChanged;
        _mainVm.ThemeChanged += OnThemeChanged;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += OnVisibilityChanged;
    }

    /// <summary>
    /// Renders the entire timeline view. Call when the tab becomes visible or data changes.
    /// </summary>
    public void Render()
    {
        if (_isRendering) return;
        _isRendering = true;
        try
        {
            if (_viewModel == null || _viewModel.Entries.Count == 0)
            {
                RenderEmpty();
                return;
            }

            ContentPanel.Children.Clear();
            _viewModel.CalculateTicks(CalculateAvailableWidth());

            RenderHeader();
            RenderBootPhases();
            RenderGanttChart();
            RenderFooter();
        }
        finally
        {
            _isRendering = false;
        }
    }

    // ── Section renderers ────────────────────────────────────

    /// <summary>
    /// Renders the header card with title and stat badges.
    /// </summary>
    private void RenderHeader()
    {
        var theme = Theme;
        var headerCard = Card(theme);
        var headerStack = new StackPanel();

        headerStack.Children.Add(Txt(theme, "📊  STARTUP TIMELINE", 18, theme.Accent, FontWeights.Bold));
        headerStack.Children.Add(Txt(theme, "When each app started loading after you signed in", 13, theme.Dim,
            margin: new Thickness(0, 4, 0, 0)));

        headerStack.Children.Add(BuildStatBadges());
        headerCard.Child = headerStack;
        ContentPanel.Children.Add(headerCard);
    }

    /// <summary>
    /// Builds the horizontal row of stat badges for the header.
    /// </summary>
    private StackPanel BuildStatBadges()
    {
        var vm = _viewModel!;
        var d = vm.Diagnostics;
        var theme = Theme;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        AddStatBadge(row, "Total Apps", vm.TotalApps.ToString(), theme.Accent);
        AddStatBadge(row, "Desktop Ready", $"~{Fmt(vm.MaxEndMs)}", theme.Green);

        if (vm.HighImpactCount > 0)
            AddStatBadge(row, "High Impact", vm.HighImpactCount.ToString(), theme.Red);
        if (vm.MediumImpactCount > 0)
            AddStatBadge(row, "Medium Impact", vm.MediumImpactCount.ToString(), theme.Orange);
        if (d is { Available: true, BootDurationMs: > 0 })
            AddStatBadge(row, "Total Boot", Fmt(d.BootDurationMs), theme.Accent);

        return row;
    }

    /// <summary>
    /// Adds a single stat badge to the target panel.
    /// </summary>
    private void AddStatBadge(StackPanel target, string label, string value, Brush color)
    {
        var theme = Theme;
        var badge = new Border
        {
            Background = theme.Surface2,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var st = new StackPanel();
        st.Children.Add(Txt(theme, value, 20, color, FontWeights.Bold));
        st.Children.Add(Txt(theme, label, 11, theme.Dim));
        badge.Child = st;
        target.Children.Add(badge);
    }

    /// <summary>
    /// Renders boot phase bar (Core OS / Apps Loading / Ready) when diagnostics are available.
    /// </summary>
    private void RenderBootPhases()
    {
        var vm = _viewModel!;
        var d = vm.Diagnostics;
        if (d is not { Available: true, BootDurationMs: > 0 })
            return;

        var theme = Theme;
        var phaseCard = Card(theme);
        var phaseStack = new StackPanel();

        phaseStack.Children.Add(Txt(theme, "BOOT PHASES", 11, theme.Dim, FontWeights.SemiBold,
            new Thickness(0, 0, 0, 8)));
        phaseStack.Children.Add(BuildPhaseBar(d, theme));
        phaseStack.Children.Add(BuildPhaseMarkers(d, vm.BootTime, theme));

        phaseCard.Child = phaseStack;
        ContentPanel.Children.Add(phaseCard);
    }

    /// <summary>
    /// Builds the three-segment boot phase bar.
    /// </summary>
    private static Grid BuildPhaseBar(BootDiagnostics d, ThemeManager theme)
    {
        var totalBoot = Math.Max(d.BootDurationMs, 1);
        var mainPct = Math.Clamp(d.MainPathMs * 100.0 / totalBoot, 5, 70);
        var postPct = Math.Clamp(d.PostBootMs * 100.0 / totalBoot, 5, 95 - mainPct);
        var restPct = Math.Max(5, 100 - mainPct - postPct);

        var bar = new Grid { Height = 36, Margin = new Thickness(0, 0, 0, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mainPct, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(postPct, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(restPct, GridUnitType.Star) });

        AddPhaseSegment(theme, bar, 0, theme.Accent, $"Core OS  {Fmt(d.MainPathMs)}", new CornerRadius(6, 0, 0, 6));
        AddPhaseSegment(theme, bar, 1, theme.Orange, $"Apps Loading  {Fmt(d.PostBootMs)}", new CornerRadius(0));
        AddPhaseSegment(theme, bar, 2, theme.Green, "✓ Ready", new CornerRadius(0, 6, 6, 0));

        return bar;
    }

    /// <summary>
    /// Adds a single phase segment to the boot phase bar grid.
    /// </summary>
    private static void AddPhaseSegment(ThemeManager theme, Grid bar, int column, Brush bg, string label, CornerRadius corners)
    {
        var segment = new Border
        {
            Background = bg,
            CornerRadius = corners,
            Child = Txt(theme, label, 11, Brushes.White, FontWeights.SemiBold,
                align: HorizontalAlignment.Center),
        };
        Grid.SetColumn(segment, column);
        bar.Children.Add(segment);
    }

    /// <summary>
    /// Builds the phase time markers row beneath the boot phase bar.
    /// </summary>
    private static StackPanel BuildPhaseMarkers(BootDiagnostics d, DateTime bootTime, ThemeManager theme)
    {
        var markers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        markers.Children.Add(Txt(theme, $"🔵 Sign-in started: {bootTime:h:mm:ss tt}", 12, theme.Dim));
        markers.Children.Add(new Border { Width = 20 });
        markers.Children.Add(Txt(theme, $"🟠 Apps loading: {bootTime.AddMilliseconds(d.MainPathMs):h:mm:ss tt}", 12, theme.Dim));
        markers.Children.Add(new Border { Width = 20 });
        markers.Children.Add(Txt(theme, $"🟢 Desktop ready: {bootTime.AddMilliseconds(d.BootDurationMs):h:mm:ss tt}", 12, theme.Dim));
        return markers;
    }

    // ── Gantt chart ──────────────────────────────────────────

    /// <summary>
    /// Renders the full Gantt chart: time axis, tick marks, and per-app bars.
    /// </summary>
    private void RenderGanttChart()
    {
        var vm = _viewModel!;
        var theme = Theme;
        var chartCard = Card(theme);
        var chartStack = new StackPanel();

        chartStack.Children.Add(Txt(theme, $"APP LAUNCH SEQUENCE  ·  {vm.Entries.Count} apps", 11, theme.Dim,
            FontWeights.SemiBold, new Thickness(0, 0, 0, 12)));

        RenderTimeAxis(chartStack);
        RenderTickMarks(chartStack);
        RenderAppBars(chartStack);

        chartCard.Child = chartStack;
        ContentPanel.Children.Add(chartCard);
    }

    /// <summary>
    /// Renders the time axis labels row (first label = absolute clock, rest = relative offsets).
    /// </summary>
    private void RenderTimeAxis(StackPanel chartStack)
    {
        var vm = _viewModel!;
        var theme = Theme;

        var axisRow = CreateThreeColumnGrid(18);
        var axisLabels = CreateTickGrid(vm.TickCount);

        for (int i = 0; i <= vm.TickCount; i++)
        {
            var lbl = BuildAxisLabel(i, vm);
            axisLabels.Children.Add(lbl);
        }

        Grid.SetColumn(axisLabels, 1);
        axisRow.Children.Add(axisLabels);
        chartStack.Children.Add(axisRow);
    }

    /// <summary>
    /// Builds a single axis label for the given tick index.
    /// </summary>
    private TextBlock BuildAxisLabel(int tickIndex, TimelineViewModel vm)
    {
        var offsetMs = tickIndex * vm.TickIntervalMs;
        var clockTime = vm.BootTime.AddMilliseconds(offsetMs);
        var timeStr = FormatTickLabel(tickIndex, offsetMs, clockTime);

        var lbl = Txt(Theme, timeStr, 9, Theme.Dim);

        if (tickIndex == 0)
        {
            lbl.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(lbl, 0);
        }
        else if (tickIndex == vm.TickCount)
        {
            lbl.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(lbl, vm.TickCount - 1);
        }
        else
        {
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(lbl, tickIndex - 1);
            Grid.SetColumnSpan(lbl, 2);
        }

        return lbl;
    }

    /// <summary>
    /// Formats a tick label: first tick is absolute time, subsequent are relative offsets.
    /// </summary>
    private static string FormatTickLabel(int tickIndex, int offsetMs, DateTime clockTime)
    {
        if (tickIndex == 0)
            return clockTime.ToString("h:mm:ss tt");

        var secs = offsetMs / 1000;
        if (secs >= 60 && secs % 60 == 0)
            return $"+{secs / 60}m";
        if (secs >= 60)
            return $"+{secs / 60}m{secs % 60}s";
        return $"+{secs}s";
    }

    /// <summary>
    /// Renders horizontal baseline and vertical tick marks.
    /// </summary>
    private void RenderTickMarks(StackPanel chartStack)
    {
        var vm = _viewModel!;
        var theme = Theme;

        var tickRow = CreateThreeColumnGrid(8, new Thickness(0, 0, 0, 6));
        var tickArea = CreateTickGrid(vm.TickCount);

        var hLine = new Border { Background = theme.Bdr, Height = 1, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumnSpan(hLine, vm.TickCount);
        tickArea.Children.Add(hLine);

        for (int i = 0; i <= vm.TickCount; i++)
            AddTickMark(tickArea, i, vm.TickCount, theme);

        Grid.SetColumn(tickArea, 1);
        tickRow.Children.Add(tickArea);
        chartStack.Children.Add(tickRow);
    }

    /// <summary>
    /// Adds a single vertical tick mark to the tick area grid.
    /// </summary>
    private static void AddTickMark(Grid tickArea, int index, int tickCount, ThemeManager theme)
    {
        var tick = new Border { Width = 1, Background = theme.Bdr };
        if (index < tickCount)
        {
            Grid.SetColumn(tick, index);
            tick.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            Grid.SetColumn(tick, tickCount - 1);
            tick.HorizontalAlignment = HorizontalAlignment.Right;
        }
        tickArea.Children.Add(tick);
    }

    /// <summary>
    /// Renders one horizontal bar row per timeline entry.
    /// </summary>
    private void RenderAppBars(StackPanel chartStack)
    {
        var vm = _viewModel!;
        for (int idx = 0; idx < vm.Entries.Count; idx++)
            chartStack.Children.Add(RenderAppBar(vm.Entries[idx], idx));
    }

    /// <summary>
    /// Builds a single app bar row: name + impact dot, colored bar, and time label.
    /// </summary>
    private UIElement RenderAppBar(TimelineEntry entry, int index)
    {
        var vm = _viewModel!;
        var theme = Theme;
        var durationMs = entry.DurationMs;

        if (entry.StartMs + durationMs > vm.TotalMs)
            durationMs = vm.TotalMs - entry.StartMs;

        var row = CreateThreeColumnGrid(minHeight: 28, margin: new Thickness(0, 1, 0, 1));
        if (index % 2 == 0)
            AddAlternateRowBackground(row, theme);

        Grid.SetColumn(BuildNamePanel(entry, theme), 0);
        row.Children.Add(BuildNamePanel(entry, theme));

        var barGrid = BuildBarGrid(entry, durationMs, vm, theme);
        Grid.SetColumn(barGrid, 1);
        row.Children.Add(barGrid);

        var durLabel = BuildTimeLabel(entry, vm, theme);
        Grid.SetColumn(durLabel, 2);
        row.Children.Add(durLabel);

        return row;
    }

    /// <summary>
    /// Builds the name panel with impact dot, truncated name, and optional approx badge.
    /// </summary>
    private static StackPanel BuildNamePanel(TimelineEntry entry, ThemeManager theme)
    {
        var namePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        namePanel.Children.Add(new Ellipse
        {
            Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0),
            Fill = GetImpactBrush(entry.Impact, theme),
        });

        var displayName = entry.Name.Length > MaxDisplayNameLength
            ? entry.Name[..20] + "…"
            : entry.Name;
        var nameLabel = Txt(theme, displayName, 11, theme.Text);
        if (entry.Name.Length > MaxDisplayNameLength)
            nameLabel.ToolTip = entry.Name;
        namePanel.Children.Add(nameLabel);

        if (entry.Source is "Estimated" or "No Data" or "Synthetic")
        {
            var estBadge = Txt(theme, "~approx", 8, theme.Dim, margin: new Thickness(4, 0, 0, 0));
            estBadge.FontStyle = FontStyles.Italic;
            namePanel.Children.Add(estBadge);
        }

        return namePanel;
    }

    /// <summary>
    /// Builds the proportional bar grid for a single entry.
    /// </summary>
    private static Grid BuildBarGrid(TimelineEntry entry, long durationMs, TimelineViewModel vm, ThemeManager theme)
    {
        double beforeProp = Math.Max(0, (double)entry.StartMs / vm.TotalMs);
        double barProp = Math.Max(0.015, (double)durationMs / vm.TotalMs);
        double afterProp = Math.Max(0, 1.0 - beforeProp - barProp);

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(beforeProp, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barProp, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(afterProp, GridUnitType.Star) });

        var barColor = GetImpactBrush(entry.Impact, theme);
        var bar = new Border
        {
            Background = barColor,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 4, 0, 4),
            MinWidth = 4,
            Opacity = entry.Source is "Estimated" or "No Data" or "Synthetic" ? 0.6 : 1.0,
            ToolTip = BuildBarTooltip(entry, durationMs, vm),
        };
        Grid.SetColumn(bar, 1);
        barGrid.Children.Add(bar);

        return barGrid;
    }

    /// <summary>
    /// Builds the tooltip text for a timeline bar.
    /// </summary>
    private static string BuildBarTooltip(TimelineEntry entry, long durationMs, TimelineViewModel vm)
    {
        var startTime = vm.BootTime.AddMilliseconds(entry.StartMs);
        var endTime = vm.BootTime.AddMilliseconds(entry.StartMs + durationMs);
        var sourceLabel = entry.Source switch
        {
            "Measured" => "⏱ Measured from Windows boot diagnostics",
            "Process" => "📊 Detected from running process",
            "Estimated" => "📐 Estimated based on app type",
            "No Data" => "📐 Estimated — no exact timing available",
            "Synthetic" => "📐 Estimated — no exact timing available",
            _ => entry.Source,
        };
        return $"{entry.Name}\n{startTime:h:mm:ss tt} → {endTime:h:mm:ss tt}\n" +
               $"Duration: ~{Fmt(durationMs)}  ·  {entry.Impact} impact\n{sourceLabel}";
    }

    /// <summary>
    /// Builds the right-aligned time label for a bar row.
    /// </summary>
    private static TextBlock BuildTimeLabel(TimelineEntry entry, TimelineViewModel vm, ThemeManager theme)
    {
        var startTime = vm.BootTime.AddMilliseconds(entry.StartMs);
        var durLabel = Txt(theme, $"{startTime:h:mm:ss}", 10, theme.Dim, align: HorizontalAlignment.Right);
        durLabel.VerticalAlignment = VerticalAlignment.Center;
        return durLabel;
    }

    // ── Footer ───────────────────────────────────────────────

    /// <summary>
    /// Renders the desktop-ready timestamp and the impact legend.
    /// </summary>
    private void RenderFooter()
    {
        var vm = _viewModel!;
        var theme = Theme;
        var readyTime = vm.BootTime.AddMilliseconds(vm.MaxEndMs);

        var footerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        footerRow.Children.Add(Txt(theme, "🏁", 14));
        footerRow.Children.Add(Txt(theme,
            $"  Desktop ready at ~{readyTime:h:mm:ss tt} ({Fmt(vm.MaxEndMs)} after sign-in)",
            13, theme.Accent, FontWeights.SemiBold));
        ContentPanel.Children.Add(footerRow);

        ContentPanel.Children.Add(BuildLegend(theme));
    }

    /// <summary>
    /// Builds the impact legend row.
    /// </summary>
    private static StackPanel BuildLegend(ThemeManager theme)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        legend.Children.Add(LegendDot(theme, theme.Red, "High impact"));
        legend.Children.Add(LegendDot(theme, theme.Orange, "Medium"));
        legend.Children.Add(LegendDot(theme, theme.Green, "Low"));
        legend.Children.Add(new Border { Width = 15 });
        legend.Children.Add(Txt(theme, "· Faded bars = approximate timing", 11, theme.Dim));
        legend.Children.Add(new Border { Width = 15 });
        legend.Children.Add(Txt(theme, "Hover bars for time details", 11, theme.Dim));
        return legend;
    }

    // ── Empty state ──────────────────────────────────────────

    /// <summary>
    /// Renders the empty-state message when no timeline data is available.
    /// </summary>
    private void RenderEmpty()
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(Txt(Theme,
            "No startup timing data available. Try running as Administrator for detailed boot diagnostics.",
            14, Theme.Dim, margin: new Thickness(0, 30, 0, 0), align: HorizontalAlignment.Center));
    }

    // ── Grid helpers ─────────────────────────────────────────

    /// <summary>
    /// Creates a three-column grid matching the timeline layout: name | chart | duration.
    /// </summary>
    private static Grid CreateThreeColumnGrid(double minHeight = 0, Thickness? margin = null)
    {
        var grid = new Grid();
        if (minHeight > 0) grid.MinHeight = minHeight;
        if (margin.HasValue) grid.Margin = margin.Value;
        else if (minHeight > 0) grid.Height = minHeight;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DurationColumnWidth) });
        return grid;
    }

    /// <summary>
    /// Creates a uniform grid with the specified number of star-sized columns for tick layout.
    /// </summary>
    private static Grid CreateTickGrid(int tickCount)
    {
        var grid = new Grid();
        for (int i = 0; i < tickCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    /// <summary>
    /// Adds a semi-transparent alternating-row background to a bar row.
    /// </summary>
    private static void AddAlternateRowBackground(Grid row, ThemeManager theme)
    {
        var bg = new Border { Background = theme.Surface2, CornerRadius = new CornerRadius(3), Opacity = 0.5 };
        Grid.SetColumnSpan(bg, 3);
        row.Children.Add(bg);
    }

    /// <summary>
    /// Returns the impact brush (Red, Orange, or Green) for the given impact level.
    /// </summary>
    private static Brush GetImpactBrush(string impact, ThemeManager theme)
    {
        return impact switch
        {
            "high" => theme.Red,
            "medium" => theme.Orange,
            _ => theme.Green,
        };
    }

    // ── Layout / events ──────────────────────────────────────

    /// <summary>
    /// Calculates the available width for the chart area based on current control width.
    /// </summary>
    private double CalculateAvailableWidth()
    {
        return Math.Max(300, ActualWidth - NameColumnWidth - DurationColumnWidth - 100);
    }

    /// <summary>
    /// Convenience accessor for the shared theme manager.
    /// </summary>
    private ThemeManager Theme => _mainVm!.Theme;

    /// <summary>
    /// Handles data changes from the view model.
    /// </summary>
    private void OnDataChanged()
    {
        if (IsVisible)
            Render();
        else
            _needsRender = true;
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

    /// <summary>
    /// Debounces resize events to avoid excessive re-renders.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || _viewModel == null || _viewModel.Entries.Count == 0)
            return;

        _resizeDebounce?.Stop();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ResizeDebounceMs) };
        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            Render();
        };
        _resizeDebounce.Start();
    }
}
