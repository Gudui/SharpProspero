// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload;

/// <summary>
/// POSIX process control and signal delivery for a payload context. Wraps <c>kill</c>,
/// <c>getpid</c>, <c>getppid</c> from <c>libc</c> and <c>sceKernelGetProcessName</c> from
/// <c>libkernel</c>.
/// </summary>
public static unsafe partial class PayloadProcessControl
{
    private const string LibC = "libc";
    private const string LibKernel = "libkernel";

    // ---- FreeBSD signal constants ----

    /// <summary>Hangup.</summary>
    public const int SigHup = 1;

    /// <summary>Interrupt (Ctrl-C).</summary>
    public const int SigInt = 2;

    /// <summary>Quit (Ctrl-\).</summary>
    public const int SigQuit = 3;

    /// <summary>Illegal instruction.</summary>
    public const int SigIll = 4;

    /// <summary>Trace/breakpoint trap.</summary>
    public const int SigTrap = 5;

    /// <summary>Abort.</summary>
    public const int SigAbrt = 6;

    /// <summary>Floating-point exception.</summary>
    public const int SigFpe = 8;

    /// <summary>Kill (cannot be caught or ignored).</summary>
    public const int SigKill = 9;

    /// <summary>Bus error.</summary>
    public const int SigBus = 10;

    /// <summary>Segmentation fault.</summary>
    public const int SigSegv = 11;

    /// <summary>Bad system call.</summary>
    public const int SigSys = 12;

    /// <summary>Broken pipe.</summary>
    public const int SigPipe = 13;

    /// <summary>Alarm clock.</summary>
    public const int SigAlrm = 14;

    /// <summary>Termination.</summary>
    public const int SigTerm = 15;

    /// <summary>Stop (cannot be caught or ignored).</summary>
    public const int SigStop = 17;

    /// <summary>Continue after stop.</summary>
    public const int SigCont = 19;

    /// <summary>Child process state changed.</summary>
    public const int SigChld = 20;

    /// <summary>User-defined signal 1.</summary>
    public const int SigUsr1 = 30;

    /// <summary>User-defined signal 2.</summary>
    public const int SigUsr2 = 31;

    /// <summary>
    /// Sends signal <paramref name="sig"/> to the process identified by <paramref name="pid"/>.
    /// </summary>
    /// <param name="pid">The target process identifier.</param>
    /// <param name="sig">The signal number (one of the <c>Sig*</c> constants).</param>
    /// <returns>Zero on success, or -1 on error.</returns>
    [LibraryImport(LibC)]
    public static partial int kill(int pid, int sig);

    /// <summary>
    /// Returns the process identifier of the calling process.
    /// </summary>
    [LibraryImport(LibC)]
    public static partial int getpid();

    /// <summary>
    /// Returns the parent process identifier of the calling process.
    /// </summary>
    [LibraryImport(LibC)]
    public static partial int getppid();

    /// <summary>
    /// Reads the process name (up to 32 bytes, NUL-terminated) for <paramref name="pid"/>
    /// into <paramref name="name"/>.
    /// </summary>
    /// <param name="pid">The target process identifier.</param>
    /// <param name="name">A buffer of at least 32 bytes to receive the name.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(LibKernel)]
    public static partial int sceKernelGetProcessName(int pid, byte* name);
}
