// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Payload.Kernel;
using System;

namespace SharpProspero.Payload.Bypass;

/// <summary>
/// FSELF (Fake-Signed ELF) bypass. Installs debug register watchpoints on the kernel's
/// SELF verification path to intercept the <c>verifyHeader</c> and
/// <c>sceSblAuthMgrIsLoadable2</c> checks, allowing unsigned SELF modules to load.
/// </summary>
public static unsafe class PayloadFselfBypass
{
    /// <summary>
    /// Installs the FSELF bypass by setting DR0 on the <c>sceSblAuthMgrIsLoadable2</c>
    /// call site and configuring DR7 for execution breakpoints.
    /// </summary>
    /// <param name="io">Kernel I/O.</param>
    /// <param name="isLoadable2Addr">Kernel address of <c>sceSblAuthMgrIsLoadable2</c>.</param>
    /// <param name="verifyHeaderLr">Return address within <c>verifyHeader</c>.</param>
    /// <returns><see langword="true"/> if the bypass was installed.</returns>
    public static bool Install(PayloadKernelIo io, ulong isLoadable2Addr, ulong verifyHeaderLr)
    {
        Span<ulong> dbregs = stackalloc ulong[8];
        KernelDebugRegs.Read(dbregs);

        dbregs[0] = isLoadable2Addr;  // DR0 = break on IsLoadable2
        dbregs[1] = verifyHeaderLr;   // DR1 = break on verifyHeader return
        dbregs[7] = KernelDebugRegs.Dr7L0 | KernelDebugRegs.Dr7L1 |
                     (KernelDebugRegs.Dr7CondExec << 16) | (KernelDebugRegs.Dr7Len1 << 18) |
                     (KernelDebugRegs.Dr7CondExec << 20) | (KernelDebugRegs.Dr7Len1 << 22);

        KernelDebugRegs.Write(dbregs);
        return true;
    }

    /// <summary>
    /// Removes the FSELF bypass by clearing the debug registers.
    /// </summary>
    public static void Remove()
    {
        Span<ulong> dbregs = stackalloc ulong[8];
        dbregs.Clear();
        KernelDebugRegs.Write(dbregs);
    }
}
