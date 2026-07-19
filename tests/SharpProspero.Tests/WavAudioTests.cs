// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Audio;
using SharpProspero.Interop;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class WavAudioTests
{
    [Fact]
    public void Wav_RoundTripsStereoSamples()
    {
        short[] samples = [0, 100, -100, 32767, -32768, 5, 6, 7];
        var audio = new PcmAudio(samples, 48000, 2);

        byte[] encoded = WavAudio.Encode(audio);
        Assert.Equal((byte)'R', encoded[0]);
        Assert.Equal((byte)'I', encoded[1]);
        Assert.Equal((byte)'F', encoded[2]);
        Assert.Equal((byte)'F', encoded[3]);

        PcmAudio decoded = WavAudio.Decode(encoded);
        Assert.Equal(48000, decoded.SampleRate);
        Assert.Equal(2, decoded.Channels);
        Assert.Equal(samples, decoded.Samples);
    }

    [Fact]
    public void Wav_RoundTripsMonoSamples()
    {
        short[] samples = [-1, 0, 1, 12345, -12345];
        PcmAudio decoded = WavAudio.Decode(WavAudio.Encode(new PcmAudio(samples, 44100, 1)));
        Assert.Equal(44100, decoded.SampleRate);
        Assert.Equal(1, decoded.Channels);
        Assert.Equal(samples, decoded.Samples);
    }

    [Fact]
    public void PcmAudio_ReportsFramesAndDuration()
    {
        var audio = new PcmAudio(new short[96000], 48000, 2); // 48000 frames of stereo
        Assert.Equal(48000, audio.FrameCount);
        Assert.Equal(1000, audio.DurationMilliseconds);
    }

    [Fact]
    public void Wav_RejectsNonWav()
    {
        Assert.Throws<ProsperoException>(() => WavAudio.Decode(new byte[10]));
    }

    [Fact]
    public void Wav_RejectsOverflowingChunkSize()
    {
        // RIFF/WAVE with a data chunk claiming a huge size must fail cleanly (ProsperoException),
        // not overflow the bounds check into an out-of-range slice.
        byte[] wav = new byte[44];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), int.MaxValue);
        Assert.Throws<ProsperoException>(() => WavAudio.Decode(wav));
    }

    [Fact]
    public void Wav_RejectsBadChannelsOnEncode()
    {
        Assert.Throws<ArgumentException>(() => WavAudio.Encode(new PcmAudio(new short[4], 48000, 3)));
        Assert.Throws<ArgumentException>(() => WavAudio.Encode(new PcmAudio(new short[4], 0, 2)));
    }
}
