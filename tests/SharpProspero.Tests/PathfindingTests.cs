// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ai;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PathfindingTests
{
    private static bool Open(int column, int row) => true;

    private static void AssertContiguous(List<(int Column, int Row)> path, bool allowDiagonal)
    {
        for (int i = 1; i < path.Count; i++)
        {
            int dc = Math.Abs(path[i].Column - path[i - 1].Column);
            int dr = Math.Abs(path[i].Row - path[i - 1].Row);
            Assert.True(Math.Max(dc, dr) == 1);
            if (!allowDiagonal)
                Assert.Equal(1, dc + dr);
        }
    }

    [Fact]
    public void FindsAStraightPathOnAnOpenGrid()
    {
        var finder = new GridPathfinder(5, 5);
        List<(int, int)> path = finder.FindPath((0, 0), (4, 0), Open);
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0) }, path);
    }

    [Fact]
    public void RoutesAroundAWall()
    {
        var finder = new GridPathfinder(5, 5);
        // A wall down column 2 for rows 0..3, with a gap at row 4.
        bool Walk(int c, int r) => !(c == 2 && r <= 3);

        List<(int Column, int Row)> path = finder.FindPath((0, 0), (4, 0), Walk);
        Assert.NotEmpty(path);
        Assert.Equal((0, 0), path[0]);
        Assert.Equal((4, 0), path[^1]);
        AssertContiguous(path, allowDiagonal: false);
        foreach ((int c, int r) in path)
            Assert.True(Walk(c, r)); // never steps on a wall
    }

    [Fact]
    public void ReturnsEmptyWhenThereIsNoPath()
    {
        var finder = new GridPathfinder(5, 5);
        // Seal the goal (4,4) off from four-way movement.
        bool Walk(int c, int r) => !((c == 3 && r == 4) || (c == 4 && r == 3));
        Assert.Empty(finder.FindPath((0, 0), (4, 4), Walk));
    }

    [Fact]
    public void HandlesTrivialAndInvalidEnds()
    {
        var finder = new GridPathfinder(5, 5);
        Assert.Equal(new[] { (2, 2) }, finder.FindPath((2, 2), (2, 2), Open)); // start is the goal
        Assert.Empty(finder.FindPath((-1, 0), (4, 4), Open));                  // off the grid
        Assert.Empty(finder.FindPath((0, 0), (4, 4), (c, r) => !(c == 4 && r == 4))); // goal blocked
    }

    [Fact]
    public void DiagonalMovesGoCornerToCorner()
    {
        var finder = new GridPathfinder(5, 5) { AllowDiagonal = true };
        List<(int Column, int Row)> path = finder.FindPath((0, 0), (3, 3), Open);
        Assert.Equal((0, 0), path[0]);
        Assert.Equal((3, 3), path[^1]);
        Assert.Equal(4, path.Count); // three diagonal steps
        AssertContiguous(path, allowDiagonal: true);
    }

    [Fact]
    public void DiagonalDoesNotCutThroughWallCorners()
    {
        var finder = new GridPathfinder(5, 5) { AllowDiagonal = true };
        // Both cells beside the (0,0)->(1,1) diagonal are walls, so the corner cannot be cut — and with
        // no other way out, (0,0) is sealed in.
        bool Walk(int c, int r) => !((c == 1 && r == 0) || (c == 0 && r == 1));
        Assert.Empty(finder.FindPath((0, 0), (1, 1), Walk));
    }
}
