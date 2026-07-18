// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SharpProspero.Security;

/// <summary>
/// The SHA-1 digest, a 20-byte hash. It is offered for checking data against the many manifests and
/// tools that still publish SHA-1 sums; prefer <see cref="Sha256"/> when a choice is available.
/// </summary>
public sealed class Sha1 : BlockHashAlgorithm
{
    private uint _h0 = 0x67452301, _h1 = 0xEFCDAB89, _h2 = 0x98BADCFE, _h3 = 0x10325476, _h4 = 0xC3D2E1F0;

    /// <inheritdoc/>
    public override int HashSize => 20;

    /// <inheritdoc/>
    protected override bool LengthIsBigEndian => true;

    /// <inheritdoc/>
    protected override void ProcessBlock(ReadOnlySpan<byte> block)
    {
        Span<uint> w = stackalloc uint[80];
        for (int i = 0; i < 16; i++)
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);
        for (int i = 16; i < 80; i++)
            w[i] = BitOperations.RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

        uint a = _h0, b = _h1, c = _h2, d = _h3, e = _h4;
        for (int i = 0; i < 80; i++)
        {
            uint f, k;
            if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
            else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
            else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
            else { f = b ^ c ^ d; k = 0xCA62C1D6; }

            uint temp = BitOperations.RotateLeft(a, 5) + f + e + k + w[i];
            e = d; d = c; c = BitOperations.RotateLeft(b, 30); b = a; a = temp;
        }

        _h0 += a; _h1 += b; _h2 += c; _h3 += d; _h4 += e;
    }

    /// <inheritdoc/>
    protected override void WriteDigest(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, _h0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], _h1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], _h2);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], _h3);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..], _h4);
    }

    /// <summary>Computes the SHA-1 digest of <paramref name="data"/>.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var sha = new Sha1();
        sha.Update(data);
        return sha.Finish();
    }

    /// <summary>Computes the SHA-1 digest of <paramref name="data"/> as a lowercase hexadecimal string.</summary>
    public static string HashHex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(Hash(data));

    /// <summary>Computes the SHA-1 digest of the file at <paramref name="path"/>.</summary>
    public static byte[] HashFile(string path) => new Sha1().ComputeFile(path);

    /// <summary>Computes the SHA-1 digest of the file at <paramref name="path"/> as a lowercase hexadecimal string.</summary>
    public static string HashFileHex(string path) => Convert.ToHexStringLower(HashFile(path));
}
