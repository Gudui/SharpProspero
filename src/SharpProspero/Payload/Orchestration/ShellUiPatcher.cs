// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Payload.Elf;
using SharpProspero.Payload.Kernel;
using SharpProspero.Payload.Process;

namespace SharpProspero.Payload.Orchestration;

/// <summary>
/// ShellUI trophy availability patcher. Finds the SceShellUI process, locates the
/// trophy-server-availability check functions, and patches them to always return
/// "unavailable" so the trophy list loads without a network connection.
/// </summary>
public static unsafe class PayloadShellUiPatcher
{
    /// <summary>NID for <c>sceNpTrophySystemIsServerAvailable</c>.</summary>
    public static readonly ulong NidTrophyServerAvail =
        PayloadNid.ComputeRaw("sceNpTrophySystemIsServerAvailable"u8);

    /// <summary>NID for <c>sceNpTrophy2SystemIsServerAvailable</c>.</summary>
    public static readonly ulong NidTrophy2ServerAvail =
        PayloadNid.ComputeRaw("sceNpTrophy2SystemIsServerAvailable"u8);

    /// <summary>
    /// Patches the trophy server availability checks in SceShellUI to always return
    /// false (unavailable). This allows the trophy list to render offline.
    /// </summary>
    /// <param name="io">Kernel I/O for reading process structures.</param>
    /// <param name="shellUiPid">PID of SceShellUI.</param>
    /// <returns><see langword="true"/> if the patches were applied.</returns>
    public static bool PatchTrophyChecks(PayloadKernelIo io, int shellUiPid)
    {
        ulong proc = PayloadKernel.WalkAllprocForPid(io, shellUiPid);
        if (proc == 0) return false;

        // Find the trophy module in SceShellUI's loaded module list.
        ulong trophyBase = PayloadHijacker.FindModuleBase(io, proc,
            "libSceNpTrophy2.sprx\0"u8, 0x3A8);
        if (trophyBase == 0) return false;

        // Resolve the two trophy availability functions by NID.
        ulong fn1 = PayloadHijacker.ResolveByNid(io, trophyBase, NidTrophyServerAvail, shellUiPid);
        ulong fn2 = PayloadHijacker.ResolveByNid(io, trophyBase, NidTrophy2ServerAvail, shellUiPid);

        // Patch both to return 0 (xor eax,eax; ret = 31 C0 C3).
        byte* patch = stackalloc byte[] { 0x31, 0xC0, 0xC3 };
        bool ok = true;
        if (fn1 != 0) ok &= PayloadProcessMemory.Write(shellUiPid, patch, (nint)fn1, 3) == 0;
        if (fn2 != 0) ok &= PayloadProcessMemory.Write(shellUiPid, patch, (nint)fn2, 3) == 0;

        return ok;
    }
}
