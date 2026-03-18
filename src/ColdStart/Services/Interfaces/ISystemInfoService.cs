namespace ColdStart.Services.Interfaces;
using ColdStart.Models;

/// <summary>
/// Retrieves system hardware and software information via WMI.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Gets current system information including hostname, OS, CPU, RAM, and boot time.
    /// </summary>
    SystemInfo GetSystemInfo();
}
