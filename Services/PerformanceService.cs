using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using ColdStart.Models;

namespace ColdStart.Services;

public class PerformanceService
{
    public PerformanceData GetPerformance()
    {
        return new PerformanceData
        {
            Cpu = GetCpuInfo(),
            Memory = GetMemoryInfo(),
            Disk = GetDiskInfo(),
            Network = GetNetworkInfo(),
            Processes = GetTopProcesses(15),
        };
    }

    private static CpuInfo GetCpuInfo()
    {
        var info = new CpuInfo { CoreCount = Environment.ProcessorCount };
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT LoadPercentage, CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in mos.Get())
            {
                info.Overall = Convert.ToDouble(obj["LoadPercentage"] ?? 0);
                info.FreqMhz = Convert.ToDouble(obj["CurrentClockSpeed"] ?? 0);
            }
        }
        catch { }
        return info;
    }

    private static MemoryInfo GetMemoryInfo()
    {
        var mem = new MemoryInfo();
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in mos.Get())
            {
                double totalKb = Convert.ToDouble(obj["TotalVisibleMemorySize"] ?? 0);
                double freeKb = Convert.ToDouble(obj["FreePhysicalMemory"] ?? 0);
                mem.TotalGb = Math.Round(totalKb / (1024 * 1024), 1);
                mem.AvailableGb = Math.Round(freeKb / (1024 * 1024), 1);
                mem.UsedGb = Math.Round(mem.TotalGb - mem.AvailableGb, 1);
                mem.Percent = mem.TotalGb > 0 ? Math.Round(mem.UsedGb / mem.TotalGb * 100, 1) : 0;
            }
        }
        catch { }
        return mem;
    }

    private static DiskInfo GetDiskInfo()
    {
        var disk = new DiskInfo();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            try
            {
                var totalGb = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 1);
                var usedGb = Math.Round((drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024 * 1024), 1);
                disk.Partitions.Add(new DiskPartition
                {
                    Drive = drive.Name,
                    Label = drive.VolumeLabel,
                    TotalGb = totalGb,
                    UsedGb = usedGb,
                    Percent = totalGb > 0 ? Math.Round(usedGb / totalGb * 100, 1) : 0,
                });
            }
            catch { }
        }
        return disk;
    }

    private static NetworkInfo GetNetworkInfo()
    {
        var net = new NetworkInfo();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var stats = ni.GetIPStatistics();
                net.SentMb += stats.BytesSent / (1024.0 * 1024);
                net.RecvMb += stats.BytesReceived / (1024.0 * 1024);
            }
            net.SentMb = Math.Round(net.SentMb, 1);
            net.RecvMb = Math.Round(net.RecvMb, 1);
        }
        catch { }
        return net;
    }

    private static List<ProcessInfo> GetTopProcesses(int count)
    {
        var procs = new List<ProcessInfo>();
        double totalMemKb = 0;

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in mos.Get())
                totalMemKb = Convert.ToDouble(obj["TotalVisibleMemorySize"] ?? 0);
        }
        catch { }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue;
                var memMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024), 1);
                procs.Add(new ProcessInfo
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    MemMb = memMb,
                    MemPercent = totalMemKb > 0 ? Math.Round(memMb * 1024 / totalMemKb * 100, 1) : 0,
                    Status = p.HasExited ? "Exited" : "Running",
                });
            }
            catch { }
            finally { p.Dispose(); }
        }

        return procs.OrderByDescending(p => p.MemMb).Take(count).ToList();
    }
}
