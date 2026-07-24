// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Audio;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class AudioDspTests
{
    private const float SampleRate = 48000f;

    // The root-mean-square amplitude of a sine at the given frequency after it passes through the filter.
    private static float FilteredRms(BiquadFilter filter, float frequency, int samples = 4096)
    {
        double sumSq = 0;
        for (int n = 0; n < samples; n++)
        {
            float input = MathF.Sin(2f * MathF.PI * frequency * n / SampleRate);
            float output = filter.Process(input);
            if (n >= samples / 2) // ignore the settling transient
                sumSq += output * (double)output;
        }
        return MathF.Sqrt((float)(sumSq / (samples / 2)));
    }

    [Fact]
    public void LowPass_PassesLowTonesAndAttenuatesHighOnes()
    {
        float low = FilteredRms(new BiquadFilter(BiquadType.LowPass, SampleRate, 1000f), 200f);
        float high = FilteredRms(new BiquadFilter(BiquadType.LowPass, SampleRate, 1000f), 12000f);
        Assert.True(low > 0.6f, $"low tone kept its level ({low})");
        Assert.True(high < 0.1f, $"high tone was cut ({high})");
    }

    [Fact]
    public void HighPass_PassesHighTonesAndAttenuatesLowOnes()
    {
        float low = FilteredRms(new BiquadFilter(BiquadType.HighPass, SampleRate, 4000f), 200f);
        float high = FilteredRms(new BiquadFilter(BiquadType.HighPass, SampleRate, 4000f), 15000f);
        Assert.True(high > 0.6f, $"high tone kept its level ({high})");
        Assert.True(low < 0.1f, $"low tone was cut ({low})");
    }

    [Fact]
    public void Notch_RemovesTheCentreToneButKeepsNeighbours()
    {
        float atCentre = FilteredRms(new BiquadFilter(BiquadType.Notch, SampleRate, 3000f, 8f), 3000f);
        float away = FilteredRms(new BiquadFilter(BiquadType.Notch, SampleRate, 3000f, 8f), 500f);
        Assert.True(atCentre < 0.2f, $"the centre tone was notched out ({atCentre})");
        Assert.True(away > 0.6f, $"a distant tone survived ({away})");
    }

    [Fact]
    public void ProcessBlock_FiltersInPlaceAndStaysInRange()
    {
        var filter = new BiquadFilter(BiquadType.LowPass, SampleRate, 2000f);
        short[] block = new short[256];
        for (int i = 0; i < block.Length; i++)
            block[i] = (short)(i % 2 == 0 ? 30000 : -30000); // near-Nyquist square, above the cut-off
        filter.ProcessBlock(block);
        foreach (short s in block[128..])
            Assert.InRange((int)s, -6000, 6000); // the fast alternation is heavily attenuated
    }

    [Fact]
    public void Reset_ClearsFilterMemory()
    {
        var a = new BiquadFilter(BiquadType.LowPass, SampleRate, 1000f);
        for (int i = 0; i < 100; i++) a.Process(1f);
        a.Reset();
        var fresh = new BiquadFilter(BiquadType.LowPass, SampleRate, 1000f);
        Assert.Equal(fresh.Process(0.5f), a.Process(0.5f), 5);
    }

    [Fact]
    public void Adsr_RisesAttacksDecaysSustainsAndReleases()
    {
        var env = new AdsrEnvelope { Attack = 0.1f, Decay = 0.1f, Sustain = 0.5f, Release = 0.1f };
        Assert.False(env.IsActive);

        env.NoteOn();
        // Attack: halfway through the attack time the level is roughly half.
        env.Process(0.05f);
        Assert.InRange(env.Level, 0.3f, 0.7f);
        Assert.True(env.IsActive);

        // Attack then decay settle to the sustain level and hold there.
        for (int i = 0; i < 40; i++) env.Process(0.01f);
        Assert.Equal(EnvelopePhase.Sustain, env.Phase);
        Assert.Equal(0.5f, env.Level, 2);
        for (int i = 0; i < 50; i++) env.Process(0.01f);
        Assert.Equal(0.5f, env.Level, 2); // stays put while held

        // Release falls back to silence and the envelope goes idle.
        env.NoteOff();
        for (int i = 0; i < 12; i++) env.Process(0.01f);
        Assert.Equal(0f, env.Level, 3);
        Assert.False(env.IsActive);
    }

    [Fact]
    public void Adsr_ZeroAttackAndDecayJumpStraightToSustain()
    {
        var env = new AdsrEnvelope { Attack = 0f, Decay = 0f, Sustain = 0.4f, Release = 0f };
        env.NoteOn();
        env.Process(0.01f);
        Assert.Equal(EnvelopePhase.Sustain, env.Phase);
        Assert.Equal(0.4f, env.Level, 3);

        env.NoteOff();
        env.Process(0.01f);
        Assert.False(env.IsActive);
        Assert.Equal(0f, env.Level, 3);
    }

    [Fact]
    public void Adsr_ResetSilencesImmediately()
    {
        var env = new AdsrEnvelope { Attack = 0.5f };
        env.NoteOn();
        env.Process(0.1f);
        Assert.True(env.Level > 0f);
        env.Reset();
        Assert.Equal(0f, env.Level);
        Assert.False(env.IsActive);
    }
}
