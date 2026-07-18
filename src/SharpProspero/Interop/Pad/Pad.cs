// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Pad;

/// <summary>Digital button bits reported in the leading word of a controller sample.</summary>
[Flags]
public enum ScePadButton : uint
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>Left stick press.</summary>
    L3 = 0x00000002,

    /// <summary>Right stick press.</summary>
    R3 = 0x00000004,

    /// <summary>Options.</summary>
    Options = 0x00000008,

    /// <summary>D-pad up.</summary>
    Up = 0x00000010,

    /// <summary>D-pad right.</summary>
    Right = 0x00000020,

    /// <summary>D-pad down.</summary>
    Down = 0x00000040,

    /// <summary>D-pad left.</summary>
    Left = 0x00000080,

    /// <summary>Left trigger, digital threshold.</summary>
    L2 = 0x00000100,

    /// <summary>Right trigger, digital threshold.</summary>
    R2 = 0x00000200,

    /// <summary>Left bumper.</summary>
    L1 = 0x00000400,

    /// <summary>Right bumper.</summary>
    R1 = 0x00000800,

    /// <summary>Triangle.</summary>
    Triangle = 0x00001000,

    /// <summary>Circle.</summary>
    Circle = 0x00002000,

    /// <summary>Cross.</summary>
    Cross = 0x00004000,

    /// <summary>Square.</summary>
    Square = 0x00008000,

    /// <summary>Touch-pad press.</summary>
    TouchPad = 0x00100000,
}

/// <summary>Vibration motor levels: the large (left) and small (right) motors, 0 (stop) to 255.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ScePadVibrationParam
{
    /// <summary>Large (left) motor level, 0 to 255.</summary>
    public byte LargeMotor;

    /// <summary>Small (right) motor level, 0 to 255.</summary>
    public byte SmallMotor;
}

/// <summary>An 8-bit-per-channel color for the controller light bar.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ScePadColor
{
    /// <summary>Red.</summary>
    public byte R;

    /// <summary>Green.</summary>
    public byte G;

    /// <summary>Blue.</summary>
    public byte B;

    /// <summary>Reserved. Leave zero.</summary>
    public byte Reserved;
}

/// <summary>
/// Controller bindings. Initialize the subsystem, open a handle for a user, then read samples. The
/// sample buffer is treated as raw bytes; <see cref="SharpProspero.Input.GamePad"/> decodes the
/// buttons, stick axes, triggers, motion and touch fields from it.
/// </summary>
public static unsafe partial class Pad
{
    private const string Lib = "libScePad";

    /// <summary>Size, in bytes, of the buffer passed to <see cref="scePadReadState"/>. Exceeds the
    /// sample structure so the service never writes past the buffer.</summary>
    public const int SampleBufferSize = 1024;

    /// <summary>Initializes the controller subsystem. Call once before opening a handle.</summary>
    [LibraryImport(Lib)]
    public static partial int scePadInit();

    /// <summary>Opens a controller handle for <paramref name="userId"/>.</summary>
    /// <returns>A non-negative handle on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePadOpen(int userId, int type, int index, void* param);

    /// <summary>Closes a controller handle.</summary>
    [LibraryImport(Lib)]
    public static partial int scePadClose(int handle);

    /// <summary>Reads the latest sample into <paramref name="data"/> (at least
    /// <see cref="SampleBufferSize"/> bytes).</summary>
    [LibraryImport(Lib)]
    public static partial int scePadReadState(int handle, void* data);

    /// <summary>Sets the vibration motors.</summary>
    [LibraryImport(Lib)]
    public static partial int scePadSetVibration(int handle, ScePadVibrationParam* param);

    /// <summary>Sets the light-bar color.</summary>
    [LibraryImport(Lib)]
    public static partial int scePadSetLightBar(int handle, ScePadColor* param);

    /// <summary>Restores the light bar to its default color.</summary>
    [LibraryImport(Lib)]
    public static partial int scePadResetLightBar(int handle);
}
