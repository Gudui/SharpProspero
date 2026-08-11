// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload;

/// <summary>
/// Walks the kernel's process list and modifies process credentials and directory jails through
/// the pipe-based read/write primitive. Every address and field offset comes from
/// <see cref="KernelOffsets1001"/>.
/// </summary>
public static unsafe class PayloadKernel
{
    private const int MaxComm = 17;

    /// <summary>
    /// Walks the process list starting at <c>allproc</c> and returns the kernel address of the first
    /// process whose <c>p_comm</c> matches the given name, or zero if none is found.
    /// </summary>
    public static ulong FindProcessByName(PayloadKernelIo io, byte* name, int nameLength)
    {
        ulong proc = io.ReadU64(KernelOffsets1001.Allproc);
        byte* comm = stackalloc byte[MaxComm];
        while (proc != 0)
        {
            io.Read(proc + (ulong)KernelOffsets1001.ProcComm, comm, MaxComm);
            if (MatchName(comm, name, nameLength))
                return proc;
            proc = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcList);
        }
        return 0;
    }

    /// <summary>
    /// Walks the process list starting at <c>allproc</c> and returns the kernel address of the process
    /// with the given <paramref name="pid"/>, or zero if none is found.
    /// </summary>
    public static ulong FindProcessByPid(PayloadKernelIo io, int pid)
    {
        ulong proc = io.ReadU64(KernelOffsets1001.Allproc);
        while (proc != 0)
        {
            int p = (int)io.ReadU32(proc + (ulong)KernelOffsets1001.ProcPid);
            if (p == pid)
                return proc;
            proc = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcList);
        }
        return 0;
    }

    /// <summary>
    /// Removes the filesystem jail from a process by pointing its root and jail directory vnodes
    /// to the kernel's own root vnode.
    /// </summary>
    public static void RemoveJail(PayloadKernelIo io, ulong proc)
    {
        ulong rootvnode = io.ReadU64(KernelOffsets1001.Rootvnode);
        ulong filedesc = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcFd);
        io.WriteU64(filedesc + (ulong)KernelOffsets1001.FdRdir, rootvnode);
        io.WriteU64(filedesc + (ulong)KernelOffsets1001.FdJdir, rootvnode);
    }

    /// <summary>
    /// Sets a process's credential to root with no prison and full authorization and capabilities.
    /// </summary>
    public static void EscalateCredentials(PayloadKernelIo io, ulong proc)
    {
        ulong ucred = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcUcred);
        io.WriteU32(ucred + (ulong)KernelOffsets1001.UcredUid, 0);
        io.WriteU32(ucred + (ulong)KernelOffsets1001.UcredRuid, 0);
        io.WriteU32(ucred + (ulong)KernelOffsets1001.UcredSvuid, 0);
        io.WriteU32(ucred + (ulong)KernelOffsets1001.UcredRgid, 0);
        io.WriteU32(ucred + (ulong)KernelOffsets1001.UcredSvgid, 0);
        io.WriteU64(ucred + (ulong)KernelOffsets1001.UcredPrison, KernelOffsets1001.Prison0);
        io.WriteU64(ucred + (ulong)KernelOffsets1001.UcredSceAuthId, 0xFFFF_FFFF_FFFF_FFFF);
        io.WriteU64(ucred + (ulong)KernelOffsets1001.UcredSceCaps, 0xFFFF_FFFF_FFFF_FFFF);
        io.WriteU64(ucred + (ulong)KernelOffsets1001.UcredSceCaps + 8, 0xFFFF_FFFF_FFFF_FFFF);
    }

    private static bool MatchName(byte* comm, byte* name, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (comm[i] != name[i])
                return false;
        }
        return comm[length] == 0;
    }
}
