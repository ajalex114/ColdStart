using System.Diagnostics;
using System.IO;
using ColdStart.Models;
using ColdStart.Services.Interfaces;
using Microsoft.Win32;

namespace ColdStart.Services;

/// <summary>
/// Provides methods to disable startup items via Registry, Startup Folder, Task Scheduler, or Services.
/// Records all changes via <see cref="ChangeTracker"/> so they can be reversed on uninstall.
/// </summary>
public class DisableService : IDisableService
{
    private readonly ChangeTracker _tracker = new();

    /// <inheritdoc />
    public (bool Success, string Message) Disable(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

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
            return (false, "Administrator privileges are required to disable this item. Restart ColdStart as Administrator.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to disable: {ex.Message}");
        }
    }

    private (bool, string) DisableRegistry(StartupItem item)
    {
        if (string.IsNullOrEmpty(item.RegistryKeyPath) || string.IsNullOrEmpty(item.RegistryValueName))
            return (false, "Registry path not available for this item.");

        RegistryKey? rootKey = null;
        string subPath = item.RegistryKeyPath;
        string rootKeyName;

        if (subPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
            subPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            rootKey = Registry.LocalMachine;
            rootKeyName = "HKLM";
            subPath = subPath.Substring(subPath.IndexOf('\\') + 1);
        }
        else if (subPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ||
                 subPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            rootKey = Registry.CurrentUser;
            rootKeyName = "HKCU";
            subPath = subPath.Substring(subPath.IndexOf('\\') + 1);
        }
        else
        {
            var isCurrentUser = item.Scope.Contains("Current User");
            rootKey = isCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            rootKeyName = isCurrentUser ? "HKCU" : "HKLM";
            subPath = item.RegistryKeyPath;
        }

        using var key = rootKey.OpenSubKey(subPath, writable: true);
        if (key == null)
            return (false, "Could not open the registry key. You may need Administrator privileges.");

        var val = key.GetValue(item.RegistryValueName);
        var backupSubPath = subPath + @"\AutorunsDisabled";
        if (val != null)
        {
            try
            {
                using var backupKey = rootKey.CreateSubKey(backupSubPath);
                backupKey?.SetValue(item.RegistryValueName, val);
            }
            catch { /* backup is best-effort */ }
        }

        key.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);

        _tracker.RecordDisable(new DisableRecord
        {
            ItemName = item.Name,
            ActionType = DisableActionType.RegistryValueRemoved,
            RegistryRootKey = rootKeyName,
            RegistrySubPath = subPath,
            RegistryValueName = item.RegistryValueName,
            RegistryBackupSubPath = backupSubPath,
        });

        return (true, $"✓ Removed \"{item.RegistryValueName}\" from startup registry. It will no longer run at login.");
    }

    private (bool, string) DisableStartupFolder(StartupItem item)
    {
        var path = item.ShortcutPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return (false, "Shortcut file not found. It may have already been removed.");

        var dir = Path.GetDirectoryName(path)!;
        var disabledDir = Path.Combine(dir, "Disabled");
        Directory.CreateDirectory(disabledDir);
        var dest = Path.Combine(disabledDir, Path.GetFileName(path));

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(path, dest);

        _tracker.RecordDisable(new DisableRecord
        {
            ItemName = item.Name,
            ActionType = DisableActionType.ShortcutMoved,
            OriginalShortcutPath = path,
            MovedShortcutPath = dest,
        });

        return (true, $"✓ Moved startup shortcut to the Disabled folder. To re-enable, move it back from:\n{dest}");
    }

    private (bool, string) DisableScheduledTask(StartupItem item)
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
        {
            _tracker.RecordDisable(new DisableRecord
            {
                ItemName = item.Name,
                ActionType = DisableActionType.ScheduledTaskDisabled,
                TaskFullPath = taskPath,
            });

            return (true, $"✓ Scheduled task \"{item.Name}\" has been disabled. To re-enable, open Task Scheduler.");
        }

        return (false, $"Failed: {(string.IsNullOrEmpty(error) ? output : error).Trim()}\nYou may need to run as Administrator.");
    }

    private (bool, string) DisableAutoService(StartupItem item)
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
        {
            _tracker.RecordDisable(new DisableRecord
            {
                ItemName = item.Name,
                ActionType = DisableActionType.ServiceStartTypeChanged,
                ServiceName = svcName,
                PreviousStartType = "auto",
            });

            return (true, $"✓ Service \"{item.Name}\" changed from Auto to Manual start. It will no longer start automatically.\nTo re-enable: services.msc → {item.Name} → Startup type → Automatic.");
        }

        return (false, $"Failed: {(string.IsNullOrEmpty(error) ? output : error).Trim()}\nYou may need to run as Administrator.");
    }
}
