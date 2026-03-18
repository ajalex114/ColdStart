namespace ColdStart.Services.Interfaces;
using ColdStart.Models;

/// <summary>
/// Provides methods to disable startup items via Registry, Startup Folder, Task Scheduler, or Services.
/// </summary>
public interface IDisableService
{
    /// <summary>
    /// Disables the specified startup item using the appropriate method.
    /// </summary>
    /// <param name="item">The startup item to disable. Must not be null.</param>
    /// <returns>A tuple indicating success and a user-friendly message.</returns>
    (bool Success, string Message) Disable(StartupItem item);
}
