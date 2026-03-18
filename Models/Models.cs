namespace ColdStart.Models;

public enum DisableMethod { Registry, StartupFolder, ScheduledTask, Service, Unknown }

public class SystemInfo
{
    public string Hostname { get; set; } = "";
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public double RamTotalGb { get; set; }
    public string Uptime { get; set; } = "";
    public string BootTime { get; set; } = "";
}

public class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Source { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Description { get; set; } = "";
    public double SizeMb { get; set; }
    public bool IsRunning { get; set; }
    public bool IsEnabled { get; set; } = true; // Whether startup is currently enabled

    // Classification
    public string Category { get; set; } = "";
    public bool Essential { get; set; }
    public string Impact { get; set; } = "low";
    public string Suggestion { get; set; } = "";
    public string Action { get; set; } = "review";
    public string HowToDisable { get; set; } = "";

    // Layman-friendly details
    public string WhatItDoes { get; set; } = "";
    public string IfDisabled { get; set; } = "";
    public string WhySlow { get; set; } = "";
    public string HowToSpeedUp { get; set; } = "";

    // Boot timing (from Event Log correlation or process measurement)
    public long StartupTimeMs { get; set; }
    public bool HasStartupTime => StartupTimeMs > 0;
    public string TimingSource { get; set; } = ""; // "Measured", "Process", or ""

    // Boot-relative process start time (ms after boot)
    public long BootOffsetMs { get; set; }
    public bool HasBootOffset => BootOffsetMs > 0;

    // Disable support
    public DisableMethod DisableMethod { get; set; } = DisableMethod.Unknown;
    public string RegistryKeyPath { get; set; } = "";
    public string RegistryValueName { get; set; } = "";
    public string ShortcutPath { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string TaskFullPath { get; set; } = "";
}

public class BootDiagnostics
{
    public bool Available { get; set; }
    public long BootDurationMs { get; set; }
    public long MainPathMs { get; set; }
    public long PostBootMs { get; set; }
    public List<DegradingApp> DegradingApps { get; set; } = new();
}

public class DegradingApp
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long TotalMs { get; set; }
    public long DegradationMs { get; set; }
}

public class StartupAnalysis
{
    public List<StartupItem> Items { get; set; } = new();
    public BootDiagnostics Diagnostics { get; set; } = new();
    public StartupSummary Summary { get; set; } = new();
}

public class StartupSummary
{
    public int Total { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public int CanDisable { get; set; }
    public double EstimatedSavingsSec { get; set; }
}

public class PerformanceData
{
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public DiskInfo Disk { get; set; } = new();
    public NetworkInfo Network { get; set; } = new();
    public List<ProcessInfo> Processes { get; set; } = new();
}

public class CpuInfo
{
    public double Overall { get; set; }
    public int CoreCount { get; set; }
    public double FreqMhz { get; set; }
}

public class MemoryInfo
{
    public double TotalGb { get; set; }
    public double UsedGb { get; set; }
    public double AvailableGb { get; set; }
    public double Percent { get; set; }
}

public class DiskInfo
{
    public List<DiskPartition> Partitions { get; set; } = new();
}

public class DiskPartition
{
    public string Drive { get; set; } = "";
    public string Label { get; set; } = "";
    public double TotalGb { get; set; }
    public double UsedGb { get; set; }
    public double Percent { get; set; }
}

public class NetworkInfo
{
    public double SentMb { get; set; }
    public double RecvMb { get; set; }
}

public class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemPercent { get; set; }
    public double MemMb { get; set; }
    public string Status { get; set; } = "";
}

public class StartupGroup
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string BadgeColor { get; set; } = "";
    public bool IsExpanded { get; set; }
    public List<StartupItem> Items { get; set; } = new();
}

// ── App Usage Models ──────────────────────────────────────
public class AppUsageData
{
    public List<AppGroup> Groups { get; set; } = new();
    public List<AppUsageEntry> Apps { get; set; } = new();
    public double TotalMemoryGb { get; set; }
    public double UsedMemoryGb { get; set; }
    public int TotalProcesses { get; set; }
}

public class AppGroup
{
    public string FriendlyName { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public List<AppUsageEntry> Processes { get; set; } = new();

    // Aggregates
    public double TotalMemoryMb => Processes.Sum(p => p.MemoryMb);
    public double TotalCpuPercent => Processes.Sum(p => p.CpuPercent);
    public TimeSpan TotalCpuTime => Processes.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.TotalCpuTime);
    public int TotalInstances => Processes.Sum(p => p.InstanceCount);
    public DateTime EarliestStart => Processes.Min(p => p.FirstStarted);
    public TimeSpan SessionDuration => DateTime.Now - EarliestStart;
    public bool IsStartupApp => Processes.Any(p => p.IsStartupApp);
    public string Publisher => Processes.FirstOrDefault(p => !string.IsNullOrEmpty(p.Publisher))?.Publisher ?? "";
}

public class AppUsageEntry
{
    public string Name { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Publisher { get; set; } = "";
    public int InstanceCount { get; set; }

    // Memory
    public double MemoryMb { get; set; }
    public double PeakMemoryMb { get; set; }
    public double MemoryPercent { get; set; }

    // CPU
    public double CpuPercent { get; set; }
    public TimeSpan TotalCpuTime { get; set; }

    // Session
    public DateTime FirstStarted { get; set; }
    public TimeSpan SessionDuration { get; set; }

    // Is a startup app?
    public bool IsStartupApp { get; set; }
    public long StartupTimeMs { get; set; }
}
