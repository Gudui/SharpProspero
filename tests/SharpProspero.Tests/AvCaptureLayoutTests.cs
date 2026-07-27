// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.AvCapture;
using SharpProspero.Platform;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The AV-capture structures must match the service layout, or a read stores the wrong fields. Offsets
// are the ones the read and open paths use on the x86-64 target.
public sealed class AvCaptureLayoutTests
{
    [Fact]
    public void VideoConfig_MatchesTheService()
    {
        // The whole block is sent to the service as configuration, so its length is part of the
        // contract: a shorter one ships whatever follows it in the caller's frame.
        Assert.Equal(0x2B8, Marshal.SizeOf<Avcap2VideoConfig>());
        Assert.Equal(0x10, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.Kind)));
        Assert.Equal(0x188, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.Mode)));
        Assert.Equal(0x1A0, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.AreaLength)));
        Assert.Equal(0x1A8, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.AreaAddress)));
        Assert.Equal(0x1C0, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.FrameAreaLength)));
        Assert.Equal(0x1C8, (int)Marshal.OffsetOf<Avcap2VideoConfig>(nameof(Avcap2VideoConfig.FrameAreaAddress)));
    }

    [Fact]
    public void VideoFrameInfo_MatchesTheService()
    {
        Assert.Equal(0x68, Marshal.SizeOf<Avcap2VideoFrameInfo>());
        Assert.Equal(0x00, (int)Marshal.OffsetOf<Avcap2VideoFrameInfo>(nameof(Avcap2VideoFrameInfo.Word0)));
        Assert.Equal(0x50, (int)Marshal.OffsetOf<Avcap2VideoFrameInfo>(nameof(Avcap2VideoFrameInfo.Word10)));
        Assert.Equal(0x59, (int)Marshal.OffsetOf<Avcap2VideoFrameInfo>(nameof(Avcap2VideoFrameInfo.StatusA)));
        Assert.Equal(0x5C, (int)Marshal.OffsetOf<Avcap2VideoFrameInfo>(nameof(Avcap2VideoFrameInfo.Result)));
        Assert.Equal(0x64, (int)Marshal.OffsetOf<Avcap2VideoFrameInfo>(nameof(Avcap2VideoFrameInfo.StatusB)));
    }

    [Fact]
    public void FrameInfo_IsValid_RequiresZeroStatusAndNonNegativeResult()
    {
        Assert.True(new Avcap2VideoFrameInfo { Result = 0, StatusA = 0, StatusB = 0 }.IsValid);
        Assert.True(new Avcap2VideoFrameInfo { Result = 5, StatusA = 0, StatusB = 0 }.IsValid);
        Assert.False(new Avcap2VideoFrameInfo { Result = -1 }.IsValid);
        Assert.False(new Avcap2VideoFrameInfo { Result = 0, StatusA = 1 }.IsValid);
        Assert.False(new Avcap2VideoFrameInfo { Result = 0, StatusB = 1 }.IsValid);
    }

    [Fact]
    public void ErrorCodes_ShareTheFacility()
    {
        Assert.Equal(unchecked((int)0x81950000), Avcap2Error.FacilityBase);
        Assert.Equal(unchecked((int)0x81950001), Avcap2Error.InvalidArgument);
    }

    [Fact]
    public void ModulePath_IsTheCommonLibraryPath()
    {
        Assert.Equal("/system/common/lib/libSceAvcap2.sprx", SystemAvCapture.ModulePath);
    }
}
