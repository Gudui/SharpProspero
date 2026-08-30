// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Kekcall dispatch interface. Invokes kernel extension calls through the getppid
/// syscall trampoline with a magic calling convention. Each call number dispatches to
/// a specific kernel operation installed by the kernel payload.
/// </summary>
public static unsafe partial class PayloadKekcall
{
    /// <summary>
    /// Invokes a kekcall with the given call number and up to 6 arguments. The call
    /// routes through <c>getppid</c> with a magic marker in the first argument that
    /// distinguishes it from a normal getppid call.
    /// </summary>
    /// <param name="callNr">The kekcall number (0 = read/write dbregs, 1 = read dbregs,
    /// 2 = write dbregs, 3 = rdmsr, etc.).</param>
    /// <param name="arg1">First argument.</param>
    /// <param name="arg2">Second argument.</param>
    /// <param name="arg3">Third argument.</param>
    /// <param name="arg4">Fourth argument.</param>
    /// <param name="arg5">Fifth argument.</param>
    /// <param name="arg6">Sixth argument.</param>
    /// <returns>The return value from the kernel function.</returns>
    public static long Invoke(int callNr, long arg1 = 0, long arg2 = 0, long arg3 = 0,
        long arg4 = 0, long arg5 = 0, long arg6 = 0)
    {
        return CrtKekcall(callNr, arg1, arg2, arg3, arg4, arg5, arg6);
    }

    // NOTE: __sp_kekcall is NOT emitted by PayloadCrtEmitter. The kekcall mechanism
    // requires a dedicated CRT trampoline that packs (callNr << 32) | SYS_getppid into
    // the full 64-bit RAX register before dispatching through ptr_syscall. The standard
    // __sp_crt_syscall gateway cannot be used because it takes a 32-bit sysno (zero-extended
    // to RAX), which cannot encode the kekcall number in the upper 32 bits. A future CRT
    // emitter update must add the __sp_kekcall symbol for this P/Invoke to resolve at link
    // time.
    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_kekcall")]
    private static partial long CrtKekcall(int nr, long a1, long a2, long a3, long a4, long a5, long a6);
}
