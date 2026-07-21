// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Buffers;
using System;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class BaseNTests
{
    [Fact]
    public void Hex_RoundTripsAndHonoursCase()
    {
        byte[] data = [0x00, 0x1F, 0xA0, 0xFF];
        Assert.Equal("001fa0ff", BaseN.ToHex(data));
        Assert.Equal("001FA0FF", BaseN.ToHex(data, upperCase: true));
        Assert.Equal(data, BaseN.FromHex("00 1f a0 ff")); // whitespace ignored
    }

    [Fact]
    public void Hex_RejectsBadInput()
    {
        Assert.Throws<FormatException>(() => BaseN.FromHex("abc"));  // odd length
        Assert.Throws<FormatException>(() => BaseN.FromHex("zz"));   // non-hex
    }

    [Theory]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void Base64_MatchesTheKnownVectors(string text, string encoded)
    {
        byte[] data = Encoding.ASCII.GetBytes(text);
        Assert.Equal(encoded, BaseN.ToBase64(data));
        Assert.Equal(data, BaseN.FromBase64(encoded));
    }

    [Fact]
    public void Base64_UrlSafeAndUnpaddedRoundTrip()
    {
        byte[] data = [0xFB, 0xFF, 0xBF]; // encodes to characters that differ between the alphabets
        string urlSafe = BaseN.ToBase64(data, urlSafe: true, padding: false);
        Assert.DoesNotContain('+', urlSafe);
        Assert.DoesNotContain('/', urlSafe);
        Assert.DoesNotContain('=', urlSafe);
        Assert.Equal(data, BaseN.FromBase64(urlSafe));                 // decode accepts the url-safe alphabet
        Assert.Equal(data, BaseN.FromBase64(BaseN.ToBase64(data)));    // and the standard one
    }

    [Theory]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Base32_MatchesTheKnownVectors(string text, string encoded)
    {
        byte[] data = Encoding.ASCII.GetBytes(text);
        Assert.Equal(encoded, BaseN.ToBase32(data));
        Assert.Equal(data, BaseN.FromBase32(encoded));
        Assert.Equal(data, BaseN.FromBase32(encoded.ToLowerInvariant())); // case-insensitive decode
    }

    [Fact]
    public void Codecs_RoundTripArbitraryBytes()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 7 + 3);

        Assert.Equal(data, BaseN.FromHex(BaseN.ToHex(data)));
        Assert.Equal(data, BaseN.FromBase64(BaseN.ToBase64(data)));
        Assert.Equal(data, BaseN.FromBase32(BaseN.ToBase32(data)));
    }
}
