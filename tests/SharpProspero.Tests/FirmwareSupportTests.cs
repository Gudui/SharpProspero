// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using Xunit;

namespace SharpProspero.Tests;

// The decision logic of the firmware-support front door, tested without the device: whether a version
// is in the supported range, and how a validation result reads. Reading the running version and
// resolving a library are device-only and are exercised on the console.
public sealed class FirmwareSupportTests
{
    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(10, 1, true)]
    [InlineData(11, 20, true)]
    [InlineData(1, 50, false)]
    public void IsSupportedOn_MatchesTheSupportedRange(int major, int minor, bool expected)
        => Assert.Equal(expected, FirmwareSupport.IsSupportedOn(FirmwareVersion.FromMajorMinor(major, minor)));

    [Fact]
    public void IsSupportedOn_RejectsTheAbsentVersion()
        => Assert.False(FirmwareSupport.IsSupportedOn(FirmwareVersion.None));

    [Fact]
    public void Validation_IsValidWhenNothingIsMissing()
    {
        SystemLibraryDescriptor descriptor = FirmwareRegistry.DynamicLibraries[0];
        var result = new FirmwareValidation(descriptor, FirmwareVersion.FromMajorMinor(10, 1), []);
        Assert.True(result.IsValid);
        Assert.Contains("all required exports present", result.ToString());
    }

    [Fact]
    public void Validation_ReportsEachMissingExport()
    {
        SystemLibraryDescriptor descriptor = FirmwareRegistry.DynamicLibraries[0];
        var result = new FirmwareValidation(
            descriptor, FirmwareVersion.FromMajorMinor(10, 1), ["sceMissingOne", "sceMissingTwo"]);
        Assert.False(result.IsValid);
        Assert.Contains("sceMissingOne", result.ToString());
        Assert.Contains("sceMissingTwo", result.ToString());
    }
}
