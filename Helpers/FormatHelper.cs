using System;

namespace ColdStart.Helpers;

/// <summary>
/// Provides formatting utilities for time, memory, and duration values.
/// </summary>
public static class FormatHelper
{
    /// <summary>
    /// Formats a millisecond value into a human-readable string.
    /// </summary>
    /// <param name="ms">The duration in milliseconds (must be non-negative).</param>
    /// <returns>
    /// A formatted string such as <c>"234ms"</c>, <c>"2.3s"</c>, or <c>"2m 34s"</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="ms"/> is negative.
    /// </exception>
    public static string Fmt(long ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "Value must be non-negative.");

        if (ms < 1000) return $"{ms}ms";

        var totalSec = ms / 1000.0;
        if (totalSec < 60) return $"{totalSec:F1}s";

        var min = (int)(totalSec / 60);
        var sec = (int)(totalSec % 60);
        return sec > 0 ? $"{min}m {sec}s" : $"{min}m";
    }

    /// <summary>
    /// Formats a megabyte value into a human-readable memory string.
    /// </summary>
    /// <param name="mb">The memory amount in megabytes (must be non-negative).</param>
    /// <returns>
    /// A formatted string such as <c>"1.5 GB"</c>, <c>"234 MB"</c>, or <c>"456 KB"</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="mb"/> is negative.
    /// </exception>
    public static string FmtMem(double mb)
    {
        if (mb < 0)
            throw new ArgumentOutOfRangeException(nameof(mb), mb, "Value must be non-negative.");

        if (mb >= 1024) return $"{mb / 1024:F1} GB";
        if (mb >= 1) return $"{mb:F0} MB";
        return $"{mb * 1024:F0} KB";
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> into a compact human-readable duration string.
    /// </summary>
    /// <param name="ts">The time span to format.</param>
    /// <returns>
    /// A formatted string such as <c>"2d 5h"</c>, <c>"3h 20m"</c>, <c>"5m 30s"</c>, or <c>"12s"</c>.
    /// </returns>
    public static string FmtDuration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalSeconds}s";
    }
}
