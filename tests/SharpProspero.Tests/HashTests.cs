// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Text;
using SharpProspero.Security;
using Xunit;

namespace SharpProspero.Tests;

// The digests are checked against published test vectors, including inputs longer than one block so the
// length-padding path is exercised. Incremental updates must match a one-shot hash of the same bytes.
public sealed class HashTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("The quick brown fox jumps over the lazy dog", "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592")]
    // 56 bytes: forces the length pad into a second block.
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")]
    public void Sha256_MatchesVectors(string input, string expected) =>
        Assert.Equal(expected, Sha256.HashHex(Ascii(input)));

    [Theory]
    [InlineData("", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    [InlineData("abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("The quick brown fox jumps over the lazy dog", "2fd4e1c67a2d28fced849ee1bb76e7391b93eb12")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "84983e441c3bd26ebaae4aa1f95129e5e54670f1")]
    public void Sha1_MatchesVectors(string input, string expected) =>
        Assert.Equal(expected, Sha1.HashHex(Ascii(input)));

    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("The quick brown fox jumps over the lazy dog", "9e107d9d372bb6826bd81d3542a419d6")]
    // 62 bytes: crosses into a second block during finalization.
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", "d174ab98d277d9f5a5611c2c9f419d9f")]
    public void Md5_MatchesVectors(string input, string expected) =>
        Assert.Equal(expected, Md5.HashHex(Ascii(input)));

    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0xCBF43926u)] // the standard CRC-32 check value
    [InlineData("abc", 0x352441C2u)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
    public void Crc32_MatchesVectors(string input, uint expected) =>
        Assert.Equal(expected, Crc32.Compute(Ascii(input)));

    [Fact]
    public void Sha256_IncrementalMatchesOneShot()
    {
        byte[] data = Ascii("The quick brown fox jumps over the lazy dog");
        var streaming = new Sha256();
        streaming.Update(data.AsSpan(0, 10));
        streaming.Update(data.AsSpan(10, 20));
        streaming.Update(data.AsSpan(30));
        Assert.Equal(Sha256.HashHex(data), Convert.ToHexStringLower(streaming.Finish()));
    }

    [Fact]
    public void Crc32_IncrementalMatchesOneShot()
    {
        byte[] data = Ascii("streamed in pieces across several updates");
        var crc = new Crc32();
        crc.Update(data.AsSpan(0, 5));
        crc.Update(data.AsSpan(5));
        Assert.Equal(Crc32.Compute(data), crc.Value);
    }

    [Fact]
    public void HashSizes_AreCorrect()
    {
        Assert.Equal(32, new Sha256().HashSize);
        Assert.Equal(20, new Sha1().HashSize);
        Assert.Equal(16, new Md5().HashSize);
        Assert.Equal(4, new Crc32().HashSize);
    }
}
