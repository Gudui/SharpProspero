// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Text;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void IsMatch_RequiresAnOrderedSubsequence()
    {
        Assert.True(FuzzyMatcher.IsMatch("abc", "aXbXc"));
        Assert.True(FuzzyMatcher.IsMatch("CFG", "config.ini")); // case-insensitive
        Assert.False(FuzzyMatcher.IsMatch("abc", "acb"));        // out of order
        Assert.False(FuzzyMatcher.IsMatch("abcd", "abc"));       // too long
    }

    [Fact]
    public void TryMatch_ReturnsTheMatchedIndices()
    {
        Assert.True(FuzzyMatcher.TryMatch("ac", "abc", out FuzzyMatch match));
        Assert.Equal([0, 2], match.MatchedIndices);
    }

    [Fact]
    public void EmptyPattern_MatchesWithZeroScore()
    {
        Assert.True(FuzzyMatcher.TryMatch("", "anything", out FuzzyMatch match));
        Assert.Equal(0, match.Score);
        Assert.Empty(match.MatchedIndices);
    }

    [Fact]
    public void AdjacentAndWordStartMatchesScoreHigher()
    {
        FuzzyMatcher.TryMatch("abc", "abcxx", out FuzzyMatch adjacent);
        FuzzyMatcher.TryMatch("abc", "aXbXc", out FuzzyMatch scattered);
        Assert.True(adjacent.Score > scattered.Score);
    }

    [Fact]
    public void FuzzyMatch_HasValueEqualityOverTheIndices()
    {
        FuzzyMatcher.TryMatch("ab", "cab", out FuzzyMatch a);
        FuzzyMatcher.TryMatch("ab", "cab", out FuzzyMatch b);
        Assert.Equal(a, b);                              // same score and indices, so equal
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Single(new HashSet<FuzzyMatch> { a, b }); // and they deduplicate
    }

    [Fact]
    public void Rank_OrdersMatchesBestFirstAndDropsNonMatches()
    {
        string[] items = ["banana", "grape", "apple"];
        List<(string Item, FuzzyMatch Match)> ranked = FuzzyMatcher.Rank("ap", items, s => s);

        Assert.Equal(2, ranked.Count);       // banana does not match
        Assert.Equal("apple", ranked[0].Item); // 'a' at a word start beats 'a' mid-word in "grape"
        Assert.Equal("grape", ranked[1].Item);
    }
}
