// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Share;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The capture parameter structures must match the module layout, or a capture reads the wrong fields.
// Offsets are computed from the header for the x86-64 target.
public sealed class ShareLayoutTests
{
    [Fact]
    public void ScreenshotParam_MatchesTheHeader()
    {
        Assert.Equal(32, Marshal.SizeOf<SceShareCaptureScreenshotParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceShareCaptureScreenshotParam>(nameof(SceShareCaptureScreenshotParam.Format)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceShareCaptureScreenshotParam>(nameof(SceShareCaptureScreenshotParam.TapPoint)));
    }

    [Fact]
    public void VideoClipParam_MatchesTheHeader()
    {
        // thisSize@0, the time union at 8 (start@8, end@12), mode@24.
        Assert.Equal(32, Marshal.SizeOf<SceShareCaptureVideoClipParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceShareCaptureVideoClipParam>(nameof(SceShareCaptureVideoClipParam.ThisSize)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceShareCaptureVideoClipParam>(nameof(SceShareCaptureVideoClipParam.StartTime)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceShareCaptureVideoClipParam>(nameof(SceShareCaptureVideoClipParam.EndTime)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceShareCaptureVideoClipParam>(nameof(SceShareCaptureVideoClipParam.Mode)));
    }

    [Fact]
    public void CurrentStatus_MatchesTheHeader()
    {
        Assert.Equal(16, Marshal.SizeOf<SceShareCurrentStatus>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceShareCurrentStatus>(nameof(SceShareCurrentStatus.Recording2kStatus)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceShareCurrentStatus>(nameof(SceShareCurrentStatus.Recording4kStatus)));
    }

    [Fact]
    public void ScreenshotFormats_MatchTheHeader()
    {
        Assert.Equal(1, (int)ScreenshotFormat.Jpeg2K);
        Assert.Equal(2, (int)ScreenshotFormat.Jpeg4K);
        Assert.Equal(11, (int)ScreenshotFormat.Png2K);
        Assert.Equal(12, (int)ScreenshotFormat.Png4K);
    }

    [Fact]
    public void FeatureFlags_MatchTheHeader()
    {
        Assert.Equal(0x00000001u, (uint)ShareFeature.VideoRecording);
        Assert.Equal(0x00000002u, (uint)ShareFeature.Screenshot);
    }
}
