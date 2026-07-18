// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Memory;
using Xunit;

namespace SharpProspero.Tests;

public sealed class HeapMonitorTests
{
    [Fact]
    public void Capture_ReturnsNonNegativeReadings()
    {
        HeapSnapshot snapshot = HeapMonitor.Capture();
        Assert.True(snapshot.HeapSizeBytes >= 0);
        Assert.True(snapshot.TotalAllocatedBytes >= 0);
        Assert.True(snapshot.HardLimitBytes >= 0);
        Assert.True(snapshot.CollectionCount >= 0);
    }

    [Fact]
    public void Pressure_IsZeroWhenNoLimit()
    {
        var snapshot = new HeapSnapshot { HeapSizeBytes = 1024, HardLimitBytes = 0 };
        Assert.Equal(0, snapshot.Pressure);
    }

    [Fact]
    public void Pressure_IsRatioOfLimit()
    {
        var snapshot = new HeapSnapshot { HeapSizeBytes = 256, HardLimitBytes = 1024 };
        Assert.Equal(0.25, snapshot.Pressure, 3);
    }
}
