// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>How a texture coordinate outside 0..1 is handled, from <see cref="AgcSamplerDescriptor.SetAddressModes"/>.</summary>
public enum AgcAddressMode : uint
{
    /// <summary>Repeat the texture (the fractional part of the coordinate).</summary>
    Wrap = 0,
    /// <summary>Repeat, mirroring every other tile.</summary>
    Mirror = 1,
    /// <summary>Clamp to the edge texel.</summary>
    ClampToEdge = 2,
    /// <summary>Mirror once, then clamp to the edge texel.</summary>
    MirrorOnceToEdge = 3,
    /// <summary>Clamp to halfway into the border.</summary>
    ClampHalfBorder = 4,
    /// <summary>Mirror once, then clamp halfway into the border.</summary>
    MirrorOnceHalfBorder = 5,
    /// <summary>Clamp to the border color.</summary>
    ClampToBorder = 6,
    /// <summary>Mirror once, then clamp to the border color.</summary>
    MirrorOnceToBorder = 7,
}

/// <summary>The minification or magnification filter, from <see cref="AgcSamplerDescriptor.SetFilter"/>.</summary>
public enum AgcFilter : uint
{
    /// <summary>Nearest texel.</summary>
    Point = 0,
    /// <summary>Linear blend of the four nearest texels.</summary>
    Bilinear = 1,
    /// <summary>Nearest, with anisotropy.</summary>
    AnisotropicPoint = 2,
    /// <summary>Linear, with anisotropy.</summary>
    AnisotropicBilinear = 3,
}

/// <summary>How mip levels are blended, from <see cref="AgcSamplerDescriptor.SetFilter"/>.</summary>
public enum AgcMipFilter : uint
{
    /// <summary>Sample only the base level.</summary>
    None = 0,
    /// <summary>Pick the nearest mip level.</summary>
    Point = 1,
    /// <summary>Blend the two nearest mip levels.</summary>
    Linear = 2,
}

/// <summary>The comparison a shadow (depth-compare) sampler applies, from <see cref="AgcSamplerDescriptor.SetDepthCompare"/>.</summary>
public enum AgcDepthCompare : uint
{
    /// <summary>Never passes.</summary>
    Never = 0,
    /// <summary>Passes when the reference is less than the texel.</summary>
    Less = 1,
    /// <summary>Passes when equal.</summary>
    Equal = 2,
    /// <summary>Passes when less than or equal.</summary>
    LessEqual = 3,
    /// <summary>Passes when greater than.</summary>
    Greater = 4,
    /// <summary>Passes when not equal.</summary>
    NotEqual = 5,
    /// <summary>Passes when greater than or equal.</summary>
    GreaterEqual = 6,
    /// <summary>Always passes.</summary>
    Always = 7,
}

/// <summary>The color used outside a texture when the address mode clamps to a border.</summary>
public enum AgcBorderColor : uint
{
    /// <summary>Transparent black (0, 0, 0, 0).</summary>
    TransparentBlack = 0,
    /// <summary>Opaque black (0, 0, 0, 1).</summary>
    OpaqueBlack = 1,
    /// <summary>Opaque white (1, 1, 1, 1).</summary>
    OpaqueWhite = 2,
    /// <summary>A color from the border-color table, selected by a pointer set separately.</summary>
    FromTable = 3,
}

/// <summary>
/// The four-word hardware descriptor a shader reads to filter a texture (an "S#"): how coordinates wrap,
/// which filters apply for magnification, minification and between mip levels, the level-of-detail range
/// and bias, anisotropy, an optional depth comparison for shadows, and the border color. Build one, write
/// its words with <see cref="WriteTo"/> into GPU-readable memory, and point a shader sampler slot at it. The bit layout
/// is the graphics-processor sampler-resource format; the setters pack each field into the exact bits the
/// hardware reads.
/// </summary>
/// <remarks>
/// The level-of-detail values are fixed-point: the range clamps are unsigned 4.8 and the bias is signed
/// 5.8, which the float setters convert for you. This is a value type; copy it where the shader can reach
/// it. A default descriptor (all zero) wraps, points and samples the base level, which is a valid start.
/// </remarks>
public struct AgcSamplerDescriptor
{
    private uint _w0, _w1, _w2, _w3;

    /// <summary>The number of 32-bit words in the descriptor.</summary>
    public const int WordCount = 4;

    private static void Set(ref uint word, int offset, int width, uint value)
    {
        uint mask = width == 32 ? 0xFFFFFFFFu : ((1u << width) - 1) << offset;
        word = (word & ~mask) | ((value << offset) & mask);
    }

    /// <summary>The word at <paramref name="index"/> (0 to 3).</summary>
    public readonly uint this[int index] => index switch
    {
        0 => _w0,
        1 => _w1,
        2 => _w2,
        3 => _w3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Writes the four words into <paramref name="destination"/>, which must hold at least four.</summary>
    public readonly void WriteTo(Span<uint> destination)
    {
        if (destination.Length < WordCount)
            throw new ArgumentException($"A sampler descriptor needs {WordCount} words.", nameof(destination));
        destination[0] = _w0; destination[1] = _w1; destination[2] = _w2; destination[3] = _w3;
    }

    /// <summary>How coordinates outside 0..1 wrap on each of the three axes.</summary>
    public void SetAddressModes(AgcAddressMode x, AgcAddressMode y, AgcAddressMode z)
    {
        Set(ref _w0, 0, 3, (uint)x);
        Set(ref _w0, 3, 3, (uint)y);
        Set(ref _w0, 6, 3, (uint)z);
    }

    /// <summary>The magnification, minification and mip filters.</summary>
    public void SetFilter(AgcFilter magnification, AgcFilter minification, AgcMipFilter mip)
    {
        Set(ref _w2, 20, 2, (uint)magnification);
        Set(ref _w2, 22, 2, (uint)minification);
        Set(ref _w2, 26, 2, (uint)mip);
    }

    /// <summary>The maximum anisotropy ratio (0 = 1x, 1 = 2x, 2 = 4x, 3 = 8x, 4 = 16x).</summary>
    public void SetMaxAnisotropy(int ratio) => Set(ref _w0, 9, 3, (uint)ratio & 0x7u);

    /// <summary>The depth-compare function for a shadow sampler.</summary>
    public void SetDepthCompare(AgcDepthCompare compare) => Set(ref _w0, 12, 3, (uint)compare);

    /// <summary>The lowest and highest mip level the sampler reads, as unsigned 4.8 fixed-point.</summary>
    public void SetLodRange(float minLod, float maxLod)
    {
        Set(ref _w1, 0, 12, ToUFixed4_8(minLod));
        Set(ref _w1, 12, 12, ToUFixed4_8(maxLod));
    }

    /// <summary>The level-of-detail bias, as signed 5.8 fixed-point.</summary>
    public void SetLodBias(float bias) => Set(ref _w2, 0, 14, ToSFixed5_8(bias));

    /// <summary>The border color used when an address mode clamps to a border.</summary>
    public void SetBorderColor(AgcBorderColor color) => Set(ref _w3, 30, 2, (uint)color);

    /// <summary>The index into the border-color table, used with <see cref="AgcBorderColor.FromTable"/>.</summary>
    public void SetBorderColorTableIndex(int index) => Set(ref _w3, 0, 12, (uint)index & 0xFFFu);

    // Converts a level of detail to unsigned 4.8 fixed-point, clamped to the 12-bit range.
    private static uint ToUFixed4_8(float value)
    {
        float clamped = Math.Clamp(value, 0f, 15.996f);
        return (uint)(clamped * 256f + 0.5f) & 0xFFFu;
    }

    // Converts a bias to signed 5.8 fixed-point, clamped to the 14-bit two's-complement range.
    private static uint ToSFixed5_8(float value)
    {
        float clamped = Math.Clamp(value, -16f, 15.996f);
        int fixedPoint = (int)MathF.Round(clamped * 256f);
        return (uint)fixedPoint & 0x3FFFu;
    }
}
