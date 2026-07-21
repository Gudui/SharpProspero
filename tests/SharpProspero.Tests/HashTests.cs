// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Security;
using System;
using System.Text;
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
        Assert.Equal(64, new Sha512().HashSize);
    }

    [Theory]
    [InlineData("", "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e")]
    [InlineData("abc", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    [InlineData("The quick brown fox jumps over the lazy dog", "07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb642e93a252a954f23912547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6")]
    // 112 bytes: exactly fills a block, forcing the length pad into a second block.
    [InlineData("abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu", "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909")]
    public void Sha512_MatchesVectors(string input, string expected) =>
        Assert.Equal(expected, Sha512.HashHex(Ascii(input)));

    [Fact]
    public void Sha512_IncrementalMatchesOneShot()
    {
        // Longer than one 128-byte block, split into thirds so a whole block is transformed mid-stream.
        byte[] data = Ascii("The SHA-512 streaming path must match hashing the whole message at once, even when the updates land at awkward offsets that cross the 128-byte block boundary more than once.");
        int a = data.Length / 3, b = 2 * data.Length / 3;
        var streaming = new Sha512();
        streaming.Update(data.AsSpan(0, a));
        streaming.Update(data.AsSpan(a, b - a));
        streaming.Update(data.AsSpan(b));
        Assert.Equal(Sha512.HashHex(data), Convert.ToHexStringLower(streaming.Finish()));
    }

    // Keyed digests checked against the published HMAC test vectors, with one case whose key is longer
    // than the block size so the key-is-hashed-first path is exercised.
    [Fact]
    public void Hmac_MatchesVectors()
    {
        byte[] jefe = Ascii("Jefe");
        byte[] message = Ascii("what do ya want for nothing?");
        Assert.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", Hmac.Sha256Hex(jefe, message));
        Assert.Equal("164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737", Hmac.Sha512Hex(jefe, message));
        Assert.Equal("effcdf6ae5eb2fa2d27416d5f184df9c259a7c79", Hmac.Sha1Hex(jefe, message));
        Assert.Equal("750c783e6ab0b503eaa86e310a5db738", Hmac.Md5Hex(jefe, message));

        // A 131-byte key (longer than the 64-byte block) is reduced by hashing before use.
        byte[] longKey = new byte[131];
        Array.Fill(longKey, (byte)0xAA);
        byte[] tcData = Ascii("Test Using Larger Than Block-Size Key - Hash Key First");
        Assert.Equal("60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54", Hmac.Sha256Hex(longKey, tcData));
    }

    [Fact]
    public void Hmac_IncrementalMatchesOneShot()
    {
        byte[] key = Ascii("a shared secret");
        byte[] data = Ascii("a message tagged in one shot or in several pieces");
        var streaming = new Hmac(key, static () => new Sha256(), 64);
        streaming.Update(data.AsSpan(0, 12));
        streaming.Update(data.AsSpan(12));
        Assert.Equal(Hmac.Sha256Hex(key, data), Convert.ToHexStringLower(streaming.Finish()));
    }
}
