// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// The tiler moves pixel bytes between linear and hardware-tiled order using the graphics address
// library's own swizzle equations (extracted and cross-checked against the shipped source). These tests
// pin the absolute low-order layout, prove the address map is a bijection (a swizzle is one-to-one over a
// block), and prove tile followed by detile is the identity for a range of shapes.
public sealed class AgcTilerTests
{
    private static AgcSurfaceDescription Rt(uint w, uint h, uint bpe, uint mips = 1, uint slices = 1, uint frags = 1)
        => new(AgcTileMode.RenderTarget, AgcSurfaceDimension.TwoD, w, h, bpe, numMips: mips, numSlices: slices, numFragments: frags);

    [Fact]
    public void TopLeftElementIsOffsetZero()
    {
        Assert.Equal(0ul, AgcTiler.ComputeElementByteOffset(Rt(256, 256, 4), 0, 0));
        Assert.Equal(0ul, AgcTiler.ComputeElementByteOffset(new AgcSurfaceDescription(AgcTileMode.Depth, AgcSurfaceDimension.TwoD, 256, 256, 4), 0, 0));
    }

    [Fact]
    public void LowColumnBitsAreContiguous()
    {
        // For a 4-byte render target the two low column bits address contiguous bytes, so the first four
        // columns of the first row are byte offsets 0, 4, 8, 12 - a run of one element each.
        var d = Rt(256, 256, 4);
        Assert.Equal(0ul, AgcTiler.ComputeElementByteOffset(d, 0, 0));
        Assert.Equal(4ul, AgcTiler.ComputeElementByteOffset(d, 1, 0));
        Assert.Equal(8ul, AgcTiler.ComputeElementByteOffset(d, 2, 0));
        Assert.Equal(12ul, AgcTiler.ComputeElementByteOffset(d, 3, 0));
    }

    [Fact]
    public void RenderTargetBlockIsABijection()
    {
        // 128x128 4-byte elements are exactly one 64 KB block; every element must map to a distinct
        // 4-byte slot and the slots must fill the block with no gaps.
        var d = Rt(128, 128, 4);
        var seen = new HashSet<ulong>();
        ulong max = 0;
        for (uint y = 0; y < 128; y++)
            for (uint x = 0; x < 128; x++)
            {
                ulong off = AgcTiler.ComputeElementByteOffset(d, x, y);
                Assert.True(off % 4 == 0, "offset is element-aligned");
                Assert.True(seen.Add(off), "offset is unique");
                if (off > max) max = off;
            }
        Assert.Equal(16384, seen.Count);
        Assert.Equal(65532ul, max); // 65536 - 4: the block is fully covered
    }

    [Theory]
    [InlineData(1920, 1080, 4)]
    [InlineData(256, 256, 4)]
    [InlineData(200, 120, 2)]
    [InlineData(64, 64, 16)]
    [InlineData(1, 1, 4)]
    [InlineData(300, 7, 1)]
    public void RoundTripRenderTarget(uint w, uint h, uint bpe)
    {
        RoundTrip(Rt(w, h, bpe));
    }

    [Theory]
    [InlineData(1280, 720, 4)]
    [InlineData(128, 128, 2)]
    public void RoundTripDepth(uint w, uint h, uint bpe)
    {
        RoundTrip(new AgcSurfaceDescription(AgcTileMode.Depth, AgcSurfaceDimension.TwoD, w, h, bpe));
    }

    [Fact]
    public void RoundTripLinear()
    {
        RoundTrip(new AgcSurfaceDescription(AgcTileMode.Linear, AgcSurfaceDimension.TwoD, 300, 128, 4));
    }

    [Fact]
    public void RoundTripMultipleSlices()
    {
        var d = Rt(128, 96, 4, slices: 4);
        for (uint s = 0; s < 4; s++)
            RoundTrip(d, arraySlice: s);
    }

    [Fact]
    public void RoundTripEveryMipLevel()
    {
        var d = Rt(512, 512, 4, mips: 6);
        for (uint m = 0; m < 6; m++)
            RoundTrip(d, mipLevel: m);
    }

    [Fact]
    public void RoundTripMultisampledRenderTarget()
    {
        RoundTrip(Rt(256, 128, 4, frags: 4));
    }

    private static void RoundTrip(in AgcSurfaceDescription d, uint mipLevel = 0, uint arraySlice = 0)
    {
        ulong tiledSize = AgcSurface.Compute(d).TotalSizeBytes;
        int linearSize = (int)AgcTiler.LinearSizeBytes(d, mipLevel);
        var source = new byte[linearSize];
        for (int i = 0; i < source.Length; i++)
            source[i] = (byte)(i * 31 + 7);

        var tiled = new byte[tiledSize];
        AgcTiler.Tile(tiled, source, d, mipLevel, arraySlice);

        var back = new byte[linearSize];
        AgcTiler.Detile(back, tiled, d, mipLevel, arraySlice);

        Assert.Equal(source, back);
        Assert.True(Array.Exists(tiled, b => b != 0), "the tiled buffer was actually written");
    }

    [Fact]
    public void TileRejectsUndersizedSpans()
    {
        var d = Rt(256, 256, 4);
        ulong tiledSize = AgcSurface.Compute(d).TotalSizeBytes;
        var linear = new byte[AgcTiler.LinearSizeBytes(d)];
        // The reported parameter name must match the public argument (tiled/linear), not an internal one.
        Assert.Equal("tiled", Assert.Throws<ArgumentException>(() => AgcTiler.Tile(new byte[tiledSize - 1], linear, d)).ParamName);
        Assert.Equal("linear", Assert.Throws<ArgumentException>(() => AgcTiler.Tile(new byte[tiledSize], new byte[8], d)).ParamName);
        Assert.Equal("tiled", Assert.Throws<ArgumentException>(() => AgcTiler.Detile(linear, new byte[tiledSize - 1], d)).ParamName);
        Assert.Equal("linear", Assert.Throws<ArgumentException>(() => AgcTiler.Detile(new byte[8], new byte[tiledSize], d)).ParamName);
    }

    [Fact]
    public void ComputeElementByteOffsetRejectsOutOfRange()
    {
        var d = Rt(64, 64, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcTiler.ComputeElementByteOffset(d, 64, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcTiler.ComputeElementByteOffset(d, 0, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcTiler.ComputeElementByteOffset(d, 0, 0, z: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgcTiler.ComputeElementByteOffset(d, 0, 0, mipLevel: 1));
    }
}
