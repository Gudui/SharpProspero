// Daemon payload: self-elevates, then listens on a TCP socket for credential escalation
// requests from application modules. Each request carries a process identifier in a fixed
// binary layout; the daemon applies the 11-field credential and filesystem write sequence
// through CRT-emitted per-field accessors, replies with the outcome, and continues.
//
// The kernel addresses and structure offsets are for firmware 10.01. All kernel access
// routes through the CRT-emitted accessors, which share a single pipe-primitive call chain
// initialized once during CRT startup from the loader's payload_args block.

using System;
using System.Runtime.InteropServices;
using SharpProspero.Payload;

namespace SampleApp;

internal static unsafe partial class Program
{
    // ---- CRT kernel log ----

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    // ---- Process identifier ----

    [SuppressGCTransition]
    [LibraryImport("libc", EntryPoint = "getpid")]
    private static partial int Getpid();

    // ---- Notification ----

    [SuppressGCTransition]
    [LibraryImport("libkernel", EntryPoint = "sceKernelSendNotificationRequest")]
    private static partial int Notify(int device, void* request, nuint size, int blocking);

    // ---- Raw syscall gateway ----
    //
    // All socket and sleep operations route through the CRT-emitted syscall shuffler,
    // which rearranges the C calling convention registers into the FreeBSD syscall ABI and
    // dispatches through the proven getpid+10 gadget. The shuffler handles six register
    // arguments (sysno + five user args) and a seventh from the caller's stack frame.

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_crt_syscall")]
    private static partial long CrtSyscall1(int sysno, long arg1);

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_crt_syscall")]
    private static partial long CrtSyscall2(int sysno, long arg1, long arg2);

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_crt_syscall")]
    private static partial long CrtSyscall3(int sysno, long arg1, long arg2, long arg3);

    [SuppressGCTransition]
    [LibraryImport("libScePosix", EntryPoint = "__sp_crt_syscall")]
    private static partial long CrtSyscall5(int sysno, long arg1, long arg2, long arg3, long arg4, long arg5);

    // ---- FreeBSD syscall numbers ----

    private const int SYS_read       = 3;
    private const int SYS_write      = 4;
    private const int SYS_close      = 6;
    private const int SYS_accept     = 30;
    private const int SYS_socket     = 97;
    private const int SYS_bind       = 104;
    private const int SYS_setsockopt = 105;
    private const int SYS_listen     = 106;
    private const int SYS_nanosleep  = 240;

    // ---- FreeBSD socket constants ----

    private const int AF_INET      = 2;
    private const int SOCK_STREAM  = 1;
    private const int SOL_SOCKET   = 0xFFFF;
    private const int SO_REUSEADDR = 0x0004;

    // ---- Protocol constants ----
    //
    // The wire format is a fixed 0xA10-byte (2576) struct:
    //   +0x00  uint32  magic       0xDEADBEEF on request, cleared on reply
    //   +0x04  int32   cmd         5 = credential escalation
    //   +0x08  int32   pid         the caller's process identifier
    //   +0x0C  int32   ret         0 on request; 0 = success, -1 = failure on reply
    //   +0x10  char[1280]  msg1    reserved (zero)
    //   +0x510 char[1280]  msg2    reserved (zero)

    private const int DaemonPort     = 9069;
    private const int CommandSize    = 0xA10;
    private const uint ExpectedMagic = 0xDEADBEEF;
    private const int EscalationCmd  = 5;

    /// <summary>How many times to retry the allproc walk before giving up on a PID.</summary>
    private const int MaxRetries = 30;

    // ---- Socket and IO wrappers ----

    /// <summary>Reads bytes from a descriptor. Returns the count read, or negative on failure.</summary>
    private static long SysRead(int fd, byte* buf, long count) =>
        CrtSyscall3(SYS_read, fd, (long)(nint)buf, count);

    /// <summary>Writes bytes to a descriptor. Returns the count written, or negative on failure.</summary>
    private static long SysWrite(int fd, byte* buf, long count) =>
        CrtSyscall3(SYS_write, fd, (long)(nint)buf, count);

    /// <summary>Closes a descriptor.</summary>
    private static int SysClose(int fd) => (int)CrtSyscall1(SYS_close, fd);

    /// <summary>Sleeps for one second using the FreeBSD nanosleep syscall.</summary>
    private static void SleepOneSecond()
    {
        long* ts = stackalloc long[4];
        ts[0] = 1;  // tv_sec
        ts[1] = 0;  // tv_nsec
        CrtSyscall2(SYS_nanosleep, (long)(nint)ts, (long)(nint)(ts + 2));
    }

    // ---- Entry point ----

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        Log("unjail: daemon start"u8);

        // 1. Read payload args from the loader. The CRT init function uses this block to
        //    latch the pipe primitive fds and kernel addresses. A null pointer means the
        //    loader did not hand us args, so the pipe primitive is unavailable.
        PayloadArgs* pargs = PayloadEntryPoint.Args;
        if (pargs == null)
        {
            Log("unjail: no payload args"u8);
            SendNotification("unjail: no payload args"u8);
            return -1;
        }
        Log("unjail: args ok"u8);

        // 2. Validate own pid.
        int ownPid = Getpid();
        if (ownPid <= 0)
        {
            Log("unjail: getpid failed"u8);
            SendNotification("unjail: getpid failed"u8);
            return -1;
        }
        Log("unjail: getpid ok"u8);

        // 3. Self-elevate so the pipe primitive stays authorized for subsequent writes.
        PayloadKernel.RaisePrivileges(ownPid);
        Log("unjail: self raised"u8);

        // 4. Set the debug authorization id on ourselves.
        PayloadKernel.SetUcredAuthId(ownPid, 0x4800000000010003);
        Log("unjail: authid set"u8);

        // 5. Cache the root vnode for the lifetime of the daemon. The CRT-emitted accessor
        //    reads the BSS-cached kernel address populated by __sp_kernel_init and dereferences
        //    it via copyout. This pointer is constant for the duration of the console session.
        ulong rootvnode = PayloadKernel.GetRootVnode();
        if (rootvnode == 0)
        {
            Log("unjail: rootvnode read failed"u8);
            SendNotification("unjail: rootvnode read failed"u8);
            return -1;
        }
        Log("unjail: rootvnode cached"u8);

        SendNotification("unjail: daemon ready"u8);
        Log("unjail: daemon ready, entering accept loop"u8);

        // 6. Listen for credential escalation requests on a TCP socket. Each accepted
        //    connection carries a single fixed-size request; the daemon reads it, applies
        //    the credential write if valid, replies with the outcome, and closes.
        TcpAcceptLoop(rootvnode);

        return 0;
    }

    // ---- TCP accept loop ----

    /// <summary>
    /// Binds a TCP socket to the daemon port and accepts connections indefinitely. Each
    /// connection carries a single fixed-size request struct. Valid requests trigger the
    /// 11-field credential escalation on the named process; the reply carries 0 on success,
    /// -1 on failure. The connection is closed after each reply.
    /// </summary>
    private static void TcpAcceptLoop(ulong rootvnode)
    {
        int s = (int)CrtSyscall3(SYS_socket, AF_INET, SOCK_STREAM, 0);
        if (s < 0)
        {
            Log("unjail: socket failed"u8);
            return;
        }

        // Allow immediate rebind after a daemon restart.
        int one = 1;
        CrtSyscall5(SYS_setsockopt, s, SOL_SOCKET, SO_REUSEADDR,
                     (long)(nint)(&one), 4);

        // FreeBSD sockaddr_in: sin_len(1), sin_family(1), sin_port(2 BE),
        // sin_addr(4), sin_zero(8). Total 16 bytes.
        byte* addr = stackalloc byte[16];
        new Span<byte>(addr, 16).Clear();
        addr[0] = 16;                          // sin_len
        addr[1] = (byte)AF_INET;               // sin_family
        addr[2] = (byte)(DaemonPort >> 8);      // sin_port high (big-endian)
        addr[3] = (byte)(DaemonPort & 0xFF);    // sin_port low
        addr[4] = 127; addr[5] = 0; addr[6] = 0; addr[7] = 1;  // sin_addr = 127.0.0.1

        if (CrtSyscall3(SYS_bind, s, (long)(nint)addr, 16) < 0)
        {
            Log("unjail: bind failed"u8);
            SysClose(s);
            return;
        }

        if (CrtSyscall2(SYS_listen, s, 2) < 0)
        {
            Log("unjail: listen failed"u8);
            SysClose(s);
            return;
        }

        Log("unjail: tcp listener ready"u8);

        byte* cmdBuf   = stackalloc byte[CommandSize];
        byte* replyBuf = stackalloc byte[CommandSize];

        while (true)
        {
            int client = (int)CrtSyscall3(SYS_accept, s, 0, 0);
            if (client < 0)
            {
                SleepOneSecond();
                continue;
            }

            // Read exactly CommandSize bytes from the client.
            int total = 0;
            while (total < CommandSize)
            {
                long n = SysRead(client, cmdBuf + total, CommandSize - total);
                if (n <= 0) break;
                total += (int)n;
            }

            // Prepare a zeroed reply.
            new Span<byte>(replyBuf, CommandSize).Clear();

            if (total >= 16)
            {
                uint magic = *(uint*)(cmdBuf + 0);
                int  cmd   = *(int*)(cmdBuf + 4);
                int  pid   = *(int*)(cmdBuf + 8);

                if (magic == ExpectedMagic && cmd == EscalationCmd && pid > 0)
                {
                    bool ok = false;
                    for (int r = 0; r < MaxRetries && !ok; r++)
                        ok = PayloadKernel.JailbreakByPid(pid, rootvnode);

                    *(int*)(replyBuf + 0x0C) = ok ? 0 : -1;
                    Log(ok ? "unjail: pid jailbroken"u8 : "unjail: jailbreak failed"u8);
                }
                else
                {
                    *(int*)(replyBuf + 0x0C) = -1;
                }
            }
            else
            {
                *(int*)(replyBuf + 0x0C) = -1;
            }

            // Send reply and close the connection.
            SysWrite(client, replyBuf, CommandSize);
            SysClose(client);
        }
    }

    // ---- Logging ----

    private static void Log(ReadOnlySpan<byte> message)
    {
        fixed (byte* p = message)
            Klog(p);
    }

    private static void SendNotification(ReadOnlySpan<byte> message)
    {
        byte* req = stackalloc byte[3120];
        new Span<byte>(req, 3120).Clear();
        int len = message.Length;
        if (len > 3074) len = 3074;
        fixed (byte* src = message)
        {
            for (int i = 0; i < len; i++)
                req[45 + i] = src[i];
        }
        Notify(0, req, 3120, 0);
    }
}
