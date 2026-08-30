// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// One entry in the fake-key registry within the shared area.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FakeKeyEntry
{
    /// <summary>The content identifier this key applies to.</summary>
    public fixed byte ContentId[48];

    /// <summary>The encryption key (32 bytes).</summary>
    public fixed byte EncryptionKey[32];

    /// <summary>The signing key (32 bytes).</summary>
    public fixed byte SigningKey[32];

    /// <summary>Key type flags.</summary>
    public uint Flags;

    /// <summary>Padding to align the entry.</summary>
    public uint Padding;
}

/// <summary>
/// Reads the shared area and manages the fake-key registry. The shared area resides
/// at a kernel address discovered through the kernel data section.
/// </summary>
public static unsafe class PayloadFakeKeys
{
    /// <summary>
    /// Reads the shared area header from the given kernel address.
    /// </summary>
    public static KstuffSharedArea ReadSharedArea(PayloadKernelIo io, ulong sharedAreaAddr)
    {
        KstuffSharedArea area;
        io.Read(sharedAreaAddr, (byte*)&area, sizeof(KstuffSharedArea));
        return area;
    }

    /// <summary>
    /// Writes a fake key entry into the shared area's key registry.
    /// </summary>
    /// <param name="io">Kernel I/O for writing to the shared area.</param>
    /// <param name="sharedAreaAddr">Base address of the shared area.</param>
    /// <param name="index">Index of the key entry to write.</param>
    /// <param name="entry">The key entry to write.</param>
    public static void WriteKey(PayloadKernelIo io, ulong sharedAreaAddr,
        int index, FakeKeyEntry* entry)
    {
        KstuffSharedArea area = ReadSharedArea(io, sharedAreaAddr);
        ulong keyAddr = sharedAreaAddr + area.FakeKeyOffset + (ulong)(index * sizeof(FakeKeyEntry));
        io.Write(keyAddr, (byte*)entry, sizeof(FakeKeyEntry));
    }

    /// <summary>
    /// Reads a fake key entry from the shared area.
    /// </summary>
    public static FakeKeyEntry ReadKey(PayloadKernelIo io, ulong sharedAreaAddr, int index)
    {
        KstuffSharedArea area = ReadSharedArea(io, sharedAreaAddr);
        ulong keyAddr = sharedAreaAddr + area.FakeKeyOffset + (ulong)(index * sizeof(FakeKeyEntry));
        FakeKeyEntry entry;
        io.Read(keyAddr, (byte*)&entry, sizeof(FakeKeyEntry));
        return entry;
    }

    /// <summary>
    /// Sets the fake key count in the shared area header.
    /// </summary>
    public static void SetKeyCount(PayloadKernelIo io, ulong sharedAreaAddr, int count)
    {
        io.WriteU32(sharedAreaAddr + 8, (uint)count);
    }
}
