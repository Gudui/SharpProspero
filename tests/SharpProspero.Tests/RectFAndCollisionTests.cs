// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Numerics;
using Xunit;

namespace SharpProspero.Tests;

public sealed class RectFAndCollisionTests
{
    [Fact]
    public void Rect_EdgesCentreAndSize()
    {
        var r = new RectF(10f, 20f, 30f, 40f);
        Assert.Equal(40f, r.Right);
        Assert.Equal(60f, r.Bottom);
        Assert.Equal(new Vector2(25f, 40f), r.Center);
        Assert.Equal(new Vector2(30f, 40f), r.Size);
        Assert.Equal(new Vector2(10f, 20f), r.Position);
        Assert.False(r.IsEmpty);
        Assert.True(new RectF(0f, 0f, 0f, 5f).IsEmpty);
    }

    [Fact]
    public void Rect_Factories()
    {
        Assert.Equal(new RectF(10f, 20f, 30f, 40f), RectF.FromEdges(10f, 20f, 40f, 60f));
        Assert.Equal(new RectF(0f, 0f, 10f, 10f), RectF.FromCenter(new Vector2(5f, 5f), 10f, 10f));
    }

    [Fact]
    public void Rect_ContainsAPointHalfOpen()
    {
        var r = new RectF(10f, 20f, 30f, 40f);
        Assert.True(r.Contains(10f, 20f));   // top-left is inside
        Assert.True(r.Contains(new Vector2(25f, 40f)));
        Assert.False(r.Contains(40f, 20f));  // right edge is outside
        Assert.False(r.Contains(10f, 60f));  // bottom edge is outside
        Assert.False(r.Contains(5f, 5f));
    }

    [Fact]
    public void Rect_ContainsAndIntersectsRectangles()
    {
        var r = new RectF(10f, 20f, 30f, 40f);
        Assert.True(r.Contains(new RectF(15f, 25f, 10f, 10f)));
        Assert.False(r.Contains(new RectF(15f, 25f, 40f, 10f)));

        Assert.True(r.Intersects(new RectF(30f, 50f, 20f, 20f)));
        Assert.False(r.Intersects(new RectF(40f, 20f, 10f, 10f))); // shares the right edge only
        Assert.False(r.Intersects(new RectF(100f, 100f, 5f, 5f)));
    }

    [Fact]
    public void Rect_IntersectionAndUnion()
    {
        var r = new RectF(10f, 20f, 30f, 40f);
        Assert.Equal(new RectF(30f, 50f, 10f, 10f), r.Intersection(new RectF(30f, 50f, 20f, 20f)));
        Assert.Equal(RectF.Empty, r.Intersection(new RectF(100f, 100f, 5f, 5f)));

        Assert.Equal(new RectF(10f, 20f, 50f, 50f), r.Union(new RectF(50f, 60f, 10f, 10f)));
        Assert.Equal(r, r.Union(RectF.Empty));
    }

    [Fact]
    public void Rect_InflateOffsetClamp()
    {
        var r = new RectF(10f, 20f, 30f, 40f);
        RectF grown = r.Inflate(5f, 5f);
        Assert.Equal(new RectF(5f, 15f, 40f, 50f), grown);
        Assert.Equal(r.Center, grown.Center); // grows around the centre

        Assert.Equal(new RectF(13f, 24f, 30f, 40f), r.Offset(3f, 4f));

        Assert.Equal(new Vector2(10f, 60f), r.Clamp(new Vector2(0f, 100f))); // pulled to the corner
        Assert.Equal(new Vector2(25f, 40f), r.Clamp(new Vector2(25f, 40f))); // already inside
    }

    [Fact]
    public void Collision_Points()
    {
        Assert.True(Collision.PointInCircle(new Vector2(3f, 4f), Vector2.Zero, 5f)); // on the edge
        Assert.False(Collision.PointInCircle(new Vector2(3f, 4f), Vector2.Zero, 4f));
    }

    [Fact]
    public void Collision_Circles()
    {
        Assert.True(Collision.CirclesOverlap(Vector2.Zero, 5f, new Vector2(8f, 0f), 5f));
        Assert.True(Collision.CirclesOverlap(Vector2.Zero, 5f, new Vector2(10f, 0f), 5f)); // touching
        Assert.False(Collision.CirclesOverlap(Vector2.Zero, 3f, new Vector2(10f, 0f), 3f));
    }

    [Fact]
    public void Collision_CircleAndRect()
    {
        var rect = new RectF(0f, 0f, 10f, 10f);
        Assert.True(Collision.CircleOverlapsRect(new Vector2(5f, 5f), 2f, rect));   // centre inside
        Assert.True(Collision.CircleOverlapsRect(new Vector2(-3f, 5f), 5f, rect));  // reaches the left edge
        Assert.True(Collision.CircleOverlapsRect(new Vector2(-3f, -4f), 5f, rect)); // reaches the corner
        Assert.False(Collision.CircleOverlapsRect(new Vector2(-10f, 5f), 3f, rect));
    }
}
