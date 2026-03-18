namespace ColdStart.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ColdStart.Models;

/// <summary>
/// Prepares and holds timeline data derived from a <see cref="StartupAnalysis"/>,
/// including per-item timing entries, boot diagnostics, and tick calculations.
/// </summary>
public partial class TimelineViewModel : ObservableObject
{
    private static readonly int[] TickCandidatesSec = { 5, 10, 15, 20, 30, 60, 120 };
    private const long MaxPhase1OffsetMs = 180_000;
    private const long MinTotalMs = 10_000;
    private const long SnapMs = 5_000;

    // ── Observable state ─────────────────────────────────────

    /// <summary>Indicates whether timeline data has been computed.</summary>
    [ObservableProperty]
    private bool _isPrepared;

    // ── Processed data ───────────────────────────────────────

    /// <summary>Ordered list of timeline entries (Phase 1 + Phase 2).</summary>
    public List<TimelineEntry> Entries { get; private set; } = new();

    /// <summary>Boot diagnostics extracted from the analysis, if available.</summary>
    public BootDiagnostics? Diagnostics { get; private set; }

    /// <summary>Estimated machine boot time.</summary>
    public DateTime BootTime { get; private set; }

    /// <summary>Maximum end time across all entries (ms).</summary>
    public long MaxEndMs { get; private set; }

    /// <summary>Total timeline duration snapped to the next 5-second boundary (ms).</summary>
    public long TotalMs { get; private set; }

    // ── Tick state ───────────────────────────────────────────

    /// <summary>Milliseconds between timeline tick marks.</summary>
    public int TickIntervalMs { get; private set; }

    /// <summary>Number of tick marks to render.</summary>
    public int TickCount { get; private set; }

    // ── Stats ────────────────────────────────────────────────

    /// <summary>Total number of timeline entries.</summary>
    public int TotalApps => Entries.Count;

    /// <summary>Number of high-impact entries.</summary>
    public int HighImpactCount => Entries.Count(e =>
        e.Impact.Equals("high", StringComparison.OrdinalIgnoreCase));

    /// <summary>Number of medium-impact entries.</summary>
    public int MediumImpactCount => Entries.Count(e =>
        e.Impact.Equals("medium", StringComparison.OrdinalIgnoreCase));

    // ── Events ───────────────────────────────────────────────

    /// <summary>
    /// Raised when timeline data changes so that views can re-render.
    /// </summary>
    public event Action? DataChanged;

    // ── Public API ───────────────────────────────────────────

    /// <summary>
    /// Prepares timeline data from the supplied startup analysis.
    /// Phase 1 contains items with measured boot offsets; Phase 2 assigns synthetic offsets
    /// to remaining enabled items.
    /// </summary>
    /// <param name="data">
    /// The startup analysis to visualize. When <see langword="null"/> the timeline is cleared.
    /// </param>
    public void PrepareTimeline(StartupAnalysis? data)
    {
        if (data is null)
        {
            ClearTimeline();
            return;
        }

        Diagnostics = data.Diagnostics;
        BootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

        var phase1 = BuildPhase1Entries(data.Items);
        var phase2 = BuildPhase2Entries(data.Items, phase1);

        Entries = phase1.Concat(phase2)
            .OrderBy(e => e.StartMs)
            .ToList();

        ComputeTimeRange();
        IsPrepared = true;
        DataChanged?.Invoke();
    }

    /// <summary>
    /// Recalculates tick intervals based on the available rendering width in pixels.
    /// </summary>
    /// <param name="availableWidth">Pixel width of the timeline area. Must be positive.</param>
    public void CalculateTicks(double availableWidth)
    {
        if (availableWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth), availableWidth,
                "Available width must be positive.");

        if (TotalMs <= 0)
        {
            TickIntervalMs = 5000;
            TickCount = 0;
            return;
        }

        int maxVisibleTicks = Math.Max(2, (int)(availableWidth / 80));
        TickIntervalMs = ChooseTickInterval(maxVisibleTicks);
        TickCount = (int)(TotalMs / TickIntervalMs);
    }

    // ── Private helpers ──────────────────────────────────────

    /// <summary>
    /// Clears all timeline state and raises <see cref="DataChanged"/>.
    /// </summary>
    private void ClearTimeline()
    {
        Entries = new();
        Diagnostics = null;
        MaxEndMs = 0;
        TotalMs = 0;
        IsPrepared = false;
        DataChanged?.Invoke();
    }

    /// <summary>
    /// Creates Phase-1 entries from items that have a real boot offset within the threshold.
    /// </summary>
    private static List<TimelineEntry> BuildPhase1Entries(List<StartupItem> items)
    {
        return items
            .Where(i => i.HasBootOffset && i.BootOffsetMs <= MaxPhase1OffsetMs)
            .Select(i => new TimelineEntry
            {
                Name = i.Name,
                StartMs = i.BootOffsetMs,
                DurationMs = ResolveDuration(i),
                Impact = i.Impact,
                Source = i.TimingSource,
            })
            .ToList();
    }

    /// <summary>
    /// Creates Phase-2 entries with synthetic offsets for enabled items not in Phase 1.
    /// </summary>
    private static List<TimelineEntry> BuildPhase2Entries(
        List<StartupItem> items, List<TimelineEntry> phase1)
    {
        var phase1Names = new HashSet<string>(
            phase1.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

        long syntheticStart = phase1.Count > 0
            ? phase1.Max(e => e.StartMs + e.DurationMs) + 500
            : 0;

        var entries = new List<TimelineEntry>();
        foreach (var item in items.Where(i => i.IsEnabled && !phase1Names.Contains(i.Name)))
        {
            long duration = ResolveDuration(item);
            entries.Add(new TimelineEntry
            {
                Name = item.Name,
                StartMs = syntheticStart,
                DurationMs = duration,
                Impact = item.Impact,
                Source = "Synthetic",
            });
            syntheticStart += duration + 200;
        }

        return entries;
    }

    /// <summary>
    /// Resolves the visual duration for a startup item based on its timing source.
    /// </summary>
    private static long ResolveDuration(StartupItem item)
    {
        if (item.HasStartupTime)
            return Math.Max(item.StartupTimeMs, 300);

        return item.TimingSource switch
        {
            "Process" => 1500,
            _ => 800,
        };
    }

    /// <summary>
    /// Computes <see cref="MaxEndMs"/> and <see cref="TotalMs"/> from current entries.
    /// </summary>
    private void ComputeTimeRange()
    {
        MaxEndMs = Entries.Count > 0
            ? Entries.Max(e => e.StartMs + e.DurationMs)
            : 0;

        TotalMs = Math.Max(MinTotalMs, ((MaxEndMs / SnapMs) + 1) * SnapMs);
    }

    /// <summary>
    /// Selects the best tick interval (in ms) so that the total tick count stays within
    /// <paramref name="maxTicks"/>.
    /// </summary>
    private int ChooseTickInterval(int maxTicks)
    {
        foreach (int candidateSec in TickCandidatesSec)
        {
            int intervalMs = candidateSec * 1000;
            if (TotalMs / intervalMs <= maxTicks)
                return intervalMs;
        }

        return TickCandidatesSec[^1] * 1000;
    }
}

/// <summary>
/// Represents a single entry on the boot timeline.
/// </summary>
public class TimelineEntry
{
    /// <summary>Display name of the startup item.</summary>
    public string Name { get; init; } = "";

    /// <summary>Start offset in milliseconds from boot.</summary>
    public long StartMs { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Impact level: "high", "medium", or "low".</summary>
    public string Impact { get; init; } = "low";

    /// <summary>Timing source (e.g. "Measured", "Process", "Synthetic").</summary>
    public string Source { get; init; } = "";
}
