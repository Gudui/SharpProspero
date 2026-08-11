// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.SystemService;
using System.Runtime.InteropServices;

namespace SharpProspero.Application;

/// <summary>
/// Ends the process.
/// </summary>
/// <remarks>
/// <para>
/// An application module must not return from its entry point. The start object the toolchain links
/// in reports a return to the platform before the status reaches the C library, and the platform
/// treats that report as a fault: the process is killed, a crash report is written, and the user is
/// shown the box that says the application closed unexpectedly. The reason recorded is "Returned from
/// main with zero", so even a clean exit with nothing wrong is reported as a crash.
/// </para>
/// <para>
/// Calling <see cref="Exit"/> from inside the entry point leaves through the C library instead. It
/// runs whatever was registered to run at teardown and ends the process, which is what the start
/// object itself would do, and the report is never made. Every module ends this way:
/// </para>
/// <code>
/// private static void Main()
/// {
///     using (var app = new Game())
///         app.Run();
///
///     ProcessExit.Exit();
/// }
/// </code>
/// <para>
/// Release what the module holds before calling it. Teardown registered with the C library still
/// runs, but nothing else does.
/// </para>
/// </remarks>
public static unsafe partial class ProcessExit
{
    private const string Lib = "libc";

    /// <summary>
    /// Ends the process with <paramref name="status"/>. Does not return.
    /// </summary>
    /// <param name="status">The status the process ends with. Zero means it finished as intended.</param>
    public static void Exit(int status = 0)
    {
        exit(status);

        // The call above does not come back. Looping keeps the method from falling off its own end if
        // a future platform ever lets it, which would return to the entry point and cause exactly the
        // fault this type exists to avoid.
        while (true)
        {
        }
    }

    /// <summary>
    /// Ends the process and records it as an abnormal termination. Does not return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this where the module has decided it cannot carry on and wants that written down: the
    /// system raises the process into a fault, tears it down, and files a report describing the end as
    /// abnormal. The user is shown the box that says the application closed unexpectedly, which is the
    /// point - it is the reporting path, not the tidy way out. <see cref="Exit"/> remains the way a
    /// module that finished its work leaves.
    /// </para>
    /// <para>
    /// Nothing registered for teardown runs, so release anything that would outlive the process, such
    /// as an open file being written, before calling it.
    /// </para>
    /// </remarks>
    public static void ExitAbnormally()
    {
        // The service takes a descriptor pointer and accepts only a null one, which is why this takes
        // no argument: the descriptor's shape is not published, so there is nothing a caller could
        // build to pass.
        SystemService.sceSystemServiceReportAbnormalTermination(null);

        // Reached only if a future platform ever declines the report and returns. Ending the process
        // some other way is still better than returning to the entry point, which this type exists to
        // avoid.
        Exit(1);
    }

    [LibraryImport(Lib)]
    private static partial void exit(int status);
}
