// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// MSR (Model-Specific Register) read through kekcall.
/// </summary>
public static class KernelMsr
{
    /// <summary>
    /// Reads a model-specific register by its address.
    /// </summary>
    /// <param name="msrAddr">The MSR address (e.g., 0xC0000080 for EFER).</param>
    /// <returns>The 64-bit MSR value.</returns>
    public static ulong Read(uint msrAddr)
    {
        return (ulong)PayloadKekcall.Invoke(3, msrAddr);
    }

    /// <summary>IA32_EFER — Extended Feature Enable Register.</summary>
    public const uint Efer = 0xC0000080;

    /// <summary>IA32_LSTAR — Long-mode SYSCALL target.</summary>
    public const uint Lstar = 0xC0000082;

    /// <summary>IA32_KERNEL_GS_BASE — Kernel GS base.</summary>
    public const uint KernelGsBase = 0xC0000102;
}
