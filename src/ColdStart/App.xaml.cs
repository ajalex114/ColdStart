using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ColdStart.Services;
using ColdStart.Services.Interfaces;
using ColdStart.ViewModels;

namespace ColdStart;

/// <summary>
/// Application entry point. Configures services and ViewModels.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Services
        ISystemInfoService sysInfo = new SystemInfoService();
        IStartupAnalyzerService startup = new StartupAnalyzerService();
        IAppUsageService appUsage = new AppUsageService();
        IDisableService disable = new DisableService();

        // ViewModel
        var mainVm = new MainViewModel(sysInfo, startup, appUsage, disable);

        // Show window
        var window = new MainWindow(mainVm);
        window.Show();
    }
}

