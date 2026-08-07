// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Compression;
using SharpProspero.Security;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// An archive's directory is the archive's word about itself, not a fact. Every figure taken from it
// decides an allocation or an offset, so each one has to be checked against what the bytes present can
// actually support before it is used.
public sealed class ZipRobustnessTests
{
    [Fact]
    public void Extract_DoesNotSizeTheBufferFromADeclaredSizeTheInputCannotSupport()
    {
        // A directory claiming two gigabytes from a few compressed bytes. Sizing the output buffer from
        // that figure asks for the whole amount before a byte is decoded; the entry is rejected either
        // way, so what this pins is that the rejection happens without the allocation.
        byte[] archive = OneEntry("big.bin", "hello"u8.ToArray(), declaredUncompressed: 0x7FFF_0000);

        var zip = ZipArchive.Open(archive);
        Assert.Throws<CompressionException>(() => zip.Extract("big.bin"));
    }

    [Fact]
    public void Extract_StillReturnsAnHonestEntry()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 4096));
        byte[] archive = OneEntry("real.txt", payload, declaredUncompressed: null);

        var zip = ZipArchive.Open(archive);
        Assert.Equal(payload, zip.Extract("real.txt"));
    }

    [Fact]
    public void Open_RejectsADirectoryWhoseExtentWrapsThirtyTwoBits()
    {
        // Offset and size are both 32-bit; added in 32-bit arithmetic their sum wraps to a small
        // number and passes a bounds check it should fail.
        byte[] archive = OneEntry("a.txt", "a"u8.ToArray(), declaredUncompressed: null);
        int eocd = archive.Length - 22;
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(eocd + 12), 0x40);        // size
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(eocd + 16), 0xFFFF_FFF0); // offset

        Assert.Throws<CompressionException>(() => ZipArchive.Open(archive));
    }

    [Fact]
    public void Build_RefusesMoreEntriesThanTheDirectoryCanCount()
    {
        var builder = new ZipBuilder();
        for (int i = 0; i <= ushort.MaxValue; i++)
            builder.Add($"f{i}", default, compress: false);

        CompressionException ex = Assert.Throws<CompressionException>(() => builder.ToArray());
        Assert.Contains("sixteen bits", ex.Message);
    }

    // A one-entry stored archive, optionally lying about the uncompressed size in both headers.
    private static byte[] OneEntry(string name, byte[] payload, long? declaredUncompressed)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        uint crc = Crc32.Compute(payload);
        uint declared = (uint)(declaredUncompressed ?? payload.Length);
        var b = new List<byte>();

        void U16(int v) => b.AddRange([(byte)v, (byte)(v >> 8)]);
        void U32(long v) => b.AddRange([(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)]);

        int localOffset = 0;
        U32(0x04034B50); U16(20); U16(0); U16(0); U16(0); U16(0);
        U32(crc); U32(payload.Length); U32(declared);
        U16(nameBytes.Length); U16(0);
        b.AddRange(nameBytes);
        b.AddRange(payload);

        int directoryOffset = b.Count;
        U32(0x02014B50); U16(20); U16(20); U16(0); U16(0); U16(0); U16(0);
        U32(crc); U32(payload.Length); U32(declared);
        U16(nameBytes.Length); U16(0); U16(0); U16(0); U16(0); U32(0); U32(localOffset);
        b.AddRange(nameBytes);
        int directorySize = b.Count - directoryOffset;

        U32(0x06054B50); U16(0); U16(0); U16(1); U16(1);
        U32(directorySize); U32(directoryOffset); U16(0);
        return [.. b];
    }
}
