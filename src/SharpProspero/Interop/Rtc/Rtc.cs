// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Rtc;

/// <summary>A wall-clock time broken into calendar fields.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceRtcDateTime
{
    /// <summary>Year, four digits.</summary>
    public ushort Year;

    /// <summary>Month, 1 to 12.</summary>
    public ushort Month;

    /// <summary>Day of the month, 1 to 31.</summary>
    public ushort Day;

    /// <summary>Hour, 0 to 23.</summary>
    public ushort Hour;

    /// <summary>Minute, 0 to 59.</summary>
    public ushort Minute;

    /// <summary>Second, 0 to 59.</summary>
    public ushort Second;

    /// <summary>Microseconds within the second, 0 to 999,999.</summary>
    public uint Microsecond;
}

/// <summary>
/// Real-time-clock bindings. Read the current wall-clock time as calendar fields or as a 64-bit tick.
/// The tick counts microseconds and the resolution call reports ticks per second.
/// </summary>
public static unsafe partial class Rtc
{
    private const string Lib = "libSceRtc";

    /// <summary>
    /// Reads the current time into <paramref name="time"/> for the time zone offset
    /// <paramref name="timeZoneMinutes"/> in minutes; pass 0 for UTC.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceRtcGetCurrentClock(SceRtcDateTime* time, int timeZoneMinutes);

    /// <summary>Reads the current time in the system's local time zone into <paramref name="time"/>.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceRtcGetCurrentClockLocalTime(SceRtcDateTime* time);

    /// <summary>Reads the current UTC time into <paramref name="tick"/> as a microsecond tick.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceRtcGetCurrentTick(ulong* tick);

    /// <summary>Ticks per second, for interpreting a tick value.</summary>
    [LibraryImport(Lib)]
    public static partial uint sceRtcGetTickResolution();
}
