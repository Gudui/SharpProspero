// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Payload.Kernel;
using SharpProspero.Payload.Posix;
using SharpProspero.Payload.Process;

namespace SharpProspero.Payload.Orchestration;

/// <summary>
/// Monitors for new shell UI process instances via kqueue and re-applies patches
/// after rest-mode resume or process restart.
/// </summary>
public static unsafe class PayloadShellUiMonitor
{
    /// <summary>
    /// Starts a persistent monitoring loop that watches for new shell UI instances
    /// via kqueue EVFILT_PROC NOTE_FORK/NOTE_EXEC on the system core process.
    /// When a new instance is detected, applies the trophy availability patch.
    /// Runs indefinitely — call on a background thread.
    /// </summary>
    /// <param name="io">Kernel I/O for process inspection.</param>
    /// <param name="sysCorePid">PID of the system core process to monitor.</param>
    public static void Run(PayloadKernelIo io, int sysCorePid)
    {
        int kq = PayloadEvent.kqueue();
        if (kq < 0) return;

        FreeBsdKevent ev = default;
        PayloadEvent.EvSet(&ev, (nuint)sysCorePid, PayloadEvent.EvfiltProc,
            PayloadEvent.EvAdd, PayloadEvent.NoteFork | PayloadEvent.NoteExec, 0, null);
        PayloadEvent.kevent(kq, &ev, 1, null, 0, null);

        byte* shellUiName = stackalloc byte[] { (byte)'S', (byte)'c', (byte)'e', (byte)'S',
            (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'U', (byte)'I', 0 };

        while (true)
        {
            FreeBsdKevent fired = default;
            int n = PayloadEvent.kevent(kq, null, 0, &fired, 1, null);
            if (n <= 0) { PayloadThread.sleep(1); continue; }

            if ((fired.fflags & (PayloadEvent.NoteFork | PayloadEvent.NoteExec)) != 0)
            {
                PayloadThread.sleep(3); // Wait for the new process to initialize.
                int shellUiPid = PayloadSysctl.FindPidByName(shellUiName);

                if (shellUiPid > 0)
                    PayloadShellUiPatcher.PatchTrophyChecks(io, shellUiPid);
            }
        }
    }
}
