using System.Diagnostics;
using System.IO;
using System.Management;
using ColdStart.Models;

namespace ColdStart.Services;

public class AppUsageService
{
    /// <summary>
    /// Gathers app usage data: memory, CPU, session duration for all user-facing apps.
    /// Takes two snapshots ~1s apart to calculate CPU%.
    /// </summary>
    public AppUsageData GetAppUsage(List<StartupItem>? startupItems = null)
    {
        // Snapshot 1: capture CPU times
        var snap1 = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        var processes = Process.GetProcesses();
        foreach (var p in processes)
        {
            try
            {
                snap1[p.Id] = (p.ProcessName, p.TotalProcessorTime);
            }
            catch { }
        }

        // Wait ~1 second for CPU% calculation
        System.Threading.Thread.Sleep(1000);

        // Snapshot 2: full data collection
        var appMap = new Dictionary<string, AppUsageEntry>(StringComparer.OrdinalIgnoreCase);
        int totalProcs = 0;
        var now = DateTime.Now;
        int cpuCount = Environment.ProcessorCount;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || p.Id == 4) continue; // Idle, System
                if (p.HasExited) continue;

                var name = p.ProcessName;

                // Skip known background/system processes
                if (IsSystemProcess(name)) continue;

                totalProcs++;

                // Calculate CPU% from the two snapshots
                double cpuPct = 0;
                if (snap1.TryGetValue(p.Id, out var s1))
                {
                    try
                    {
                        var elapsed = p.TotalProcessorTime - s1.Cpu;
                        cpuPct = elapsed.TotalMilliseconds / (1000.0 * cpuCount) * 100;
                        cpuPct = Math.Clamp(cpuPct, 0, 100);
                    }
                    catch { }
                }

                var memMb = p.WorkingSet64 / (1024.0 * 1024.0);
                var peakMb = 0.0;
                try { peakMb = p.PeakWorkingSet64 / (1024.0 * 1024.0); } catch { }

                DateTime startTime = now;
                try { startTime = p.StartTime; } catch { }

                TimeSpan totalCpu = TimeSpan.Zero;
                try { totalCpu = p.TotalProcessorTime; } catch { }

                string exePath = "";
                try { exePath = p.MainModule?.FileName ?? ""; } catch { }

                if (!appMap.TryGetValue(name, out var entry))
                {
                    entry = new AppUsageEntry
                    {
                        Name = name,
                        ExePath = exePath,
                        FirstStarted = startTime,
                    };
                    appMap[name] = entry;
                }

                entry.InstanceCount++;
                entry.MemoryMb += memMb;
                entry.PeakMemoryMb = Math.Max(entry.PeakMemoryMb, entry.PeakMemoryMb + peakMb);
                entry.CpuPercent += cpuPct;
                entry.TotalCpuTime += totalCpu;

                if (startTime < entry.FirstStarted)
                    entry.FirstStarted = startTime;
            }
            catch { }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        // Get total physical memory for percentage calc
        double totalMemGb = 0, usedMemGb = 0;
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in mos.Get())
            {
                var totalKb = Convert.ToDouble(mo["TotalVisibleMemorySize"]);
                var freeKb = Convert.ToDouble(mo["FreePhysicalMemory"]);
                totalMemGb = totalKb / (1024.0 * 1024.0);
                usedMemGb = (totalKb - freeKb) / (1024.0 * 1024.0);
            }
        }
        catch { }

        double totalMemMb = totalMemGb * 1024;

        // Build startup lookup for cross-reference
        var startupLookup = new Dictionary<string, StartupItem>(StringComparer.OrdinalIgnoreCase);
        if (startupItems != null)
        {
            foreach (var si in startupItems)
            {
                var exeName = Path.GetFileNameWithoutExtension(
                    ExtractExePath(si.Command));
                if (!string.IsNullOrEmpty(exeName) && !startupLookup.ContainsKey(exeName))
                    startupLookup[exeName] = si;
                // Also try item name
                if (!startupLookup.ContainsKey(si.Name))
                    startupLookup[si.Name] = si;
            }
        }

        // Finalize entries
        var apps = new List<AppUsageEntry>();
        foreach (var entry in appMap.Values)
        {
            entry.SessionDuration = now - entry.FirstStarted;
            entry.MemoryPercent = totalMemMb > 0 ? (entry.MemoryMb / totalMemMb) * 100 : 0;

            // Try to get publisher and product name from exe
            if (!string.IsNullOrEmpty(entry.ExePath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(entry.ExePath);
                    entry.Publisher = vi.CompanyName ?? "";
                    // Use FileDescription or ProductName for friendly name
                    var friendly = vi.FileDescription ?? vi.ProductName ?? "";
                    if (!string.IsNullOrWhiteSpace(friendly) && friendly.Length > 2)
                        entry.FriendlyName = friendly;
                }
                catch { }
            }

            // Apply known friendly name overrides
            if (string.IsNullOrEmpty(entry.FriendlyName))
                entry.FriendlyName = GetFriendlyName(entry.Name);

            // Determine group key (parent application)
            entry.GroupKey = DetermineGroupKey(entry.Name, entry.ExePath, entry.Publisher);

            // Cross-reference with startup items
            if (startupLookup.TryGetValue(entry.Name, out var si))
            {
                entry.IsStartupApp = true;
                entry.StartupTimeMs = si.StartupTimeMs;
            }

            // Only include apps using meaningful resources or that are user-facing
            if (entry.MemoryMb >= 1 || entry.CpuPercent > 0)
                apps.Add(entry);
        }

        // Build groups from apps
        var groups = apps
            .GroupBy(a => a.GroupKey)
            .Select(g =>
            {
                var friendlyName = GetGroupFriendlyName(g.Key, g.ToList());
                return new AppGroup
                {
                    GroupKey = g.Key,
                    FriendlyName = friendlyName,
                    Processes = g.OrderByDescending(p => p.MemoryMb).ToList(),
                };
            })
            .OrderByDescending(g => g.TotalMemoryMb)
            .ToList();

        return new AppUsageData
        {
            Apps = apps.OrderByDescending(a => a.MemoryMb).ToList(),
            Groups = groups,
            TotalMemoryGb = totalMemGb,
            UsedMemoryGb = usedMemGb,
            TotalProcesses = totalProcs,
        };
    }

    static bool IsSystemProcess(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "svchost" or "csrss" or "wininit" or "services"
            or "lsass" or "smss" or "dwm" or "fontdrvhost" or "winlogon"
            or "memory compression" or "registry" or "dashost" or "sihost"
            or "ctfmon" or "conhost" or "dllhost" or "taskhostw"
            or "runtimebroker" or "applicationframehost" or "systemsettingsbroker"
            or "searchindexer" or "securityhealthservice" or "spoolsv"
            or "wudfhost" or "audiodg" or "msdtc" or "vds"
            or "lsaiso" or "sgrmbroker" or "unsecapp" or "wmiprvse"
            or "searchhost" or "startmenuexperiencehost"
            or "textinputhost" or "lockapp" or "shellexperiencehost"
            or "backgroundtaskhost" or "cexecsvc" or "wlanext"
            or "rdpsa" or "rdpinput" or "rdpclip";
    }

    static string ExtractExePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    // ── Friendly Name Mapping ────────────────────────────────
    static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers
        ["msedge"] = "Microsoft Edge",
        ["chrome"] = "Google Chrome",
        ["firefox"] = "Mozilla Firefox",
        ["opera"] = "Opera",
        ["brave"] = "Brave Browser",

        // Microsoft Office
        ["WINWORD"] = "Microsoft Word",
        ["EXCEL"] = "Microsoft Excel",
        ["POWERPNT"] = "Microsoft PowerPoint",
        ["OUTLOOK"] = "Microsoft Outlook",
        ["ONENOTE"] = "Microsoft OneNote",
        ["MSACCESS"] = "Microsoft Access",
        ["lync"] = "Skype for Business",
        ["olk"] = "Microsoft Outlook (New)",

        // Dev tools
        ["devenv"] = "Visual Studio",
        ["Code"] = "Visual Studio Code",
        ["rider64"] = "JetBrains Rider",
        ["idea64"] = "IntelliJ IDEA",
        ["pycharm64"] = "PyCharm",
        ["webstorm64"] = "WebStorm",
        ["GitHubDesktop"] = "GitHub Desktop",
        ["git"] = "Git",
        ["node"] = "Node.js",
        ["python"] = "Python",
        ["java"] = "Java",
        ["dotnet"] = ".NET Runtime",
        ["WindowsTerminal"] = "Windows Terminal",
        ["powershell"] = "PowerShell",
        ["pwsh"] = "PowerShell",
        ["cmd"] = "Command Prompt",

        // Communication
        ["Teams"] = "Microsoft Teams",
        ["ms-teams"] = "Microsoft Teams",
        ["Slack"] = "Slack",
        ["Discord"] = "Discord",
        ["Zoom"] = "Zoom",
        ["Telegram"] = "Telegram",
        ["WhatsApp"] = "WhatsApp",
        ["Signal"] = "Signal",

        // Creative
        ["Photoshop"] = "Adobe Photoshop",
        ["Illustrator"] = "Adobe Illustrator",
        ["AfterFX"] = "Adobe After Effects",
        ["Premiere Pro"] = "Adobe Premiere Pro",
        ["figma_agent"] = "Figma",

        // System / Utilities
        ["explorer"] = "File Explorer",
        ["taskmgr"] = "Task Manager",
        ["SnippingTool"] = "Snipping Tool",
        ["mspaint"] = "Paint",
        ["notepad"] = "Notepad",
        ["Taskmgr"] = "Task Manager",
        ["SecurityHealthSystray"] = "Windows Security",
        ["PhoneExperienceHost"] = "Phone Link",
        ["WidgetService"] = "Windows Widgets",
        ["GameBar"] = "Xbox Game Bar",
        ["mstsc"] = "Remote Desktop",
        ["WinStore.App"] = "Microsoft Store",
        ["Calculator"] = "Calculator",
        ["Spotify"] = "Spotify",
        ["OneDrive"] = "OneDrive",
        ["GoogleDriveFS"] = "Google Drive",
        ["Dropbox"] = "Dropbox",
        ["1Password"] = "1Password",
        ["KeePass"] = "KeePass",

        // Media
        ["vlc"] = "VLC Media Player",
        ["Spotify"] = "Spotify",
        ["wmplayer"] = "Windows Media Player",
    };

    static string GetFriendlyName(string processName)
    {
        return FriendlyNames.TryGetValue(processName, out var friendly) ? friendly : processName;
    }

    // ── Grouping Logic ───────────────────────────────────────
    // Maps process names to their parent application group key.
    static readonly Dictionary<string, string> GroupMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Visual Studio family
        ["devenv"] = "VisualStudio",
        ["PerfWatson2"] = "VisualStudio",
        ["MSBuild"] = "VisualStudio",
        ["VBCSCompiler"] = "VisualStudio",
        ["vshost"] = "VisualStudio",
        ["ServiceHub.Host.dotnet.x64"] = "VisualStudio",
        ["ServiceHub.RoslynCodeAnalysisService"] = "VisualStudio",
        ["ServiceHub.IdentityHost"] = "VisualStudio",
        ["ServiceHub.SettingsHost"] = "VisualStudio",
        ["ServiceHub.ThreadedWaitDialog"] = "VisualStudio",
        ["ServiceHub.IndexingService"] = "VisualStudio",
        ["ServiceHub.Host.AnyCPU"] = "VisualStudio",
        ["ServiceHub.TestWindowStoreHost"] = "VisualStudio",
        ["vstest.console"] = "VisualStudio",
        ["testhost"] = "VisualStudio",

        // VS Code family
        ["Code"] = "VSCode",

        // Edge
        ["msedge"] = "Edge",
        ["msedgewebview2"] = "Edge",

        // Chrome
        ["chrome"] = "Chrome",
        ["GoogleUpdate"] = "Chrome",

        // Firefox
        ["firefox"] = "Firefox",

        // Teams
        ["Teams"] = "Teams",
        ["ms-teams"] = "Teams",

        // Office shared
        ["OfficeClickToRun"] = "MicrosoftOffice",
        ["AppVShNotify"] = "MicrosoftOffice",

        // .NET
        ["dotnet"] = "DotNet",
    };

    static string DetermineGroupKey(string processName, string exePath, string publisher)
    {
        // 1. Check explicit mapping
        if (GroupMappings.TryGetValue(processName, out var mapped))
            return mapped;

        // 2. Check if name starts with a known prefix (e.g. ServiceHub.*)
        if (processName.StartsWith("ServiceHub.", StringComparison.OrdinalIgnoreCase))
            return "VisualStudio";

        // 3. Group by install directory — apps in the same folder belong together
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var dir = Path.GetDirectoryName(exePath) ?? "";
                // Use the parent folder that's 2 levels deep in Program Files
                // e.g. "C:\Program Files\Microsoft VS Code\..." → "Microsoft VS Code"
                if (dir.Contains("Program Files", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = dir.Split(Path.DirectorySeparatorChar);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Contains("Program Files") && i + 1 < parts.Length)
                            return $"dir:{parts[i + 1]}";
                    }
                }
            }
            catch { }
        }

        // 4. Fallback: each process is its own group
        return processName;
    }

    static readonly Dictionary<string, string> GroupFriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VisualStudio"] = "Visual Studio",
        ["VSCode"] = "Visual Studio Code",
        ["Edge"] = "Microsoft Edge",
        ["Chrome"] = "Google Chrome",
        ["Firefox"] = "Mozilla Firefox",
        ["Teams"] = "Microsoft Teams",
        ["MicrosoftOffice"] = "Microsoft Office",
        ["DotNet"] = ".NET Runtime",
    };

    static string GetGroupFriendlyName(string groupKey, List<AppUsageEntry> processes)
    {
        // Check known group names
        if (GroupFriendlyNames.TryGetValue(groupKey, out var friendly))
            return friendly;

        // For directory-based groups, use the directory name
        if (groupKey.StartsWith("dir:"))
            return groupKey[4..];

        // Use the best friendly name from the processes
        var best = processes.OrderByDescending(p => p.MemoryMb).First();
        return !string.IsNullOrEmpty(best.FriendlyName) ? best.FriendlyName : best.Name;
    }
}
