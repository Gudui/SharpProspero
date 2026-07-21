// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class StringTableTests
{
    [Fact]
    public void Get_ReturnsOwnThenFallbackThenTheKey()
    {
        var en = new StringTable("en").Set("hi", "Hello").Set("bye", "Goodbye");
        var fr = new StringTable("fr", fallback: en).Set("hi", "Bonjour");

        Assert.Equal("Bonjour", fr.Get("hi"));       // own entry wins
        Assert.Equal("Goodbye", fr.Get("bye"));      // falls back to en
        Assert.Equal("missing", fr.Get("missing"));  // key returned when nothing has it
    }

    [Fact]
    public void Contains_FollowsTheFallbackChain()
    {
        var en = new StringTable("en").Set("only", "x");
        var fr = new StringTable("fr", fallback: en);
        Assert.True(fr.Contains("only"));
        Assert.False(fr.Contains("nope"));
    }

    [Fact]
    public void TryGet_ReportsWhetherItWasFound()
    {
        var table = new StringTable("en").Set("a", "1");
        Assert.True(table.TryGet("a", out string found));
        Assert.Equal("1", found);
        Assert.False(table.TryGet("b", out string missing));
        Assert.Equal("b", missing); // the key, so it is visible
    }

    [Fact]
    public void Format_FillsPositionalArguments()
    {
        var table = new StringTable("en").Set("greet", "Hi, {0} and {1}");
        Assert.Equal("Hi, A and B", table.Format("greet", "A", "B"));
    }

    [Fact]
    public void Format_WithNoArgumentsLeavesBracesAlone()
    {
        var table = new StringTable("en").Set("raw", "keep {these} braces");
        Assert.Equal("keep {these} braces", table.Format("raw"));
    }

    [Fact]
    public void Add_LoadsManyEntries()
    {
        var table = new StringTable("en").Add(new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" });
        Assert.Equal(2, table.Count);
        Assert.Equal("1", table.Get("x"));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyLocale()
        => Assert.Throws<ArgumentException>(() => new StringTable(""));
}
