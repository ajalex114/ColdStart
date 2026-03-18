using System.Diagnostics;
using System.IO;
using ColdStart.Models;
using Microsoft.Win32;

namespace ColdStart.Services;

public static class DisableService
{
    public static (bool Success, string Message) Disable(StartupItem item)
    {
        try
        {
            return item.DisableMethod switch
            {
                DisableMethod.Registry => DisableRegistry(item),
                DisableMethod.StartupFolder => DisableStartupFolder(item),
                DisableMethod.ScheduledTask => DisableScheduledTask(item),
                DisableMethod.Service => DisableAutoService(item),
                _ => (false, "This item cannot be disabled automatically. Use the manual instructions shown below."),
            };
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Administrator privileges are required to disable this item. Right-click the app and select 'Run as Administrator'.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to disable: {ex.Message}");
        }
    }

    private static (bool, string) DisableRegistry(StartupItem item)
    {
        if (string.IsNullOrEmpty(item.RegistryKeyPath) || string.IsNullOrEmpty(item.RegistryValueName))
            return (false, "Registry path not available for this item.");

        // Determine root key
        RegistryKey? rootKey = null;
        string subPath = item.RegistryKeyPath;

        if (subPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
            subPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            rootKey = Registry.LocalMachine;
            subPath = subPath.Substring(subPath.IndexOf('\\') + 1);
        }
        else if (subPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ||
                 subPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            rootKey = Registry.CurrentUser;
            subPath = subPath.Substring(subPath.IndexOf('\\') + 1);
        }
        else
        {
            // Try to infer from scope
            rootKey = item.Scope.Contains("Current User") ? Registry.CurrentUser : Registry.LocalMachine;
            subPath = item.RegistryKeyPath;
        }

        using var key = rootKey.OpenSubKey(subPath, writable: true);
        if (key == null)
            return (false, "Could not open the registry key. You may need Administrator privileges.");

        // Backup the value to a "disabled" subkey before deleting
        var val = key.GetValue(item.RegistryValueName);
        if (val != null)
        {
            try
            {
                using var backupKey = rootKey.CreateSubKey(subPath + @"\AutorunsDisabled");
                backupKey?.SetValue(item.RegistryValueName, val);
            }
            catch { /* backup is best-effort */ }
        }

        key.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
        return (true, $"✓ Removed \"{item.RegistryValueName}\" from startup registry. It will no longer run at login.");
    }

    private static (bool, string) DisableStartupFolder(StartupItem item)
    {
        var path = item.ShortcutPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return (false, "Shortcut file not found. It may have already been removed.");

        // Move to a "Disabled" subfolder for recovery
        var dir = Path.GetDirectoryName(path)!;
        var disabledDir = Path.Combine(dir, "Disabled");
        Directory.CreateDirectory(disabledDir);
        var dest = Path.Combine(disabledDir, Path.GetFileName(path));

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(path, dest);

        return (true, $"✓ Moved startup shortcut to the Disabled folder. To re-enable, move it back from:\n{dest}");
    }

    private static (bool, string) DisableScheduledTask(StartupItem item)
    {
        var taskPath = item.TaskFullPath;
        if (string.IsNullOrEmpty(taskPath))
            taskPath = item.Name;

        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Change /TN \"{taskPath}\" /Disable",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
        var output = proc?.StandardOutput.ReadToEnd() ?? "";
        var error = proc?.StandardError.ReadToEnd() ?? "";

        if (proc?.ExitCode == 0)
            return (true, $"✓ Scheduled task \"{item.Name}\" has been disabled. To re-enable, open Task Scheduler.");
        else
            return (false, $"Failed: {(string.IsNullOrEmpty(error) ? output : error).Trim()}\nYou may need to run as Administrator.");
    }

    private static (bool, string) DisableAutoService(StartupItem item)
    {
        var svcName = item.ServiceName;
        if (string.IsNullOrEmpty(svcName))
            return (false, "Service name not available. Use services.msc to disable manually.");

        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"config \"{svcName}\" start= demand",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
        var output = proc?.StandardOutput.ReadToEnd() ?? "";
        var error = proc?.StandardError.ReadToEnd() ?? "";

        if (proc?.ExitCode == 0)
            return (true, $"✓ Service \"{item.Name}\" changed from Auto to Manual start. It will no longer start automatically.\nTo re-enable: services.msc → {item.Name} → Startup type → Automatic.");
        else
            return (false, $"Failed: {(string.IsNullOrEmpty(error) ? output : error).Trim()}\nYou may need to run as Administrator.");
    }
}
