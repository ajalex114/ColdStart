namespace ColdStart.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ColdStart.Models;
using ColdStart.Services.Interfaces;

/// <summary>
/// Manages application-usage data, filtering, and sorting for the App Usage tab.
/// </summary>
public partial class AppUsageViewModel : ObservableObject
{
    private readonly IAppUsageService _appUsageService;

    /// <summary>
    /// Initializes a new instance of <see cref="AppUsageViewModel"/>.
    /// </summary>
    /// <param name="appUsageService">Service that gathers live application resource usage.</param>
    public AppUsageViewModel(IAppUsageService appUsageService)
    {
        ArgumentNullException.ThrowIfNull(appUsageService);
        _appUsageService = appUsageService;
    }

    // ── Observable data ──────────────────────────────────────

    /// <summary>Raw app-usage data returned by the service.</summary>
    [ObservableProperty]
    private AppUsageData? _data;

    /// <summary>Indicates whether a data load is in progress.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Indicates whether data has been loaded at least once.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    // ── Filter state ─────────────────────────────────────────

    /// <summary>Free-text search applied to app names, publishers, and process names.</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>Sort column key: Memory, CPU, CPUTime, Duration, or Name.</summary>
    [ObservableProperty]
    private string _sortBy = "Memory";

    /// <summary>Sort direction; <see langword="false"/> = descending (default).</summary>
    [ObservableProperty]
    private bool _sortAscending;

    /// <summary>Memory usage filter: All, &gt;1GB, &gt;500MB, or &gt;100MB.</summary>
    [ObservableProperty]
    private string _filterMemory = "All";

    /// <summary>CPU usage filter: All, &gt;10%, &gt;5%, or &gt;1%.</summary>
    [ObservableProperty]
    private string _filterCpu = "All";

    /// <summary>App type filter: All, Startup, or Non-Startup.</summary>
    [ObservableProperty]
    private string _filterType = "All";

    // ── Computed ──────────────────────────────────────────────

    /// <summary>Filtered and sorted app groups ready for display.</summary>
    public List<AppGroup> FilteredGroups { get; private set; } = new();

    /// <summary>Indicates whether any filter deviates from its default value.</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(SearchQuery)
        || FilterMemory != "All"
        || FilterCpu != "All"
        || FilterType != "All";

    // ── Events ───────────────────────────────────────────────

    /// <summary>
    /// Raised whenever the filtered data changes so that views can re-render.
    /// </summary>
    public event Action? DataChanged;

    // ── Commands ─────────────────────────────────────────────

    /// <summary>
    /// Loads app-usage data on a background thread and applies current filters.
    /// </summary>
    /// <param name="startupItems">Optional startup items for cross-referencing. May be <see langword="null"/>.</param>
    [RelayCommand]
    private async Task LoadAsync(List<StartupItem>? startupItems)
    {
        IsLoading = true;
        try
        {
            Data = await Task.Run(() => _appUsageService.GetAppUsage(startupItems));
            IsLoaded = true;
            ApplyFiltersAndSort();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Resets all filter properties to their default values and clears the search query.
    /// </summary>
    [RelayCommand]
    private void ResetFilters()
    {
        SearchQuery = "";
        SortBy = "Memory";
        SortAscending = false;
        FilterMemory = "All";
        FilterCpu = "All";
        FilterType = "All";
    }

    /// <summary>
    /// Toggles sort direction when the same column is selected, or switches to a new column
    /// in descending order.
    /// </summary>
    /// <param name="sortKey">The column key to sort by. Must not be <see langword="null"/>.</param>
    [RelayCommand]
    private void ToggleSort(string sortKey)
    {
        ArgumentNullException.ThrowIfNull(sortKey);

        if (SortBy == sortKey)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortBy = sortKey;
            SortAscending = false;
        }
    }

    // ── Property-changed hooks ───────────────────────────────

    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSortByChanged(string value) => ApplyFiltersAndSort();
    partial void OnSortAscendingChanged(bool value) => ApplyFiltersAndSort();
    partial void OnFilterMemoryChanged(string value) => ApplyFiltersAndSort();
    partial void OnFilterCpuChanged(string value) => ApplyFiltersAndSort();
    partial void OnFilterTypeChanged(string value) => ApplyFiltersAndSort();

    // ── Filtering pipeline ───────────────────────────────────

    /// <summary>
    /// Re-filters, re-sorts the data, and raises <see cref="DataChanged"/>.
    /// </summary>
    private void ApplyFiltersAndSort()
    {
        if (Data is null)
        {
            FilteredGroups = new();
            DataChanged?.Invoke();
            return;
        }

        var groups = FilterGroups(Data.Groups);
        FilteredGroups = SortGroups(groups).ToList();
        DataChanged?.Invoke();
    }

    /// <summary>
    /// Applies all active filters to the source groups and returns surviving entries.
    /// </summary>
    private List<AppGroup> FilterGroups(List<AppGroup> groups)
    {
        IEnumerable<AppGroup> filtered = groups;
        filtered = ApplySearchFilter(filtered);
        filtered = ApplyMemoryFilter(filtered);
        filtered = ApplyCpuFilter(filtered);
        filtered = ApplyTypeFilter(filtered);
        return filtered.ToList();
    }

    /// <summary>Filters groups whose name, publisher, or any process name matches the query.</summary>
    private IEnumerable<AppGroup> ApplySearchFilter(IEnumerable<AppGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return groups;

        var query = SearchQuery.Trim();
        return groups.Where(g => MatchesSearch(g, query));
    }

    /// <summary>Determines whether a group matches the search query.</summary>
    private static bool MatchesSearch(AppGroup group, string query)
    {
        if (group.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (group.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return group.Processes.Any(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filters groups by minimum memory threshold.</summary>
    private IEnumerable<AppGroup> ApplyMemoryFilter(IEnumerable<AppGroup> groups)
    {
        return FilterMemory switch
        {
            ">1GB" => groups.Where(g => g.TotalMemoryMb > 1024),
            ">500MB" => groups.Where(g => g.TotalMemoryMb > 500),
            ">100MB" => groups.Where(g => g.TotalMemoryMb > 100),
            _ => groups,
        };
    }

    /// <summary>Filters groups by minimum CPU percentage.</summary>
    private IEnumerable<AppGroup> ApplyCpuFilter(IEnumerable<AppGroup> groups)
    {
        return FilterCpu switch
        {
            ">10%" => groups.Where(g => g.TotalCpuPercent > 10),
            ">5%" => groups.Where(g => g.TotalCpuPercent > 5),
            ">1%" => groups.Where(g => g.TotalCpuPercent > 1),
            _ => groups,
        };
    }

    /// <summary>Filters groups by startup vs. non-startup type.</summary>
    private IEnumerable<AppGroup> ApplyTypeFilter(IEnumerable<AppGroup> groups)
    {
        return FilterType switch
        {
            "Startup" => groups.Where(g => g.IsStartupApp),
            "Non-Startup" => groups.Where(g => !g.IsStartupApp),
            _ => groups,
        };
    }

    // ── Sorting ──────────────────────────────────────────────

    /// <summary>
    /// Sorts groups by the current <see cref="SortBy"/> column and direction.
    /// </summary>
    private IEnumerable<AppGroup> SortGroups(List<AppGroup> groups)
    {
        return SortBy switch
        {
            "Name" => ApplyDirection(groups, g => g.FriendlyName, StringComparer.OrdinalIgnoreCase),
            "CPU" => ApplyDirection(groups, g => g.TotalCpuPercent),
            "CPUTime" => ApplyDirection(groups, g => g.TotalCpuTime),
            "Duration" => ApplyDirection(groups, g => g.SessionDuration),
            // "Memory" (default)
            _ => ApplyDirection(groups, g => g.TotalMemoryMb),
        };
    }

    /// <summary>
    /// Applies ascending or descending ordering using the specified key selector.
    /// </summary>
    private IEnumerable<AppGroup> ApplyDirection<TKey>(
        IEnumerable<AppGroup> groups,
        Func<AppGroup, TKey> keySelector,
        IComparer<TKey>? comparer = null)
    {
        return SortAscending
            ? groups.OrderBy(keySelector, comparer)
            : groups.OrderByDescending(keySelector, comparer);
    }
}
