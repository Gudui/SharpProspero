// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class SurfaceDrawingTests
{
    private const int Size = 40;

    private static void WithSurface(SurfaceAction action)
    {
        uint[] pixels = new uint[Size * Size];
        fixed (uint* p = pixels)
            action(new Surface(p, Size, Size), pixels);
    }

    private delegate void SurfaceAction(Surface surface, uint[] pixels);

    private static uint At(uint[] pixels, int x, int y) => pixels[y * Size + x];

    [Fact]
    public void Region_ClipsAndOffsetsOrigin()
    {
        WithSurface((surface, pixels) =>
        {
            Surface region = surface.Region(10, 8, 6, 5);
            Assert.Equal(6, region.Width);
            Assert.Equal(5, region.Height);

            region.Clear(Color.White);
            // Only the region rectangle is written; a pixel just outside stays background.
            Assert.Equal(0xFFFFFFFFu, At(pixels, 10, 8));
            Assert.Equal(0xFFFFFFFFu, At(pixels, 15, 12));
            Assert.Equal(0u, At(pixels, 9, 8));
            Assert.Equal(0u, At(pixels, 16, 8));
            Assert.Equal(0u, At(pixels, 10, 13));
        });
    }

    [Fact]
    public void Region_ClampsToBounds()
    {
        WithSurface((surface, _) =>
        {
            Surface region = surface.Region(-5, -5, 100, 100);
            Assert.Equal(Size, region.Width);
            Assert.Equal(Size, region.Height);
        });
    }

    [Fact]
    public void FillVerticalGradient_EndpointsMatchColors()
    {
        WithSurface((surface, pixels) =>
        {
            Color top = Color.FromRgb(0, 0, 0);
            Color bottom = Color.FromRgb(0xFF, 0xFF, 0xFF);
            surface.FillVerticalGradient(0, 0, Size, 10, top, bottom);

            Assert.Equal(top.Value, At(pixels, 0, 0));
            Assert.Equal(bottom.Value, At(pixels, 0, 9));
            // A middle row is between the endpoints.
            byte mid = (byte)(At(pixels, 0, 5) & 0xFF);
            Assert.InRange(mid, 1, 254);
        });
    }

    [Fact]
    public void FillRoundedRect_RoundsTheCorners()
    {
        WithSurface((surface, pixels) =>
        {
            surface.FillRoundedRect(4, 4, 20, 20, 6, Color.White);
            // The corner pixel is cut away; the center and mid-edges are filled.
            Assert.Equal(0u, At(pixels, 4, 4));
            Assert.Equal(0xFFFFFFFFu, At(pixels, 14, 14));
            Assert.Equal(0xFFFFFFFFu, At(pixels, 14, 4));   // top edge midpoint
            Assert.Equal(0xFFFFFFFFu, At(pixels, 4, 14));    // left edge midpoint
        });
    }

    [Fact]
    public void FillTriangle_FillsInteriorNotExterior()
    {
        WithSurface((surface, pixels) =>
        {
            surface.FillTriangle(0, 0, 12, 0, 0, 12, Color.White);
            Assert.Equal(0xFFFFFFFFu, At(pixels, 1, 1));    // inside (x+y < 12)
            Assert.Equal(0u, At(pixels, 10, 10));           // outside the hypotenuse
        });
    }

    [Fact]
    public void FillPolygon_FillsASquareLikeARect()
    {
        WithSurface((surface, pixels) =>
        {
            ReadOnlySpan<(int, int)> square = [(5, 5), (15, 5), (15, 15), (5, 15)];
            surface.FillPolygon(square, Color.White);
            Assert.Equal(0xFFFFFFFFu, At(pixels, 10, 10));
            Assert.Equal(0u, At(pixels, 2, 2));
            Assert.Equal(0u, At(pixels, 20, 20));
        });
    }

    [Fact]
    public void BlitScaled_NearestSamplesTheSource()
    {
        WithSurface((dest, destPixels) =>
        {
            uint[] src = [Color.Red.Value, Color.Green.Value, Color.Blue.Value, Color.White.Value]; // 2x2
            fixed (uint* sp = src)
            {
                var source = new Surface(sp, 2, 2);
                dest.BlitScaled(source, 0, 0, 4, 4);
            }
            // Corners map to the four source pixels.
            Assert.Equal(Color.Red.Value, At(destPixels, 0, 0));
            Assert.Equal(Color.Green.Value, At(destPixels, 3, 0));
            Assert.Equal(Color.Blue.Value, At(destPixels, 0, 3));
            Assert.Equal(Color.White.Value, At(destPixels, 3, 3));
        });
    }

    [Fact]
    public void ThickLine_CoversMultipleRows()
    {
        WithSurface((surface, pixels) =>
        {
            surface.DrawLine(5, 20, 34, 20, Color.White, thickness: 5);
            // A horizontal thick line covers rows above and below the center.
            Assert.Equal(0xFFFFFFFFu, At(pixels, 20, 19));
            Assert.Equal(0xFFFFFFFFu, At(pixels, 20, 20));
            Assert.Equal(0xFFFFFFFFu, At(pixels, 20, 21));
        });
    }
}
