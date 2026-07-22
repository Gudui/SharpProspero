// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The linear surface layout is arithmetic taken from the graphics address library's linear path. These
// vectors are computed by hand from the same rule: rows pad in X to a 256-byte block, size sums padded
// rows across mips and slices, base alignment is 256.
public sealed class LinearSurfaceTests
{
    [Fact]
    public void FullScreenColourSurfaceIsExact()
    {
        // 1920x1080, 4 bytes per element. Block width = 256/4 = 64 elements; 1920 is a multiple of 64.
        LinearSurfaceLayout l = LinearSurface.Compute(1920, 1080, 4);
        Assert.Equal(1920u, l.PaddedWidthInElements);
        Assert.Equal(7680u, l.RowPitchBytes);          // 1920 * 4
        Assert.Equal(8294400ul, l.SizeBytes);          // 1920 * 1080 * 4
        Assert.Equal(256u, l.BaseAlignBytes);
    }

    [Fact]
    public void NarrowSurfacePadsTheRowToTheBlock()
    {
        // 100 wide, 4 bytes: block width 64, padded to 128. 50 tall.
        LinearSurfaceLayout l = LinearSurface.Compute(100, 50, 4);
        Assert.Equal(128u, l.PaddedWidthInElements);
        Assert.Equal(512u, l.RowPitchBytes);           // 128 * 4
        Assert.Equal(25600ul, l.SizeBytes);            // 128 * 50 * 4
    }

    [Fact]
    public void SingleByteElementUsesA256ElementBlock()
    {
        // 1 byte per element: block width 256; 300 pads to 512.
        LinearSurfaceLayout l = LinearSurface.Compute(300, 2, 1);
        Assert.Equal(512u, l.PaddedWidthInElements);
        Assert.Equal(512u, l.RowPitchBytes);
        Assert.Equal(1024ul, l.SizeBytes);             // 512 * 2 * 1
    }

    [Fact]
    public void SixteenByteElementUsesA16ElementBlock()
    {
        LinearSurfaceLayout l = LinearSurface.Compute(10, 10, 16);
        Assert.Equal(16u, l.PaddedWidthInElements);    // align(10, 16)
        Assert.Equal(256u, l.RowPitchBytes);           // 16 * 16
        Assert.Equal(2560ul, l.SizeBytes);             // 16 * 10 * 16
    }

    [Fact]
    public void MipChainSumsEveryLevel()
    {
        // 4x4, 4 bytes, 3 mips. Each level's row pads to 64 elements (256 bytes).
        // mip0: ceil 4x4 -> 64*4*4 = 1024; mip1: ceil 2x2 -> 64*2*4 = 512; mip2: ceil 1x1 -> 64*1*4 = 256.
        LinearSurfaceLayout l = LinearSurface.Compute(4, 4, 4, numMips: 3);
        Assert.Equal(1792ul, l.SizeBytes);
    }

    [Fact]
    public void SlicesMultiplyTheSize()
    {
        LinearSurfaceLayout one = LinearSurface.Compute(64, 64, 4);
        LinearSurfaceLayout six = LinearSurface.Compute(64, 64, 4, numSlices: 6);
        Assert.Equal(one.SizeBytes * 6, six.SizeBytes);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearSurface.Compute(0, 10, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearSurface.Compute(10, 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearSurface.Compute(10, 10, 3));   // not a power of two
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearSurface.Compute(10, 10, 32));  // over 16 bytes
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearSurface.Compute(10, 10, 4, numMips: 0));
    }
}
