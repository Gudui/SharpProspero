// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Graphics;

/// <summary>
/// A 32-bit color packed for the display's B8-G8-R8-A8 sRGB surface. In memory the bytes run blue,
/// green, red, alpha; the packed integer places red in bits 16-23, green in 8-15, blue in 0-7 and
/// alpha in 24-31.
/// </summary>
public readonly struct Color
{
    /// <summary>The packed 32-bit value for the surface.</summary>
    public readonly uint Value;

    /// <summary>Wraps an already-packed value.</summary>
    public Color(uint packed) => Value = packed;

    /// <summary>
    /// Packs a fully opaque color from 8-bit red, green and blue components (alpha 0xFF). Use
    /// <see cref="FromArgb"/> to set a specific alpha for blended surfaces.
    /// </summary>
    public static Color FromRgb(byte r, byte g, byte b)
        => FromArgb(0xFF, r, g, b);

    /// <summary>Packs a color from 8-bit alpha, red, green and blue components.</summary>
    public static Color FromArgb(byte a, byte r, byte g, byte b)
        => new(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    /// <summary>Alpha component (bits 24-31).</summary>
    public byte A => (byte)(Value >> 24);

    /// <summary>Red component (bits 16-23).</summary>
    public byte R => (byte)(Value >> 16);

    /// <summary>Green component (bits 8-15).</summary>
    public byte G => (byte)(Value >> 8);

    /// <summary>Blue component (bits 0-7).</summary>
    public byte B => (byte)Value;

    /// <summary>The same red, green and blue with a new alpha, for compositing with a blended blit.</summary>
    public Color WithAlpha(byte alpha) => new((Value & 0x00FFFFFFu) | ((uint)alpha << 24));

    /// <summary>Black.</summary>
    public static Color Black => FromRgb(0, 0, 0);

    /// <summary>White.</summary>
    public static Color White => FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Full-intensity red.</summary>
    public static Color Red => FromRgb(0xFF, 0, 0);

    /// <summary>Full-intensity green.</summary>
    public static Color Green => FromRgb(0, 0xFF, 0);

    /// <summary>Full-intensity blue.</summary>
    public static Color Blue => FromRgb(0, 0, 0xFF);

    /// <summary>Fully transparent.</summary>
    public static Color Transparent => FromArgb(0, 0, 0, 0);

    /// <summary>
    /// Blends component-wise from <paramref name="from"/> to <paramref name="to"/>; <paramref name="t"/>
    /// clamps to the range 0 to 1.
    /// </summary>
    public static Color Lerp(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * t + 0.5f);
        return FromArgb(Mix(from.A, to.A), Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    /// <summary>
    /// An opaque color from hue (0-360 degrees, wrapped), saturation and value (each clamped to 0-1).
    /// </summary>
    public static Color FromHsv(float hue, float saturation, float value)
    {
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);
        hue -= MathF.Floor(hue / 360f) * 360f;

        float c = value * saturation;
        float x = c * (1f - MathF.Abs((hue / 60f) % 2f - 1f));
        float m = value - c;
        (float r, float g, float b) = hue switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return FromRgb((byte)((r + m) * 255f + 0.5f), (byte)((g + m) * 255f + 0.5f), (byte)((b + m) * 255f + 0.5f));
    }

    /// <summary>Implicitly reads the packed value.</summary>
    public static implicit operator uint(Color color) => color.Value;
}
