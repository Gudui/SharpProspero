// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>A time value with whole seconds and a nanosecond remainder.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct KernelTimespec
{
    /// <summary>Whole seconds.</summary>
    public long Seconds;

    /// <summary>Nanoseconds within the second, 0 to 999,999,999.</summary>
    public long Nanoseconds;
}

/// <summary>
/// Timing bindings. The process-time counter is a monotonic microsecond clock that starts at process
/// launch; the clock-get call reads one of the system clocks; the sleep call suspends the caller.
/// </summary>
public static unsafe partial class KernelClock
{
    private const string Lib = "libkernel";

    /// <summary>The wall-clock clock identifier.</summary>
    public const int ClockRealtime = 0;

    /// <summary>The monotonic clock identifier.</summary>
    public const int ClockMonotonic = 4;

    /// <summary>Microseconds elapsed since the process started, on a monotonic counter.</summary>
    [LibraryImport(Lib)]
    public static partial long sceKernelGetProcessTime();

    /// <summary>Reads clock <paramref name="clockId"/> into <paramref name="time"/>.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceKernelClockGettime(int clockId, KernelTimespec* time);

    /// <summary>Suspends the calling thread for <paramref name="microseconds"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelUsleep(uint microseconds);
}
