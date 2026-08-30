// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Payload.Kernel;
using System;

namespace SharpProspero.Payload.Bypass;

/// <summary>
/// FPKG (Fake Package) crypto bypass. Intercepts PFS key derivation to substitute
/// custom encryption and signing keys, allowing fake-signed packages to be mounted
/// and read.
/// </summary>
public static unsafe class PayloadFpkgBypass
{
    /// <summary>
    /// Registers a set of fake PFS keys for a content identifier. When the kernel
    /// attempts to derive keys for this content, the registered keys are used instead
    /// of the real ones.
    /// </summary>
    /// <param name="io">Kernel I/O.</param>
    /// <param name="sharedAreaAddr">Address of the shared area.</param>
    /// <param name="contentId">The content identifier (48 bytes).</param>
    /// <param name="encKey">The 32-byte XTS encryption key.</param>
    /// <param name="sigKey">The 32-byte HMAC signing key.</param>
    /// <returns>The index of the registered key, or -1 on failure.</returns>
    public static int RegisterKeys(PayloadKernelIo io, ulong sharedAreaAddr,
        ReadOnlySpan<byte> contentId, ReadOnlySpan<byte> encKey, ReadOnlySpan<byte> sigKey)
    {
        var area = PayloadFakeKeys.ReadSharedArea(io, sharedAreaAddr);
        int idx = area.FakeKeyCount;
        if (idx >= area.FakeKeyCapacity) return -1;

        FakeKeyEntry entry = default;
        fixed (byte* cid = contentId)
        {
            int copyLen = Math.Min(contentId.Length, 48);
            for (int i = 0; i < copyLen; i++) entry.ContentId[i] = cid[i];
        }
        fixed (byte* ek = encKey)
        {
            int copyLen = Math.Min(encKey.Length, 32);
            for (int i = 0; i < copyLen; i++) entry.EncryptionKey[i] = ek[i];
        }
        fixed (byte* sk = sigKey)
        {
            int copyLen = Math.Min(sigKey.Length, 32);
            for (int i = 0; i < copyLen; i++) entry.SigningKey[i] = sk[i];
        }
        entry.Flags = 1;

        PayloadFakeKeys.WriteKey(io, sharedAreaAddr, idx, &entry);
        PayloadFakeKeys.SetKeyCount(io, sharedAreaAddr, idx + 1);

        return idx;
    }

    /// <summary>
    /// Derives and registers PFS keys from an EKPFS seed for a content identifier.
    /// </summary>
    public static int DeriveAndRegister(PayloadKernelIo io, ulong sharedAreaAddr,
        ReadOnlySpan<byte> contentId, ReadOnlySpan<byte> ekpfs)
    {
        Span<byte> xtsKey = stackalloc byte[32];
        Span<byte> hmacKey = stackalloc byte[32];
        PayloadPfsCrypto.DeriveKeys(ekpfs, xtsKey, hmacKey);
        return RegisterKeys(io, sharedAreaAddr, contentId, xtsKey, hmacKey);
    }
}
