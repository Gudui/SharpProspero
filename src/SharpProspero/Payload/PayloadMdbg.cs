// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload;

/// <summary>
/// Extended debug system call interface. Provides access to the full mdbg command set
/// beyond the copyout/copyin operations in <see cref="PayloadDebug"/>: process listing,
/// thread listing, process/thread information, and suspend/resume.
/// </summary>
public static unsafe partial class PayloadMdbg
{
    private const string Lib = "libScePosix";

    /// <summary>Process-list command.</summary>
    public const int CmdProcessList = 0x14;

    /// <summary>Thread-list command.</summary>
    public const int CmdThreadList = 0x15;

    /// <summary>Process-info command.</summary>
    public const int CmdProcessInfo = 0x18;

    /// <summary>Thread-info command.</summary>
    public const int CmdThreadInfo = 0x19;

    /// <summary>Suspend command.</summary>
    public const int CmdSuspend = 0x1E;

    /// <summary>Resume command.</summary>
    public const int CmdResume = 0x1F;

    /// <summary>
    /// Calls the mdbg debug system call with a command argument block.
    /// </summary>
    /// <param name="cmd">A pointer to the command argument block. The first 4 bytes are
    /// the command identifier (<see cref="CmdProcessList"/> etc.), followed by command-specific
    /// data.</param>
    /// <param name="arg2">Second argument (command-specific, often null).</param>
    /// <param name="arg3">Third argument (command-specific, often null).</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [SuppressGCTransition]
    [LibraryImport(Lib, EntryPoint = "__sp_mdbg_call")]
    public static partial int mdbg_call(void* cmd, void* arg2, void* arg3);
}

/// <summary>
/// Full notification request structure (0xC30 bytes). Exposes all fields for constructing
/// detailed notifications with priority, icons, URIs, and target user/app identifiers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 0xC30)]
public unsafe struct NotificationRequest
{
    /// <summary>Notification type (0x64 for standard text toast).</summary>
    public int Type;

    /// <summary>Request identifier (-1 for untracked).</summary>
    public int RequestId;

    /// <summary>Priority level.</summary>
    public int Priority;

    /// <summary>Message identifier.</summary>
    public int MsgId;

    /// <summary>Target identifier (16 bytes, typically all 0xFF for broadcast).</summary>
    public fixed byte TargetId[16];

    /// <summary>User identifier.</summary>
    public int UserId;

    /// <summary>Application identifier.</summary>
    public int AppId;

    /// <summary>Error number associated with the notification.</summary>
    public int ErrorNum;

    /// <summary>Whether to use an icon image URI.</summary>
    public int UseIconImageUri;

    /// <summary>The notification message text (up to 1024 bytes, NUL-terminated UTF-8).</summary>
    public fixed byte Message[1024];

    /// <summary>Icon image URI (up to 1024 bytes, NUL-terminated UTF-8).</summary>
    public fixed byte Uri[1024];

    /// <summary>Additional string field (up to 1024 bytes).</summary>
    public fixed byte ExtraString[1024];
}

/// <summary>
/// POSIX asynchronous I/O control block for <c>aio_read</c>, <c>aio_write</c>,
/// <c>aio_suspend</c>, and <c>aio_return</c>. Layout matches FreeBSD x86_64
/// <c>struct aiocb</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FreeBsdAiocb
{
    /// <summary>File descriptor.</summary>
    public int Fildes;

    /// <summary>File offset for the operation.</summary>
    public long Offset;

    /// <summary>Buffer for read/write data.</summary>
    public void* Buf;

    /// <summary>Number of bytes to transfer.</summary>
    public nuint Nbytes;

    private fixed int _spare[2];

    private nint _spare2;

    /// <summary>Operation code for <c>lio_listio</c>.</summary>
    public int LioOpcode;

    /// <summary>Request priority.</summary>
    public int AioReqprio;

    /// <summary>Completion status (kernel-internal).</summary>
    public long Status;

    /// <summary>Error code (kernel-internal).</summary>
    public long ErrorCode;

    /// <summary>Kernel info pointer (kernel-internal).</summary>
    public nint KernelInfo;

    /// <summary>Signal event notification (80 bytes on FreeBSD x86_64).</summary>
    private fixed byte _sigevent[80];
}

/// <summary>
/// Asynchronous I/O operations for efficient file copying and bulk transfers.
/// </summary>
public static unsafe partial class PayloadAsyncIo
{
    private const string Lib = "libc";

    /// <summary>Submits an asynchronous read request.</summary>
    [LibraryImport(Lib)]
    public static partial int aio_read(FreeBsdAiocb* aiocbp);

    /// <summary>Submits an asynchronous write request.</summary>
    [LibraryImport(Lib)]
    public static partial int aio_write(FreeBsdAiocb* aiocbp);

    /// <summary>
    /// Waits for one or more async I/O operations to complete.
    /// </summary>
    /// <param name="list">Array of pointers to <see cref="FreeBsdAiocb"/> entries.</param>
    /// <param name="nent">Number of entries in the array.</param>
    /// <param name="timeout">Maximum wait time, or null for infinite.</param>
    [LibraryImport(Lib)]
    public static partial int aio_suspend(FreeBsdAiocb** list, int nent,
        SharpProspero.Interop.Kernel.KernelTimespec* timeout);

    /// <summary>
    /// Returns the result of a completed async I/O operation.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial long aio_return(FreeBsdAiocb* aiocbp);
}
