// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Kernel sysent table manipulation. Reads, copies, and patches system call entries
/// and the sysentvec structure.
/// </summary>
public static unsafe class KernelSysent
{
    /// <summary>Size of one sysent entry (16 bytes: function pointer + argc).</summary>
    public const int SysentSize = 16;

    /// <summary>
    /// Reads the function pointer from a sysent entry for the given syscall number.
    /// </summary>
    public static ulong ReadSysentFunction(PayloadKernelIo io, ulong sysentsBase, int syscallNr)
    {
        return io.ReadU64(sysentsBase + (ulong)(syscallNr * SysentSize));
    }

    /// <summary>
    /// Writes a new function pointer to a sysent entry.
    /// </summary>
    public static void WriteSysentFunction(PayloadKernelIo io, ulong sysentsBase,
        int syscallNr, ulong funcAddr)
    {
        io.WriteU64(sysentsBase + (ulong)(syscallNr * SysentSize), funcAddr);
    }

    /// <summary>
    /// Reads the sv_flags field from a sysentvec structure.
    /// </summary>
    public static uint ReadSvFlags(PayloadKernelIo io, ulong sysentvecAddr)
    {
        return io.ReadU32(sysentvecAddr + 14);
    }

    /// <summary>
    /// Writes the sv_flags field of a sysentvec structure. Used to temporarily disable
    /// kernel patches by setting sv_flags to <c>0xFFFF</c> (max syscall = disabled) or
    /// restore them to <c>0xDEB7</c>.
    /// </summary>
    public static void WriteSvFlags(PayloadKernelIo io, ulong sysentvecAddr, uint flags)
    {
        io.WriteU32(sysentvecAddr + 14, flags);
    }

    /// <summary>Disable value for sv_flags (max syscall number = disabled).</summary>
    public const uint SvFlagsDisabled = 0xFFFF;

    /// <summary>Enabled value for sv_flags.</summary>
    public const uint SvFlagsEnabled = 0xDEB7;
}
