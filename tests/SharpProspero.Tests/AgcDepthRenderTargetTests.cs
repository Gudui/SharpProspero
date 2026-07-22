// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The depth-stencil render-target register block encodes each field into the exact register bits the
// graphics context register definitions specify - including a floating-point clear value stored as its
// raw bit pattern. These tests pin those encodings.
public sealed class AgcDepthRenderTargetTests
{
    private static CxRegister[] Defaults()
    {
        var d = new CxRegister[16];
        for (int i = 0; i < 16; i++)
            d[i] = new CxRegister((ushort)(0x400 + i), 0);
        return d;
    }

    private static CxDepthRenderTarget Fresh() => new CxDepthRenderTarget().Init(Defaults());

    [Fact]
    public void InitRequiresSixteenDefaults()
    {
        Assert.Throws<ArgumentException>(() => new CxDepthRenderTarget().Init(new CxRegister[8]));
    }

    [Fact]
    public void DepthAndStencilFormatsRoundTrip()
    {
        var rt = Fresh();
        rt.SetDepthFormat(CxDepthRenderTarget.DepthFormat.k32Float);
        rt.SetNumFragments(CxDepthRenderTarget.NumFragments.k4);
        Assert.Equal(CxDepthRenderTarget.DepthFormat.k32Float, rt.GetDepthFormat());
        Assert.Equal(CxDepthRenderTarget.NumFragments.k4, rt.GetNumFragments());
    }

    [Fact]
    public void WidthAndHeightShareRegisterWithoutClobbering()
    {
        var rt = Fresh();
        rt.SetWidth(1920);
        rt.SetHeight(1080);
        Assert.Equal(1920u, rt.GetWidth());
        Assert.Equal(1080u, rt.GetHeight());
    }

    [Fact]
    public void DepthClearValueStoresRawFloatBits()
    {
        var rt = Fresh();
        rt.SetDepthClearValue(1.0f);
        Assert.Equal(1.0f, rt.GetDepthClearValue());
        Assert.Equal(0x3f800000u, rt.Registers[14].Value);   // IEEE-754 bits of 1.0f
        rt.SetDepthClearValue(0.5f);
        Assert.Equal(0.5f, rt.GetDepthClearValue());
    }

    [Fact]
    public void StencilClearValueRoundTrips()
    {
        var rt = Fresh();
        rt.SetStencilClearValue(0xAB);
        Assert.Equal(0xABu, rt.GetStencilClearValue());
    }

    [Fact]
    public void AddressesSplitAcrossTwoRegisters()
    {
        var rt = Fresh();
        const ulong depth = 0x0000_00CD_EF12_3400;
        const ulong htile = 0x0000_0012_3456_7800;
        rt.SetDepthWriteAddress(depth);
        rt.SetHtileAddress(htile);
        Assert.Equal(depth, rt.GetDepthWriteAddress());
        Assert.Equal(htile, rt.GetHtileAddress());
    }

    [Fact]
    public void ArraySliceIndicesUseTheFullThirteenBitSplitField()
    {
        // Both indices are 13-bit fields packed into non-contiguous bits of register 11; the high bits must
        // survive the round trip (they were dropped before the split-field fix).
        var rt = Fresh();
        rt.SetBaseArraySliceIndex(0x1fff);
        rt.SetLastArraySliceIndex(0x1fff);
        Assert.Equal(0x1fffu, rt.GetBaseArraySliceIndex());
        Assert.Equal(0x1fffu, rt.GetLastArraySliceIndex());
        // Only the high split bits set.
        rt.SetLastArraySliceIndex(0x1800);
        Assert.Equal(0x1800u, rt.GetLastArraySliceIndex());
        rt.SetBaseArraySliceIndex(0x1800);
        Assert.Equal(0x1800u, rt.GetBaseArraySliceIndex());
    }

    [Fact]
    public void MipAndSliceIndicesRoundTrip()
    {
        var rt = Fresh();
        rt.SetCurrentMipLevel(7);
        rt.SetBaseArraySliceIndex(3);
        rt.SetLastArraySliceIndex(11);
        Assert.Equal(7u, rt.GetCurrentMipLevel());
        Assert.Equal(3u, rt.GetBaseArraySliceIndex());
        Assert.Equal(11u, rt.GetLastArraySliceIndex());
    }
}
