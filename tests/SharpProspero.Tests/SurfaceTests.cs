// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class SurfaceTests
{
    private const int Width = 32;
    private const int Height = 16;

    private static void WithSurface(SurfaceAction action)
    {
        uint[] pixels = new uint[Width * Height];
        fixed (uint* p = pixels)
        {
            action(new Surface(p, Width, Height), pixels);
        }
    }

    private delegate void SurfaceAction(Surface surface, uint[] pixels);

    [Fact]
    public void Clear_FillsEveryPixel()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.FromRgb(0x11, 0x22, 0x33));
            foreach (uint pixel in pixels)
                Assert.Equal(0xFF112233u, pixel);
        });
    }

    [Fact]
    public void SetPixel_IgnoresOutOfBounds()
    {
        WithSurface((surface, pixels) =>
        {
            surface.SetPixel(-1, 0, Color.White);
            surface.SetPixel(Width, 0, Color.White);
            surface.SetPixel(0, Height, Color.White);
            foreach (uint pixel in pixels)
                Assert.Equal(0u, pixel);

            surface.SetPixel(3, 4, Color.White);
            Assert.Equal(0xFFFFFFFFu, pixels[4 * Width + 3]);
        });
    }

    [Fact]
    public void FillRect_ClipsToBounds()
    {
        WithSurface((surface, pixels) =>
        {
            surface.FillRect(-4, -4, 8, 8, Color.White);
            // Only the in-bounds quadrant (0..3, 0..3) is written.
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    uint expected = x < 4 && y < 4 ? 0xFFFFFFFFu : 0u;
                    Assert.Equal(expected, pixels[y * Width + x]);
                }
            }
        });
    }

    [Fact]
    public void MeasureText_ScalesWithGlyphWidth()
    {
        Assert.Equal(5 * BitmapFont.GlyphSize * 2, Surface.MeasureText("Hello", 2));
    }

    [Fact]
    public void DrawText_WritesForegroundPixels()
    {
        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.Black);
            surface.DrawText("A", 0, 0, 1, Color.White);

            int lit = 0;
            foreach (uint pixel in pixels)
            {
                if (pixel == 0xFFFFFFFFu)
                    lit++;
            }
            Assert.True(lit > 0, "Expected the glyph to set at least one pixel.");
        });
    }

    [Fact]
    public void DrawRect_WritesOnlyTheOutline()
    {
        WithSurface((surface, pixels) =>
        {
            surface.DrawRect(2, 3, 5, 4, Color.White);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    bool onEdge = (x is >= 2 and <= 6 && (y == 3 || y == 6))
                               || (y is >= 3 and <= 6 && (x == 2 || x == 6));
                    bool inside = x is >= 2 and <= 6 && y is >= 3 and <= 6;
                    uint expected = onEdge ? 0xFFFFFFFFu : 0u;
                    Assert.Equal(expected, pixels[y * Width + x]);
                    if (inside && !onEdge)
                        Assert.Equal(0u, pixels[y * Width + x]);
                }
        });
    }

    [Fact]
    public void DrawLine_DrawsHorizontalRunAndEndpoints()
    {
        WithSurface((surface, pixels) =>
        {
            surface.DrawLine(2, 5, 8, 5, Color.White);
            for (int x = 2; x <= 8; x++)
                Assert.Equal(0xFFFFFFFFu, pixels[5 * Width + x]);
            Assert.Equal(0u, pixels[5 * Width + 1]);
            Assert.Equal(0u, pixels[5 * Width + 9]);

            // A diagonal sets both endpoints.
            surface.DrawLine(0, 0, 6, 6, Color.White);
            Assert.Equal(0xFFFFFFFFu, pixels[0]);
            Assert.Equal(0xFFFFFFFFu, pixels[6 * Width + 6]);
        });
    }

    [Fact]
    public void DrawLine_ClipsOffscreenEndpoints()
    {
        WithSurface((surface, pixels) =>
        {
            // Endpoints outside the surface must not write out of bounds; the span crosses the whole
            // width, so every in-bounds column on the row lands.
            surface.DrawLine(-20, 8, 60, 8, Color.White);
            for (int x = 0; x < Width; x++)
                Assert.Equal(0xFFFFFFFFu, pixels[8 * Width + x]);
        });
    }

    [Fact]
    public void FillCircle_FillsCenterAndClips()
    {
        WithSurface((surface, pixels) =>
        {
            surface.FillCircle(5, 5, 3, Color.White);
            Assert.Equal(0xFFFFFFFFu, pixels[5 * Width + 5]);       // center
            Assert.Equal(0xFFFFFFFFu, pixels[5 * Width + 8]);       // right edge on the axis
            Assert.Equal(0u, pixels[5 * Width + 9]);                // just past the radius
            Assert.Equal(0u, pixels[0]);                           // corner untouched

            // A circle straddling the top-left corner must not write out of bounds.
            surface.FillCircle(0, 0, 4, Color.White);
            Assert.Equal(0xFFFFFFFFu, pixels[0]);
        });
    }

    [Fact]
    public void DrawCircle_DrawsOutlineNotInterior()
    {
        WithSurface((surface, pixels) =>
        {
            surface.DrawCircle(8, 7, 4, Color.White);
            Assert.Equal(0xFFFFFFFFu, pixels[7 * Width + 12]);  // rightmost point (8+4)
            Assert.Equal(0xFFFFFFFFu, pixels[7 * Width + 4]);   // leftmost point (8-4)
            Assert.Equal(0u, pixels[7 * Width + 8]);            // center stays clear
        });
    }

    [Fact]
    public void BlitBlended_CompositesBySourceAlpha()
    {
        // A half-transparent red source over a black destination yields a mid-red.
        uint[] srcPixels = new uint[2 * 2];
        uint halfRed = Color.Red.WithAlpha(128).Value;
        for (int i = 0; i < srcPixels.Length; i++)
            srcPixels[i] = halfRed;

        WithSurface((surface, pixels) =>
        {
            surface.Clear(Color.Black);
            fixed (uint* sp = srcPixels)
            {
                surface.BlitBlended(new Surface(sp, 2, 2), 4, 4);
            }
            var blended = new Color(pixels[4 * Width + 4]);
            Assert.InRange(blended.R, 120, 136);   // ~128 red
            Assert.Equal(0, blended.G);
            Assert.Equal(0, blended.B);
            Assert.Equal(0xFF, blended.A);         // destination stays opaque

            // Fully transparent source leaves the destination unchanged.
            uint[] clear = new uint[1];
            fixed (uint* cp = clear)
            {
                surface.BlitBlended(new Surface(cp, 1, 1), 6, 6);
            }
            Assert.Equal(0xFF000000u, pixels[6 * Width + 6]);
        });
    }

    [Fact]
    public void Blit_CopiesClippedRegion()
    {
        uint[] srcPixels = new uint[4 * 4];
        for (int i = 0; i < srcPixels.Length; i++)
            srcPixels[i] = 0xFF010203u;
        WithSurface((surface, pixels) =>
        {
            fixed (uint* sp = srcPixels)
            {
                var src = new Surface(sp, 4, 4);
                // Straddle the top-left corner: only the lower-right 3x3 lands on the surface.
                surface.Blit(src, -1, -1);
            }
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    uint expected = x < 3 && y < 3 ? 0xFF010203u : 0u;
                    Assert.Equal(expected, pixels[y * Width + x]);
                }
        });
    }

    [Fact]
    public void Stride_AddressesPaddedRows()
    {
        // A framebuffer whose pitch (stride) is wider than the drawn width leaves the padding columns
        // untouched.
        const int stride = Width + 8;
        uint[] pixels = new uint[stride * Height];
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, Width, Height, stride);
            surface.Clear(Color.White);
            for (int y = 0; y < Height; y++)
            {
                Assert.Equal(0xFFFFFFFFu, pixels[y * stride + (Width - 1)]);
                Assert.Equal(0u, pixels[y * stride + Width]); // first padding column stays clear
            }
        }
    }
}
