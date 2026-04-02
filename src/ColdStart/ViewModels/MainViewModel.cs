namespace ColdStart.ViewModels;

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ColdStart.Helpers;
using ColdStart.Services.Interfaces;

/// <summary>
/// Top-level orchestrator that owns the shared <see cref="ThemeManager"/>,
/// child view-models, and coordinates lazy tab loading.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ISystemInfoService _sysInfo;

    /// <summary>
    /// Initializes a new instance of <see cref="MainViewModel"/> and creates all child view-models.
    /// </summary>
    /// <param name="sysInfo">System information service.</param>
    /// <param name="startupAnalyzer">Startup analyzer service.</param>
    /// <param name="appUsage">Application usage service.</param>
    /// <param name="disableService">Startup-item disable service.</param>
    public MainViewModel(
        ISystemInfoService sysInfo,
        IStartupAnalyzerService startupAnalyzer,
        IAppUsageService appUsage,
        IDisableService disableService)
    {
        ArgumentNullException.ThrowIfNull(sysInfo);
        ArgumentNullException.ThrowIfNull(startupAnalyzer);
        ArgumentNullException.ThrowIfNull(appUsage);
        ArgumentNullException.ThrowIfNull(disableService);

        _sysInfo = sysInfo;
        _theme = new ThemeManager();

        StartupVm = new StartupViewModel(startupAnalyzer, disableService);
        TimelineVm = new TimelineViewModel();
        AppUsageVm = new AppUsageViewModel(appUsage);
    }

    // ── Observable properties ────────────────────────────────

    /// <summary>The shared theme manager bound by all views.</summary>
    [ObservableProperty]
    private ThemeManager _theme;

    /// <summary>Index of the active tab (0 = Startup, 1 = Timeline, 2 = App Usage).</summary>
    [ObservableProperty]
    private int _activeTabIndex;

    /// <summary>Formatted system-information text displayed in the header.</summary>
    [ObservableProperty]
    private string _systemInfoText = "Loading system info...";

    /// <summary>Indicates whether system information has been loaded.</summary>
    [ObservableProperty]
    private bool _isSystemInfoLoaded;

    /// <summary>Indicates whether the current process is running with administrator privileges.</summary>
    public bool IsAdmin { get; } = AdminHelper.IsAdmin();

    // ── Child view-models ────────────────────────────────────

    /// <summary>View-model for the Startup tab.</summary>
    public StartupViewModel StartupVm { get; }

    /// <summary>View-model for the Timeline tab.</summary>
    public TimelineViewModel TimelineVm { get; }

    /// <summary>View-model for the App Usage tab.</summary>
    public AppUsageViewModel AppUsageVm { get; }

    // ── Events ───────────────────────────────────────────────

    /// <summary>
    /// Raised after the theme is cycled so that views can trigger a re-render.
    /// </summary>
    public event Action? ThemeChanged;

    // ── Commands ─────────────────────────────────────────────

    /// <summary>
    /// Switches the active tab and triggers lazy-loading of the target tab's data.
    /// </summary>
    /// <param name="index">Zero-based tab index (0 = Startup, 1 = Timeline, 2 = App Usage).</param>
    [RelayCommand]
    private void SwitchTab(int index)
    {
        if (index < 0 || index > 2)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Tab index must be 0, 1, or 2.");

        ActiveTabIndex = index;
    }

    /// <summary>
    /// Cycles the application theme and raises <see cref="ThemeChanged"/>.
    /// </summary>
    [RelayCommand]
    private void CycleTheme()
    {
        Theme.CycleTheme();
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Loads system information asynchronously and updates <see cref="SystemInfoText"/>.
    /// </summary>
    [RelayCommand]
    private async Task LoadSystemInfoAsync()
    {
        var info = await Task.Run(() => _sysInfo.GetSystemInfo());
        SystemInfoText = FormatSystemInfo(info);
        IsSystemInfoLoaded = true;
    }

    /// <summary>
    /// Initializes the application by loading system info and startup data concurrently.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await Task.WhenAll(
            LoadSystemInfoAsync(),
            StartupVm.LoadCommand.ExecuteAsync(null));
    }

    // ── Property-changed hooks ───────────────────────────────

    /// <summary>
    /// Reacts to tab changes by lazily loading Timeline or App Usage data.
    /// </summary>
    partial void OnActiveTabIndexChanged(int value)
    {
        LazyLoadTabAsync(value).ConfigureAwait(false);
    }

    // ── Private helpers ──────────────────────────────────────

    /// <summary>
    /// Lazily loads data for the selected tab when it is shown for the first time.
    /// </summary>
    private async Task LazyLoadTabAsync(int tabIndex)
    {
        if (tabIndex == 1 && !TimelineVm.IsPrepared)
        {
            TimelineVm.PrepareTimeline(StartupVm.AnalysisData);
        }
        else if (tabIndex == 2 && !AppUsageVm.IsLoaded)
        {
            await AppUsageVm.LoadCommand.ExecuteAsync(StartupVm.AnalysisData?.Items);
        }
    }

    /// <summary>
    /// Builds a single-line system-information summary from <paramref name="info"/>.
    /// </summary>
    private static string FormatSystemInfo(Models.SystemInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return $"{info.Hostname}  •  {info.Os}  •  {info.Cpu} ({info.Cores} cores)  •  {info.RamTotalGb:F1} GB RAM  •  Up {info.Uptime}";
    }
}
