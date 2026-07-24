// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Compression;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class InflateTests
{
    private static byte[] DeflateRaw(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] ZlibWrap(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] GzipWrap(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public static TheoryData<byte[]> Samples()
    {
        var data = new TheoryData<byte[]>();
        data.Add(Encoding.UTF8.GetBytes("Hello, world!"));               // tiny
        data.Add(Encoding.UTF8.GetBytes(new string('A', 5000)));         // long run -> back references
        var mixed = new byte[20000];                                     // repeating text -> dynamic Huffman
        byte[] phrase = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog. ");
        for (int i = 0; i < mixed.Length; i++) mixed[i] = phrase[i % phrase.Length];
        data.Add(mixed);
        var pseudoRandom = new byte[8000];                               // incompressible -> stored blocks
        uint state = 0x12345678;
        for (int i = 0; i < pseudoRandom.Length; i++) { state = state * 1664525 + 1013904223; pseudoRandom[i] = (byte)(state >> 24); }
        data.Add(pseudoRandom);
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Raw_RoundTripsAgainstTheDeflateEncoder(byte[] original)
        => Assert.Equal(original, Inflate.Raw(DeflateRaw(original), original.Length));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Zlib_RoundTripsAndVerifiesAdler32(byte[] original)
        => Assert.Equal(original, Inflate.Zlib(ZlibWrap(original)));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Gzip_RoundTripsAndVerifiesTheTrailer(byte[] original)
        => Assert.Equal(original, Inflate.Gzip(GzipWrap(original)));

    [Fact]
    public void Zlib_RejectsATamperedChecksum()
    {
        byte[] stream = ZlibWrap(Encoding.UTF8.GetBytes("data with a checksum"));
        stream[^1] ^= 0xFF; // corrupt the Adler-32 trailer
        Assert.Throws<CompressionException>(() => Inflate.Zlib(stream));
    }

    [Fact]
    public void Gzip_RejectsANonGzipHeader()
        => Assert.Throws<CompressionException>(() => Inflate.Gzip([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17]));

    [Fact]
    public void Raw_ThrowsOnATruncatedStream()
    {
        byte[] full = DeflateRaw(Encoding.UTF8.GetBytes(new string('Z', 4000)));
        Assert.Throws<CompressionException>(() => Inflate.Raw(full.AsSpan(0, full.Length / 2).ToArray()));
    }

    [Fact]
    public void Adler32_MatchesTheKnownValueForAbc()
        => Assert.Equal(0x024D0127u, Inflate.Adler32("abc"u8));

    [Fact]
    public void Raw_ReturnsEmptyForEmptyInput()
        => Assert.Empty(Inflate.Raw([]));

    [Fact]
    public void Gzip_HandlesARealEmptyMember()
    {
        // What `gzip` produces for empty input: header, an empty DEFLATE block, then a zero CRC and length.
        byte[] emptyGzip =
        [
            0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, // header
            0x03, 0x00,                                                 // empty final block
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // crc32 = 0, isize = 0
        ];
        Assert.Empty(Inflate.Gzip(emptyGzip));
    }

    [Fact]
    public void Zlib_HandlesARealEmptyStream()
    {
        // What zlib produces for empty input: header, an empty DEFLATE block, then Adler-32 of 1.
        byte[] emptyZlib = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        Assert.Empty(Inflate.Zlib(emptyZlib));
    }
}
