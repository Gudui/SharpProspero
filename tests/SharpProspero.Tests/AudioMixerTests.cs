// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Audio;
using Xunit;

namespace SharpProspero.Tests;

public sealed class AudioMixerTests
{
    private static PcmAudio Mono(int rate, params short[] samples) => new(samples, rate, 1);

    private static PcmAudio Stereo(int rate, params short[] interleaved) => new(interleaved, rate, 2);

    [Fact]
    public void EmptyMixer_ProducesSilence()
    {
        var mixer = new AudioMixer(48000);
        short[] block = new short[8];
        mixer.Mix(block);
        Assert.All(block, s => Assert.Equal((short)0, s));
        Assert.Equal(0, mixer.ActiveVoices);
    }

    [Fact]
    public void MonoClip_SpreadsToBothChannels()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(Mono(48000, 1000, 1000, 1000, 1000));
        short[] block = new short[8]; // 4 frames
        mixer.Mix(block);
        for (int i = 0; i < block.Length; i++)
            Assert.Equal((short)1000, block[i]);
    }

    [Fact]
    public void StereoClip_KeepsLeftAndRight()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(Stereo(48000, 100, 200, 300, 400)); // 2 frames
        short[] block = new short[4];
        mixer.Mix(block);
        Assert.Equal(new short[] { 100, 200, 300, 400 }, block);
    }

    [Fact]
    public void Voices_SumAndClamp()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(Mono(48000, 1000));
        mixer.Play(Mono(48000, 1000));
        short[] block = new short[2];
        mixer.Mix(block);
        Assert.Equal((short)2000, block[0]); // summed

        var loud = new AudioMixer(48000);
        loud.Play(Mono(48000, 30000));
        loud.Play(Mono(48000, 30000));
        short[] one = new short[2];
        loud.Mix(one);
        Assert.Equal((short)32767, one[0]); // clamped, not wrapped
    }

    [Fact]
    public void MasterVolume_ScalesEverything()
    {
        var mixer = new AudioMixer(48000) { MasterVolume = 0.5f };
        mixer.Play(Mono(48000, 1000));
        short[] block = new short[2];
        mixer.Mix(block);
        Assert.Equal((short)500, block[0]);
    }

    [Fact]
    public void OneShot_DropsOutWhenItEnds()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(Mono(48000, 1000, 1000, 1000, 1000)); // 4 frames
        short[] block = new short[12]; // 6 frames — longer than the clip
        mixer.Mix(block);

        Assert.Equal((short)1000, block[6]); // frame 3, still playing
        Assert.Equal((short)0, block[8]);    // frame 4, past the end
        Assert.Equal(0, mixer.ActiveVoices);  // and the voice is gone
    }

    [Fact]
    public void Loop_KeepsPlayingAndWraps()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(Mono(48000, 10, 20, 30, 40), loop: true);
        short[] block = new short[12]; // 6 frames
        mixer.Mix(block);

        Assert.Equal((short)10, block[8]);  // frame 4 wrapped back to the start
        Assert.Equal((short)20, block[10]); // frame 5
        Assert.Equal(1, mixer.ActiveVoices);
    }

    [Fact]
    public void LowerRateClip_PlaysSlower()
    {
        // A clip at half the mixer rate advances one source frame every two output frames.
        var mixer = new AudioMixer(48000);
        mixer.Play(Mono(24000, 100, 200));
        short[] block = new short[8]; // 4 frames
        mixer.Mix(block);
        Assert.Equal((short)100, block[0]);
        Assert.Equal((short)100, block[2]);
        Assert.Equal((short)200, block[4]);
        Assert.Equal((short)200, block[6]);
    }

    [Fact]
    public void MaxVoices_DropsTheOldest()
    {
        var mixer = new AudioMixer(48000) { MaxVoices = 2 };
        mixer.Play(Mono(48000, 1, 1));
        mixer.Play(Mono(48000, 2, 2));
        mixer.Play(Mono(48000, 3, 3));
        Assert.Equal(2, mixer.ActiveVoices);
    }

    [Fact]
    public void Play_IgnoresEmptyClips()
    {
        var mixer = new AudioMixer(48000);
        mixer.Play(new PcmAudio([], 48000, 1));
        mixer.Play(new PcmAudio([1, 2], 0, 1)); // bad rate
        Assert.Equal(0, mixer.ActiveVoices);
    }

    [Fact]
    public void ToneGenerator_RenderClipIsAStereoMixerSound()
    {
        var tone = new ToneGenerator(48000) { Frequency = 440 };
        PcmAudio clip = tone.RenderClip(0.01);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(48000, clip.SampleRate);
        Assert.Equal((int)(0.01 * 48000) * 2, clip.Samples.Length);

        // It plays through the mixer without a hitch.
        var mixer = new AudioMixer(48000);
        mixer.Play(clip);
        short[] block = new short[512];
        mixer.Mix(block);
        Assert.Equal(1, mixer.ActiveVoices);
    }
}
