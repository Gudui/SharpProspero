// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class ColorAndScalingTests
{
    // --- Color HSV and tints ---

    [Fact]
    public void ToHsv_ReadsThePrimaries()
    {
        (float h, float s, float v) = Color.Red.ToHsv();
        Assert.True(System.MathF.Abs(h - 0f) < 0.5f);
        Assert.Equal(1f, s, 2);
        Assert.Equal(1f, v, 2);

        Assert.True(System.MathF.Abs(Color.Green.ToHsv().Hue - 120f) < 0.5f);
        Assert.True(System.MathF.Abs(Color.Blue.ToHsv().Hue - 240f) < 0.5f);

        // Grey has no saturation.
        Assert.Equal(0f, Color.FromRgb(128, 128, 128).ToHsv().Saturation, 2);
    }

    [Fact]
    public void ToHsv_RoundTripsWithFromHsv()
    {
        Color original = Color.FromHsv(200f, 0.6f, 0.8f);
        (float h, float s, float v) = original.ToHsv();
        Assert.True(System.MathF.Abs(h - 200f) < 1.5f);
        Assert.True(System.MathF.Abs(s - 0.6f) < 0.02f);
        Assert.True(System.MathF.Abs(v - 0.8f) < 0.02f);
    }

    [Fact]
    public void ToHsv_IgnoresAlpha()
    {
        (float h, float s, float v) = Color.FromArgb(100, 255, 0, 0).ToHsv();
        Assert.True(System.MathF.Abs(h) < 0.5f);
        Assert.Equal(1f, s, 2);
        Assert.Equal(1f, v, 2);
    }

    [Fact]
    public void DarkenAndLighten_MoveTowardBlackAndWhiteAndKeepAlpha()
    {
        Color dark = Color.FromArgb(200, 255, 255, 255).Darken(0.5f);
        Assert.Equal(128, dark.R);
        Assert.Equal(200, dark.A); // alpha is kept

        Color light = Color.Black.Lighten(0.5f);
        Assert.Equal(128, light.R);
        Assert.Equal(255, light.A);
    }

    // --- Radial gradient ---

    [Fact]
    public void FillRadialGradient_BrightAtTheCentreAndDarkAtTheCorner()
    {
        const int w = 21, h = 21;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
            new Surface(p, w, h).FillRadialGradient(0, 0, w, h, Color.White, Color.Black);

        Assert.True(new Color(pixels[(10 * w) + 10]).R > 200); // centre is near white
        Assert.True(new Color(pixels[0]).R < 60);              // the far corner is near black
    }

    // --- Bilinear scaling ---

    [Fact]
    public void BlitScaledSmooth_InterpolatesBetweenSourcePixels()
    {
        uint[] source = [Color.Black.Value, Color.White.Value]; // 2x1: black then white
        uint[] dest = new uint[4]; // 4x1
        fixed (uint* sp = source)
        fixed (uint* dp = dest)
        {
            var src = new Surface(sp, 2, 1);
            new Surface(dp, 4, 1).BlitScaledSmooth(src, 0, 0, 4, 1);
        }

        Assert.Equal(Color.Black.Value, dest[0]); // clamps to the first source pixel
        Assert.Equal(Color.White.Value, dest[3]); // clamps to the last
        int mid1 = new Color(dest[1]).R;
        int mid2 = new Color(dest[2]).R;
        Assert.InRange(mid1, 40, 110);   // partway between black and white
        Assert.InRange(mid2, 150, 215);
        Assert.True(mid2 > mid1);        // brightening left to right
    }

    [Fact]
    public void BlitScaledSmooth_ReproducesAtTheSameSize()
    {
        uint[] source = [Color.Red.Value, Color.Green.Value, Color.Blue.Value, Color.White.Value]; // 2x2
        uint[] dest = new uint[4];
        fixed (uint* sp = source)
        fixed (uint* dp = dest)
        {
            var src = new Surface(sp, 2, 2);
            new Surface(dp, 2, 2).BlitScaledSmooth(src, 0, 0, 2, 2);
        }
        // At a one-to-one scale each destination pixel lands on a source pixel centre.
        Assert.Equal(Color.Red.Value, dest[0]);
        Assert.Equal(Color.White.Value, dest[3]);
    }
}
