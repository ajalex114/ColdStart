namespace ColdStart.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using ColdStart.Models;
using ColdStart.ViewModels;
using Xunit;

public class TimelineViewModelTests
{
    private static TimelineViewModel CreateVm() => new();

    // ── PrepareTimeline with valid data ──────────────────────

    [Fact]
    public void PrepareTimeline_ValidData_PopulatesEntries()
    {
        var vm = CreateVm();
        var data = BuildTestAnalysis();

        vm.PrepareTimeline(data);

        Assert.NotEmpty(vm.Entries);
        Assert.True(vm.IsPrepared);
    }

    [Fact]
    public void PrepareTimeline_ValidData_SetsDiagnostics()
    {
        var vm = CreateVm();
        var data = BuildTestAnalysis();

        vm.PrepareTimeline(data);

        Assert.NotNull(vm.Diagnostics);
        Assert.Same(data.Diagnostics, vm.Diagnostics);
    }

    [Fact]
    public void PrepareTimeline_ValidData_FiresDataChanged()
    {
        var vm = CreateVm();
        var fired = false;
        vm.DataChanged += () => fired = true;

        vm.PrepareTimeline(BuildTestAnalysis());

        Assert.True(fired);
    }

    // ── PrepareTimeline with null ────────────────────────────

    [Fact]
    public void PrepareTimeline_Null_ClearsState()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        vm.PrepareTimeline(null);

        Assert.Empty(vm.Entries);
        Assert.Null(vm.Diagnostics);
        Assert.False(vm.IsPrepared);
        Assert.Equal(0, vm.MaxEndMs);
        Assert.Equal(0, vm.TotalMs);
    }

    [Fact]
    public void PrepareTimeline_Null_FiresDataChanged()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        var fired = false;
        vm.DataChanged += () => fired = true;
        vm.PrepareTimeline(null);

        Assert.True(fired);
    }

    // ── Phase 1 entries (items with BootOffsetMs) ────────────

    [Fact]
    public void PrepareTimeline_Phase1_ContainsItemsWithBootOffset()
    {
        var data = new StartupAnalysis
        {
            Items = new List<StartupItem>
            {
                new() { Name = "WithOffset", Impact = "high", BootOffsetMs = 2000, StartupTimeMs = 1000, IsEnabled = true },
                new() { Name = "NoOffset", Impact = "low", BootOffsetMs = 0, StartupTimeMs = 500, IsEnabled = true },
            }
        };

        var vm = CreateVm();
        vm.PrepareTimeline(data);

        var withOffset = vm.Entries.FirstOrDefault(e => e.Name == "WithOffset");
        Assert.NotNull(withOffset);
        Assert.Equal(2000, withOffset.StartMs);
    }

    [Fact]
    public void PrepareTimeline_Phase1_ExcludesItemsAboveMaxOffset()
    {
        var data = new StartupAnalysis
        {
            Items = new List<StartupItem>
            {
                new() { Name = "TooLate", Impact = "low", BootOffsetMs = 200_000, StartupTimeMs = 1000, IsEnabled = true },
            }
        };

        var vm = CreateVm();
        vm.PrepareTimeline(data);

        // TooLate is beyond 180_000ms threshold so not in Phase1 but is enabled so appears in Phase2
        var entry = vm.Entries.Single(e => e.Name == "TooLate");
        Assert.Equal("Synthetic", entry.Source);
    }

    // ── Phase 2 entries (synthetic offsets) ───────────────────

    [Fact]
    public void PrepareTimeline_Phase2_AssignsSyntheticOffsets()
    {
        var data = new StartupAnalysis
        {
            Items = new List<StartupItem>
            {
                new() { Name = "Phase1Item", Impact = "high", BootOffsetMs = 1000, StartupTimeMs = 500, IsEnabled = true },
                new() { Name = "Phase2Item", Impact = "low", BootOffsetMs = 0, StartupTimeMs = 300, IsEnabled = true },
            }
        };

        var vm = CreateVm();
        vm.PrepareTimeline(data);

        var phase2 = vm.Entries.FirstOrDefault(e => e.Name == "Phase2Item");
        Assert.NotNull(phase2);
        Assert.Equal("Synthetic", phase2.Source);
        // Phase2 starts after Phase1 max end + 500ms gap
        Assert.True(phase2.StartMs > 1000);
    }

    [Fact]
    public void PrepareTimeline_Phase2_SkipsDisabledItems()
    {
        var data = new StartupAnalysis
        {
            Items = new List<StartupItem>
            {
                new() { Name = "DisabledItem", Impact = "low", BootOffsetMs = 0, StartupTimeMs = 300, IsEnabled = false },
            }
        };

        var vm = CreateVm();
        vm.PrepareTimeline(data);

        Assert.DoesNotContain(vm.Entries, e => e.Name == "DisabledItem");
    }

    // ── CalculateTicks ───────────────────────────────────────

    [Fact]
    public void CalculateTicks_ValidWidth_SetsTicks()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        vm.CalculateTicks(800);

        Assert.True(vm.TickIntervalMs > 0);
        Assert.True(vm.TickCount >= 0);
    }

    [Fact]
    public void CalculateTicks_NarrowWidth_FewerTicks()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        vm.CalculateTicks(200);
        var narrowCount = vm.TickCount;

        vm.CalculateTicks(2000);
        var wideCount = vm.TickCount;

        Assert.True(wideCount >= narrowCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CalculateTicks_NonPositiveWidth_Throws(double width)
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        Assert.Throws<ArgumentOutOfRangeException>(() => vm.CalculateTicks(width));
    }

    [Fact]
    public void CalculateTicks_NoData_ZeroTickCount()
    {
        var vm = CreateVm();
        // TotalMs is 0 when no data has been prepared
        vm.CalculateTicks(800);

        Assert.Equal(0, vm.TickCount);
    }

    // ── Computed properties ──────────────────────────────────

    [Fact]
    public void TotalApps_ReturnsEntryCount()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        Assert.Equal(vm.Entries.Count, vm.TotalApps);
    }

    [Fact]
    public void HighImpactCount_CountsHighEntries()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        var expected = vm.Entries.Count(e => e.Impact.Equals("high", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, vm.HighImpactCount);
        Assert.True(vm.HighImpactCount > 0);
    }

    [Fact]
    public void MediumImpactCount_CountsMediumEntries()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        var expected = vm.Entries.Count(e => e.Impact.Equals("medium", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, vm.MediumImpactCount);
    }

    // ── MaxEndMs and TotalMs ─────────────────────────────────

    [Fact]
    public void MaxEndMs_EqualsMaxEntryEnd()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        var expectedMax = vm.Entries.Max(e => e.StartMs + e.DurationMs);
        Assert.Equal(expectedMax, vm.MaxEndMs);
    }

    [Fact]
    public void TotalMs_AtLeast10000()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        Assert.True(vm.TotalMs >= 10_000);
    }

    [Fact]
    public void TotalMs_SnappedTo5SecBoundary()
    {
        var vm = CreateVm();
        vm.PrepareTimeline(BuildTestAnalysis());

        Assert.Equal(0, vm.TotalMs % 5000);
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
                    Name = "MeasuredApp", Impact = "high", IsEnabled = true,
                    StartupTimeMs = 6000, TimingSource = "Measured", BootOffsetMs = 1000,
                },
                new()
                {
                    Name = "ProcessApp", Impact = "medium", IsEnabled = true,
                    StartupTimeMs = 3000, TimingSource = "Process", BootOffsetMs = 5000,
                },
                new()
                {
                    Name = "NoOffsetApp", Impact = "low", IsEnabled = true,
                    StartupTimeMs = 500, BootOffsetMs = 0,
                },
                new()
                {
                    Name = "DisabledApp", Impact = "medium", IsEnabled = false,
                    StartupTimeMs = 4000, BootOffsetMs = 0,
                },
            },
            Diagnostics = new BootDiagnostics
            {
                Available = true, BootDurationMs = 30000,
                MainPathMs = 15000, PostBootMs = 15000,
            },
            Summary = new StartupSummary { Total = 4, High = 1, Medium = 2, Low = 1 },
        };
    }
}
