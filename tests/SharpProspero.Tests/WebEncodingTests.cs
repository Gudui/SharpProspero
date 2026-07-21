// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class WebEncodingTests
{
    [Fact]
    public void PercentEncode_KeepsUnreservedAndEscapesTheRest()
    {
        Assert.Equal("aA0-._~", WebEncoding.PercentEncode("aA0-._~"));
        Assert.Equal("a%20b%26c", WebEncoding.PercentEncode("a b&c"));
        Assert.Equal("a+b%26c", WebEncoding.PercentEncode("a b&c", spaceAsPlus: true));
        Assert.Equal("%C3%A9", WebEncoding.PercentEncode("é")); // UTF-8 of e-acute
    }

    [Fact]
    public void PercentDecode_ReversesEncodingAndHandlesPlus()
    {
        Assert.Equal("a b&c", WebEncoding.PercentDecode("a%20b%26c"));
        Assert.Equal("a b", WebEncoding.PercentDecode("a+b", plusAsSpace: true));
        Assert.Equal("a+b", WebEncoding.PercentDecode("a+b")); // plus left alone by default
        Assert.Equal("é", WebEncoding.PercentDecode("%C3%A9"));
    }

    [Fact]
    public void PercentDecode_RejectsMalformedEscapes()
    {
        Assert.Throws<FormatException>(() => WebEncoding.PercentDecode("%2"));
        Assert.Throws<FormatException>(() => WebEncoding.PercentDecode("%GG"));
    }

    [Fact]
    public void BuildQuery_EncodesNamesAndValues()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("q", "hello world"),
            new("x", "a&b"),
        };
        Assert.Equal("q=hello+world&x=a%26b", WebEncoding.BuildQuery(pairs));
    }

    [Fact]
    public void ParseQuery_DecodesPairsAndSkipsAnyUrlPrefix()
    {
        List<KeyValuePair<string, string>> pairs = WebEncoding.ParseQuery("http://host/path?q=hello+world&x=a%26b");
        Assert.Equal(2, pairs.Count);
        Assert.Equal(new KeyValuePair<string, string>("q", "hello world"), pairs[0]);
        Assert.Equal(new KeyValuePair<string, string>("x", "a&b"), pairs[1]);
    }

    [Fact]
    public void ParseQuery_TreatsANameWithoutValueAsEmpty()
    {
        List<KeyValuePair<string, string>> pairs = WebEncoding.ParseQuery("flag&k=v");
        Assert.Equal("flag", pairs[0].Key);
        Assert.Equal(string.Empty, pairs[0].Value);
        Assert.Equal("v", pairs[1].Value);
    }

    [Fact]
    public void PercentDecode_PreservesNonBmpCharacters()
    {
        // A non-BMP character is a UTF-16 surrogate pair; decoding must keep it whole, not split it.
        Assert.Equal("a\U0001F600b", WebEncoding.PercentDecode(WebEncoding.PercentEncode("a\U0001F600b")));
        Assert.Equal("\U0001F600", WebEncoding.PercentDecode("\U0001F600")); // literal, unescaped
    }

    [Fact]
    public void BuildQuery_ThenParseQuery_RoundTrips()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("name", "a b+c"),
            new("path", "/x/y?z"),
        };
        List<KeyValuePair<string, string>> parsed = WebEncoding.ParseQuery(WebEncoding.BuildQuery(pairs));
        Assert.Equal(pairs, parsed);
    }
}
