// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class SurfaceEffectsTests
{
    private const int W = 8, H = 6;

    private static void WithSurface(Action<Surface, uint[]> action)
    {
        uint[] pixels = new uint[W * H];
        fixed (uint* p = pixels)
            action(new Surface(p, W, H), pixels);
    }

    [Fact]
    public void Invert_FlipsColorKeepsAlpha()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.FromArgb(0x80, 10, 20, 30));
            surface.Invert();
            Color c = new(pixels[0]);
            Assert.Equal(0x80, c.A);
            Assert.Equal(245, c.R);
            Assert.Equal(235, c.G);
            Assert.Equal(225, c.B);
        });
    }

    [Fact]
    public void ToGrayscale_MakesChannelsEqual()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.FromRgb(255, 0, 0));
            surface.ToGrayscale();
            Color c = new(pixels[0]);
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.G, c.B);
            // Rec.601 luma of pure red is about 0.30 -> ~77.
            Assert.InRange(c.R, 74, 80);
        });
    }

    [Fact]
    public void AdjustBrightness_ClampsAtEnds()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.FromRgb(250, 5, 128));
            surface.AdjustBrightness(20);
            Color c = new(pixels[0]);
            Assert.Equal(255, c.R); // clamped
            Assert.Equal(25, c.G);
            Assert.Equal(148, c.B);

            surface.Clear(Color.FromRgb(10, 200, 100));
            surface.AdjustBrightness(-30);
            c = new(pixels[0]);
            Assert.Equal(0, c.R); // clamped
            Assert.Equal(170, c.G);
            Assert.Equal(70, c.B);
        });
    }

    [Fact]
    public void FlipHorizontal_MirrorsRows()
    {
        WithSurface((surface, pixels) =>
        {
            for (int x = 0; x < W; x++)
                surface.SetPixel(x, 0, Color.FromRgb((byte)(x * 10), 0, 0));
            surface.FlipHorizontal();
            Assert.Equal((byte)((W - 1) * 10), new Color(pixels[0]).R);
            Assert.Equal(0, new Color(pixels[W - 1]).R);
        });
    }

    [Fact]
    public void FlipVertical_MirrorsColumns()
    {
        WithSurface((surface, pixels) =>
        {
            for (int y = 0; y < H; y++)
                surface.SetPixel(0, y, Color.FromRgb(0, (byte)(y * 10), 0));
            surface.FlipVertical();
            Assert.Equal((byte)((H - 1) * 10), new Color(pixels[0]).G);
            Assert.Equal(0, new Color(pixels[(H - 1) * W]).G);
        });
    }

    [Fact]
    public void Tint_MovesTowardTargetColor()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.FromRgb(0, 0, 0));
            surface.Tint(Color.FromRgb(200, 100, 40), 0.5f);
            Color c = new(pixels[0]);
            Assert.InRange(c.R, 98, 102);
            Assert.InRange(c.G, 48, 52);
            Assert.InRange(c.B, 18, 22);
        });
    }

    [Fact]
    public void BoxBlur_AveragesTowardNeighbours()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.Black);
            surface.SetPixel(4, 3, Color.White);
            surface.BoxBlur(1);
            // The bright pixel spreads to its neighbours and dims itself.
            Assert.True(new Color(pixels[3 * W + 4]).R < 255);
            Assert.True(new Color(pixels[3 * W + 5]).R > 0);
            Assert.True(new Color(pixels[2 * W + 4]).R > 0);
        });
    }
}
