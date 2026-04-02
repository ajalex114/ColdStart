using System.Diagnostics;
using System.Security.Principal;

namespace ColdStart.Helpers;

/// <summary>
/// Provides helper methods for detecting and requesting administrator privileges.
/// </summary>
public static class AdminHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when the current process is running elevated (administrator).
    /// </summary>
    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Restarts the current application with administrator privileges.
    /// Returns <see langword="true"/> if the elevation was initiated (caller should exit).
    /// Returns <see langword="false"/> if the user cancelled the UAC prompt.
    /// </summary>
    public static bool RestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled the UAC dialog
            return false;
        }
    }
}
