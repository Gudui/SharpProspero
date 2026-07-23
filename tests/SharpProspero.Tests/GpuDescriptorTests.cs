// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// The descriptors and the viewport block pack fields into the exact bits the graphics processor reads;
// these pin those positions against the recovered layout.
public sealed class GpuDescriptorTests
{
    [Fact]
    public void TextureDescriptorPacksTheGroundedFields()
    {
        var texture = new AgcTextureDescriptor();
        texture.SetBaseAddress(0x100000);            // >> 8 = 0x1000 into word0
        texture.SetFormat(56);                       // k8_8_8_8UNorm, nine bits at word1[20:29]
        texture.SetDimensions(256, 128);             // width-1 split word1[30:32]+word2[0:12], height-1 word2[14:28]
        texture.SetType(AgcImageType.Texture2D);     // 9 in word3[28:31]
        texture.SetChannelOrder(AgcChannelSource.Red, AgcChannelSource.Green, AgcChannelSource.Blue, AgcChannelSource.Alpha);
        texture.SetMipRange(0, 8);                   // last level 8 in word3[16:19]
        texture.SetTilingIndex(13);                  // word3[20:24]

        Span<uint> words = stackalloc uint[AgcTextureDescriptor.WordCount];
        texture.WriteTo(words);

        Assert.Equal(0x1000u, words[0]);                                  // base >> 8
        // word1: format 56 at [20:29], and the low two bits of width-1 (255 & 3 = 3) at [30:32].
        Assert.Equal((56u << 20) | (3u << 30), words[1]);
        // word2: the high twelve bits of width-1 (255 >> 2 = 63), then height-1 at [14:28].
        Assert.Equal(63u | (127u << 14), words[2]);
        // word3: channel order 4|5<<3|6<<6|7<<9, last level 8<<16, tiling 13<<20, type 9<<28.
        uint expectedWord3 = (4u | (5u << 3) | (6u << 6) | (7u << 9)) | (8u << 16) | (13u << 20) | (9u << 28);
        Assert.Equal(expectedWord3, words[3]);
        Assert.Equal(0u, words[4]);                                      // no pitch field; word4 stays zero
    }

    [Fact]
    public void TextureDescriptorArrayRangeIsInTheDepthWord()
    {
        var texture = new AgcTextureDescriptor();
        texture.SetArrayRange(baseSlice: 2, lastSlice: 5);

        Span<uint> words = stackalloc uint[AgcTextureDescriptor.WordCount];
        texture.WriteTo(words);

        // The base slice sits at word4[16:29]; the last slice shares the depth field at word4[0:13].
        Assert.Equal(5u | (2u << 16), words[4]);
    }

    [Fact]
    public void SamplerDescriptorPacksTheGroundedFields()
    {
        var sampler = new AgcSamplerDescriptor();
        sampler.SetAddressModes(AgcAddressMode.ClampToEdge, AgcAddressMode.ClampToEdge, AgcAddressMode.Wrap);
        sampler.SetMaxAnisotropy(4);                 // word0[9:11]
        sampler.SetFilter(AgcFilter.Bilinear, AgcFilter.Bilinear, AgcMipFilter.Linear);
        sampler.SetLodRange(0f, 12f);                // max 12 -> 12*256 = 0xC00 in word1[12:23]
        sampler.SetBorderColor(AgcBorderColor.OpaqueWhite);

        Span<uint> words = stackalloc uint[AgcSamplerDescriptor.WordCount];
        sampler.WriteTo(words);

        Assert.Equal(2u | (2u << 3) | (4u << 9), words[0]);              // clamp x, clamp y, aniso 4
        Assert.Equal(0xC00u << 12, words[1]);                            // max lod 12.0 in 4.8 fixed point
        Assert.Equal((1u << 20) | (1u << 22) | (2u << 26), words[2]);    // mag, min, mip filters
        Assert.Equal(2u << 30, words[3]);                                // border color opaque white
    }

    [Fact]
    public void ViewportMapsClipSpaceOntoThePixelRectangle()
    {
        var viewport = new AgcViewport();
        viewport.SetViewport(0f, 0f, 1920f, 1080f);
        viewport.SetScissor(0, 0, 1920, 1080);

        Span<CxRegister> registers = stackalloc CxRegister[AgcViewport.RegisterCount];
        int count = viewport.WriteTo(registers);
        Assert.Equal(AgcViewport.RegisterCount, count);

        // The x scale is half the width and the y scale is negated (clip-space y points up).
        Assert.Equal((ushort)0x10F, registers[0].Offset);
        Assert.Equal(BitConverter.SingleToUInt32Bits(960f), registers[0].Value);
        Assert.Equal((ushort)0x111, registers[2].Offset);
        Assert.Equal(BitConverter.SingleToUInt32Bits(-540f), registers[2].Value);

        // The scissor top-left carries the window-offset-disable bit; the bottom-right packs the corner.
        Assert.Equal((ushort)0x090, registers[12].Offset);
        Assert.Equal(0x80000000u, registers[12].Value);
        Assert.Equal((ushort)0x091, registers[13].Offset);
        Assert.Equal(1920u | (1080u << 16), registers[13].Value);
    }

    [Fact]
    public void DescriptorsRejectAShortDestination()
    {
        var texture = new AgcTextureDescriptor();
        Assert.Throws<ArgumentException>(() => texture.WriteTo(new uint[4]));
        var sampler = new AgcSamplerDescriptor();
        Assert.Throws<ArgumentException>(() => sampler.WriteTo(new uint[2]));
    }
}

// The blend and depth-stencil control registers pack pre-shifted enum values (and a few masked numeric
// fields); these pin the packing against the register-struct definitions.
public sealed class GpuStateBlockTests
{
    [Fact]
    public void BlendControlEnablesAndSelectsFactors()
    {
        var blend = new CxBlendControl().Init([new CxRegister(0x1E0, 0)]);
        blend.SetBlend(CxBlendControl.Blend.kEnable)
             .SetColorSourceMultiplier(CxBlendControl.ColorSourceMultiplier.kSrcAlpha)
             .SetColorDestMultiplier(CxBlendControl.ColorDestMultiplier.kOneMinusSrcAlpha)
             .SetColorBlendFunc(CxBlendControl.ColorBlendFunc.kAdd);

        // enable (bit 30) | src alpha (0x4) | dest one-minus-src-alpha (0x500).
        Assert.Equal(0x40000000u | 0x4u | 0x500u, blend.Registers[0].Value);
        Assert.Equal(CxBlendControl.ColorSourceMultiplier.kSrcAlpha, blend.GetColorSourceMultiplier());

        blend.SetSlot(3);
        Assert.Equal((ushort)0x1E3, blend.Registers[0].Offset);
    }

    [Fact]
    public void BlendColorStoresFloats()
    {
        // The four constant-colour registers sit at consecutive offsets in red, green, blue, alpha order;
        // each component must land in its own register (a distinct value per channel catches a transposed
        // pair, which equal values would hide).
        var color = new CxBlendColor().Init([new CxRegister(0x105, 0), new CxRegister(0x106, 0), new CxRegister(0x107, 0), new CxRegister(0x108, 0)]);
        color.SetRed(0.1f).SetGreen(0.2f).SetBlue(0.3f).SetAlpha(0.4f);
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.1f), color.Registers[0].Value); // 0x105 red
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.2f), color.Registers[1].Value); // 0x106 green
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.3f), color.Registers[2].Value); // 0x107 blue
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.4f), color.Registers[3].Value); // 0x108 alpha
        Assert.Equal(0.1f, color.GetRed());
        Assert.Equal(0.2f, color.GetGreen());
        Assert.Equal(0.3f, color.GetBlue());
        Assert.Equal(0.4f, color.GetAlpha());
    }

    [Fact]
    public void DepthStencilControlEnablesTheTests()
    {
        var depth = new CxDepthStencilControl().Init([new CxRegister(0x200, 0)]);
        depth.SetDepth(CxDepthStencilControl.Depth.kEnable)
             .SetDepthWrite(CxDepthStencilControl.DepthWrite.kEnable)
             .SetStencil(CxDepthStencilControl.Stencil.kEnable);
        Assert.Equal(0x2u | 0x4u | 0x1u, depth.Registers[0].Value);
        Assert.Equal(CxDepthStencilControl.Depth.kEnable, depth.GetDepth());
    }

    [Fact]
    public void StencilControlPacksTheMaskedBytes()
    {
        var stencil = new CxStencilControl().Init([new CxRegister(0x210, 0)]);
        stencil.SetTestValue(0xAB).SetWriteMask(0xCD);
        Assert.Equal(0xABu | (0xCDu << 16), stencil.Registers[0].Value);
        Assert.Equal(0xCDu, stencil.GetWriteMask());
    }
}
