namespace ColdStart.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdStart.Models;
using ColdStart.Services.Interfaces;
using ColdStart.ViewModels;
using Moq;
using Xunit;

public class AppUsageViewModelTests
{
    private readonly Mock<IAppUsageService> _serviceMock = new();

    private AppUsageViewModel CreateVm() => new(_serviceMock.Object);

    // ── Constructor null checks ──────────────────────────────

    [Fact]
    public void Ctor_NullService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppUsageViewModel(null!));
    }

    // ── LoadCommand ──────────────────────────────────────────

    [Fact]
    public async Task LoadCommand_SetsDataAndIsLoaded()
    {
        var usageData = BuildTestData();
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(usageData);
        var vm = CreateVm();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Data);
        Assert.Same(usageData, vm.Data);
        Assert.True(vm.IsLoaded);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadCommand_PopulatesFilteredGroups()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.FilteredGroups);
    }

    // ── SearchQuery filter ───────────────────────────────────

    [Fact]
    public async Task SearchQuery_FiltersGroupsByName()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchQuery = "Chrome";

        Assert.All(vm.FilteredGroups, g =>
        {
            var nameMatch = g.FriendlyName.Contains("Chrome", StringComparison.OrdinalIgnoreCase);
            var pubMatch = g.Publisher.Contains("Chrome", StringComparison.OrdinalIgnoreCase);
            var procMatch = g.Processes.Any(p =>
                p.Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
                || p.FriendlyName.Contains("Chrome", StringComparison.OrdinalIgnoreCase));
            Assert.True(nameMatch || pubMatch || procMatch);
        });
    }

    // ── FilterMemory ─────────────────────────────────────────

    [Theory]
    [InlineData(">1GB", 1024)]
    [InlineData(">500MB", 500)]
    [InlineData(">100MB", 100)]
    public async Task FilterMemory_OnlyAboveThreshold(string filter, double thresholdMb)
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterMemory = filter;

        Assert.All(vm.FilteredGroups, g =>
            Assert.True(g.TotalMemoryMb > thresholdMb));
    }

    // ── FilterCpu ────────────────────────────────────────────

    [Theory]
    [InlineData(">10%", 10)]
    [InlineData(">5%", 5)]
    [InlineData(">1%", 1)]
    public async Task FilterCpu_OnlyAboveThreshold(string filter, double thresholdPercent)
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterCpu = filter;

        Assert.All(vm.FilteredGroups, g =>
            Assert.True(g.TotalCpuPercent > thresholdPercent));
    }

    // ── FilterType ───────────────────────────────────────────

    [Fact]
    public async Task FilterType_Startup_OnlyStartupApps()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterType = "Startup";

        Assert.All(vm.FilteredGroups, g => Assert.True(g.IsStartupApp));
    }

    [Fact]
    public async Task FilterType_NonStartup_OnlyNonStartupApps()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterType = "Non-Startup";

        Assert.All(vm.FilteredGroups, g => Assert.False(g.IsStartupApp));
    }

    // ── ToggleSortCommand ────────────────────────────────────

    [Fact]
    public void ToggleSort_SameKey_TogglesSortAscending()
    {
        var vm = CreateVm();
        Assert.False(vm.SortAscending);

        vm.ToggleSortCommand.Execute("Memory");
        Assert.True(vm.SortAscending);

        vm.ToggleSortCommand.Execute("Memory");
        Assert.False(vm.SortAscending);
    }

    [Fact]
    public void ToggleSort_DifferentKey_SwitchesColumnDescending()
    {
        var vm = CreateVm();
        vm.ToggleSortCommand.Execute("Memory"); // now ascending
        Assert.True(vm.SortAscending);

        vm.ToggleSortCommand.Execute("CPU");
        Assert.Equal("CPU", vm.SortBy);
        Assert.False(vm.SortAscending);
    }

    // ── ResetFilters ─────────────────────────────────────────

    [Fact]
    public async Task ResetFilters_RestoresDefaults()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchQuery = "test";
        vm.FilterMemory = ">1GB";
        vm.FilterCpu = ">10%";
        vm.FilterType = "Startup";
        vm.SortAscending = true;

        vm.ResetFiltersCommand.Execute(null);

        Assert.Equal("", vm.SearchQuery);
        Assert.Equal("Memory", vm.SortBy);
        Assert.False(vm.SortAscending);
        Assert.Equal("All", vm.FilterMemory);
        Assert.Equal("All", vm.FilterCpu);
        Assert.Equal("All", vm.FilterType);
    }

    // ── HasActiveFilters ─────────────────────────────────────

    [Fact]
    public void HasActiveFilters_DefaultIsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.HasActiveFilters);
    }

    [Theory]
    [InlineData(nameof(AppUsageViewModel.SearchQuery), "test")]
    [InlineData(nameof(AppUsageViewModel.FilterMemory), ">1GB")]
    [InlineData(nameof(AppUsageViewModel.FilterCpu), ">10%")]
    [InlineData(nameof(AppUsageViewModel.FilterType), "Startup")]
    public void HasActiveFilters_TrueWhenFilterSet(string property, string value)
    {
        var vm = CreateVm();
        typeof(AppUsageViewModel).GetProperty(property)!.SetValue(vm, value);
        Assert.True(vm.HasActiveFilters);
    }

    // ── DataChanged event ────────────────────────────────────

    [Fact]
    public async Task DataChanged_FiresOnFilterChange()
    {
        _serviceMock.Setup(s => s.GetAppUsage(It.IsAny<List<StartupItem>?>())).Returns(BuildTestData());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var firedCount = 0;
        vm.DataChanged += () => firedCount++;

        vm.FilterMemory = ">100MB";
        vm.SearchQuery = "test";
        vm.FilterCpu = ">1%";

        Assert.Equal(3, firedCount);
    }

    // ── Helper ───────────────────────────────────────────────

    private static AppUsageData BuildTestData()
    {
        var now = DateTime.Now;

        return new AppUsageData
        {
            TotalMemoryGb = 16.0,
            UsedMemoryGb = 10.0,
            TotalProcesses = 120,
            Groups = new List<AppGroup>
            {
                new()
                {
                    FriendlyName = "Google Chrome",
                    GroupKey = "chrome",
                    Processes = new List<AppUsageEntry>
                    {
                        new()
                        {
                            Name = "chrome", FriendlyName = "Google Chrome", GroupKey = "chrome",
                            Publisher = "Google LLC", InstanceCount = 10,
                            MemoryMb = 1500, CpuPercent = 12, TotalCpuTime = TimeSpan.FromMinutes(30),
                            FirstStarted = now.AddHours(-2), IsStartupApp = true, StartupTimeMs = 3000,
                        }
                    }
                },
                new()
                {
                    FriendlyName = "Visual Studio Code",
                    GroupKey = "code",
                    Processes = new List<AppUsageEntry>
                    {
                        new()
                        {
                            Name = "code", FriendlyName = "Visual Studio Code", GroupKey = "code",
                            Publisher = "Microsoft", InstanceCount = 5,
                            MemoryMb = 800, CpuPercent = 6, TotalCpuTime = TimeSpan.FromMinutes(15),
                            FirstStarted = now.AddHours(-1), IsStartupApp = false,
                        }
                    }
                },
                new()
                {
                    FriendlyName = "Notepad",
                    GroupKey = "notepad",
                    Processes = new List<AppUsageEntry>
                    {
                        new()
                        {
                            Name = "notepad", FriendlyName = "Notepad", GroupKey = "notepad",
                            Publisher = "Microsoft", InstanceCount = 1,
                            MemoryMb = 20, CpuPercent = 0.1, TotalCpuTime = TimeSpan.FromSeconds(2),
                            FirstStarted = now.AddMinutes(-10), IsStartupApp = false,
                        }
                    }
                },
            }
        };
    }
}
