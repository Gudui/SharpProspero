// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class QuadtreeTests
{
    [Fact]
    public void Query_ReturnsOverlappingItemsAndExcludesTheRest()
    {
        var tree = new Quadtree<string>(new RectF(0, 0, 100, 100), maxItemsPerNode: 1);
        tree.Insert("tl", new RectF(10, 10, 5, 5));
        tree.Insert("tr", new RectF(80, 10, 5, 5));
        tree.Insert("bl", new RectF(10, 80, 5, 5));
        tree.Insert("br", new RectF(80, 80, 5, 5)); // forces subdivision

        Assert.Equal(4, tree.Count);
        List<string> hits = tree.Query(new RectF(0, 0, 20, 20));
        Assert.Contains("tl", hits);
        Assert.DoesNotContain("tr", hits);
        Assert.DoesNotContain("br", hits);
    }

    [Fact]
    public void Query_MatchesABruteForceScanAfterSubdivision()
    {
        var tree = new Quadtree<int>(new RectF(0, 0, 100, 100), maxItemsPerNode: 4, maxDepth: 6);
        var all = new List<(int Id, RectF Bounds)>();
        int id = 0;
        for (int gx = 0; gx < 10; gx++)
        {
            for (int gy = 0; gy < 10; gy++)
            {
                var bounds = new RectF(gx * 10f, gy * 10f, 4f, 4f);
                tree.Insert(id, bounds);
                all.Add((id, bounds));
                id++;
            }
        }

        var area = new RectF(25, 25, 30, 30);
        List<int> hits = tree.Query(area);
        List<int> expected = all.Where(x => x.Bounds.Intersects(area)).Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(expected, hits.OrderBy(x => x).ToList());
    }

    [Fact]
    public void Query_FindsAnItemThatStraddlesTheSplitLines()
    {
        var tree = new Quadtree<string>(new RectF(0, 0, 100, 100), maxItemsPerNode: 1);
        tree.Insert("center", new RectF(40, 40, 20, 20)); // spans the middle, cannot sink into a child
        tree.Insert("a", new RectF(5, 5, 2, 2));
        tree.Insert("b", new RectF(90, 90, 2, 2));
        tree.Insert("c", new RectF(5, 90, 2, 2));
        tree.Insert("d", new RectF(90, 5, 2, 2));

        Assert.Contains("center", tree.Query(new RectF(45, 45, 2, 2)));
    }

    [Fact]
    public void Clear_EmptiesTheTree()
    {
        var tree = new Quadtree<int>(new RectF(0, 0, 100, 100));
        tree.Insert(1, new RectF(1, 1, 1, 1));
        tree.Clear();
        Assert.Equal(0, tree.Count);
        Assert.Empty(tree.Query(new RectF(0, 0, 100, 100)));
    }

    [Fact]
    public void Query_FindsAnItemInsertedOutsideTheTreeBounds()
    {
        // An item outside the world bounds is stored at the root; a query that overlaps it must still
        // find it, matching a brute-force scan, rather than being pruned away with the root bounds.
        var tree = new Quadtree<int>(new RectF(0, 0, 100, 100));
        tree.Insert(1, new RectF(200, 200, 10, 10));
        Assert.Contains(1, tree.Query(new RectF(205, 205, 2, 2)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveLimitsAndEmptyBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quadtree<int>(new RectF(0, 0, 10, 10), maxItemsPerNode: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quadtree<int>(new RectF(0, 0, 10, 10), maxDepth: 0));
        Assert.Throws<ArgumentException>(() => new Quadtree<int>(new RectF(0, 0, 0, 10)));  // empty bounds
    }
}
