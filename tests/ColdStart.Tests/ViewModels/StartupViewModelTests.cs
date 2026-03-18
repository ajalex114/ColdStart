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

public class StartupViewModelTests
{
    private readonly Mock<IStartupAnalyzerService> _analyzerMock = new();
    private readonly Mock<IDisableService> _disableMock = new();

    private StartupViewModel CreateVm() => new(_analyzerMock.Object, _disableMock.Object);

    // ── Constructor null checks ──────────────────────────────

    [Fact]
    public void Ctor_NullAnalyzer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StartupViewModel(null!, _disableMock.Object));
    }

    [Fact]
    public void Ctor_NullDisableService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StartupViewModel(_analyzerMock.Object, null!));
    }

    // ── LoadCommand ──────────────────────────────────────────

    [Fact]
    public async Task LoadCommand_SetsAnalysisDataAndFilters()
    {
        var analysis = BuildTestAnalysis();
        _analyzerMock.Setup(a => a.Analyze()).Returns(analysis);
        var vm = CreateVm();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.AnalysisData);
        Assert.Same(analysis, vm.AnalysisData);
        Assert.NotEmpty(vm.FilteredGroups);
    }

    [Fact]
    public async Task LoadCommand_IsLoadingTransitions()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        var loadingValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsLoading))
                loadingValues.Add(vm.IsLoading);
        };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
        Assert.Contains(true, loadingValues);
        Assert.Contains(false, loadingValues);
    }

    // ── FilterImpact ─────────────────────────────────────────

    [Theory]
    [InlineData("High")]
    [InlineData("Medium")]
    [InlineData("Low")]
    public async Task FilterImpact_OnlyMatchingImpactItems(string impact)
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterImpact = impact;

        var allItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.All(allItems, item =>
            Assert.Equal(impact, item.Impact, StringComparer.OrdinalIgnoreCase));
    }

    // ── FilterStatus ─────────────────────────────────────────

    [Fact]
    public async Task FilterStatus_Enabled_OnlyEnabledInActionGroups()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterStatus = "Enabled";

        // Action-based groups (safe_to_disable, can_disable, review, keep) filter
        // by both action AND the group's own enabled filter, so all items from those
        // groups whose underlying filter pipeline includes "Enabled" should be enabled.
        var nonDisabledGroups = vm.FilteredGroups.Where(g => g.Key != "_disabled");
        var items = nonDisabledGroups.SelectMany(g => g.Items).ToList();
        Assert.All(items, item => Assert.True(item.IsEnabled));
    }

    [Fact]
    public async Task FilterStatus_Disabled_OnlyDisabledInDisabledGroup()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterStatus = "Disabled";

        var disabledGroup = vm.FilteredGroups.Single(g => g.Key == "_disabled");
        Assert.All(disabledGroup.Items, item => Assert.False(item.IsEnabled));
    }

    // ── FilterTime ───────────────────────────────────────────

    [Theory]
    [InlineData("> 5s", 5000)]
    [InlineData("> 2s", 2000)]
    [InlineData("> 1s", 1000)]
    public async Task FilterTime_OnlyAboveThreshold(string filter, int thresholdMs)
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterTime = filter;

        var allItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.All(allItems, item => Assert.True(item.StartupTimeMs > thresholdMs));
    }

    // ── FilterSource ─────────────────────────────────────────

    [Theory]
    [InlineData("Measured")]
    [InlineData("Process")]
    [InlineData("Estimated")]
    public async Task FilterSource_OnlyMatchingSource(string source)
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterSource = source;

        var allItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.All(allItems, item => Assert.Equal(source, item.TimingSource));
    }

    // ── SearchQuery ──────────────────────────────────────────

    [Fact]
    public async Task SearchQuery_FiltersByName()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchQuery = "SlowApp";

        var allItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.All(allItems, item =>
            Assert.Contains("SlowApp", item.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchQuery_EmptyReturnsAll()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var countBefore = vm.FilteredGroups.SelectMany(g => g.Items).Count();
        vm.SearchQuery = "SlowApp";
        vm.SearchQuery = "";
        var countAfter = vm.FilteredGroups.SelectMany(g => g.Items).Count();

        Assert.Equal(countBefore, countAfter);
    }

    // ── ResetFilters ─────────────────────────────────────────

    [Fact]
    public async Task ResetFilters_RestoresDefaults()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchQuery = "test";
        vm.FilterImpact = "High";
        vm.FilterStatus = "Enabled";
        vm.FilterTime = "> 5s";
        vm.FilterSource = "Measured";

        vm.ResetFiltersCommand.Execute(null);

        Assert.Equal("", vm.SearchQuery);
        Assert.Equal("Impact", vm.SortBy);
        Assert.Equal("All", vm.FilterImpact);
        Assert.Equal("All", vm.FilterStatus);
        Assert.Equal("All", vm.FilterTime);
        Assert.Equal("All", vm.FilterSource);
    }

    // ── HasActiveFilters ─────────────────────────────────────

    [Fact]
    public void HasActiveFilters_DefaultIsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.HasActiveFilters);
    }

    [Theory]
    [InlineData(nameof(StartupViewModel.SearchQuery), "test")]
    [InlineData(nameof(StartupViewModel.FilterImpact), "High")]
    [InlineData(nameof(StartupViewModel.FilterStatus), "Enabled")]
    [InlineData(nameof(StartupViewModel.FilterTime), "> 5s")]
    [InlineData(nameof(StartupViewModel.FilterSource), "Measured")]
    public void HasActiveFilters_TrueWhenFilterSet(string property, string value)
    {
        var vm = CreateVm();
        typeof(StartupViewModel).GetProperty(property)!.SetValue(vm, value);
        Assert.True(vm.HasActiveFilters);
    }

    // ── DisableItemCommand ───────────────────────────────────

    [Fact]
    public async Task DisableItemCommand_CallsServiceAndUpdatesItem()
    {
        var analysis = BuildTestAnalysis();
        _analyzerMock.Setup(a => a.Analyze()).Returns(analysis);
        var target = analysis.Items.First(i => i.IsEnabled);
        _disableMock.Setup(d => d.Disable(target)).Returns((true, "OK"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.DisableItemCommand.Execute(target);

        _disableMock.Verify(d => d.Disable(target), Times.Once);
        Assert.False(target.IsEnabled);
    }

    [Fact]
    public async Task DisableItemCommand_FailureDoesNotChangeItem()
    {
        var analysis = BuildTestAnalysis();
        _analyzerMock.Setup(a => a.Analyze()).Returns(analysis);
        var target = analysis.Items.First(i => i.IsEnabled);
        _disableMock.Setup(d => d.Disable(target)).Returns((false, "Error"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.DisableItemCommand.Execute(target);

        Assert.True(target.IsEnabled);
    }

    [Fact]
    public void DisableItemCommand_NullThrows()
    {
        var vm = CreateVm();
        Assert.Throws<ArgumentNullException>(() => vm.DisableItemCommand.Execute(null!));
    }

    // ── DataChanged event ────────────────────────────────────

    [Fact]
    public async Task DataChanged_FiresOnFilterChange()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var fired = false;
        vm.DataChanged += () => fired = true;
        vm.FilterImpact = "High";

        Assert.True(fired);
    }

    [Fact]
    public async Task DataChanged_FiresOnSearchQueryChange()
    {
        _analyzerMock.Setup(a => a.Analyze()).Returns(BuildTestAnalysis());
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        var fired = false;
        vm.DataChanged += () => fired = true;
        vm.SearchQuery = "test";

        Assert.True(fired);
    }

    // ── Helper ───────────────────────────────────────────────

    private static StartupAnalysis BuildTestAnalysis()
    {
        return new StartupAnalysis
        {
            Items = new List<StartupItem>
            {
                new()
                {
                    Name = "SlowApp", Impact = "high", IsEnabled = true, IsRunning = true,
                    StartupTimeMs = 6000, TimingSource = "Measured", BootOffsetMs = 1000,
                    Command = @"C:\SlowApp.exe", Publisher = "SlowCorp",
                    Description = "A slow application", Essential = false,
                    Action = "safe_to_disable", Category = "Utility", Source = "Registry"
                },
                new()
                {
                    Name = "MediumApp", Impact = "medium", IsEnabled = true, IsRunning = false,
                    StartupTimeMs = 3000, TimingSource = "Process", BootOffsetMs = 5000,
                    Command = @"C:\MediumApp.exe", Publisher = "MedCorp",
                    Description = "A medium app", Essential = false,
                    Action = "can_disable", Category = "Communication", Source = "Startup Folder"
                },
                new()
                {
                    Name = "LowApp", Impact = "low", IsEnabled = true, IsRunning = true,
                    StartupTimeMs = 500, TimingSource = "Estimated", BootOffsetMs = 0,
                    Command = @"C:\LowApp.exe", Publisher = "LowCorp",
                    Description = "A low-impact app", Essential = false,
                    Action = "review", Category = "Utility", Source = "Registry"
                },
                new()
                {
                    Name = "EssentialApp", Impact = "high", IsEnabled = true, IsRunning = true,
                    StartupTimeMs = 2500, TimingSource = "Measured", BootOffsetMs = 500,
                    Command = @"C:\Essential.exe", Publisher = "Microsoft",
                    Description = "System essential", Essential = true,
                    Action = "keep", Category = "System", Source = "Registry"
                },
                new()
                {
                    Name = "DisabledApp", Impact = "medium", IsEnabled = false, IsRunning = false,
                    StartupTimeMs = 4000, TimingSource = "Process", BootOffsetMs = 0,
                    Command = @"C:\Disabled.exe", Publisher = "OldCorp",
                    Description = "Disabled application", Essential = false,
                    Action = "safe_to_disable", Category = "Utility", Source = "Startup Folder"
                },
            },
            Diagnostics = new BootDiagnostics
            {
                Available = true,
                BootDurationMs = 30000,
                MainPathMs = 15000,
                PostBootMs = 15000,
                DegradingApps = new List<DegradingApp>
                {
                    new() { Name = "SlowApp", Path = @"C:\SlowApp.exe", TotalMs = 6000, DegradationMs = 3000 }
                }
            },
            Summary = new StartupSummary
            {
                Total = 5, High = 2, Medium = 2, Low = 1,
                CanDisable = 3, EstimatedSavingsSec = 13.0
            }
        };
    }
}
