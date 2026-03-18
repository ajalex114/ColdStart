namespace ColdStart.Services.Interfaces;
using ColdStart.Models;

/// <summary>
/// Analyzes Windows startup items from Registry, Startup Folder, Scheduled Tasks, Services, and UWP apps.
/// </summary>
public interface IStartupAnalyzerService
{
    /// <summary>
    /// Performs a full startup analysis, discovering items from all sources and correlating timing data.
    /// </summary>
    /// <returns>Complete analysis including items, boot diagnostics, and summary statistics.</returns>
    StartupAnalysis Analyze();
}
