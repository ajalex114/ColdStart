using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColdStart.Services;

/// <summary>
/// Records all changes made by the app so they can be reversed on uninstall.
/// Changes are persisted to <c>%LocalAppData%\ColdStart\changes.json</c>.
/// </summary>
public class ChangeTracker
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ColdStart");

    private static readonly string ChangesFilePath = Path.Combine(AppDataDir, "changes.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private ChangeLog _log;

    public ChangeTracker()
    {
        _log = Load();
    }

    /// <summary>
    /// Records a disable action for later reversal on uninstall.
    /// </summary>
    public void RecordDisable(DisableRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Timestamp = DateTime.UtcNow;
        _log.Actions.Add(record);
        Save();
    }

    /// <summary>
    /// Returns all recorded disable actions.
    /// </summary>
    public IReadOnlyList<DisableRecord> GetAllActions() => _log.Actions.AsReadOnly();

    /// <summary>
    /// Removes a recorded action (after it has been reversed).
    /// </summary>
    public void RemoveAction(DisableRecord record)
    {
        _log.Actions.Remove(record);
        Save();
    }

    /// <summary>
    /// Deletes the change log file and the app data directory if empty.
    /// Called during uninstall cleanup.
    /// </summary>
    public static void DeleteChangeLog()
    {
        try
        {
            if (File.Exists(ChangesFilePath))
                File.Delete(ChangesFilePath);

            if (Directory.Exists(AppDataDir) && Directory.GetFileSystemEntries(AppDataDir).Length == 0)
                Directory.Delete(AppDataDir);
        }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Returns the path to the app's local data directory.
    /// </summary>
    public static string GetAppDataDirectory() => AppDataDir;

    private ChangeLog Load()
    {
        try
        {
            if (File.Exists(ChangesFilePath))
            {
                var json = File.ReadAllText(ChangesFilePath);
                return JsonSerializer.Deserialize<ChangeLog>(json, JsonOptions) ?? new ChangeLog();
            }
        }
        catch { /* corrupted file — start fresh */ }

        return new ChangeLog();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(_log, JsonOptions);
            File.WriteAllText(ChangesFilePath, json);
        }
        catch { /* best-effort persistence */ }
    }
}

/// <summary>
/// The serializable change log stored on disk.
/// </summary>
public class ChangeLog
{
    public List<DisableRecord> Actions { get; set; } = new();
}

/// <summary>
/// Records a single disable action with enough info to reverse it.
/// </summary>
public class DisableRecord
{
    public string ItemName { get; set; } = "";
    public DisableActionType ActionType { get; set; }
    public DateTime Timestamp { get; set; }

    // Registry-specific
    public string? RegistryRootKey { get; set; }
    public string? RegistrySubPath { get; set; }
    public string? RegistryValueName { get; set; }
    public string? RegistryBackupSubPath { get; set; }

    // Startup folder-specific
    public string? OriginalShortcutPath { get; set; }
    public string? MovedShortcutPath { get; set; }

    // Scheduled task-specific
    public string? TaskFullPath { get; set; }

    // Service-specific
    public string? ServiceName { get; set; }
    public string? PreviousStartType { get; set; }
}

/// <summary>
/// The type of disable action that was performed.
/// </summary>
public enum DisableActionType
{
    RegistryValueRemoved,
    ShortcutMoved,
    ScheduledTaskDisabled,
    ServiceStartTypeChanged,
}
