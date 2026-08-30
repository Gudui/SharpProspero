// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Kernel function caller. Calls an arbitrary kernel function from userspace by
/// temporarily redirecting <c>sys_getpid</c> through the sysent table, invoking it,
/// and restoring the original entry. Requires elevated privileges and a writable
/// sysent table.
/// </summary>
public static unsafe partial class PayloadKfncall
{
    /// <summary>
    /// Calls an arbitrary kernel function at <paramref name="kfnAddr"/>. Arguments are
    /// passed by writing them to the calling thread's <c>td_frame</c> register save area
    /// before the syscall, so the kernel function receives them in the standard AMD64
    /// calling convention (RDI, RSI, RDX, RCX, R8, R9).
    /// </summary>
    /// <returns>The return value from the kernel function.</returns>
    public static ulong Call(PayloadKernelIo io, ulong sysentsAddr, ulong kfnAddr,
        ulong arg1 = 0, ulong arg2 = 0, ulong arg3 = 0,
        ulong arg4 = 0, ulong arg5 = 0, ulong arg6 = 0)
    {
        // Save the original sys_getpid sysent entry (syscall 20).
        ulong origFn = io.ReadU64(sysentsAddr + 20 * 16);
        ulong origArgc = io.ReadU64(sysentsAddr + 20 * 16 + 8);

        // Redirect sys_getpid to the target function with 6 args.
        io.WriteU64(sysentsAddr + 20 * 16, kfnAddr);
        io.WriteU64(sysentsAddr + 20 * 16 + 8, 6);

        // Use the CRT syscall trampoline to pass arguments through the syscall ABI.
        // The trampoline sets RDI=arg1, RSI=arg2, RDX=arg3, RCX(R10)=arg4, R8=arg5, R9=arg6.
        ulong result = (ulong)CrtSyscallWithArgs(20, (long)arg1, (long)arg2,
            (long)arg3, (long)arg4, (long)arg5, (long)arg6);

        // Restore the original sysent entry.
        io.WriteU64(sysentsAddr + 20 * 16, origFn);
        io.WriteU64(sysentsAddr + 20 * 16 + 8, origArgc);

        return result;
    }

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_crt_syscall")]
    private static partial long CrtSyscallWithArgs(int sysno, long a1, long a2,
        long a3, long a4, long a5, long a6);
}
