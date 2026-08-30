// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Kernel-level NPDRM and PFS structures for intercepting the crypto bypass path
/// at the kernel/secure processor boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NpdrmRifRecord
{
    /// <summary>RIF version.</summary>
    public ushort Version;

    /// <summary>RIF format type (1 = retail, 2 = debug).</summary>
    public ushort Type;

    /// <summary>License flags.</summary>
    public ulong Flags;

    /// <summary>Content identifier (48 bytes).</summary>
    public fixed byte ContentId[48];

    /// <summary>Encrypted secret key (16 bytes).</summary>
    public fixed byte Secret[16];

    /// <summary>Encrypted content key (16 bytes).</summary>
    public fixed byte ContentKey[16];

    /// <summary>Remaining fields up to 1024 bytes.</summary>
    public fixed byte Reserved[880];
}

/// <summary>
/// CCP (Crypto Coprocessor) message structure for hardware-accelerated crypto
/// operations. The bypass intercepts these messages to replace hardware key handles
/// with inline fake keys.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CcpMessage
{
    /// <summary>Operation code.</summary>
    public uint Opcode;

    /// <summary>Status.</summary>
    public uint Status;

    /// <summary>Data length.</summary>
    public uint DataLength;

    /// <summary>Key handle or inline key slot.</summary>
    public uint KeyHandle;

    /// <summary>Source physical address.</summary>
    public ulong SourceAddr;

    /// <summary>Destination physical address.</summary>
    public ulong DestAddr;

    /// <summary>Key physical address (for inline keys).</summary>
    public ulong KeyAddr;

    /// <summary>IV physical address.</summary>
    public ulong IvAddr;
}
