// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

// A module states the system it was built against, and a package that ships the module has to require
// at least that. The digits are stored as they read, so 0x11 is eleven and not seventeen; the 11.20
// cases below are the ones that catch treating them as plain numbers.
public sealed class SystemVersionTests
{
    [Theory]
    [InlineData(0x02000009u, "02.00")]   // read out of a real module
    [InlineData(0x02000021u, "02.00")]   // same system, a later patch
    [InlineData(0x11200009u, "11.20")]   // eleven twenty, not seventeen thirty-two
    [InlineData(0x10010000u, "10.01")]
    [InlineData(0u, "")]
    public void FormatSystemVersion_ReadsTheDigitsAsWritten(uint sdkVersion, string expected)
        => Assert.Equal(expected, PrxImage.FormatSystemVersion(sdkVersion));

    [Theory]
    // A package requiring 11.20 carries this exact value; it is the one confirmed against a real
    // package's metadata, so it anchors the whole encoding.
    [InlineData(0x11200009u, "0x1120000000000000")]
    [InlineData(0x02000009u, "0x0200000000000000")]
    [InlineData(0x02000021u, "0x0200000000000000")]   // the patch never reaches the requirement
    [InlineData(0u, "")]
    public void FormatRequiredSystemSoftwareVersion_MatchesWhatAPackageCarries(uint sdkVersion, string expected)
        => Assert.Equal(expected, PrxImage.FormatRequiredSystemSoftwareVersion(sdkVersion));
}
