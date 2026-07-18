// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using Xunit;

namespace SharpProspero.Tests;

// The firmware version is stored as the system stores it: one byte per part, digits written out as they
// read. 10.01 is bytes 0x10 0x01, not 0x0A 0x01. The 11.20 and 10.01-vs-09.60 cases below are the ones
// that catch treating the bytes as plain numbers.
public sealed class FirmwareVersionTests
{
    [Theory]
    [InlineData(2, 0, "02.00", 0x0200)]
    [InlineData(10, 1, "10.01", 0x1001)]
    [InlineData(11, 20, "11.20", 0x1120)]
    [InlineData(9, 60, "09.60", 0x0960)]
    public void FromMajorMinor_RoundTripsThroughTheDigits(int major, int minor, string expected, int packed)
    {
        FirmwareVersion v = FirmwareVersion.FromMajorMinor(major, minor);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(expected, v.ToString());
        Assert.Equal((ushort)packed, v.Packed);
    }

    [Theory]
    [InlineData(0x11200009u, 11, 20)]   // the version sits in the high 16 bits of the kernel value
    [InlineData(0x10010000u, 10, 1)]
    [InlineData(0x02000021u, 2, 0)]     // a later patch of 02.00 still reads 02.00
    public void FromSystemValue_TakesTheHighHalf(uint systemValue, int major, int minor)
    {
        FirmwareVersion v = FirmwareVersion.FromSystemValue(systemValue);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
    }

    [Fact]
    public void Ordering_FollowsTheDigitsNotSeventeenThirtyTwo()
    {
        // 10.01 is newer than 09.60, and 11.20 is eleven-twenty, not seventeen thirty-two.
        FirmwareVersion tenOhOne = FirmwareVersion.FromMajorMinor(10, 1);
        FirmwareVersion nineSixty = FirmwareVersion.FromMajorMinor(9, 60);
        Assert.True(tenOhOne > nineSixty);
        Assert.True(tenOhOne.IsAtLeast(nineSixty));
        Assert.False(nineSixty.IsAtLeast(tenOhOne));
    }

    [Theory]
    [InlineData("10.01", 10, 1)]
    [InlineData("1001", 10, 1)]
    [InlineData("2.00", 2, 0)]
    [InlineData("0200", 2, 0)]
    public void TryParse_ReadsTheWrittenForms(string text, int major, int minor)
    {
        Assert.True(FirmwareVersion.TryParse(text, out FirmwareVersion v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("1.2.3")]
    [InlineData("10.1")]     // the minor must be two digits
    [InlineData("0")]        // packs to zero, which is the absent version
    public void TryParse_RejectsNonVersions(string text)
        => Assert.False(FirmwareVersion.TryParse(text, out _));

    [Fact]
    public void None_HasNoValueAndPrintsEmpty()
    {
        Assert.False(FirmwareVersion.None.HasValue);
        Assert.Equal("", FirmwareVersion.None.ToString());
    }
}
