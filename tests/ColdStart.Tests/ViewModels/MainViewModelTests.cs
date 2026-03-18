namespace ColdStart.Tests.ViewModels;

using System;
using System.Threading;
using System.Windows;
using ColdStart.Helpers;
using ColdStart.Services.Interfaces;
using ColdStart.ViewModels;
using Moq;
using Xunit;

public class MainViewModelTests
{
    private readonly Mock<ISystemInfoService> _sysInfoMock = new();
    private readonly Mock<IStartupAnalyzerService> _analyzerMock = new();
    private readonly Mock<IAppUsageService> _appUsageMock = new();
    private readonly Mock<IDisableService> _disableMock = new();

    private MainViewModel CreateVm() =>
        new(_sysInfoMock.Object, _analyzerMock.Object, _appUsageMock.Object, _disableMock.Object);

    // ── Constructor null checks ──────────────────────────────

    [Fact]
    public void Ctor_NullSysInfo_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(null!, _analyzerMock.Object, _appUsageMock.Object, _disableMock.Object));
    }

    [Fact]
    public void Ctor_NullAnalyzer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(_sysInfoMock.Object, null!, _appUsageMock.Object, _disableMock.Object));
    }

    [Fact]
    public void Ctor_NullAppUsage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(_sysInfoMock.Object, _analyzerMock.Object, null!, _disableMock.Object));
    }

    [Fact]
    public void Ctor_NullDisableService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MainViewModel(_sysInfoMock.Object, _analyzerMock.Object, _appUsageMock.Object, null!));
    }

    // ── SwitchTabCommand ─────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SwitchTabCommand_SetsActiveTabIndex(int index)
    {
        var vm = CreateVm();
        vm.SwitchTabCommand.Execute(index);
        Assert.Equal(index, vm.ActiveTabIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void SwitchTabCommand_InvalidIndex_Throws(int index)
    {
        var vm = CreateVm();
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.SwitchTabCommand.Execute(index));
    }

    // ── CycleThemeCommand ────────────────────────────────────

    [Fact]
    public void CycleThemeCommand_ChangesCurrentTheme()
    {
        EnsureWpfApplication();

        var vm = CreateVm();
        var initialTheme = vm.Theme.CurrentTheme;

        vm.CycleThemeCommand.Execute(null);

        Assert.NotEqual(initialTheme, vm.Theme.CurrentTheme);
    }

    [Fact]
    public void CycleThemeCommand_FiresThemeChanged()
    {
        EnsureWpfApplication();

        var vm = CreateVm();
        var fired = false;
        vm.ThemeChanged += () => fired = true;

        vm.CycleThemeCommand.Execute(null);

        Assert.True(fired);
    }

    [Fact]
    public void CycleTheme_CyclesDarkLightSystemDark()
    {
        EnsureWpfApplication();

        var vm = CreateVm();
        Assert.Equal(ThemeMode.Dark, vm.Theme.CurrentTheme);

        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Light, vm.Theme.CurrentTheme);

        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.System, vm.Theme.CurrentTheme);

        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Dark, vm.Theme.CurrentTheme);
    }

    // ── Child VMs accessible ─────────────────────────────────

    [Fact]
    public void ChildViewModels_AreNotNull()
    {
        var vm = CreateVm();

        Assert.NotNull(vm.StartupVm);
        Assert.NotNull(vm.TimelineVm);
        Assert.NotNull(vm.AppUsageVm);
    }

    [Fact]
    public void ChildViewModels_AreCorrectTypes()
    {
        var vm = CreateVm();

        Assert.IsType<StartupViewModel>(vm.StartupVm);
        Assert.IsType<TimelineViewModel>(vm.TimelineVm);
        Assert.IsType<AppUsageViewModel>(vm.AppUsageVm);
    }

    // ── WPF Application bootstrapping for theme tests ────────

    private static readonly object _appLock = new();

    private static void EnsureWpfApplication()
    {
        lock (_appLock)
        {
            if (Application.Current == null)
            {
                // Create a WPF Application on an STA thread so ThemeManager can access resources
                var ready = new ManualResetEventSlim(false);
                var thread = new Thread(() =>
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.Wait();
                // Give WPF time to initialize Application.Current
                Thread.Sleep(100);
            }
        }
    }
}
