// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class RectPackerTests
{
    private static bool Overlaps(PackedRect a, PackedRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Fact]
    public void PlacedRectanglesStayInBoundsAndNeverOverlap()
    {
        var packer = new RectPacker(128, 128);
        var placed = new List<PackedRect>();
        var rng = new GameRandom(12345UL);
        for (int i = 0; i < 200; i++)
        {
            PackedRect? slot = packer.Insert(rng.Next(4, 24), rng.Next(4, 24), i);
            if (slot is { } rect)
            {
                Assert.InRange(rect.X, 0, 128 - rect.Width);
                Assert.InRange(rect.Y, 0, 128 - rect.Height);
                foreach (PackedRect other in placed)
                    Assert.False(Overlaps(rect, other), $"{rect} overlaps {other}");
                placed.Add(rect);
            }
        }
        Assert.NotEmpty(placed);
    }

    [Fact]
    public void ExactTiling_FillsTheAreaCompletely()
    {
        var packer = new RectPacker(64, 64);
        int placed = 0;
        for (int i = 0; i < 16; i++) // sixteen 16x16 tiles fit a 64x64 area exactly
            if (packer.Insert(16, 16, i) is not null)
                placed++;
        Assert.Equal(16, placed);
        Assert.Equal(1f, packer.Occupancy, 3);
        Assert.Null(packer.Insert(16, 16)); // nothing left
    }

    [Fact]
    public void Insert_RejectsOversizedOrEmptyRectangles()
    {
        var packer = new RectPacker(32, 32);
        Assert.Null(packer.Insert(33, 10));
        Assert.Null(packer.Insert(10, 33));
        Assert.Null(packer.Insert(0, 10));
        Assert.Null(packer.Insert(10, -1));
    }

    [Fact]
    public void Pack_PlacesABatchAndKeepsEveryIdWhenItAllFits()
    {
        var packer = new RectPacker(256, 256);
        var items = Enumerable.Range(0, 30).Select(i => (Id: i, Width: 10 + (i % 7) * 5, Height: 12 + (i % 5) * 4));
        IReadOnlyList<PackedRect> placed = packer.Pack(items);
        Assert.Equal(30, placed.Count);
        Assert.Equal(30, placed.Select(p => p.Id).Distinct().Count());
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
                Assert.False(Overlaps(placed[i], placed[j]));
    }

    [Fact]
    public void Reset_ReturnsToEmpty()
    {
        var packer = new RectPacker(32, 32);
        packer.Insert(20, 20);
        packer.Reset();
        Assert.Equal(0f, packer.Occupancy);
        Assert.NotNull(packer.Insert(32, 32));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectPacker(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectPacker(10, -1));
    }
}
