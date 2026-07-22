// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Audio;
using SharpProspero.Platform;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The encoder itself runs on the device; off the device these cover the managed wrapper's argument
// validation (which runs before any device call) and the constants transcribed from the encoder header.
public sealed class AacEncoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void CreateRejectsUnsupportedChannelCounts(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.Create(channels, bitRate: 128000));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(27999)]
    [InlineData(320001)]
    [InlineData(1000000)]
    public void CreateRejectsBitRateOutOfRange(int bitRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.Create(channels: 2, bitRate: bitRate));
    }

    [Fact]
    public void CreateRejectsNon48kSampleRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.Create(channels: 2, bitRate: 128000, sampleRate: 44100));
    }

    [Fact]
    public void ConstantsMatchTheEncoderHeader()
    {
        Assert.Equal(1, M4aacEnc.ChannelMono);
        Assert.Equal(2, M4aacEnc.ChannelStereo);
        Assert.Equal(48000, M4aacEnc.SamplingRate48K);
        Assert.Equal(28000, M4aacEnc.MinBitRate);
        Assert.Equal(320000, M4aacEnc.MaxBitRate);
        Assert.Equal(1024, M4aacEnc.FrameSamples);
        Assert.Equal(1536, M4aacEnc.MaxOutputBufferSize);
    }

    [Fact]
    public void OutputFormatValuesMatchTheEncoderHeader()
    {
        Assert.Equal(0x00000000u, (uint)M4aacEncOutputFormat.AacLcRaw);
        Assert.Equal(0x00000001u, (uint)M4aacEncOutputFormat.AacLcAdts);
        Assert.Equal(0x00010000u, (uint)M4aacEncOutputFormat.AacLcRawDualMono);
        Assert.Equal(0x00010001u, (uint)M4aacEncOutputFormat.AacLcAdtsDualMono);
    }

    [Fact]
    public void AudioEncodeExceptionCarriesTheCodes()
    {
        var ex = new AudioEncodeException("encode", -2136408063, -1);
        Assert.Equal(-2136408063, ex.ResultCode);
        Assert.Equal(-1, ex.InternalError);
    }
}
