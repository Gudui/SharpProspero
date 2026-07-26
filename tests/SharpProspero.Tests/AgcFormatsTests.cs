// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

// The format and shader-stage enumerations are generated from the SDK's own declarations. These spot
// checks pin a few well-known values so a regeneration that shifted them would be caught.
public sealed class AgcFormatsTests
{
    [Fact]
    public void ChannelTypeValuesMatchTheDeclarations()
    {
        Assert.Equal(0x00u, (uint)AgcFormats.ChannelType.kUNorm);
        Assert.Equal(0x06u, (uint)AgcFormats.ChannelType.kSrgb);
        Assert.Equal(0x07u, (uint)AgcFormats.ChannelType.kFloat);
    }

    [Fact]
    public void ChannelLayoutHasTheExpectedWidths()
    {
        Assert.Equal(0x0au, (uint)AgcFormats.ChannelLayout.k8_8_8_8);
        Assert.Equal(0x04u, (uint)AgcFormats.ChannelLayout.k32);
    }

    [Fact]
    public void TypedFormatIsAChannelLayoutCombinedWithAType()
    {
        // The eight-bit-per-channel sRGB surface format the display path uses.
        Assert.Equal(130u, (uint)AgcFormats.TypedFormat.k8_8_8_8Srgb);
        Assert.Equal(0u, (uint)AgcFormats.TypedFormat.kInvalid);
    }

    [Fact]
    public void ShaderKindsAreNumberedAsDeclared()
    {
        Assert.Equal(0, (int)ShaderKind.Compute);
        Assert.Equal(1, (int)ShaderKind.Pixel);
        Assert.Equal(2, (int)ShaderKind.Geometry);
        Assert.Equal(8, (int)ShaderKind.Function);
    }
}
