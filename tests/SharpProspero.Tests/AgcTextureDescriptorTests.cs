using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

// Pins the two descriptor fields that had no correct writer: the compression surface's address, which
// is split between the top byte of word six and the whole of word seven, and the length of the mip
// chain, which lives in word five and is what tells the processor how far the chain runs.
public sealed class AgcTextureDescriptorTests
{
    [Fact]
    public void SetMetadataAddress_SplitsTheAddressBetweenWordSixAndWordSeven()
    {
        var d = new AgcTextureDescriptor();
        d.SetMetadataAddress(0x0000_1234_5678_9A00UL);

        // The byte just above the 256-byte unit goes in the top byte of word six.
        Assert.Equal(0x9Au, d[6] >> 24);
        // Everything above that is word seven, unshifted.
        Assert.Equal(0x1234_5678u, d[7]);
        Assert.Equal(0x0000_1234_5678_9A00UL, d.MetadataAddress);
    }

    [Fact]
    public void SetMetadataAddress_RefusesAnAddressInsideA256ByteUnit()
    {
        var d = new AgcTextureDescriptor();
        Assert.Throws<ArgumentException>(() => d.SetMetadataAddress(0x1080));
    }

    [Fact]
    public void SetMetadataAddress_LeavesTheOtherBitsOfWordSixAlone()
    {
        var d = new AgcTextureDescriptor();
        d.SetMetadataEnabled(true);
        d.SetMetadataAddress(0x0000_0000_0001_0100UL);

        Assert.Equal(1u, (d[6] >> 21) & 1);   // the compression bit survives
        Assert.Equal(0x01u, d[6] >> 24);
        Assert.Equal(1u, d[7]);
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(8, 7u)]
    [InlineData(15, 14u)]
    public void SetMipLevelCount_WritesOneLessThanTheCountInWordFive(int levels, uint stored)
    {
        var d = new AgcTextureDescriptor();
        d.SetMipLevelCount(levels);
        Assert.Equal(stored, (d[5] >> 4) & 0xF);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void SetMipLevelCount_RefusesACountOutsideTheChain(int levels)
    {
        var d = new AgcTextureDescriptor();
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SetMipLevelCount(levels));
    }

    [Fact]
    public void SetFragmentCount_WritesTheSampleCountInBothPlaces()
    {
        var d = new AgcTextureDescriptor();
        d.SetFragmentCount(2);                       // four samples per texel
        Assert.Equal(2u, (d[3] >> 16) & 0xF);        // the last-level field
        Assert.Equal(2u, (d[5] >> 4) & 0xF);         // and the chain-length field
    }

    [Fact]
    public void SetMipLevelCount_DoesNotDisturbTheMipRange()
    {
        var d = new AgcTextureDescriptor();
        d.SetMipRange(2, 6);
        d.SetMipLevelCount(9);
        Assert.Equal(2u, (d[3] >> 12) & 0xF);
        Assert.Equal(6u, (d[3] >> 16) & 0xF);
        Assert.Equal(8u, (d[5] >> 4) & 0xF);
    }
}
