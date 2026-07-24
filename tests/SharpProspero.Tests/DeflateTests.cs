// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Compression;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class DeflateTests
{
    private static byte[] BclDecompress(byte[] data, Func<Stream, Stream> wrap)
    {
        using var input = new MemoryStream(data);
        using Stream decompress = wrap(input);
        using var output = new MemoryStream();
        decompress.CopyTo(output);
        return output.ToArray();
    }

    public static TheoryData<byte[]> Samples()
    {
        var data = new TheoryData<byte[]>();
        data.Add([]);
        data.Add(Encoding.UTF8.GetBytes("Hello, world!"));
        data.Add(Encoding.UTF8.GetBytes(new string('A', 5000)));
        byte[] phrase = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog. ");
        var mixed = new byte[20000];
        for (int i = 0; i < mixed.Length; i++) mixed[i] = phrase[i % phrase.Length];
        data.Add(mixed);
        var pseudoRandom = new byte[8000];
        uint state = 0x2468ACE0;
        for (int i = 0; i < pseudoRandom.Length; i++) { state = state * 1664525 + 1013904223; pseudoRandom[i] = (byte)(state >> 24); }
        data.Add(pseudoRandom);
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Raw_IsReadByOurOwnInflater(byte[] original)
        => Assert.Equal(original, Inflate.Raw(Deflate.Raw(original), original.Length));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Raw_IsReadByTheFrameworkDecoder(byte[] original)
        => Assert.Equal(original, BclDecompress(Deflate.Raw(original), s => new DeflateStream(s, CompressionMode.Decompress)));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Zlib_RoundTripsBothWays(byte[] original)
    {
        byte[] compressed = Deflate.Zlib(original);
        Assert.Equal(original, Inflate.Zlib(compressed));
        Assert.Equal(original, BclDecompress(compressed, s => new ZLibStream(s, CompressionMode.Decompress)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Gzip_RoundTripsBothWays(byte[] original)
    {
        byte[] compressed = Deflate.Gzip(original);
        Assert.Equal(original, Inflate.Gzip(compressed));
        Assert.Equal(original, BclDecompress(compressed, s => new GZipStream(s, CompressionMode.Decompress)));
    }

    [Fact]
    public void Raw_CompressesRepetitiveData()
    {
        byte[] original = Encoding.UTF8.GetBytes(new string('Z', 10000));
        byte[] compressed = Deflate.Raw(original);
        Assert.True(compressed.Length < original.Length / 10, $"a long run compresses well ({compressed.Length} bytes)");
    }
}
