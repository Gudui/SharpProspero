// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Collections.Generic;
using SharpProspero.Numerics;
using Xunit;

namespace SharpProspero.Tests;

public sealed class RandomTests
{
    [Fact]
    public void SameSeed_ProducesTheSameSequence()
    {
        var a = new GameRandom(12345);
        var b = new GameRandom(12345);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new GameRandom(1);
        var b = new GameRandom(2);
        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void NextDouble_StaysInUnitInterval()
    {
        var r = new GameRandom(999);
        for (int i = 0; i < 10_000; i++)
        {
            double d = r.NextDouble();
            Assert.True(d >= 0.0 && d < 1.0);
        }
    }

    [Fact]
    public void Next_StaysWithinRangeAndCoversIt()
    {
        var r = new GameRandom(42);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            int v = r.Next(10, 20);
            Assert.InRange(v, 10, 19);
            seen.Add(v);
        }
        Assert.Equal(10, seen.Count); // every value in [10,20) appears
    }

    [Fact]
    public void Next_EmptyOrInvertedRange_ReturnsMin()
    {
        var r = new GameRandom(7);
        Assert.Equal(5, r.Next(5, 5));
        Assert.Equal(9, r.Next(9, 3));
    }
}
