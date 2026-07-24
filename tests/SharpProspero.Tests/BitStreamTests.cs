// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Buffers;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class BitStreamTests
{
    [Fact]
    public void WriteThenRead_RoundTripsMixedFields()
    {
        var writer = new BitWriter();
        writer.WriteBits(0b101, 3);
        writer.WriteBit(true);
        writer.WriteBits(0xABCD, 16);
        writer.WriteBits(0, 5);
        writer.WriteBits(0xFFFFFFFF, 32);

        var reader = new BitReader(writer.ToArray());
        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.True(reader.ReadBit());
        Assert.Equal(0xABCDu, reader.ReadBits(16));
        Assert.Equal(0u, reader.ReadBits(5));
        Assert.Equal(0xFFFFFFFFu, reader.ReadBits(32));
    }

    [Fact]
    public void BitsAreMostSignificantFirst()
    {
        var writer = new BitWriter();
        writer.WriteBits(0b101, 3); // partial byte padded on the right -> 1010_0000
        byte[] bytes = writer.ToArray();
        Assert.Single(bytes);
        Assert.Equal(0b1010_0000, bytes[0]);
    }

    [Fact]
    public void BitLength_TracksWrittenBits()
    {
        var writer = new BitWriter();
        writer.WriteBits(0, 10);
        Assert.Equal(10, writer.BitLength);
        writer.AlignToByte();
        Assert.Equal(16, writer.BitLength);
        Assert.Equal(2, writer.ToArray().Length);
    }

    [Fact]
    public void AlignToByte_SkipsToTheNextBoundaryOnRead()
    {
        var writer = new BitWriter();
        writer.WriteBits(0b1, 1);
        writer.AlignToByte();
        writer.WriteBits(0x7E, 8);

        var reader = new BitReader(writer.ToArray());
        Assert.True(reader.ReadBit());
        reader.AlignToByte();
        Assert.Equal(8, reader.BitPosition);
        Assert.Equal(0x7Eu, reader.ReadBits(8));
    }

    [Fact]
    public void TryReadBits_ReturnsFalseAtTheEndInsteadOfThrowing()
    {
        var reader = new BitReader([0xFF]);
        Assert.True(reader.TryReadBits(4, out uint first));
        Assert.Equal(0xFu, first);
        Assert.False(reader.TryReadBits(8, out uint _));
        Assert.Equal(4, reader.BitsRemaining);
    }

    [Fact]
    public void ReadPastTheEnd_Throws()
    {
        var reader = new BitReader([0x00]);
        reader.ReadBits(8);
        Assert.Throws<InvalidOperationException>(() => reader.ReadBit());
    }

    [Fact]
    public void CountOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitWriter().WriteBits(0, 33));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader([0]).ReadBits(33));
    }
}
