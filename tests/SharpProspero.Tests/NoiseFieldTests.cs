using SharpProspero.Numerics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class NoiseFieldTests
{
    [Fact]
    public void SameSeed_GivesTheSameField()
    {
        var a = new NoiseField(1234);
        var b = new NoiseField(1234);
        Assert.Equal(a.Noise2D(3.7f, 9.1f), b.Noise2D(3.7f, 9.1f));
        Assert.Equal(a.Noise3D(1.2f, 3.4f, 5.6f), b.Noise3D(1.2f, 3.4f, 5.6f));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentFields()
    {
        Assert.NotEqual(new NoiseField(1).Noise2D(2.5f, 2.5f), new NoiseField(2).Noise2D(2.5f, 2.5f));
    }

    [Fact]
    public void Values_StayInRange()
    {
        var field = new NoiseField(7);
        for (int i = 0; i < 500; i++)
        {
            float x = i * 0.137f, y = i * 0.091f;
            Assert.InRange(field.Noise2D(x, y), -1f, 1f);
            Assert.InRange(field.Noise3D(x, y, x - y), -1f, 1f);
            Assert.InRange(field.FractalNoise2D(x, y, 5), -1f, 1f);
        }
    }

    [Fact]
    public void IsSmooth_AcrossNearbyPoints()
    {
        var field = new NoiseField(42);
        float a = field.Noise2D(10.0f, 10.0f);
        float b = field.Noise2D(10.01f, 10.0f);
        Assert.True(MathF.Abs(a - b) < 0.1f, "a small step gives a small change");
    }

    [Fact]
    public void IsZero_AtLatticePoints()
    {
        // Gradient noise passes through zero at integer coordinates.
        var field = new NoiseField(99);
        Assert.True(MathF.Abs(field.Noise2D(4f, 7f)) < 1e-5f);
        Assert.True(MathF.Abs(field.Noise3D(2f, 3f, 5f)) < 1e-5f);
    }
}
