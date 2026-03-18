namespace ColdStart.Services.Interfaces;
using ColdStart.Models;

/// <summary>
/// Monitors real-time application resource usage including memory, CPU, and session duration.
/// </summary>
public interface IAppUsageService
{
    /// <summary>
    /// Gathers app usage data by taking two CPU snapshots ~1s apart.
    /// </summary>
    /// <param name="startupItems">Optional startup items for cross-referencing. Can be null.</param>
    /// <returns>App usage data including groups, memory totals, and process counts.</returns>
    AppUsageData GetAppUsage(List<StartupItem>? startupItems = null);
}
