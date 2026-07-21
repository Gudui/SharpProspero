// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Animation;
using SharpProspero.Numerics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class SplineTests
{
    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }

    [Fact]
    public void CubicBezier_PassesThroughItsEndpoints()
    {
        var p0 = new Vector2(0, 0);
        var p1 = new Vector2(1, 2);
        var p2 = new Vector2(3, 2);
        var p3 = new Vector2(4, 0);
        AssertClose(p0, Spline.Bezier(p0, p1, p2, p3, 0f));
        AssertClose(p3, Spline.Bezier(p0, p1, p2, p3, 1f));
    }

    [Fact]
    public void QuadraticBezier_PassesThroughItsEndpoints()
    {
        var p0 = new Vector2(0, 0);
        var p1 = new Vector2(2, 4);
        var p2 = new Vector2(4, 0);
        AssertClose(p0, Spline.Bezier(p0, p1, p2, 0f));
        AssertClose(p2, Spline.Bezier(p0, p1, p2, 1f));
    }

    [Fact]
    public void Bezier_ClampsTOutsideZeroToOne()
    {
        var p0 = new Vector2(0, 0);
        var p1 = new Vector2(1, 1);
        var p2 = new Vector2(2, 2);
        var p3 = new Vector2(3, 3);
        AssertClose(p0, Spline.Bezier(p0, p1, p2, p3, -1f));
        AssertClose(p3, Spline.Bezier(p0, p1, p2, p3, 2f));
    }

    [Fact]
    public void CatmullRom_PassesThroughEveryWaypoint()
    {
        Vector2[] points = [new(0, 0), new(2, 3), new(5, 1)];
        AssertClose(points[0], Spline.CatmullRom(points, 0f));   // first
        AssertClose(points[1], Spline.CatmullRom(points, 0.5f)); // the middle waypoint
        AssertClose(points[2], Spline.CatmullRom(points, 1f));   // last
    }

    [Fact]
    public void NaN_t_MapsToTheStartPointNotANaNPosition()
    {
        var p0 = new Vector2(1, 2);
        var p3 = new Vector2(9, 9);
        AssertClose(p0, Spline.Bezier(p0, new Vector2(3, 3), new Vector2(6, 6), p3, float.NaN));

        Vector2[] points = [new(0, 0), new(5, 5)];
        AssertClose(points[0], Spline.CatmullRom(points, float.NaN));
    }

    [Fact]
    public void CatmullRom_HandlesDegenerateInput()
    {
        Vector2[] single = [new(7, 8)];
        AssertClose(single[0], Spline.CatmullRom(single, 0.3f));
        Assert.Throws<ArgumentException>(() => Spline.CatmullRom([], 0f));
    }
}
