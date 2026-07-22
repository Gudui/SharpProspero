// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The tiled surface layout is the graphics address library's own size computation: block dimensions from
// the swizzle-mode tables, rows and columns padded to the block, size the product times the element size.
// These vectors are computed by hand from the same block tables. None of this uses the address swizzle
// equations (those only place pixel bytes into tiled order, which is done offline).
public sealed class AgcSurfaceTests
{
    [Fact]
    public void RenderTargetUses64KBBlocksAndPadsToThem()
    {
        // 1920x1080, 4 bytes. Render-target tiling: 64KB blocks are 128x128 elements for 4-byte elements.
        // 1920 pads to 1920 (a multiple of 128); 1080 pads to 1152 (9*128).
        var l = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 1920, 1080, 4));
        Assert.Equal(128u, l.BlockWidth);
        Assert.Equal(128u, l.BlockHeight);
        Assert.Equal(65536u, l.BaseAlignBytes);
        Assert.Equal(1920u, l.Mips[0].PaddedWidth);
        Assert.Equal(1152u, l.Mips[0].PaddedHeight);
        Assert.Equal(8847360ul, l.TotalSizeBytes);   // 1920 * 1152 * 4
    }

    [Fact]
    public void DepthTargetSizesLikeTheRenderTargetForTheSameExtent()
    {
        // Depth uses a Z-order 64KB swizzle; the size (block dims) is the same as the render target here.
        var rt = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 1280, 720, 4));
        var ds = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.Depth, AgcSurfaceDimension.TwoD, 1280, 720, 4));
        Assert.Equal(rt.TotalSizeBytes, ds.TotalSizeBytes);
        Assert.Equal(65536u, ds.BaseAlignBytes);
    }

    [Fact]
    public void SmallRenderTargetIsOneBlock()
    {
        var l = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 64, 64, 4));
        Assert.Equal(65536ul, l.TotalSizeBytes);     // 128 * 128 * 4 = one 64KB block
    }

    [Fact]
    public void MultisampledTargetUsesTheMsaaBlockTableAndScalesByFragments()
    {
        // 4x MSAA (fragLog2 2), 4 bytes: MSAA block is 64x64; size scales by the fragment count.
        var l = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 256, 256, 4, numFragments: 4));
        Assert.Equal(64u, l.BlockWidth);
        Assert.Equal(64u, l.BlockHeight);
        Assert.Equal(1048576ul, l.TotalSizeBytes);   // 256 * 256 * 4 * 4 fragments
    }

    [Fact]
    public void LinearModeMatchesTheLinearSurfaceHelper()
    {
        foreach ((uint w, uint h, uint bpe) in new[] { (1920u, 1080u, 4u), (100u, 50u, 4u), (300u, 2u, 1u), (10u, 10u, 16u) })
        {
            var full = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.Linear, AgcSurfaceDimension.TwoD, w, h, bpe));
            LinearSurfaceLayout simple = LinearSurface.Compute(w, h, bpe);
            Assert.Equal(simple.SizeBytes, full.TotalSizeBytes);
            Assert.Equal(256u, full.BaseAlignBytes);
        }
    }

    [Fact]
    public void SlicesMultiplyTheTiledSize()
    {
        var one = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 256, 256, 4));
        var six = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 256, 256, 4, numSlices: 6));
        Assert.Equal(one.TotalSizeBytes * 6, six.TotalSizeBytes);
    }

    [Fact]
    public void MippedSurfaceEngagesTheMipTail()
    {
        // A full mip chain packs its small levels into the mip tail, so the tail begins before the last level.
        var l = AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 256, 256, 4, numMips: 9));
        Assert.True(l.FirstMipLevelInTail < 9, "the small mips should fall into the tail");
        Assert.True(l.TotalSizeBytes >= 256ul * 256 * 4, "at least the top mip");
        Assert.Equal(9, l.Mips.Length);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 0, 10, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 10, 10, 3)));  // bpe not a power of two
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcSurface.Compute(new AgcSurfaceDescription(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, 10, 10, 4, numMips: 17)));
    }
}
