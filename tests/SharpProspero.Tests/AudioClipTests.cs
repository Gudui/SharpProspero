using SharpProspero.Audio;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class AudioClipTests
{
    [Fact]
    public void Resample_HalvesTheFrameCountWhenHalvingTheRate()
    {
        var audio = new PcmAudio(new short[100], 48000, 1);
        PcmAudio half = AudioClip.Resample(audio, 24000);
        Assert.Equal(24000, half.SampleRate);
        Assert.Equal(50, half.FrameCount);
    }

    [Fact]
    public void Resample_InterpolatesBetweenSamples()
    {
        // 0, 100 at 2 Hz upsampled to 4 Hz gives a midpoint near 50.
        var audio = new PcmAudio([0, 100], 2, 1);
        PcmAudio up = AudioClip.Resample(audio, 4);
        Assert.Equal(4, up.FrameCount);
        Assert.InRange(up.Samples[1], 40, 60);
    }

    [Fact]
    public void ToMono_AveragesTheChannels()
    {
        var stereo = new PcmAudio([100, 300, -200, 0], 48000, 2); // frames (100,300) and (-200,0)
        PcmAudio mono = AudioClip.ToMono(stereo);
        Assert.Equal(1, mono.Channels);
        Assert.Equal(new short[] { 200, -100 }, mono.Samples);
    }

    [Fact]
    public void ToStereo_DuplicatesEachSample()
    {
        PcmAudio stereo = AudioClip.ToStereo(new PcmAudio([5, -7], 48000, 1));
        Assert.Equal(2, stereo.Channels);
        Assert.Equal(new short[] { 5, 5, -7, -7 }, stereo.Samples);
    }

    [Fact]
    public void Gain_ScalesAndClamps()
    {
        PcmAudio loud = AudioClip.Gain(new PcmAudio([100, 20000], 48000, 1), 2.0f);
        Assert.Equal(200, loud.Samples[0]);
        Assert.Equal(short.MaxValue, loud.Samples[1]); // 40000 clamps to 32767
    }

    [Fact]
    public void Normalize_BringsThePeakToFullScale()
    {
        PcmAudio norm = AudioClip.Normalize(new PcmAudio([1000, -2000], 48000, 1));
        Assert.Equal(short.MaxValue, Math.Max(Math.Abs((int)norm.Samples[0]), Math.Abs((int)norm.Samples[1])));
    }

    [Fact]
    public void Concat_JoinsMatchingClips_AndRejectsMismatched()
    {
        PcmAudio joined = AudioClip.Concat(new PcmAudio([1, 2], 48000, 1), new PcmAudio([3, 4], 48000, 1));
        Assert.Equal(new short[] { 1, 2, 3, 4 }, joined.Samples);
        Assert.Throws<ArgumentException>(() => AudioClip.Concat(new PcmAudio([1], 48000, 1), new PcmAudio([2], 44100, 1)));
    }

    [Fact]
    public void Trim_ExtractsAFrameRange()
    {
        PcmAudio cut = AudioClip.Trim(new PcmAudio([1, 2, 3, 4, 5], 48000, 1), 1, 3);
        Assert.Equal(new short[] { 2, 3, 4 }, cut.Samples);
    }
}
