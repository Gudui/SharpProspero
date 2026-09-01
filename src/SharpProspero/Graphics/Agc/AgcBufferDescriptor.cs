// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// A buffer resource descriptor: the four 32-bit words that tell a shader where a buffer is, how its
/// records are sized, and how to read them. A vertex program reads its vertices through one of these,
/// and its transform matrices through a constant-buffer one. Build the descriptor, then write its four
/// words into the shader's user data so the program can reach the buffer.
/// </summary>
public readonly struct AgcBufferDescriptor
{
    /// <summary>The four descriptor words, in order.</summary>
    public readonly uint Word0, Word1, Word2, Word3;

    private AgcBufferDescriptor(uint w0, uint w1, uint w2, uint w3)
    {
        Word0 = w0; Word1 = w1; Word2 = w2; Word3 = w3;
    }

    /// <summary>Writes the four words into <paramref name="dest"/> (length at least 4).</summary>
    public void WriteTo(Span<uint> dest)
    {
        dest[0] = Word0; dest[1] = Word1; dest[2] = Word2; dest[3] = Word3;
    }

    /// <summary>
    /// Writes this four-dword descriptor as consecutive shader user-data registers.
    /// <paramref name="dwordOffset"/> is relative to the supplied shader-stage user-data base.
    /// </summary>
    /// <returns>The four registers written.</returns>
    public int WriteShaderRegisters(Span<CxRegister> destination, uint userDataBaseOffset, int dwordOffset)
    {
        if (destination.Length < 4) throw new ArgumentException("Four destination registers are required.", nameof(destination));
        if (dwordOffset < 0 || userDataBaseOffset + (uint)dwordOffset + 3 > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dwordOffset));

        uint first = userDataBaseOffset + (uint)dwordOffset;
        destination[0] = new CxRegister((ushort)(first + 0), Word0);
        destination[1] = new CxRegister((ushort)(first + 1), Word1);
        destination[2] = new CxRegister((ushort)(first + 2), Word2);
        destination[3] = new CxRegister((ushort)(first + 3), Word3);
        return 4;
    }

    // Swizzle mapping: 3 bits per channel (0=0, 1=1, 4=X, 5=Y, 6=Z, 7=W).
    private const uint SwizzleX001 = 4 | (0 << 3) | (0 << 6) | (1 << 9);  // 0x204
    private const uint SwizzleXYZW = 4 | (5 << 3) | (6 << 6) | (7 << 9);  // 0xFAC

    // RDNA2 / GFX10.3 buffer resource descriptor format encodings (Word3).
    // In RDNA2, NUM_FORMAT is 3 bits at [14:12] and DATA_FORMAT is 6 bits at [20:15].
    private const uint NumFormatUnorm = 0;
    private const uint NumFormatFloat = 7;
    private const uint DataFormat8 = 1;               // 8-bit unsigned element (BUF_DATA_FORMAT_8)
    private const uint DataFormat32 = 4;              // 32-bit element (BUF_DATA_FORMAT_32, 4 bytes)
    private const uint DataFormat32_32_32_32 = 14;    // four 32-bit floats (BUF_DATA_FORMAT_32_32_32_32, 16 bytes)

    /// <summary>
    /// A structured buffer of <paramref name="elementCount"/> records, each <paramref name="strideInBytes"/>
    /// bytes, at <paramref name="address"/>. A vertex program reads a vertex from one of these by index.
    /// </summary>
    public static AgcBufferDescriptor Structured(ulong address, uint strideInBytes, uint elementCount)
    {
        if (strideInBytes >= 1u << 14) throw new ArgumentOutOfRangeException(nameof(strideInBytes), "The stride must be less than 16384 bytes.");
        uint word0 = (uint)(address & 0xFFFFFFFF);
        uint word1 = (uint)((address >> 32) & 0xFFFF) | ((strideInBytes & 0x3FFF) << 16);
        uint word3 = SwizzleX001 | (NumFormatUnorm << 12) | (DataFormat8 << 15) | (3u << 28);
        return new AgcBufferDescriptor(word0, word1, elementCount, word3);
    }

    /// <summary>
    /// A constant buffer of <paramref name="sizeInBytes"/> bytes at <paramref name="address"/>. A vertex
    /// program reads its transform matrices from one of these.
    /// </summary>
    public static AgcBufferDescriptor Constant(ulong address, uint sizeInBytes)
    {
        uint word0 = (uint)(address & 0xFFFFFFFF);
        uint word1 = (uint)((address >> 32) & 0xFFFF);
        uint word3 = SwizzleXYZW | (NumFormatFloat << 12) | (DataFormat32_32_32_32 << 15) | (3u << 28);
        return new AgcBufferDescriptor(word0, word1, sizeInBytes, word3);
    }
}
