// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>The shape a texture presents to a shader, from <see cref="AgcTextureDescriptor.SetType"/>.</summary>
public enum AgcImageType : uint
{
    /// <summary>A one-dimensional texture.</summary>
    Texture1D = 8,
    /// <summary>A two-dimensional texture.</summary>
    Texture2D = 9,
    /// <summary>A three-dimensional (volume) texture.</summary>
    Texture3D = 10,
    /// <summary>A cube map (six faces).</summary>
    Cubemap = 11,
    /// <summary>An array of one-dimensional textures.</summary>
    Texture1DArray = 12,
    /// <summary>An array of two-dimensional textures.</summary>
    Texture2DArray = 13,
    /// <summary>A multisampled two-dimensional texture.</summary>
    Texture2DMultisample = 14,
    /// <summary>An array of multisampled two-dimensional textures.</summary>
    Texture2DArrayMultisample = 15,
}

/// <summary>Which source channel feeds an output channel, from <see cref="AgcTextureDescriptor.SetChannelOrder"/>.</summary>
public enum AgcChannelSource : uint
{
    /// <summary>A constant zero.</summary>
    Zero = 0,
    /// <summary>A constant one.</summary>
    One = 1,
    /// <summary>The red (first) channel.</summary>
    Red = 4,
    /// <summary>The green (second) channel.</summary>
    Green = 5,
    /// <summary>The blue (third) channel.</summary>
    Blue = 6,
    /// <summary>The alpha (fourth) channel.</summary>
    Alpha = 7,
}

/// <summary>
/// The eight-word hardware descriptor a shader reads to sample a texture (a "T#"): where the pixels are,
/// how big the surface is, its format, how its channels map to red-green-blue-alpha, and the mip and
/// array ranges. Build one for an image already laid out in tiled memory (by the host texture tool or by
/// <see cref="AgcTiler"/>), write its words with <see cref="WriteTo"/> into GPU-readable memory, and point
/// a shader resource slot at it. The bit layout is the graphics-processor image-resource format; the
/// setters pack each field into the exact bits the hardware reads.
/// </summary>
/// <remarks>
/// A descriptor addresses memory in 256-byte units, so the base address and the metadata address are the
/// byte address shifted right by eight. This is a value type; copy it where the shader can reach it.
/// </remarks>
public struct AgcTextureDescriptor
{
    private uint _w0, _w1, _w2, _w3, _w4, _w5, _w6, _w7;

    /// <summary>The number of 32-bit words in the descriptor.</summary>
    public const int WordCount = 8;

    private static void Set(ref uint word, int offset, int width, uint value)
    {
        uint mask = width == 32 ? 0xFFFFFFFFu : ((1u << width) - 1) << offset;
        word = (word & ~mask) | ((value << offset) & mask);
    }

    /// <summary>The word at <paramref name="index"/> (0 to 7).</summary>
    public readonly uint this[int index] => index switch
    {
        0 => _w0,
        1 => _w1,
        2 => _w2,
        3 => _w3,
        4 => _w4,
        5 => _w5,
        6 => _w6,
        7 => _w7,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Writes the eight words into <paramref name="destination"/>, which must hold at least eight.</summary>
    public readonly void WriteTo(Span<uint> destination)
    {
        if (destination.Length < WordCount)
            throw new ArgumentException($"A texture descriptor needs {WordCount} words.", nameof(destination));
        destination[0] = _w0; destination[1] = _w1; destination[2] = _w2; destination[3] = _w3;
        destination[4] = _w4; destination[5] = _w5; destination[6] = _w6; destination[7] = _w7;
    }

    /// <summary>The base GPU byte address of the pixels. Stored as the address in 256-byte units.</summary>
    public void SetBaseAddress(ulong gpuByteAddress)
    {
        ulong units = gpuByteAddress >> 8;
        _w0 = (uint)units;
        Set(ref _w1, 0, 6, (uint)(units >> 32) & 0x3Fu); // the high address bits sit below the memory-type field
    }

    /// <summary>The pixel format, a six-bit surface-format value the surface was laid out with.</summary>
    public void SetDataFormat(uint surfaceFormat) => Set(ref _w1, 20, 6, surfaceFormat & 0x3Fu);

    /// <summary>How the stored channel values are interpreted (see <see cref="AgcTextureChannelType"/>).</summary>
    public void SetChannelType(AgcTextureChannelType channelType) => Set(ref _w1, 26, 4, (uint)channelType);

    /// <summary>The surface width and height in texels (each at most 16384).</summary>
    public void SetDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Set(ref _w2, 0, 14, (uint)(width - 1));
        Set(ref _w2, 14, 14, (uint)(height - 1));
    }

    /// <summary>The row pitch in texels, when it differs from the width (each at most 16384).</summary>
    public void SetPitch(int pitchInTexels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pitchInTexels, 1);
        Set(ref _w4, 13, 14, (uint)(pitchInTexels - 1));
    }

    /// <summary>The depth of a volume texture, or the slice count of an array (at most 8192).</summary>
    public void SetDepthOrSlices(int depthOrSlices)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depthOrSlices, 1);
        Set(ref _w4, 0, 13, (uint)(depthOrSlices - 1));
    }

    /// <summary>Which source channel feeds each of the four output channels.</summary>
    public void SetChannelOrder(AgcChannelSource x, AgcChannelSource y, AgcChannelSource z, AgcChannelSource w)
    {
        Set(ref _w3, 0, 3, (uint)x);
        Set(ref _w3, 3, 3, (uint)y);
        Set(ref _w3, 6, 3, (uint)z);
        Set(ref _w3, 9, 3, (uint)w);
    }

    /// <summary>The lowest and highest mip level the shader may sample.</summary>
    public void SetMipRange(int baseLevel, int lastLevel)
    {
        Set(ref _w3, 12, 4, (uint)baseLevel);
        Set(ref _w3, 16, 4, (uint)lastLevel);
    }

    /// <summary>The first and last array slice the shader may sample.</summary>
    public void SetArrayRange(int baseSlice, int lastSlice)
    {
        Set(ref _w5, 0, 13, (uint)baseSlice);
        Set(ref _w5, 13, 13, (uint)lastSlice);
    }

    /// <summary>The tiling (swizzle) mode index the surface was laid out with.</summary>
    public void SetTilingIndex(int tilingIndex) => Set(ref _w3, 20, 5, (uint)tilingIndex);

    /// <summary>The texture's dimensionality.</summary>
    public void SetType(AgcImageType type) => Set(ref _w3, 28, 4, (uint)type);

    /// <summary>The base address of the compression/metadata surface, in 256-byte units.</summary>
    public void SetMetadataAddress(ulong gpuByteAddress) => _w7 = (uint)(gpuByteAddress >> 8);

    /// <summary>Whether the surface carries compression metadata.</summary>
    public void SetMetadataEnabled(bool enabled) => Set(ref _w6, 21, 1, enabled ? 1u : 0u);
}

/// <summary>
/// How a texture's stored channel values are read, the number-format field of an
/// <see cref="AgcTextureDescriptor"/>. The values are the graphics-processor channel-type encoding.
/// </summary>
public enum AgcTextureChannelType : uint
{
    /// <summary>Unsigned, mapped to 0..1.</summary>
    UNorm = 0,
    /// <summary>Signed, mapped to -1..1.</summary>
    SNorm = 1,
    /// <summary>Unsigned scaled to its integer range as a float.</summary>
    UScaled = 2,
    /// <summary>Signed scaled to its integer range as a float.</summary>
    SScaled = 3,
    /// <summary>Unsigned integer.</summary>
    UInt = 4,
    /// <summary>Signed integer.</summary>
    SInt = 5,
    /// <summary>Floating point.</summary>
    Float = 7,
    /// <summary>Unsigned, mapped to 0..1, read as sRGB.</summary>
    Srgb = 9,
}
