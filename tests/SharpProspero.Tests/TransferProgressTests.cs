// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using Xunit;

namespace SharpProspero.Tests;

// The progress reading interprets four fields of the service's record. The record's own layout is
// checked on the device; here the interpretation of a reading is checked: the finished bit, the error
// sign, and the percentage.
public sealed class TransferProgressTests
{
    [Fact]
    public void PercentIsTransferredOverTotal()
    {
        var half = new TransferProgress(0, 0, TotalBytes: 200, TransferredBytes: 100);
        Assert.Equal(50, half.PercentComplete);

        var whole = new TransferProgress(0, 0, TotalBytes: 200, TransferredBytes: 200);
        Assert.Equal(100, whole.PercentComplete);
    }

    [Fact]
    public void PercentIsZeroBeforeTheSizeIsKnown()
    {
        var starting = new TransferProgress(0, 0, TotalBytes: 0, TransferredBytes: 0);
        Assert.Equal(0, starting.PercentComplete);
    }

    [Fact]
    public void CompleteIsTheLowTwoBitsOfTheState()
    {
        Assert.True(new TransferProgress(0x3, 0, 1, 1).IsComplete);   // sub-state 3 = finished
        Assert.False(new TransferProgress(0x1, 0, 1, 0).IsComplete);
        Assert.False(new TransferProgress(0x2, 0, 1, 0).IsComplete);

        // The upper bits carry a phase and must not be mistaken for the finished state.
        Assert.False(new TransferProgress(0xC, 0, 1, 0).IsComplete);
    }

    [Fact]
    public void ErrorIsANegativeResultCode()
    {
        Assert.True(new TransferProgress(0, unchecked((int)0x80990001), 0, 0).HasError);
        Assert.False(new TransferProgress(0, 0, 0, 0).HasError);
    }

    [Fact]
    public void TheAcceptedFindKindsAreSixSevenEight()
    {
        Assert.Equal([6, 7, 8], DownloadService.FindKinds.ToArray());
    }
}
