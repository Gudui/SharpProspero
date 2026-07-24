// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Numerics;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class WeightedTableTests
{
    [Fact]
    public void DrawsAreRoughlyProportionalToWeight()
    {
        var table = new WeightedTable<string>()
            .Add("common", 70)
            .Add("uncommon", 25)
            .Add("rare", 5);
        Assert.Equal(100.0, table.TotalWeight, 6);

        var counts = new Dictionary<string, int> { ["common"] = 0, ["uncommon"] = 0, ["rare"] = 0 };
        var rng = new GameRandom(99UL);
        const int draws = 100_000;
        for (int i = 0; i < draws; i++)
            counts[table.Pick(rng)]++;

        Assert.InRange(counts["common"] / (double)draws, 0.66, 0.74);
        Assert.InRange(counts["uncommon"] / (double)draws, 0.22, 0.28);
        Assert.InRange(counts["rare"] / (double)draws, 0.03, 0.07);
    }

    [Fact]
    public void ZeroWeightEntriesAreNeverDrawn()
    {
        var table = new WeightedTable<int>()
            .Add(1, 0)
            .Add(2, 10)
            .Add(3, 0);
        var rng = new GameRandom(7UL);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(2, table.Pick(rng));
    }

    [Fact]
    public void EmptyOrAllZeroTable_TryPickFailsAndPickThrows()
    {
        var rng = new GameRandom(1UL);
        var empty = new WeightedTable<int>();
        Assert.False(empty.TryPick(rng, out _));
        Assert.Throws<InvalidOperationException>(() => empty.Pick(rng));

        var allZero = new WeightedTable<int>().Add(5, 0).Add(6, 0);
        Assert.False(allZero.TryPick(rng, out _));
    }

    [Fact]
    public void NegativeOrNonFiniteWeight_Throws()
    {
        var table = new WeightedTable<int>();
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Add(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Add(1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Add(1, double.PositiveInfinity));
    }

    [Fact]
    public void Clear_EmptiesTheTable()
    {
        var table = new WeightedTable<int>().Add(1, 5).Add(2, 5);
        table.Clear();
        Assert.Equal(0, table.Count);
        Assert.Equal(0.0, table.TotalWeight);
    }
}
