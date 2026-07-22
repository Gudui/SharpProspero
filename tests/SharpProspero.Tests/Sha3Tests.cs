// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Security;
using System;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class Sha3Tests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Theory]
    // FIPS 202 known-answer vectors for the empty message and "abc".
    [InlineData(Sha3Variant.Bits256, "", "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a")]
    [InlineData(Sha3Variant.Bits256, "abc", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532")]
    [InlineData(Sha3Variant.Bits384, "", "0c63a75b845e4f7d01107d852e4c2485c51a50aaaa94fc61995e71bbee983a2ac3713831264adb47fb6bd1e058d5f004")]
    [InlineData(Sha3Variant.Bits384, "abc", "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25")]
    [InlineData(Sha3Variant.Bits512, "", "a69f73cca23a9ac5c8b567dc185a756e97c982164fe25859e0d1dcc1475c80a615b2123af1f5f94c11e3e9402c3ac558f500199d95b6d3e301758586281dcd26")]
    [InlineData(Sha3Variant.Bits512, "abc", "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0")]
    public void Hash_MatchesFips202Vectors(Sha3Variant variant, string message, string expected)
        => Assert.Equal(expected, Sha3.HashHex(Ascii(message), variant));

    [Fact]
    public void HashSize_MatchesTheVariant()
    {
        Assert.Equal(32, Sha3.Hash([], Sha3Variant.Bits256).Length);
        Assert.Equal(48, Sha3.Hash([], Sha3Variant.Bits384).Length);
        Assert.Equal(64, Sha3.Hash([], Sha3Variant.Bits512).Length);
    }

    [Fact]
    public void Streaming_InAnyChunkSizes_MatchesOneShot()
    {
        byte[] data = new byte[500]; // spans several 136-byte rate blocks
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 31) + 7);

        var streamed = new Sha3(Sha3Variant.Bits256);
        streamed.Update(data.AsSpan(0, 137)); // crosses a rate boundary
        streamed.Update(data.AsSpan(137, 200));
        streamed.Update(data.AsSpan(337));

        Assert.Equal(Sha3.HashHex(data), Convert.ToHexStringLower(streamed.Finish()));
    }

    [Fact]
    public void DefaultVariant_IsSha3_256()
        => Assert.Equal(Sha3.HashHex(Ascii("abc"), Sha3Variant.Bits256), Sha3.HashHex(Ascii("abc")));
}
