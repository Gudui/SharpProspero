// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The render-target register block encodes each field into the exact register bits the graphics context
// register definitions specify. These tests pin those bit patterns, prove fields packed into the same
// register do not disturb one another, and drive the full setup sequence from a description.
public sealed class AgcRenderTargetTests
{
    // Sixteen synthetic defaults: distinct offsets, zero values - enough to exercise the value encoding
    // and the slot offset arithmetic off-device (the real defaults come from the driver).
    private static CxRegister[] Defaults()
    {
        var d = new CxRegister[16];
        for (int i = 0; i < 16; i++)
            d[i] = new CxRegister((ushort)(0x300 + i), 0);
        return d;
    }

    private static CxRenderTarget Fresh() => new CxRenderTarget().Init(Defaults());

    [Fact]
    public void InitRequiresSixteenDefaults()
    {
        Assert.Throws<ArgumentException>(() => new CxRenderTarget().Init(new CxRegister[15]));
    }

    [Fact]
    public void FormatAndChannelFieldsSharePackWithoutClobbering()
    {
        var rt = Fresh();
        rt.SetFormat(CxRenderTarget.Format.k8_8_8_8);
        rt.SetChannelType(CxRenderTarget.ChannelType.kFloat);
        rt.SetChannelOrder(CxRenderTarget.ChannelOrder.kReversed);

        Assert.Equal(CxRenderTarget.Format.k8_8_8_8, rt.GetFormat());
        Assert.Equal(CxRenderTarget.ChannelType.kFloat, rt.GetChannelType());
        Assert.Equal(CxRenderTarget.ChannelOrder.kReversed, rt.GetChannelOrder());
        // All three live in register 2; the raw value is exactly their bit patterns combined.
        Assert.Equal(0x28u | 0x700u | 0x1000u, rt.Registers[2].Value);
    }

    [Fact]
    public void DimensionFieldsSharingRegister14RoundTrip()
    {
        var rt = Fresh();
        rt.SetHeight(1080);
        rt.SetWidth(1920);
        rt.SetNumMipLevels(3);
        Assert.Equal(1920u, rt.GetWidth());
        Assert.Equal(1080u, rt.GetHeight());
        Assert.Equal(3u, rt.GetNumMipLevels());
    }

    [Fact]
    public void TileModeAndDimensionBelongOnlyToAttrib3()
    {
        var rt = Fresh();
        rt[3] = new CxRegister(rt[3].Offset, 0x0001_FFFF);

        rt.SetTileMode(CxRenderTarget.TileMode.kRenderTarget);
        rt.SetDimension(CxRenderTarget.Dimension.k2d);

        // GFX10.3 carries COLOR_SW_MODE and RESOURCE_TYPE in CB_COLOR0_ATTRIB3 (entry 15).
        // Entry 3 contains separate legacy color/FMask tile indices and sample fields.
        Assert.Equal(0x0001_FFFFu, rt.Registers[3].Value);
        Assert.Equal(CxRenderTarget.TileMode.kRenderTarget, rt.GetTileMode());
        Assert.Equal(CxRenderTarget.Dimension.k2d, rt.GetDimension());
    }

    [Fact]
    public void WidthEncodesWithTheMinusOneBias()
    {
        var rt = Fresh();
        rt.SetWidth(1920);
        // (1920 - 1) << 14, masked to the width field.
        Assert.Equal(((1920u - 1) << 14) & 0x0fffc000u, rt.Registers[14].Value);
        Assert.Equal(1u, Fresh().GetWidth()); // default of 0 decodes to width 1 via the +1 bias
    }

    [Fact]
    public void DataAddressSplitsAcrossTwoRegisters()
    {
        var rt = Fresh();
        const ulong address = 0x0000_00AB_CDEF_1200;
        rt.SetDataAddress(address);
        Assert.Equal((uint)((address >> 8) & 0xffffffff), rt.Registers[0].Value);
        Assert.Equal((uint)((address >> 40) & 0xff), rt.Registers[10].Value & 0xff);
        Assert.Equal(address, rt.GetDataAddress());
    }

    [Fact]
    public void ArraySliceIndicesRoundTrip()
    {
        var rt = Fresh();
        rt.SetBaseArraySliceIndex(5);
        rt.SetLastArraySliceIndex(4095);
        rt.SetCurrentMipLevel(9);
        Assert.Equal(5u, rt.GetBaseArraySliceIndex());
        Assert.Equal(4095u, rt.GetLastArraySliceIndex());
        Assert.Equal(9u, rt.GetCurrentMipLevel());
    }

    [Fact]
    public void SlotShiftsRegisterOffsets()
    {
        var rt = Fresh();
        rt.SetSlot(3);
        Assert.Equal(3u, rt.GetSlot());
        Assert.Equal((ushort)(0x300 + 3 * 15), rt.Registers[0].Offset);  // registers 0..9 stride 15
        Assert.Equal((ushort)(0x30a + 3 * 1), rt.Registers[10].Offset);  // registers 10..15 stride 1
        Assert.Throws<ArgumentOutOfRangeException>(() => rt.SetSlot(8));
    }

    [Fact]
    public void InitializeEncodesTheDescription()
    {
        var rt = Fresh();
        var spec = new RenderTargetSpec(
            CxRenderTarget.Format.k8_8_8_8,
            CxRenderTarget.ChannelType.kUNorm,
            CxRenderTarget.ChannelOrder.kStandard,
            1920, 1080,
            dataAddress: 0x0000_0001_0000_0000);
        AgcRenderTargetSetup.Initialize(rt, spec);

        Assert.Equal(CxRenderTarget.Format.k8_8_8_8, rt.GetFormat());
        Assert.Equal(1920u, rt.GetWidth());
        Assert.Equal(1080u, rt.GetHeight());
        Assert.Equal(CxRenderTarget.TileMode.kRenderTarget, rt.GetTileMode());
        Assert.Equal(CxRenderTarget.Dimension.k2d, rt.GetDimension());
        Assert.Equal(0x0000_0001_0000_0000ul, rt.GetDataAddress());
        // UNorm derives round-by-half, no blend bypass, blend clamp on.
        Assert.Equal(CxRenderTarget.RoundMode.kRoundByHalf, rt.GetRoundMode());
        Assert.Equal(CxRenderTarget.BlendBypass.kDisable, rt.GetBlendBypass());
        Assert.Equal(CxRenderTarget.BlendClamp.kEnable, rt.GetBlendClamp());
        Assert.Equal(CxRenderTarget.MetadataPipeAlignment.kEnable, rt.GetMetadataPipeAlignment());
        Assert.Equal(0u, rt.Registers[3].Value);
        Assert.Equal(0x4506_C000u, rt.Registers[15].Value);
    }

    [Fact]
    public void IntegerFormatDisablesBlend()
    {
        var rt = Fresh();
        var spec = new RenderTargetSpec(
            CxRenderTarget.Format.k32, CxRenderTarget.ChannelType.kUInt, CxRenderTarget.ChannelOrder.kStandard,
            64, 64, dataAddress: 0);
        AgcRenderTargetSetup.Initialize(rt, spec);
        Assert.Equal(CxRenderTarget.BlendBypass.kEnable, rt.GetBlendBypass());
        Assert.Equal(CxRenderTarget.BlendClamp.kDisable, rt.GetBlendClamp());
        Assert.Equal(CxRenderTarget.RoundMode.kTruncate, rt.GetRoundMode());
    }
}
