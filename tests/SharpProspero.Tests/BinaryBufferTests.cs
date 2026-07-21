// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Buffers;
using System.IO;
using Xunit;

namespace SharpProspero.Tests;

public sealed class BinaryBufferTests
{
    [Fact]
    public void WriteThenRead_RoundTripsEveryKind()
    {
        var writer = new ByteWriter();
        writer.WriteByte(0xAB);
        writer.WriteSByte(-3);
        writer.WriteInt16LittleEndian(-2);
        writer.WriteUInt32BigEndian(0x01020304u);
        writer.WriteInt64LittleEndian(-123456789012345L);
        writer.WriteUInt64LittleEndian(0xDEADBEEFCAFEBABEUL);
        writer.WriteSingleLittleEndian(1.5f);
        writer.WriteDoubleBigEndian(-2.75d);
        int textBytes = writer.WriteUtf8("héllo");
        writer.WriteBytes([1, 2, 3]);

        var reader = new SpanReader(writer.ToArray());
        Assert.Equal((byte)0xAB, reader.ReadByte());
        Assert.Equal((sbyte)-3, reader.ReadSByte());
        Assert.Equal((short)-2, reader.ReadInt16LittleEndian());
        Assert.Equal(0x01020304u, reader.ReadUInt32BigEndian());
        Assert.Equal(-123456789012345L, reader.ReadInt64LittleEndian());
        Assert.Equal(0xDEADBEEFCAFEBABEUL, reader.ReadUInt64LittleEndian());
        Assert.Equal(1.5f, reader.ReadSingleLittleEndian());
        Assert.Equal(-2.75d, reader.ReadDoubleBigEndian());
        Assert.Equal("héllo", reader.ReadUtf8(textBytes));
        Assert.Equal([1, 2, 3], reader.ReadBytes(3).ToArray());
        Assert.True(reader.End);
    }

    [Fact]
    public void ByteOrder_IsAsRequested()
    {
        var little = new ByteWriter();
        little.WriteInt32LittleEndian(0x01020304);
        Assert.Equal([0x04, 0x03, 0x02, 0x01], little.ToArray());

        var big = new ByteWriter();
        big.WriteInt32BigEndian(0x01020304);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], big.ToArray());
    }

    [Fact]
    public void Reader_TracksPositionAndSkips()
    {
        var reader = new SpanReader([1, 2, 3, 4]);
        Assert.Equal(4, reader.Length);
        Assert.Equal(4, reader.Remaining);

        reader.ReadByte();
        Assert.Equal(1, reader.Position);
        Assert.Equal(3, reader.Remaining);

        reader.Skip(3);
        Assert.True(reader.End);
    }

    [Fact]
    public void Reader_ThrowsPastTheEnd() =>
        Assert.Throws<EndOfStreamException>(() =>
        {
            var reader = new SpanReader(new byte[2]);
            reader.ReadInt32LittleEndian();
        });

    [Fact]
    public void Writer_GrowsBeyondItsInitialCapacity()
    {
        var writer = new ByteWriter(capacity: 4);
        for (int i = 0; i < 100; i++)
            writer.WriteInt32LittleEndian(i);
        Assert.Equal(400, writer.Count);

        var reader = new SpanReader(writer.WrittenSpan);
        for (int i = 0; i < 100; i++)
            Assert.Equal(i, reader.ReadInt32LittleEndian());
    }

    [Fact]
    public void Writer_ClearReusesTheBuffer()
    {
        var writer = new ByteWriter();
        writer.WriteInt32LittleEndian(1);
        writer.Clear();
        Assert.Equal(0, writer.Count);
        writer.WriteByte(9);
        Assert.Equal([9], writer.ToArray());
    }
}
