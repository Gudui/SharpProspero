// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Shared area structure for communication between the kernel payload and userspace.
/// Layout matches the shared-area structure used for kernel-userspace communication.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KstuffSharedArea
{
    /// <summary>Area version identifier.</summary>
    public uint Version;

    /// <summary>Flags indicating which features are active.</summary>
    public uint Flags;

    /// <summary>Number of registered fake keys.</summary>
    public int FakeKeyCount;

    /// <summary>Maximum number of fake keys the area can hold.</summary>
    public int FakeKeyCapacity;

    /// <summary>Offset to the fake key array from the start of the area.</summary>
    public uint FakeKeyOffset;

    /// <summary>Performance metrics and status counters.</summary>
    public fixed ulong Metrics[16];
}
