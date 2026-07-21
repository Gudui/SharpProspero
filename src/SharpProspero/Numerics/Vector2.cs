// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Numerics;

/// <summary>
/// A two-dimensional vector of single-precision numbers, for a position, a velocity or a direction in
/// game and drawing code. It is a small value type with the usual operators and helpers, so movement and
/// steering read as arithmetic rather than a pair of loose floats.
/// </summary>
/// <remarks>Creates a vector with the given components.</remarks>
public readonly struct Vector2(float x, float y) : IEquatable<Vector2>
{
    /// <summary>The horizontal component.</summary>
    public float X { get; } = x;

    /// <summary>The vertical component.</summary>
    public float Y { get; } = y;

    /// <summary>The zero vector.</summary>
    public static Vector2 Zero => new(0f, 0f);

    /// <summary>The vector whose components are both one.</summary>
    public static Vector2 One => new(1f, 1f);

    /// <summary>The unit vector along the x-axis.</summary>
    public static Vector2 UnitX => new(1f, 0f);

    /// <summary>The unit vector along the y-axis.</summary>
    public static Vector2 UnitY => new(0f, 1f);

    /// <summary>The squared length, cheaper than <see cref="Length"/> when only comparing distances.</summary>
    public float LengthSquared => (X * X) + (Y * Y);

    /// <summary>The length (magnitude) of the vector.</summary>
    public float Length => MathF.Sqrt(LengthSquared);

    /// <summary>A vector in the same direction with length one, or zero when this vector is (near) zero.</summary>
    public Vector2 Normalized()
    {
        float length = Length;
        return length <= 1e-6f ? Zero : new Vector2(X / length, Y / length);
    }

    /// <summary>This vector with a different x component.</summary>
    public Vector2 WithX(float value) => new(value, Y);

    /// <summary>This vector with a different y component.</summary>
    public Vector2 WithY(float value) => new(X, value);

    /// <summary>Turns the vector by <paramref name="radians"/> (clockwise on a screen whose y grows downward).</summary>
    public Vector2 Rotate(float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Vector2((X * cos) - (Y * sin), (X * sin) + (Y * cos));
    }

    /// <summary>Adds two vectors component-wise.</summary>
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts <paramref name="b"/> from <paramref name="a"/> component-wise.</summary>
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Negates the vector.</summary>
    public static Vector2 operator -(Vector2 v) => new(-v.X, -v.Y);

    /// <summary>Scales the vector by <paramref name="scalar"/>.</summary>
    public static Vector2 operator *(Vector2 v, float scalar) => new(v.X * scalar, v.Y * scalar);

    /// <summary>Scales the vector by <paramref name="scalar"/>.</summary>
    public static Vector2 operator *(float scalar, Vector2 v) => v * scalar;

    /// <summary>Divides the vector by <paramref name="scalar"/>.</summary>
    public static Vector2 operator /(Vector2 v, float scalar) => new(v.X / scalar, v.Y / scalar);

    /// <summary>The dot product of two vectors.</summary>
    public static float Dot(Vector2 a, Vector2 b) => (a.X * b.X) + (a.Y * b.Y);

    /// <summary>The distance between two points.</summary>
    public static float Distance(Vector2 a, Vector2 b) => (a - b).Length;

    /// <summary>The squared distance between two points, cheaper than <see cref="Distance"/> for comparisons.</summary>
    public static float DistanceSquared(Vector2 a, Vector2 b) => (a - b).LengthSquared;

    /// <summary>A point <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (not clamped).</summary>
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    /// <inheritdoc/>
    public bool Equals(Vector2 other) => X == other.X && Y == other.Y;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Whether two vectors have equal components.</summary>
    public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);

    /// <summary>Whether two vectors differ in either component.</summary>
    public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y})";
}
