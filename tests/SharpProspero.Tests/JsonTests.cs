// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using Xunit;

namespace SharpProspero.Tests;

public sealed class JsonTests
{
    [Theory]
    [InlineData("null", JsonType.Null)]
    [InlineData("true", JsonType.Boolean)]
    [InlineData("false", JsonType.Boolean)]
    [InlineData("42", JsonType.Number)]
    [InlineData("\"text\"", JsonType.String)]
    [InlineData("[]", JsonType.Array)]
    [InlineData("{}", JsonType.Object)]
    public void Parse_ReadsEachKind(string text, JsonType expected) =>
        Assert.Equal(expected, JsonValue.Parse(text).Type);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-42", -42)]
    [InlineData("1000", 1000)]
    [InlineData("1e3", 1000)]
    [InlineData("2.5", 2)] // truncated by AsInt
    public void Parse_ReadsNumbers(string text, int expected) =>
        Assert.Equal(expected, JsonValue.Parse(text).AsInt());

    [Fact]
    public void Parse_ReadsAnObjectAndArray()
    {
        JsonValue value = JsonValue.Parse("{\"name\":\"Ada\",\"age\":36,\"tags\":[\"a\",\"b\"]}");
        Assert.Equal("Ada", value.GetString("name"));
        Assert.Equal(36, value.GetInt("age"));
        Assert.Equal(2, value["tags"].Count);
        Assert.Equal("b", value["tags"][1].AsString());
    }

    [Fact]
    public void MissingValues_ReturnTheFallback()
    {
        JsonValue value = JsonValue.Parse("{\"a\":1}");
        Assert.True(value["missing"].IsNull);
        Assert.Equal(99, value.GetInt("missing", 99));
        Assert.Equal("x", value.GetString("a", "x")); // present but not a string
        Assert.True(value[3].IsNull);                  // an index into an object is nothing
    }

    [Fact]
    public void CompactWrite_RoundTripsExactly()
    {
        const string text = "{\"a\":1,\"b\":[true,null,\"x\"],\"c\":2.5}";
        Assert.Equal(text, JsonValue.Parse(text).Write(indented: false));
    }

    [Fact]
    public void Objects_KeepKeyOrderThroughAWrite()
    {
        JsonValue value = JsonValue.Parse("{\"z\":1,\"a\":2,\"m\":3}");
        Assert.Equal(new[] { "z", "a", "m" }, value.Keys);
        Assert.Equal("{\"z\":1,\"a\":2,\"m\":3}", value.Write());

        // Replacing an existing key keeps its place.
        value["a"] = 5;
        Assert.Equal(new[] { "z", "a", "m" }, value.Keys);
        Assert.Equal(5, value.GetInt("a"));
    }

    [Fact]
    public void Strings_EscapeAndUnescape()
    {
        Assert.Equal("line1\nline2\t\"q\"", JsonValue.Parse("\"line1\\nline2\\t\\\"q\\\"\"").AsString());
        Assert.Equal("é", JsonValue.Parse("\"\\u00e9\"").AsString());
        // Building a string re-escapes the control characters.
        Assert.Equal("\"a\\nb\"", JsonValue.Of("a\nb").Write());
    }

    [Fact]
    public void Build_ComposesAndWrites()
    {
        var reply = JsonValue.NewObject();
        reply["ok"] = true;
        reply["count"] = 3;
        reply["items"] = JsonValue.NewArray().Add("a").Add("b");

        Assert.Equal("{\"ok\":true,\"count\":3,\"items\":[\"a\",\"b\"]}", reply.Write());

        // Indented output lays out over several lines and parses back to the same shape.
        string indented = reply.Write(indented: true);
        Assert.Contains("\n", indented);
        Assert.Equal(reply.Write(), JsonValue.Parse(indented).Write());
    }

    [Fact]
    public void WhitespaceBetweenTokensIsIgnored() =>
        Assert.Equal(1, JsonValue.Parse("  {\n  \"a\" :\t1\r\n}  ").GetInt("a"));

    [Fact]
    public void Parse_IgnoresALeadingByteOrderMark() =>
        Assert.Equal(1, JsonValue.Parse("﻿{\"a\":1}").GetInt("a")); // a file saved with a BOM still parses

    [Theory]
    [InlineData("{\"a\":1}trailing")]
    [InlineData("\"unterminated")]
    [InlineData("nul")]
    [InlineData("{\"a\" 1}")]
    [InlineData("[1,]")]
    public void Parse_ThrowsOnBadInput(string text) =>
        Assert.Throws<JsonException>(() => JsonValue.Parse(text));

    [Fact]
    public void TryParse_ReportsFailureWithoutThrowing()
    {
        Assert.False(JsonValue.TryParse("{bad}", out JsonValue value));
        Assert.True(value.IsNull);
        Assert.True(JsonValue.TryParse("{\"a\":1}", out _));
    }

    [Fact]
    public void Parse_RejectsRunawayNesting() =>
        Assert.Throws<JsonException>(() => JsonValue.Parse(new string('[', 300)));

    [Fact]
    public void SettingOnANonObjectThrows() =>
        Assert.Throws<System.InvalidOperationException>(() => JsonValue.NewArray()["key"] = 1);
}
