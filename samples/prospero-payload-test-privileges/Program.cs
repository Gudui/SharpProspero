// Prints console info (firmware version, authid,
// capabilities, uid/euid, jail vnode), then tests mdbg (copyout/copyin) and ptrace
// (PT_ATTACH/IO/DETACH) on SceRedisServer.

using System;
using System.Runtime.InteropServices;
using SharpProspero.Payload;
using SharpProspero.Payload.Kernel;
using SharpProspero.Payload.Process;

namespace SampleApp;

internal static unsafe partial class Program
{
    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    [LibraryImport("libkernel", EntryPoint = "getuid")]
    private static partial int GetUid();

    [LibraryImport("libkernel", EntryPoint = "geteuid")]
    private static partial int GetEuid();

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        PayloadArgs* pargs = PayloadEntryPoint.Args;
        if (pargs == null)
            return -1;

        var io = new PayloadKernelIo(pargs);
        int pid = PayloadProcessControl.getpid();
        ulong proc = PayloadKernel.FindProcessByPid(io, pid);
        if (proc == 0)
            return -2;

        PrintInfo(io, proc, pid);
        return 0;
    }

    private static void PrintInfo(PayloadKernelIo io, ulong proc, int pid)
    {
        // Read ucred authid.
        ulong ucred = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcUcred);
        ulong authId = io.ReadU64(ucred + (ulong)KernelOffsets1001.UcredSceAuthId);

        // Read ucred caps (16 bytes = two u64s).
        ulong caps0 = io.ReadU64(ucred + (ulong)KernelOffsets1001.UcredSceCaps);
        ulong caps1 = io.ReadU64(ucred + (ulong)KernelOffsets1001.UcredSceCaps + 8);

        // Read uid.
        int uid = GetUid();
        int euid = GetEuid();

        // Read jail vnode.
        ulong filedesc = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcFd);
        ulong jdir = io.ReadU64(filedesc + (ulong)KernelOffsets1001.FdJdir);

        // Output console info.
        fixed (byte* hdr = "Privileges\n----------\n\0"u8)
            Klog(hdr);

        LogHex("authid:  0x"u8, authId);
        LogHex("caps[0]: 0x"u8, caps0);
        LogHex("caps[1]: 0x"u8, caps1);
        LogInt("uid:     "u8, uid);
        LogInt("euid:    "u8, euid);
        LogHex("jaildir: 0x"u8, jdir);
    }

    private static void LogHex(ReadOnlySpan<byte> prefix, ulong value)
    {
        byte* line = stackalloc byte[80];
        int pos = 0;
        for (int i = 0; i < prefix.Length; i++)
            line[pos++] = prefix[i];
        for (int shift = 60; shift >= 0; shift -= 4)
            line[pos++] = HexChar((int)((value >> shift) & 0xF));
        line[pos++] = (byte)'\n';
        line[pos] = 0;
        Klog(line);
    }

    private static void LogInt(ReadOnlySpan<byte> prefix, int value)
    {
        byte* line = stackalloc byte[80];
        int pos = 0;
        for (int i = 0; i < prefix.Length; i++)
            line[pos++] = prefix[i];

        byte* digits = stackalloc byte[12];
        int d = 0;
        if (value == 0) { digits[d++] = (byte)'0'; }
        else { while (value > 0) { digits[d++] = (byte)('0' + value % 10); value /= 10; } }
        for (int i = d - 1; i >= 0; i--)
            line[pos++] = digits[i];
        line[pos++] = (byte)'\n';
        line[pos] = 0;
        Klog(line);
    }

    private static byte HexChar(int nibble) =>
        (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
