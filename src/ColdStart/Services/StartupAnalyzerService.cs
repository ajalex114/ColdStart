using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Management;
using ColdStart.Models;
using ColdStart.Services.Interfaces;
using Microsoft.Win32;

namespace ColdStart.Services;

/// <summary>
/// Analyzes Windows startup items from Registry, Startup Folder, Scheduled Tasks, Services, and UWP apps.
/// </summary>
public class StartupAnalyzerService : IStartupAnalyzerService
{
    /// <inheritdoc />
    public StartupAnalysis Analyze()
    {
        var runningProcesses = GetRunningProcessNames();
        var processStartTimes = GetProcessStartTimes();
        var items = new List<StartupItem>();

        items.AddRange(GetRegistryStartups(runningProcesses));
        items.AddRange(GetStartupFolderItems(runningProcesses));
        items.AddRange(GetScheduledTaskStartups());
        items.AddRange(GetThirdPartyAutoStartServices());
        items.AddRange(GetUwpStartupApps(runningProcesses));

        // De-duplicate by name
        items = items
            .GroupBy(i => i.Name.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

        var diagnostics = GetBootDiagnostics();

        // Phase 1: Correlate Event Log degradation times (most accurate)
        CorrelateBootTiming(items, diagnostics.DegradingApps);

        // Phase 2: For items still without timing, use process start time relative to boot
        CorrelateProcessTiming(items, processStartTimes);

        // Phase 3: Discard process times that are clearly not boot-related (>5 min),
        // then estimate timing for any remaining items without data
        foreach (var item in items)
        {
            if (item.TimingSource == "Process" && item.StartupTimeMs > 300_000)
            {
                // Process was restarted long after boot — not a startup measurement
                item.StartupTimeMs = 0;
                item.TimingSource = "";
            }

            if (item.StartupTimeMs == 0 && !item.Essential)
            {
                item.StartupTimeMs = item.Impact switch
                {
                    "high"   => 4000, // ~4s typical for heavy apps
                    "medium" => 1500, // ~1.5s typical
                    _        => 500,  // ~0.5s typical
                };
                item.TimingSource = "Estimated";
            }
        }

        var high = items.Count(i => i.Impact == "high" && !i.Essential);
        var med = items.Count(i => i.Impact == "medium" && !i.Essential);
        var canDisable = items.Count(i => i.Action is "can_disable" or "safe_to_disable");

        // Estimate savings using actual degradation data where available,
        // impact-based estimates otherwise. "Process" timing is boot-offset, not duration,
        // so it cannot be used for savings.
        var estSavingsMs = items
            .Where(i => !i.Essential && i.Action is "can_disable" or "safe_to_disable" or "review")
            .Sum(i => i.TimingSource switch
            {
                "Measured" => i.StartupTimeMs,  // real degradation time from event log
                _ => i.Impact switch            // fallback to impact-based estimate
                {
                    "high"   => 4000L,
                    "medium" => 1500L,
                    _        => 500L,
                },
            });
        var estSavings = Math.Round(estSavingsMs / 1000.0, 1);

        return new StartupAnalysis
        {
            Items = items,
            Diagnostics = diagnostics,
            Summary = new StartupSummary
            {
                Total = items.Count,
                High = high,
                Medium = med,
                Low = items.Count - high - med,
                CanDisable = canDisable,
                EstimatedSavingsSec = estSavings,
            },
        };
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch { }
            finally { p.Dispose(); }
        }
        return names;
    }

    // ── Correlate Event Log boot timing with discovered items ───
    private static void CorrelateBootTiming(List<StartupItem> items, List<DegradingApp> degradingApps)
    {
        foreach (var deg in degradingApps)
        {
            var degNameLower = deg.Name.ToLowerInvariant();
            var degPathLower = (deg.Path ?? "").ToLowerInvariant();
            var degFileName = Path.GetFileNameWithoutExtension(degPathLower);

            foreach (var item in items)
            {
                if (item.StartupTimeMs > 0) continue; // Already matched

                var itemNameLower = item.Name.ToLowerInvariant();
                var itemCmdLower = (item.Command ?? "").ToLowerInvariant();
                var itemExeName = Path.GetFileNameWithoutExtension(
                    ExtractExePath(item.Command)).ToLowerInvariant();

                // Match by: friendly name contains, exe filename match, or path match
                bool match = false;
                if (!string.IsNullOrEmpty(degFileName) && !string.IsNullOrEmpty(itemExeName))
                    match = degFileName == itemExeName;
                if (!match && degNameLower.Contains(itemNameLower))
                    match = true;
                if (!match && itemNameLower.Contains(degNameLower))
                    match = true;
                if (!match && !string.IsNullOrEmpty(degPathLower) && itemCmdLower.Contains(degPathLower))
                    match = true;

                if (match)
                {
                    item.StartupTimeMs = deg.TotalMs;
                    item.TimingSource = "Measured";
                    break;
                }
            }
        }
    }

    // ── Collect process start times relative to boot ────────────
    private static Dictionary<string, long> GetProcessStartTimes()
    {
        var bootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var times = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                var name = p.ProcessName;
                var startTime = p.StartTime;
                var delayMs = (long)(startTime - bootTime).TotalMilliseconds;
                if (delayMs < 0) delayMs = 0;

                // Keep the earliest start time for each process name
                // Only count processes that started within 10 minutes of boot (startup-relevant)
                if (delayMs <= 600_000 && (!times.ContainsKey(name) || delayMs < times[name]))
                    times[name] = delayMs;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return times;
    }

    // ── For items without Event Log data, use process start time ─
    // Also sets BootOffsetMs on ALL items for timeline visualization
    private static void CorrelateProcessTiming(List<StartupItem> items, Dictionary<string, long> processStartTimes)
    {
        foreach (var item in items)
        {
            var exeName = Path.GetFileNameWithoutExtension(
                ExtractExePath(item.Command)).ToLowerInvariant();

            long? bootOffset = null;

            // Try exact match first
            if (!string.IsNullOrEmpty(exeName) && processStartTimes.TryGetValue(exeName, out var delayMs))
            {
                bootOffset = delayMs;
            }
            else
            {
                // Try matching by item name
                var itemNameLower = item.Name.ToLowerInvariant();
                foreach (var (procName, ms) in processStartTimes)
                {
                    if (procName.ToLowerInvariant().Contains(itemNameLower) ||
                        itemNameLower.Contains(procName.ToLowerInvariant()))
                    {
                        bootOffset = ms;
                        break;
                    }
                }
            }

            // Always capture boot offset for timeline visualization
            if (bootOffset.HasValue)
                item.BootOffsetMs = bootOffset.Value;

            // Set startup time only for items without existing timing that are running
            if (item.StartupTimeMs > 0 || !item.IsRunning) continue;

            if (bootOffset.HasValue)
            {
                item.StartupTimeMs = bootOffset.Value;
                item.TimingSource = "Process";
            }
        }
    }

    // ── Registry Startups ───────────────────────────────────────
    private static List<StartupItem> GetRegistryStartups(HashSet<string> running)
    {
        var items = new List<StartupItem>();

        // Read the StartupApproved keys to know enabled/disabled state
        var disabledItems = GetDisabledStartupNames();

        var regPaths = new (RegistryKey Root, string Path, string Scope, string FullPrefix)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "All Users",
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Current User",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "All Users (32-bit)",
                @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
        };

        foreach (var (root, path, scope, fullPrefix) in regPaths)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var command = key.GetValue(valueName)?.ToString() ?? "";
                    var exePath = ExtractExePath(command);
                    var (pub, desc, sizeMb) = GetFileInfo(exePath);
                    var exeName = Path.GetFileNameWithoutExtension(exePath);
                    var profile = AppClassifier.Classify(valueName, command, pub, desc);

                    items.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = command,
                        Scope = scope,
                        Source = "Registry",
                        Publisher = pub,
                        Description = desc,
                        SizeMb = sizeMb,
                        IsRunning = running.Contains(exeName),
                        IsEnabled = !disabledItems.Contains(valueName),
                        Category = profile.Category,
                        Essential = profile.Essential,
                        Impact = profile.Impact,
                        Action = profile.Action,
                        Suggestion = profile.Suggestion,
                        WhatItDoes = profile.WhatItDoes,
                        IfDisabled = profile.IfDisabled,
                        WhySlow = profile.WhySlow,
                        HowToSpeedUp = profile.HowToSpeedUp,
                        HowToDisable = "Task Manager → Startup tab → Find this item → Click Disable",
                        DisableMethod = DisableMethod.Registry,
                        RegistryKeyPath = fullPrefix,
                        RegistryValueName = valueName,
                    });
                }
            }
            catch { }
        }
        return items;
    }

    /// <summary>
    /// Reads the StartupApproved registry to find items disabled via Task Manager.
    /// The binary value's first byte: 02/06 = enabled, 03/07 = disabled.
    /// </summary>
    private static HashSet<string> GetDisabledStartupNames()
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var approvedPaths = new (RegistryKey Root, string Path)[]
        {
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
        };

        foreach (var (root, path) in approvedPaths)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is byte[] data && data.Length >= 1)
                    {
                        // First byte: 02/06 = enabled, 03/07 = disabled
                        if ((data[0] & 0x01) == 0x01)
                            disabled.Add(name);
                    }
                }
            }
            catch { }
        }
        return disabled;
    }

    // ── Startup Folder ──────────────────────────────────────────
    private static List<StartupItem> GetStartupFolderItems(HashSet<string> running)
    {
        var items = new List<StartupItem>();
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var target = file;

                if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    target = ResolveShortcut(file) ?? file;

                var (pub, desc, _) = GetFileInfo(target);
                var exeName = Path.GetFileNameWithoutExtension(target);
                var profile = AppClassifier.Classify(name, target, pub, desc);

                items.Add(new StartupItem
                {
                    Name = name,
                    Command = target,
                    Scope = "User",
                    Source = "Startup Folder",
                    Publisher = pub,
                    Description = desc,
                    IsRunning = running.Contains(exeName),
                    Category = profile.Category,
                    Essential = profile.Essential,
                    Impact = profile.Impact,
                    Action = profile.Action,
                    Suggestion = profile.Suggestion,
                    WhatItDoes = profile.WhatItDoes,
                    IfDisabled = profile.IfDisabled,
                    WhySlow = profile.WhySlow,
                    HowToSpeedUp = profile.HowToSpeedUp,
                    HowToDisable = "Delete the shortcut from your Startup folder (Win+R → shell:startup)",
                    DisableMethod = DisableMethod.StartupFolder,
                    ShortcutPath = file,
                });
            }
        }
        return items;
    }

    // ── Scheduled Tasks ─────────────────────────────────────────
    private static List<StartupItem> GetScheduledTaskStartups()
    {
        var items = new List<StartupItem>();
        try
        {
            using var mos = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\TaskScheduler",
                "SELECT * FROM MSFT_ScheduledTask WHERE State != 1");

            foreach (ManagementObject task in mos.Get())
            {
                try
                {
                    var triggers = task.GetRelated("MSFT_TaskTrigger");
                    bool isBootOrLogon = false;
                    foreach (ManagementObject trig in triggers)
                    {
                        if (trig.ClassPath.ClassName.Contains("Logon") ||
                            trig.ClassPath.ClassName.Contains("Boot"))
                        {
                            isBootOrLogon = true;
                            break;
                        }
                    }
                    if (!isBootOrLogon) continue;

                    var name = task["TaskName"]?.ToString() ?? "";
                    var taskPath = task["TaskPath"]?.ToString() ?? "";
                    var desc = task["Description"]?.ToString() ?? "";
                    if (desc.Length > 200) desc = desc[..200];
                    var author = task["Author"]?.ToString() ?? "Unknown";

                    string exe = "";
                    var actions = task.GetRelated("MSFT_TaskExecAction");
                    foreach (ManagementObject act in actions) { exe = act["Execute"]?.ToString() ?? ""; break; }

                    var profile = AppClassifier.Classify(name, exe, author, desc);
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = exe,
                        Scope = taskPath,
                        Source = "Scheduled Task",
                        Publisher = author,
                        Description = desc,
                        Category = profile.Category,
                        Essential = profile.Essential,
                        Impact = profile.Impact,
                        Action = profile.Action,
                        Suggestion = profile.Suggestion,
                        WhatItDoes = profile.WhatItDoes,
                        IfDisabled = profile.IfDisabled,
                        WhySlow = profile.WhySlow,
                        HowToSpeedUp = profile.HowToSpeedUp,
                        HowToDisable = "Task Scheduler → Find this task → Right-click → Disable",
                        DisableMethod = DisableMethod.ScheduledTask,
                        TaskFullPath = taskPath + name,
                    });
                }
                catch { }
            }
        }
        catch { }
        return items;
    }

    // ── Third-party Auto-Start Services ─────────────────────────
    private static List<StartupItem> GetThirdPartyAutoStartServices()
    {
        var items = new List<StartupItem>();
        try
        {
            using var mos = new ManagementObjectSearcher(
                "SELECT DisplayName, Name, PathName, Description, StartMode, State FROM Win32_Service WHERE StartMode='Auto' AND State='Running'");

            foreach (ManagementObject svc in mos.Get())
            {
                var pathName = svc["PathName"]?.ToString() ?? "";
                var exePath = ExtractExePath(pathName);
                var (pub, _, _) = GetFileInfo(exePath);

                if (pub.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;

                var displayName = svc["DisplayName"]?.ToString() ?? "";
                var svcName = svc["Name"]?.ToString() ?? "";
                var desc = svc["Description"]?.ToString() ?? "";
                if (desc.Length > 200) desc = desc[..200];

                var profile = AppClassifier.Classify(displayName, pathName, pub, desc);
                items.Add(new StartupItem
                {
                    Name = displayName,
                    Command = pathName,
                    Scope = "System",
                    Source = "Service",
                    Publisher = pub,
                    Description = desc,
                    IsRunning = true,
                    Category = profile.Category,
                    Essential = profile.Essential,
                    Impact = profile.Impact,
                    Action = profile.Action,
                    Suggestion = profile.Suggestion,
                    WhatItDoes = profile.WhatItDoes,
                    IfDisabled = profile.IfDisabled,
                    WhySlow = profile.WhySlow,
                    HowToSpeedUp = profile.HowToSpeedUp,
                    HowToDisable = "services.msc → Find this service → Properties → Set Startup type to Manual",
                    DisableMethod = DisableMethod.Service,
                    ServiceName = svcName,
                });
            }
        }
        catch { }
        return items;
    }

    // ── UWP / Store App Startup Tasks ───────────────────────────
    private static List<StartupItem> GetUwpStartupApps(HashSet<string> running)
    {
        var items = new List<StartupItem>();
        // Read the disabled-items set so we can cross-reference
        var disabledRegistry = GetDisabledStartupNames();

        try
        {
            // UWP startup tasks are stored per-package under AppModel\SystemAppData
            var basePath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
            using var baseKey = Registry.CurrentUser.OpenSubKey(basePath);
            if (baseKey == null) return items;

            foreach (var packageFamily in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var pkgKey = baseKey.OpenSubKey(packageFamily);
                    if (pkgKey == null) continue;

                    foreach (var subName in pkgKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var taskKey = pkgKey.OpenSubKey(subName);
                            if (taskKey == null) continue;

                            var stateVal = taskKey.GetValue("State");
                            if (stateVal == null) continue;

                            var state = Convert.ToInt32(stateVal);
                            // State: 0 = disabled by user, 1 = disabled by policy, 2 = enabled, 4 = enabled by policy
                            bool isEnabled = state == 2 || state == 4;

                            // Extract a friendly name from the package family name
                            var friendlyName = ExtractUwpFriendlyName(packageFamily);
                            var profile = AppClassifier.Classify(friendlyName, packageFamily, "Microsoft", "");

                            items.Add(new StartupItem
                            {
                                Name = friendlyName,
                                Command = packageFamily,
                                Scope = "Current User",
                                Source = "Store App",
                                Publisher = "Microsoft",
                                Description = $"UWP/Store app startup task",
                                IsRunning = running.Any(r => packageFamily.Contains(r, StringComparison.OrdinalIgnoreCase)
                                    || r.Contains(friendlyName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)),
                                IsEnabled = isEnabled,
                                Category = profile.Category,
                                Essential = profile.Essential,
                                Impact = profile.Impact,
                                Action = profile.Action,
                                Suggestion = profile.Suggestion,
                                WhatItDoes = profile.WhatItDoes,
                                IfDisabled = profile.IfDisabled,
                                WhySlow = profile.WhySlow,
                                HowToSpeedUp = profile.HowToSpeedUp,
                                HowToDisable = "Task Manager → Startup apps tab → Find this item → Toggle off",
                                DisableMethod = DisableMethod.Unknown, // UWP apps need special handling
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return items;
    }

    /// <summary>
    /// Extracts a human-friendly name from a UWP package family name.
    /// e.g. "Microsoft.WindowsCalculator_8wekyb3d8bbwe" → "Windows Calculator"
    /// </summary>
    private static string ExtractUwpFriendlyName(string packageFamily)
    {
        // Remove the publisher hash suffix (e.g. "_8wekyb3d8bbwe")
        var name = packageFamily;
        var underscoreIdx = name.LastIndexOf('_');
        if (underscoreIdx > 0) name = name[..underscoreIdx];

        // Remove "Microsoft." prefix
        if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            name = name["Microsoft.".Length..];

        // Convert PascalCase / dot-separated to spaces
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '.' || c == '-')
            {
                result.Append(' ');
            }
            else if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                result.Append(' ');
                result.Append(c);
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString().Trim();
    }

    // ── Boot Diagnostics from Event Log ─────────────────────────
    private static BootDiagnostics GetBootDiagnostics()
    {
        var result = new BootDiagnostics();

        try
        {
            var query = new EventLogQuery(
                "Microsoft-Windows-Diagnostics-Performance/Operational",
                PathType.LogName, "*[System[EventID=100]]") { ReverseDirection = true };

            using var reader = new EventLogReader(query);
            var evt = reader.ReadEvent();
            if (evt != null)
            {
                result.Available = true;
                var xml = evt.ToXml();
                result.BootDurationMs = ExtractLong(xml, "BootTime");
                result.MainPathMs = ExtractLong(xml, "MainPathBootTime");
                result.PostBootMs = ExtractLong(xml, "BootPostBootTime");
            }
        }
        catch { }

        try
        {
            var query = new EventLogQuery(
                "Microsoft-Windows-Diagnostics-Performance/Operational",
                PathType.LogName, "*[System[EventID=101]]") { ReverseDirection = true };

            using var reader = new EventLogReader(query);
            int count = 0;
            while (reader.ReadEvent() is { } evt && count < 20)
            {
                var xml = evt.ToXml();
                var totalMs = ExtractLong(xml, "TotalTime");
                var friendlyName = ExtractStr(xml, "FriendlyName");
                var path = ExtractStr(xml, "Name") ?? "";
                var name = friendlyName
                    ?? DeriveNameFromPath(path)
                    ?? "Unknown";

                if (name == "Unknown" || totalMs <= 0)
                    continue;

                result.DegradingApps.Add(new DegradingApp
                {
                    Name = name,
                    Path = path,
                    TotalMs = totalMs,
                    DegradationMs = ExtractLong(xml, "DegradationTime"),
                });
                count++;
            }
        }
        catch { }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";
        var cleaned = command.Replace("\"", "").Replace("'", "");
        var idx = cleaned.IndexOf(" -", StringComparison.Ordinal);
        if (idx < 0) idx = cleaned.IndexOf(" /", StringComparison.Ordinal);
        return (idx > 0 ? cleaned[..idx] : cleaned).Trim();
    }

    private static (string Publisher, string Description, double SizeMb) GetFileInfo(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var fi = new FileInfo(path);
                return (vi.CompanyName ?? "Unknown", vi.FileDescription ?? "",
                        Math.Round(fi.Length / (1024.0 * 1024), 2));
            }
        }
        catch { }
        return ("Unknown", "", 0);
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            var shell = (dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var shortcut = shell.CreateShortcut(lnkPath);
            return shortcut.TargetPath;
        }
        catch { return null; }
    }

    private static long ExtractLong(string xml, string name)
    {
        var marker = $"Name=\"{name}\">";
        var idx = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        var start = idx + marker.Length;
        var end = xml.IndexOf('<', start);
        return end > 0 && long.TryParse(xml[start..end], out var val) ? val : 0;
    }

    private static string? ExtractStr(string xml, string name)
    {
        var marker = $"Name=\"{name}\">";
        var idx = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var end = xml.IndexOf('<', start);
        if (end < 0) return null;
        var val = xml[start..end].Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    /// <summary>
    /// Derives a human-readable name from an executable path (e.g. "C:\...\OneDrive.exe" → "OneDrive").
    /// </summary>
    private static string? DeriveNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(fileName) ? null : fileName;
    }
}
