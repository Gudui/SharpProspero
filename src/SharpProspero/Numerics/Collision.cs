// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Numerics;

/// <summary>
/// The overlap tests game code reaches for, over <see cref="Vector2"/> and <see cref="RectF"/>: two
/// circles, a circle and a rectangle, and a point in a circle. Rectangle-against-rectangle and
/// point-in-rectangle live on <see cref="RectF"/> itself.
/// </summary>
public static class Collision
{
    /// <summary>Whether a point is within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    public static bool PointInCircle(Vector2 point, Vector2 center, float radius)
        => Vector2.DistanceSquared(point, center) <= radius * radius;

    /// <summary>Whether two circles overlap or touch.</summary>
    public static bool CirclesOverlap(Vector2 centerA, float radiusA, Vector2 centerB, float radiusB)
    {
        float reach = radiusA + radiusB;
        return Vector2.DistanceSquared(centerA, centerB) <= reach * reach;
    }

    /// <summary>Whether a circle overlaps or touches <paramref name="rect"/>.</summary>
    public static bool CircleOverlapsRect(Vector2 center, float radius, RectF rect)
    {
        // The nearest point of the rectangle to the circle's centre is within the radius exactly when
        // they meet.
        Vector2 nearest = rect.Clamp(center);
        return Vector2.DistanceSquared(center, nearest) <= radius * radius;
    }
}
