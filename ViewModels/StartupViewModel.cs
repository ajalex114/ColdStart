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
/// Manages startup-analysis data, filtering, sorting, and grouping for the Startup tab.
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupAnalyzerService _analyzer;
    private readonly IDisableService _disableService;

    /// <summary>
    /// Initializes a new instance of <see cref="StartupViewModel"/>.
    /// </summary>
    /// <param name="analyzer">Service that discovers and classifies startup items.</param>
    /// <param name="disableService">Service that disables individual startup items.</param>
    public StartupViewModel(IStartupAnalyzerService analyzer, IDisableService disableService)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(disableService);

        _analyzer = analyzer;
        _disableService = disableService;
    }

    // ── Observable data ──────────────────────────────────────

    /// <summary>Full analysis result returned by the analyzer service.</summary>
    [ObservableProperty]
    private StartupAnalysis? _analysisData;

    /// <summary>Indicates whether an analysis is currently in progress.</summary>
    [ObservableProperty]
    private bool _isLoading;

    // ── Filter state ─────────────────────────────────────────

    /// <summary>Free-text search query applied to item names, publishers, and descriptions.</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>Sort order key: Impact, Startup Time, Name, or Status.</summary>
    [ObservableProperty]
    private string _sortBy = "Impact";

    /// <summary>Impact-level filter: All, High, Medium, or Low.</summary>
    [ObservableProperty]
    private string _filterImpact = "All";

    /// <summary>Enabled-status filter: All, Enabled, or Disabled.</summary>
    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>Startup-time filter: All, &gt; 5s, &gt; 2s, or &gt; 1s.</summary>
    [ObservableProperty]
    private string _filterTime = "All";

    /// <summary>Data-source filter: All, Measured, Process, or Estimated.</summary>
    [ObservableProperty]
    private string _filterSource = "All";

    // ── Computed / processed data ────────────────────────────

    /// <summary>Grouped and filtered startup items ready for display.</summary>
    public List<StartupItemGroup> FilteredGroups { get; private set; } = new();

    /// <summary>Indicates whether any filter deviates from its default value.</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(SearchQuery)
        || FilterImpact != "All"
        || FilterStatus != "All"
        || FilterTime != "All"
        || FilterSource != "All";

    // ── Events ───────────────────────────────────────────────

    /// <summary>
    /// Raised whenever filtered groups change so that views can re-render.
    /// </summary>
    public event Action? DataChanged;

    // ── Commands ─────────────────────────────────────────────

    /// <summary>
    /// Runs the startup analyzer on a background thread and applies filters to the result.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            AnalysisData = await Task.Run(() => _analyzer.Analyze());
            ApplyFiltersAndSort();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Resets every filter property to its default value and clears the search query.
    /// </summary>
    [RelayCommand]
    private void ResetFilters()
    {
        SearchQuery = "";
        SortBy = "Impact";
        FilterImpact = "All";
        FilterStatus = "All";
        FilterTime = "All";
        FilterSource = "All";
    }

    /// <summary>
    /// Disables the specified startup item through the disable service.
    /// </summary>
    /// <param name="item">The startup item to disable. Must not be <see langword="null"/>.</param>
    [RelayCommand]
    private void DisableItem(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var (success, _) = _disableService.Disable(item);
        if (success)
        {
            item.IsEnabled = false;
            ApplyFiltersAndSort();
        }
    }

    // ── Property-changed hooks ───────────────────────────────

    /// <inheritdoc cref="OnSearchQueryChanged"/>
    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();

    /// <inheritdoc cref="OnSortByChanged"/>
    partial void OnSortByChanged(string value) => ApplyFiltersAndSort();

    /// <inheritdoc cref="OnFilterImpactChanged"/>
    partial void OnFilterImpactChanged(string value) => ApplyFiltersAndSort();

    /// <inheritdoc cref="OnFilterStatusChanged"/>
    partial void OnFilterStatusChanged(string value) => ApplyFiltersAndSort();

    /// <inheritdoc cref="OnFilterTimeChanged"/>
    partial void OnFilterTimeChanged(string value) => ApplyFiltersAndSort();

    /// <inheritdoc cref="OnFilterSourceChanged"/>
    partial void OnFilterSourceChanged(string value) => ApplyFiltersAndSort();

    // ── Filtering pipeline ───────────────────────────────────

    /// <summary>
    /// Re-filters, re-sorts, and re-groups all items, then raises <see cref="DataChanged"/>.
    /// </summary>
    private void ApplyFiltersAndSort()
    {
        if (AnalysisData is null)
        {
            FilteredGroups = new();
            DataChanged?.Invoke();
            return;
        }

        var items = FilterItems(AnalysisData.Items);
        FilteredGroups = BuildGroups(items);
        DataChanged?.Invoke();
    }

    /// <summary>
    /// Applies all active filters to the source list and returns the surviving items.
    /// </summary>
    private List<StartupItem> FilterItems(List<StartupItem> items)
    {
        IEnumerable<StartupItem> filtered = items;
        filtered = ApplySearchFilter(filtered);
        filtered = ApplyImpactFilter(filtered);
        filtered = ApplyStatusFilter(filtered);
        filtered = ApplyTimeFilter(filtered);
        filtered = ApplySourceFilter(filtered);
        return filtered.ToList();
    }

    /// <summary>Filters items whose name, publisher, or description matches the search query.</summary>
    private IEnumerable<StartupItem> ApplySearchFilter(IEnumerable<StartupItem> items)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return items;

        var query = SearchQuery.Trim();
        return items.Where(i =>
            i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || i.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
            || i.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filters items by impact level.</summary>
    private IEnumerable<StartupItem> ApplyImpactFilter(IEnumerable<StartupItem> items)
    {
        return FilterImpact == "All"
            ? items
            : items.Where(i => i.Impact.Equals(FilterImpact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filters items by enabled/disabled status.</summary>
    private IEnumerable<StartupItem> ApplyStatusFilter(IEnumerable<StartupItem> items)
    {
        return FilterStatus switch
        {
            "Enabled" => items.Where(i => i.IsEnabled),
            "Disabled" => items.Where(i => !i.IsEnabled),
            _ => items,
        };
    }

    /// <summary>Filters items by minimum startup time threshold.</summary>
    private IEnumerable<StartupItem> ApplyTimeFilter(IEnumerable<StartupItem> items)
    {
        return FilterTime switch
        {
            "> 5s" => items.Where(i => i.StartupTimeMs > 5000),
            "> 2s" => items.Where(i => i.StartupTimeMs > 2000),
            "> 1s" => items.Where(i => i.StartupTimeMs > 1000),
            _ => items,
        };
    }

    /// <summary>Filters items by timing data source.</summary>
    private IEnumerable<StartupItem> ApplySourceFilter(IEnumerable<StartupItem> items)
    {
        return FilterSource switch
        {
            "Measured" => items.Where(i => i.TimingSource == "Measured"),
            "Process" => items.Where(i => i.TimingSource == "Process"),
            "Estimated" => items.Where(i => i.TimingSource == "Estimated"),
            _ => items,
        };
    }

    // ── Grouping ─────────────────────────────────────────────

    /// <summary>
    /// Builds the five standard groups from filtered items, applying the current sort order.
    /// </summary>
    private List<StartupItemGroup> BuildGroups(List<StartupItem> items)
    {
        var hasFilter = HasActiveFilters;

        var groups = new List<StartupItemGroup>
        {
            CreateGroup("safe_to_disable", "✅ Safe to Disable",
                "These can be disabled with no impact", "#4ade80",
                isExpanded: true, enabledFilter: true, items, hasFilter),

            CreateGroup("can_disable", "⚡ Consider Disabling",
                "Review before disabling", "#fcd34d",
                isExpanded: true, enabledFilter: true, items, hasFilter),

            CreateGroup("review", "🔍 Review",
                "May be needed — check before disabling", "#7d9bff",
                isExpanded: hasFilter, enabledFilter: null, items, hasFilter),

            CreateGroup("keep", "🔒 System Essential",
                "Required for normal system operation", "#f87171",
                isExpanded: hasFilter, enabledFilter: null, items, hasFilter),

            BuildDisabledGroup(items, hasFilter),
        };

        return groups;
    }

    /// <summary>
    /// Creates a single group by filtering items that match <paramref name="action"/>
    /// and optionally restricting by enabled state.
    /// </summary>
    private StartupItemGroup CreateGroup(
        string key, string title, string description, string badgeColor,
        bool isExpanded, bool? enabledFilter,
        List<StartupItem> items, bool hasFilter)
    {
        var groupItems = items.Where(i => i.Action == key);
        groupItems = ApplyEnabledFilter(groupItems, enabledFilter);

        return new StartupItemGroup
        {
            Key = key,
            Title = title,
            Description = description,
            BadgeColor = badgeColor,
            IsExpanded = isExpanded,
            EnabledFilter = enabledFilter,
            Items = SortItems(groupItems).ToList(),
        };
    }

    /// <summary>
    /// Builds the special "_disabled" group that collects ALL disabled items regardless of action.
    /// </summary>
    private StartupItemGroup BuildDisabledGroup(List<StartupItem> items, bool hasFilter)
    {
        var disabled = items.Where(i => !i.IsEnabled);
        return new StartupItemGroup
        {
            Key = "_disabled",
            Title = "⊘ Already Disabled",
            Description = "Currently disabled startup items",
            BadgeColor = "#a0a4b8",
            IsExpanded = hasFilter,
            EnabledFilter = false,
            Items = SortItems(disabled).ToList(),
        };
    }

    /// <summary>Applies enabled/disabled filtering when <paramref name="enabledFilter"/> is non-null.</summary>
    private static IEnumerable<StartupItem> ApplyEnabledFilter(
        IEnumerable<StartupItem> items, bool? enabledFilter)
    {
        return enabledFilter switch
        {
            true => items.Where(i => i.IsEnabled),
            false => items.Where(i => !i.IsEnabled),
            _ => items,
        };
    }

    // ── Sorting ──────────────────────────────────────────────

    /// <summary>
    /// Sorts items according to the current <see cref="SortBy"/> value.
    /// </summary>
    private IEnumerable<StartupItem> SortItems(IEnumerable<StartupItem> items)
    {
        return SortBy switch
        {
            "Startup Time" => items
                .OrderByDescending(i => i.StartupTimeMs)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),

            "Name" => items
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),

            "Status" => items
                .OrderByDescending(i => i.IsEnabled)
                .ThenByDescending(i => i.IsRunning)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),

            // "Impact" (default)
            _ => items
                .OrderByDescending(i => ImpactRank(i.Impact))
                .ThenByDescending(i => i.StartupTimeMs),
        };
    }

    /// <summary>Maps an impact string to a numeric rank for sorting (higher = worse).</summary>
    private static int ImpactRank(string impact)
    {
        return impact?.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0,
        };
    }
}

/// <summary>
/// Represents a logical grouping of startup items with display metadata.
/// </summary>
public class StartupItemGroup
{
    /// <summary>Machine-readable group key (e.g. "safe_to_disable", "_disabled").</summary>
    public string Key { get; init; } = "";

    /// <summary>Human-readable title shown in the group header.</summary>
    public string Title { get; init; } = "";

    /// <summary>Short description shown below the title.</summary>
    public string Description { get; init; } = "";

    /// <summary>Hex color string for the count badge (e.g. "#4ade80").</summary>
    public string BadgeColor { get; init; } = "";

    /// <summary>Whether the group is expanded by default.</summary>
    public bool IsExpanded { get; init; }

    /// <summary>
    /// Optional enabled filter: <see langword="true"/> = only enabled,
    /// <see langword="false"/> = only disabled, <see langword="null"/> = all.
    /// </summary>
    public bool? EnabledFilter { get; init; }

    /// <summary>Startup items belonging to this group.</summary>
    public List<StartupItem> Items { get; set; } = new();
}
