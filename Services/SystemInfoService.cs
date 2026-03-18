using System.Management;
using System.Runtime.InteropServices;
using ColdStart.Models;

namespace ColdStart.Services;

public class SystemInfoService
{
    public SystemInfo GetSystemInfo()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var bootTime = DateTime.Now - uptime;
        string uptimeStr = uptime.Days > 0
            ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
            : uptime.Hours > 0
                ? $"{uptime.Hours}h {uptime.Minutes}m"
                : $"{uptime.Minutes}m";

        return new SystemInfo
        {
            Hostname = Environment.MachineName,
            Os = GetWmi("Win32_OperatingSystem", "Caption") ?? RuntimeInformation.OSDescription,
            Cpu = GetWmi("Win32_Processor", "Name") ?? "Unknown CPU",
            Cores = Environment.ProcessorCount,
            RamTotalGb = Math.Round(GetTotalRam() / (1024.0 * 1024 * 1024), 1),
            Uptime = uptimeStr,
            BootTime = bootTime.ToString("MMM dd, yyyy 'at' hh:mm tt"),
        };
    }

    private static string? GetWmi(string wmiClass, string property)
    {
        try
        {
            using var mos = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (var obj in mos.Get())
                return obj[property]?.ToString()?.Trim();
        }
        catch { }
        return null;
    }

    private static long GetTotalRam()
    {
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in mos.Get())
                return Convert.ToInt64(obj["TotalPhysicalMemory"]);
        }
        catch { }
        return 0;
    }
}
