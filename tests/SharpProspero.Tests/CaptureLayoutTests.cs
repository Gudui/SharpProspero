// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;
using SharpProspero.Interop.Audio;
using SharpProspero.Interop.Image;
using SharpProspero.Interop.SystemService;
using Xunit;

namespace SharpProspero.Tests;

// The capture and system-service structures must match the module layouts, or the encoder and service
// read the wrong fields. Offsets are computed from the headers for the x86-64 target.
public sealed class CaptureLayoutTests
{
    [Fact]
    public void JpegCreateParam_MatchesTheHeader()
    {
        Assert.Equal(8, Marshal.SizeOf<SceJpegEncCreateParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceJpegEncCreateParam>(nameof(SceJpegEncCreateParam.ThisSize)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceJpegEncCreateParam>(nameof(SceJpegEncCreateParam.Attribute)));
    }

    [Fact]
    public void JpegEncodeParam_MatchesTheHeader()
    {
        Assert.Equal(48, Marshal.SizeOf<SceJpegEncEncodeParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ImageMemAddr)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.JpegMemAddr)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ImageMemSize)));
        Assert.Equal(20, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.JpegMemSize)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ImageWidth)));
        Assert.Equal(28, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ImageHeight)));
        Assert.Equal(32, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ImagePitch)));
        Assert.Equal(36, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.PixelFormat)));
        Assert.Equal(38, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.EncodeMode)));
        Assert.Equal(40, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.ColorSpace)));
        Assert.Equal(42, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.SamplingType)));
        Assert.Equal(43, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.CompressionRatio)));
        Assert.Equal(44, (int)Marshal.OffsetOf<SceJpegEncEncodeParam>(nameof(SceJpegEncEncodeParam.RestartInterval)));
    }

    [Fact]
    public void JpegOutputInfo_IsEightBytes()
    {
        Assert.Equal(8, Marshal.SizeOf<SceJpegEncOutputInfo>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceJpegEncOutputInfo>(nameof(SceJpegEncOutputInfo.DataSize)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceJpegEncOutputInfo>(nameof(SceJpegEncOutputInfo.ProcessedHeight)));
    }

    [Fact]
    public void JpegBgra_MatchesTheDisplaySurface() => Assert.Equal(1, (int)JpegEncPixelFormat.Bgra8);

    [Fact]
    public void SystemServiceStatus_MatchesTheHeader()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SceSystemServiceStatus>(nameof(SceSystemServiceStatus.EventNum)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceSystemServiceStatus>(nameof(SceSystemServiceStatus.IsSystemUiOverlaid)));
        Assert.Equal(5, (int)Marshal.OffsetOf<SceSystemServiceStatus>(nameof(SceSystemServiceStatus.IsInBackgroundExecution)));
        // The reserved tail leaves the structure large enough for the service to write into.
        Assert.True(Marshal.SizeOf<SceSystemServiceStatus>() >= 134);
    }

    [Fact]
    public void SystemServiceSafeArea_MatchesTheHeader()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SceSystemServiceDisplaySafeAreaInfo>(nameof(SceSystemServiceDisplaySafeAreaInfo.Ratio)));
        Assert.True(Marshal.SizeOf<SceSystemServiceDisplaySafeAreaInfo>() >= 132);
    }

    [Fact]
    public void SystemServiceEvent_HoldsTheFullEventBuffer()
    {
        Assert.Equal(8196, Marshal.SizeOf<SceSystemServiceEvent>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceSystemServiceEvent>(nameof(SceSystemServiceEvent.EventType)));
    }

    [Fact]
    public void AudioInFormats_CombineFormatAndChannels()
    {
        Assert.Equal(0x01u, (uint)AudioInFormat.S16Mono);
        Assert.Equal(0x02u, (uint)AudioInFormat.S16Stereo);
        Assert.Equal(0x11u, (uint)AudioInFormat.FloatMono);
        Assert.Equal(0x12u, (uint)AudioInFormat.FloatStereo);
    }

    [Fact]
    public void AudioInRates_MatchTheHeader()
    {
        Assert.Equal(16000u, AudioIn.Freq16k);
        Assert.Equal(48000u, AudioIn.Freq48k);
        Assert.Equal(256u, AudioIn.Grain256);
    }
}
