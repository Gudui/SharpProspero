// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;
using SharpProspero.Graphics;
using SharpProspero.Interop.Font;
using Xunit;

namespace SharpProspero.Tests;

// The font-engine structures must match the module's layout, or the engine reads the wrong fields. The
// offsets are computed from the header for the x86-64 target and confirmed against the module.
public sealed class FontLayoutTests
{
    [Fact]
    public void Memory_Is64Bytes() => Assert.Equal(64, Marshal.SizeOf<SceFontMemory>());

    [Fact]
    public void OpenDetail_MatchesTheHeader()
    {
        Assert.Equal(32, Marshal.SizeOf<SceFontOpenDetail>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.DetailId)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.Flags)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.SubFontIndex)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.UniqueId)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.Reserved2)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceFontOpenDetail>(nameof(SceFontOpenDetail.Reserved1)));
    }

    [Fact]
    public void RenderSurface_MatchesTheInitLayout()
    {
        // sceFontRenderSurfaceInit writes buffer@0, widthByte@8, pixelSizeByte@0xC, width@0x10, height@0x14.
        Assert.Equal(128, Marshal.SizeOf<SceFontRenderSurface>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceFontRenderSurface>(nameof(SceFontRenderSurface.Buffer)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontRenderSurface>(nameof(SceFontRenderSurface.WidthByte)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceFontRenderSurface>(nameof(SceFontRenderSurface.PixelSizeByte)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceFontRenderSurface>(nameof(SceFontRenderSurface.Width)));
        Assert.Equal(20, (int)Marshal.OffsetOf<SceFontRenderSurface>(nameof(SceFontRenderSurface.Height)));
    }

    [Fact]
    public void GlyphMetrics_Is32Bytes()
    {
        Assert.Equal(32, Marshal.SizeOf<SceFontGlyphMetrics>());
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontGlyphMetrics>(nameof(SceFontGlyphMetrics.HorizontalBearingX)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceFontGlyphMetrics>(nameof(SceFontGlyphMetrics.HorizontalAdvance)));
    }

    [Fact]
    public void TransImage_Is24Bytes()
    {
        Assert.Equal(24, Marshal.SizeOf<SceFontTransImage>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceFontTransImage>(nameof(SceFontTransImage.Address)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontTransImage>(nameof(SceFontTransImage.WidthByte)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceFontTransImage>(nameof(SceFontTransImage.ImageWidth)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceFontTransImage>(nameof(SceFontTransImage.ImageHeight)));
    }

    [Fact]
    public void RenderResult_PlacesTheImageMetricsAtOffset40()
    {
        Assert.Equal(64, Marshal.SizeOf<SceFontRenderResult>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceFontRenderResult>(nameof(SceFontRenderResult.TransImage)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontRenderResult>(nameof(SceFontRenderResult.SurfaceImage)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceFontRenderResult>(nameof(SceFontRenderResult.UpdateX)));
        Assert.Equal(40, (int)Marshal.OffsetOf<SceFontRenderResult>(nameof(SceFontRenderResult.ImageMetrics)));
    }

    [Fact]
    public void GlyphImageMetrics_Is24Bytes()
    {
        Assert.Equal(24, Marshal.SizeOf<SceFontGlyphImageMetrics>());
        Assert.Equal(8, (int)Marshal.OffsetOf<SceFontGlyphImageMetrics>(nameof(SceFontGlyphImageMetrics.Advance)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceFontGlyphImageMetrics>(nameof(SceFontGlyphImageMetrics.Width)));
    }

    [Fact]
    public void Edition_IsTheSevenZeroZeroEdition()
        => Assert.Equal(0x0700100000000000UL, SceFont.Edition);

    // The compositor the text renderer builds on: blend a color over a pixel at a coverage alpha.
    [Fact]
    public unsafe void BlendPixel_CompositesCoverageInColor()
    {
        uint[] pixels = new uint[4]; // a 2x2 surface
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, 2, 2);
            surface.Clear(Color.Black);

            // Full coverage replaces the pixel with the color.
            surface.BlendPixel(0, 0, 0x00FFFFFFu, 255);
            Assert.Equal(Color.White.Value, pixels[0]);

            // Zero coverage leaves the pixel unchanged.
            surface.BlendPixel(1, 0, 0x00FFFFFFu, 0);
            Assert.Equal(Color.Black.Value, pixels[1]);

            // Half coverage of white over black is a mid grey.
            surface.BlendPixel(0, 1, 0x00FFFFFFu, 128);
            var mid = new Color(pixels[2]);
            Assert.InRange(mid.R, 120, 136);
            Assert.Equal(mid.R, mid.G);
            Assert.Equal(mid.R, mid.B);

            // Out-of-bounds coordinates are ignored, not a crash.
            surface.BlendPixel(9, 9, 0x00FFFFFFu, 255);
        }
    }
}
