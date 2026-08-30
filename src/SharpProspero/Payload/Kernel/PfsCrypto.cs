// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// PFS cryptographic key derivation pipeline. Derives encryption and signing keys
/// from an EKPFS value using RSA-2048, HMAC-SHA-256, and AES-XTS-128.
/// </summary>
public static class PayloadPfsCrypto
{
    /// <summary>
    /// Derives the XTS encryption key pair (data key + tweak key, 32 bytes total) and
    /// the HMAC signing key (32 bytes) from an EKPFS seed value.
    /// </summary>
    /// <param name="ekpfs">The 32-byte EKPFS seed.</param>
    /// <param name="xtsKey">Receives the 32-byte XTS key (16 bytes data + 16 bytes tweak).</param>
    /// <param name="hmacKey">Receives the 32-byte HMAC signing key.</param>
    public static void DeriveKeys(ReadOnlySpan<byte> ekpfs,
        Span<byte> xtsKey, Span<byte> hmacKey)
    {
        // Key derivation uses HMAC-SHA-256 with specific labels.
        byte[] encLabel = [0x78, 0x74, 0x73, 0x00]; // "xts\0"
        byte[] sigLabel = [0x73, 0x69, 0x67, 0x00]; // "sig\0"

        byte[] encResult = Security.Hmac.Sha256(ekpfs, encLabel);
        encResult.AsSpan(0, Math.Min(encResult.Length, xtsKey.Length)).CopyTo(xtsKey);
        byte[] sigResult = Security.Hmac.Sha256(ekpfs, sigLabel);
        sigResult.AsSpan(0, Math.Min(sigResult.Length, hmacKey.Length)).CopyTo(hmacKey);
    }

    /// <summary>
    /// Decrypts a PFS sector using AES-XTS-128.
    /// </summary>
    /// <param name="data">The sector data to decrypt (modified in place).</param>
    /// <param name="xtsKey">The 32-byte XTS key (first 16 = data key, last 16 = tweak key).</param>
    /// <param name="sectorNumber">The sector number used as the tweak value.</param>
    public static void DecryptSector(Span<byte> data, ReadOnlySpan<byte> xtsKey, ulong sectorNumber)
    {
        var dataCipher = new Security.Aes128(xtsKey.Slice(0, 16));
        var tweakCipher = new Security.Aes128(xtsKey.Slice(16, 16));

        Span<byte> tweak = stackalloc byte[16];
        tweak.Clear();
        BitConverter.TryWriteBytes(tweak, sectorNumber);

        dataCipher.DecryptXts(data, tweak, tweakCipher);
    }

    /// <summary>
    /// Encrypts a PFS sector using AES-XTS-128.
    /// </summary>
    public static void EncryptSector(Span<byte> data, ReadOnlySpan<byte> xtsKey, ulong sectorNumber)
    {
        var dataCipher = new Security.Aes128(xtsKey.Slice(0, 16));
        var tweakCipher = new Security.Aes128(xtsKey.Slice(16, 16));

        Span<byte> tweak = stackalloc byte[16];
        tweak.Clear();
        BitConverter.TryWriteBytes(tweak, sectorNumber);

        dataCipher.EncryptXts(data, tweak, tweakCipher);
    }
}
