// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The shape operations are checked by reading back the pixels they wrote, so the borders, the filled
// area and the panel stretch are verified rather than assumed.
public sealed unsafe class SurfaceShapeTests
{
    private static void WithSurface(int width, int height, Action<Surface, uint[]> action)
    {
        uint[] pixels = new uint[width * height];
        fixed (uint* p = pixels)
            action(new Surface(p, width, height), pixels);
    }

    [Fact]
    public void DrawRectThick_DrawsTheBorderInsideAndLeavesTheMiddle()
    {
        WithSurface(10, 10, (surface, pixels) =>
        {
            surface.DrawRectThick(0, 0, 10, 10, 2, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[0]);                 // top-left corner
            Assert.Equal(white, pixels[(1 * 10) + 1]);      // still inside the two-pixel border
            Assert.Equal(0u, pixels[(2 * 10) + 2]);         // first pixel past the border
            Assert.Equal(0u, pixels[(5 * 10) + 5]);         // middle is untouched
            Assert.Equal(white, pixels[(9 * 10) + 9]);      // bottom-right corner
        });
    }

    [Fact]
    public void DrawRectThick_IgnoresAnEmptyRectangle()
    {
        WithSurface(4, 4, (surface, pixels) =>
        {
            surface.DrawRectThick(0, 0, 0, 4, 1, Color.White);
            surface.DrawRectThick(0, 0, 4, 0, 1, Color.White);
            Assert.All(pixels, p => Assert.Equal(0u, p));
        });
    }

    [Fact]
    public void FillEllipse_FillsTheCentreAndLeavesTheCorners()
    {
        WithSurface(21, 21, (surface, pixels) =>
        {
            surface.FillEllipse(10, 10, 10, 5, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[(10 * 21) + 10]);    // centre
            Assert.Equal(white, pixels[(10 * 21) + 19]);    // along the wide axis
            Assert.Equal(0u, pixels[(0 * 21) + 0]);         // corner is outside the ellipse
            Assert.Equal(0u, pixels[(0 * 21) + 20]);
        });
    }

    [Fact]
    public void DrawEllipse_MarksTheEdgeAndLeavesTheInside()
    {
        WithSurface(21, 21, (surface, pixels) =>
        {
            surface.DrawEllipse(10, 10, 8, 8, Color.White);

            Assert.Equal(0u, pixels[(10 * 21) + 10]);                 // hollow
            Assert.Equal(Color.White.Value, pixels[(10 * 21) + 18]);  // right edge
            Assert.Equal(Color.White.Value, pixels[(10 * 21) + 2]);   // left edge
        });
    }

    [Fact]
    public void BlitNineSlice_KeepsCornersAndStretchesTheMiddle()
    {
        // A three by three source: each corner a distinct color, the middle another.
        uint[] source = new uint[9];
        uint tl = Color.FromRgb(10, 0, 0).Value, tr = Color.FromRgb(20, 0, 0).Value;
        uint bl = Color.FromRgb(30, 0, 0).Value, br = Color.FromRgb(40, 0, 0).Value;
        uint mid = Color.FromRgb(50, 0, 0).Value;
        source[0] = tl; source[2] = tr; source[6] = bl; source[8] = br;
        source[1] = mid; source[3] = mid; source[4] = mid; source[5] = mid; source[7] = mid;

        fixed (uint* sp = source)
        {
            var src = new Surface(sp, 3, 3);
            WithSurface(9, 9, (surface, pixels) =>
            {
                surface.BlitNineSlice(src, 0, 0, 9, 9, 1);

                Assert.Equal(tl, pixels[0]);                // corners keep their own pixel
                Assert.Equal(tr, pixels[8]);
                Assert.Equal(bl, pixels[(8 * 9) + 0]);
                Assert.Equal(br, pixels[(8 * 9) + 8]);
                Assert.Equal(mid, pixels[(4 * 9) + 4]);     // middle stretched across
            });
        }
    }

    [Fact]
    public void BlitNineSlice_IgnoresAnEmptyTarget()
    {
        uint[] source = new uint[9];
        fixed (uint* sp = source)
        {
            var src = new Surface(sp, 3, 3);
            WithSurface(4, 4, (surface, pixels) =>
            {
                surface.BlitNineSlice(src, 0, 0, 0, 4, 1);
                Assert.All(pixels, p => Assert.Equal(0u, p));
            });
        }
    }

    [Fact]
    public void FillPie_FillsTheSweptQuadrantAndLeavesTheRest()
    {
        WithSurface(41, 41, (surface, pixels) =>
        {
            // Angles are measured from the positive x-axis with y growing downward, so a sweep of a
            // quarter turn from zero covers the lower-right quadrant.
            surface.FillPie(20, 20, 15, 0f, MathF.PI / 2f, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[((20 + 5) * 41) + (20 + 5)]); // lower-right: inside the sweep
            Assert.Equal(0u, pixels[((20 - 5) * 41) + (20 - 5)]);    // upper-left: outside the sweep
            Assert.Equal(0u, pixels[((20 - 5) * 41) + (20 + 5)]);    // upper-right: outside the sweep
        });
    }

    [Fact]
    public void FillArcRing_FillsTheBandAndLeavesTheHoleAndOutside()
    {
        WithSurface(41, 41, (surface, pixels) =>
        {
            surface.FillArcRing(20, 20, 6, 12, 0f, MathF.Tau, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(0u, pixels[(20 * 41) + 20]);          // centre is in the hole
            Assert.Equal(white, pixels[(20 * 41) + (20 + 9)]); // within the band
            Assert.Equal(0u, pixels[(20 * 41) + (20 + 14)]);   // beyond the outer radius
        });
    }

    [Fact]
    public void DrawCircleThick_MarksTheRingAndLeavesTheHole()
    {
        WithSurface(41, 41, (surface, pixels) =>
        {
            surface.DrawCircleThick(20, 20, 12, 3, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[(20 * 41) + (20 + 11)]); // just inside the outer edge
            Assert.Equal(0u, pixels[(20 * 41) + (20 + 4)]);     // inside the hole
            Assert.Equal(0u, pixels[(20 * 41) + 20]);           // centre
        });
    }

    [Fact]
    public void DrawArc_TracesTheSweptSideOnly()
    {
        WithSurface(41, 41, (surface, pixels) =>
        {
            // From zero, a half turn sweeps through the bottom, so the bottom point is on the arc and the
            // top point is not.
            surface.DrawArc(20, 20, 12, 0f, MathF.PI, Color.White);

            Assert.Equal(Color.White.Value, pixels[((20 + 12) * 41) + 20]); // bottom point
            Assert.Equal(0u, pixels[((20 - 12) * 41) + 20]);                // top point
        });
    }

    [Fact]
    public void DrawPolyline_ConnectsInOrderWithoutClosing()
    {
        WithSurface(10, 10, (surface, pixels) =>
        {
            ReadOnlySpan<(int X, int Y)> path = [(0, 0), (8, 0), (8, 8)];
            surface.DrawPolyline(path, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[(0 * 10) + 4]); // along the first segment
            Assert.Equal(white, pixels[(4 * 10) + 8]); // along the second segment
            Assert.Equal(0u, pixels[(4 * 10) + 4]);    // the ends are not joined back together
        });
    }

    [Fact]
    public void DrawTriangle_MarksTheEdgesAndLeavesTheInside()
    {
        WithSurface(12, 12, (surface, pixels) =>
        {
            surface.DrawTriangle(0, 0, 8, 0, 0, 8, Color.White);

            uint white = Color.White.Value;
            Assert.Equal(white, pixels[(0 * 12) + 4]); // top edge
            Assert.Equal(white, pixels[(4 * 12) + 0]); // left edge
            Assert.Equal(0u, pixels[(2 * 12) + 2]);    // interior
        });
    }
}
