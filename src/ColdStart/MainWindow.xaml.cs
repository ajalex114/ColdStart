namespace ColdStart;

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ColdStart.Helpers;
using ColdStart.ViewModels;

/// <summary>
/// Main application window. Acts as a thin shell that delegates to child Views and ViewModels.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;

        InitializeComponent();

        // Wire Views to ViewModels
        StartupView.Initialize(_vm.StartupVm, _vm);
        TimelineView.Initialize(_vm.TimelineVm, _vm);
        AppUsageView.Initialize(_vm.AppUsageVm, _vm);

        // Wire View events
        StartupView.TimelineRequested += () => SwitchTab(1);
        StartupView.RescanRequested += async () => await HandleRescanAsync();

        // Theme changes
        _vm.ThemeChanged += ApplyThemeToShell;

        // Update SystemInfoText when VM changes
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SystemInfoText))
                SystemInfoText.Text = _vm.SystemInfoText;
        };

        // Keyboard shortcuts
        KeyDown += OnGlobalKeyDown;

        // Show admin banner if not elevated
        if (!_vm.IsAdmin)
            AdminBanner.Visibility = Visibility.Visible;

        // Initial theme
        ApplyThemeToShell();

        // Load data
        Loaded += async (_, _) =>
        {
            StartupView.ShowLoading();
            await _vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    // ── Theme application to shell elements ──────────────────

    private void ApplyThemeToShell()
    {
        var t = _vm.Theme;
        Background = t.Bg;

        // Title
        TitleIcon.Foreground = t.Accent;
        TitleText.Foreground = t.Text;

        // System info
        SystemInfoText.Foreground = t.Dim;
        DeviceInfoIcon.Foreground = t.Dim;
        SystemInfoText.Text = _vm.SystemInfoText;

        // Device info card
        DeviceInfoCard.Background = t.Surface;
        DeviceInfoCard.BorderBrush = t.Bdr;

        // Theme toggle pill
        ThemeBorder.Background = t.Surface2;
        ThemeBorder.BorderBrush = t.Bdr;
        ThemeBtn.Foreground = t.Text;
        ThemeBtn.Text = t.ThemeLabel;
        ThemeIcon.Text = t.ThemeIcon;
        ThemeIcon.Foreground = t.Text;

        // Tab bar
        TabBarBorder.Background = t.Surface;
        RefreshTabButtonColors();
    }

    // ── Tab switching ────────────────────────────────────────

    private void SwitchTab(int index)
    {
        _vm.SwitchTabCommand.Execute(index);

        StartupView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TimelineView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppUsageView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        RefreshTabButtonColors();

        // Prepare timeline when switching to it (DataChanged event triggers render)
        if (index == 1 && _vm.StartupVm.AnalysisData != null)
        {
            _vm.TimelineVm.PrepareTimeline(_vm.StartupVm.AnalysisData);
        }

        // Load app usage on first visit
        if (index == 2 && !_vm.AppUsageVm.IsLoaded)
        {
            AppUsageView.ShowLoading();
            _ = _vm.AppUsageVm.LoadCommand.ExecuteAsync(_vm.StartupVm.AnalysisData?.Items);
        }
    }

    private void RefreshTabButtonColors()
    {
        var t = _vm.Theme;
        StartupTabBtn.Background = StartupView.Visibility == Visibility.Visible ? t.AccentBg : Brushes.Transparent;
        StartupTabBtn.Foreground = StartupView.Visibility == Visibility.Visible ? t.Accent : t.Dim;
        TimelineTabBtn.Background = TimelineView.Visibility == Visibility.Visible ? t.AccentBg : Brushes.Transparent;
        TimelineTabBtn.Foreground = TimelineView.Visibility == Visibility.Visible ? t.Accent : t.Dim;
        PerfTabBtn.Background = AppUsageView.Visibility == Visibility.Visible ? t.AccentBg : Brushes.Transparent;
        PerfTabBtn.Foreground = AppUsageView.Visibility == Visibility.Visible ? t.Accent : t.Dim;
    }

    private async Task HandleRescanAsync()
    {
        StartupView.ShowLoading();
        await _vm.StartupVm.LoadCommand.ExecuteAsync(null);
    }

    // ── Event handlers ──────────────────────────────────────

    private void ThemeToggle_Click(object sender, MouseButtonEventArgs e) => _vm.CycleThemeCommand.Execute(null);
    private void StartupTab_Click(object sender, RoutedEventArgs e) => SwitchTab(0);
    private void TimelineTab_Click(object sender, RoutedEventArgs e) => SwitchTab(1);
    private void PerfTab_Click(object sender, RoutedEventArgs e) => SwitchTab(2);

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (AdminHelper.RestartAsAdmin())
            Application.Current.Shutdown();
    }

    // ── Keyboard shortcuts ──────────────────────────────────

    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1: SwitchTab(0); e.Handled = true; return;
                case Key.D2: SwitchTab(1); e.Handled = true; return;
                case Key.D3: SwitchTab(2); e.Handled = true; return;
                case Key.R: _ = HandleRescanAsync(); e.Handled = true; return;
                case Key.T: _vm.CycleThemeCommand.Execute(null); e.Handled = true; return;
            }
        }

        if (e.Key == Key.Escape)
        {
            _vm.StartupVm.SearchQuery = "";
            e.Handled = true;
        }
    }
}
