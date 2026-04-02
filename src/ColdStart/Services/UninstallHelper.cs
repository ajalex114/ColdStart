using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ColdStart.Services;

/// <summary>
/// Reverses changes recorded by <see cref="ChangeTracker"/> and cleans up all app traces.
/// Used during uninstall or via the in-app "Undo All" feature.
/// </summary>
public static class UninstallHelper
{
    /// <summary>
    /// Reverses all recorded disable actions and removes the change log.
    /// Returns a summary of what was restored.
    /// </summary>
    public static string ReverseAllChanges()
    {
        var tracker = new ChangeTracker();
        var actions = tracker.GetAllActions();
        int restored = 0, failed = 0;

        foreach (var action in actions)
        {
            try
            {
                var success = action.ActionType switch
                {
                    DisableActionType.RegistryValueRemoved => RestoreRegistryValue(action),
                    DisableActionType.ShortcutMoved => RestoreShortcut(action),
                    DisableActionType.ScheduledTaskDisabled => RestoreScheduledTask(action),
                    DisableActionType.ServiceStartTypeChanged => RestoreServiceStartType(action),
                    _ => false,
                };

                if (success) restored++;
                else failed++;
            }
            catch
            {
                failed++;
            }
        }

        // Clean up the change log
        ChangeTracker.DeleteChangeLog();

        return $"Restored {restored} item(s){(failed > 0 ? $", {failed} could not be restored" : "")}.";
    }

    /// <summary>
    /// Removes all ColdStart data from the machine:
    /// - %LocalAppData%\ColdStart directory
    /// - Registry backup keys created by the app
    /// </summary>
    public static void CleanupAppData()
    {
        // Remove LocalAppData\ColdStart
        var appDataDir = ChangeTracker.GetAppDataDirectory();
        try
        {
            if (Directory.Exists(appDataDir))
                Directory.Delete(appDataDir, recursive: true);
        }
        catch { /* best-effort */ }

        // Clean up AutorunsDisabled backup keys the app may have created
        CleanupRegistryBackups(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\AutorunsDisabled");
        CleanupRegistryBackups(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\AutorunsDisabled");
        CleanupRegistryBackups(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run\AutorunsDisabled");
    }

    private static bool RestoreRegistryValue(DisableRecord action)
    {
        if (string.IsNullOrEmpty(action.RegistryBackupSubPath) ||
            string.IsNullOrEmpty(action.RegistrySubPath) ||
            string.IsNullOrEmpty(action.RegistryValueName))
            return false;

        var rootKey = action.RegistryRootKey switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => null,
        };
        if (rootKey == null) return false;

        // Read backup value
        using var backupKey = rootKey.OpenSubKey(action.RegistryBackupSubPath);
        var backupValue = backupKey?.GetValue(action.RegistryValueName);
        if (backupValue == null) return false;

        // Restore to original key
        using var originalKey = rootKey.OpenSubKey(action.RegistrySubPath, writable: true);
        if (originalKey == null) return false;

        originalKey.SetValue(action.RegistryValueName, backupValue);

        // Remove backup
        try
        {
            using var backupWriteKey = rootKey.OpenSubKey(action.RegistryBackupSubPath, writable: true);
            backupWriteKey?.DeleteValue(action.RegistryValueName, throwOnMissingValue: false);
        }
        catch { /* best-effort cleanup of backup */ }

        return true;
    }

    private static bool RestoreShortcut(DisableRecord action)
    {
        if (string.IsNullOrEmpty(action.MovedShortcutPath) ||
            string.IsNullOrEmpty(action.OriginalShortcutPath))
            return false;

        if (!File.Exists(action.MovedShortcutPath))
            return false;

        File.Move(action.MovedShortcutPath, action.OriginalShortcutPath, overwrite: true);

        // Clean up empty Disabled directory
        var disabledDir = Path.GetDirectoryName(action.MovedShortcutPath);
        if (disabledDir != null && Directory.Exists(disabledDir) &&
            Directory.GetFileSystemEntries(disabledDir).Length == 0)
        {
            Directory.Delete(disabledDir);
        }

        return true;
    }

    private static bool RestoreScheduledTask(DisableRecord action)
    {
        if (string.IsNullOrEmpty(action.TaskFullPath)) return false;

        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Change /TN \"{action.TaskFullPath}\" /Enable",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
        return proc?.ExitCode == 0;
    }

    private static bool RestoreServiceStartType(DisableRecord action)
    {
        if (string.IsNullOrEmpty(action.ServiceName)) return false;

        var startType = action.PreviousStartType ?? "auto";
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"config \"{action.ServiceName}\" start= {startType}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);
        return proc?.ExitCode == 0;
    }

    private static void CleanupRegistryBackups(RegistryKey rootKey, string subPath)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subPath);
            if (key != null && key.ValueCount == 0)
                rootKey.DeleteSubKey(subPath, throwOnMissingSubKey: false);
        }
        catch { /* best-effort */ }
    }
}
