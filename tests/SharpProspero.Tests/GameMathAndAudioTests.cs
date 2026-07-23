// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Audio;
using SharpProspero.Numerics;
using System;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class GameMathAndAudioTests
{
    // --- Transform ---

    [Fact]
    public void LookRotation_FacesTheTargetWithAProperRotation()
    {
        // Looking down +X: the local forward (-Z) must rotate to +X, and up must stay up. A reflection
        // basis (the earlier defect) would leave forward pointing at -Z instead.
        System.Numerics.Quaternion q = Transform.LookRotation(new System.Numerics.Vector3(1, 0, 0), System.Numerics.Vector3.UnitY);
        System.Numerics.Vector3 forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, q);
        System.Numerics.Vector3 up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, q);
        Assert.True((forward - new System.Numerics.Vector3(1, 0, 0)).Length() < 1e-4f, "forward faces the target");
        Assert.True(up.Y > 0.9f, "up stays up (no roll, not mirrored)");
    }

    // --- Vector2 ---

    [Fact]
    public void Vector2_ArithmeticAndProperties()
    {
        var a = new Vector2(3f, 4f);
        Assert.Equal(25f, a.LengthSquared);
        Assert.Equal(5f, a.Length, 4);

        Assert.Equal(new Vector2(4f, 6f), a + new Vector2(1f, 2f));
        Assert.Equal(new Vector2(2f, 2f), a - new Vector2(1f, 2f));
        Assert.Equal(new Vector2(6f, 8f), a * 2f);
        Assert.Equal(new Vector2(6f, 8f), 2f * a);
        Assert.Equal(new Vector2(1.5f, 2f), a / 2f);
        Assert.Equal(new Vector2(-3f, -4f), -a);
        Assert.Equal(new Vector2(9f, 4f), a.WithX(9f));
    }

    [Fact]
    public void Vector2_Normalized_HasUnitLengthAndHandlesZero()
    {
        Vector2 n = new Vector2(3f, 4f).Normalized();
        Assert.Equal(1f, n.Length, 4);
        Assert.Equal(Vector2.Zero, Vector2.Zero.Normalized());
    }

    [Fact]
    public void Vector2_DotDistanceLerpRotate()
    {
        Assert.Equal(0f, Vector2.Dot(Vector2.UnitX, Vector2.UnitY));
        Assert.Equal(11f, Vector2.Dot(new Vector2(1f, 2f), new Vector2(3f, 4f)));
        Assert.Equal(5f, Vector2.Distance(Vector2.Zero, new Vector2(3f, 4f)), 4);

        Vector2 mid = Vector2.Lerp(Vector2.Zero, new Vector2(10f, 20f), 0.5f);
        Assert.Equal(new Vector2(5f, 10f), mid);

        // A quarter turn takes the x-axis onto the y-axis.
        Vector2 turned = Vector2.UnitX.Rotate(MathF.PI / 2f);
        Assert.Equal(0f, turned.X, 4);
        Assert.Equal(1f, turned.Y, 4);
    }

    [Fact]
    public void Vector2_EqualityAndHashing()
    {
        Assert.True(new Vector2(1f, 2f) == new Vector2(1f, 2f));
        Assert.True(new Vector2(1f, 2f) != new Vector2(1f, 3f));
        Assert.Equal(new Vector2(1f, 2f).GetHashCode(), new Vector2(1f, 2f).GetHashCode());
    }

    // --- GameRandom helpers ---

    [Fact]
    public void GameRandom_HelpersAreInRangeAndDeterministic()
    {
        var a = new GameRandom(1234);
        var b = new GameRandom(1234);
        for (int i = 0; i < 1000; i++)
        {
            float single = a.NextSingle();
            Assert.InRange(single, 0f, 0.99999994f);
            Assert.Equal(single, b.NextSingle()); // same seed, same sequence

            float ranged = a.NextSingle(-2f, 2f);
            Assert.InRange(ranged, -2f, 2f);
            b.NextSingle(-2f, 2f);
        }

        Assert.False(new GameRandom(1).NextBool(0.0));
        Assert.True(new GameRandom(1).NextBool(1.0));
    }

    [Fact]
    public void NextSingle_StaysBelowOne()
    {
        var rng = new GameRandom(9876);
        for (int i = 0; i < 100_000; i++)
            Assert.True(rng.NextSingle() < 1f);
        // The largest value the generator can produce is still strictly below one.
        Assert.True(((1u << 24) - 1) * (1f / (1u << 24)) < 1f);
    }

    [Fact]
    public void GameRandom_PickAndShuffle()
    {
        var rng = new GameRandom(7);
        int[] one = [42];
        Assert.Equal(42, rng.Pick<int>(one));
        Assert.Throws<ArgumentException>(() => rng.Pick(ReadOnlySpan<int>.Empty));

        // Shuffle is a permutation, and the same seed gives the same order.
        int[] x = [1, 2, 3, 4, 5, 6, 7, 8];
        int[] y = [1, 2, 3, 4, 5, 6, 7, 8];
        new GameRandom(99).Shuffle<int>(x);
        new GameRandom(99).Shuffle<int>(y);
        Assert.Equal(x, y);
        Assert.Equal(Enumerable.Range(1, 8), x.OrderBy(v => v));
    }

    // --- ToneGenerator ---

    [Fact]
    public void ToneGenerator_FillsMatchingStereoSamples()
    {
        var tone = new ToneGenerator(48000) { Waveform = Waveform.Sine, Frequency = 440, Amplitude = 0.5f };
        short[] block = new short[512];
        tone.Fill(block);

        // Left and right of each frame are equal.
        for (int i = 0; i < block.Length; i += 2)
            Assert.Equal(block[i], block[i + 1]);

        // Not silent, and within the amplitude ceiling.
        int peak = block.Max(s => Math.Abs((int)s));
        Assert.InRange(peak, 1, (int)(0.5f * 32767) + 1);
    }

    [Fact]
    public void ToneGenerator_SilentAtZeroAmplitude()
    {
        var tone = new ToneGenerator(48000) { Amplitude = 0f };
        short[] block = new short[256];
        tone.Fill(block);
        Assert.All(block, s => Assert.Equal((short)0, s));
    }

    [Fact]
    public void ToneGenerator_SquareWaveSwingsFullScale()
    {
        // One cycle spans 100 frames, so the first half is high and the second is low.
        var tone = new ToneGenerator(1000) { Waveform = Waveform.Square, Frequency = 10, Amplitude = 1f };
        short[] block = new short[200];
        tone.Fill(block);

        Assert.Equal(32767, block[0]);      // frame 0, phase 0
        Assert.Equal(-32767, block[120]);   // frame 60, phase 0.6
    }

    [Fact]
    public void ToneGenerator_RenderLengthAndReset()
    {
        var tone = new ToneGenerator(48000) { Waveform = Waveform.Sine, Frequency = 440 };
        short[] rendered = tone.Render(0.01);
        Assert.Equal((int)(0.01 * 48000) * 2, rendered.Length);

        tone.Reset();
        short[] block = new short[4];
        tone.Fill(block);
        Assert.Equal(0, block[0]); // a sine restarted from phase zero begins at zero
    }
}
