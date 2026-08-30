// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.VideoOut;

/// <summary>
/// Display output bindings. A module opens a handle on a bus, registers one or more framebuffers,
/// then submits flips to present them. The bindings map one to one onto the underlying service; the
/// <see cref="SharpProspero.Graphics"/> layer wraps them in a double-buffered display device.
/// </summary>
public static unsafe partial class VideoOut
{
    private const string Lib = "libSceVideoOut";

    /// <summary>Opens a display handle on <paramref name="busType"/> for <paramref name="userId"/>.</summary>
    /// <returns>A non-negative handle on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutOpen(int userId, int busType, int index, void* param);

    /// <summary>Closes a display handle.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutClose(int handle);

    /// <summary>Sets the maximum number of vertical blanks skipped between flips.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutSetFlipRate(int handle, int rate);

    /// <summary>Fills <paramref name="attribute"/> from the supplied geometry and format.</summary>
    [LibraryImport(Lib)]
    public static partial void sceVideoOutSetBufferAttribute2(
        SceVideoOutBufferAttribute2* attribute, ulong pixelFormat, uint tilingMode,
        uint width, uint height, ulong option, uint dccControl, ulong dccCbRegisterClearColor);

    /// <summary>
    /// Registers <paramref name="bufferNum"/> framebuffers described by <paramref name="attribute"/>
    /// into the set <paramref name="setIndex"/> starting at <paramref name="bufferIndexStart"/>.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutRegisterBuffers2(
        int handle, int setIndex, int bufferIndexStart, SceVideoOutBuffers* buffers, int bufferNum,
        SceVideoOutBufferAttribute2* attribute, int category, void* option);

    /// <summary>Removes a registered buffer set.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutUnregisterBuffers(int handle, int setIndex);

    /// <summary>Queues a flip to <paramref name="bufferIndex"/> with the given timing.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutSubmitFlip(int handle, int bufferIndex, uint flipMode, long flipArg);

    /// <summary>Blocks until the next vertical blank on <paramref name="handle"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutWaitVblank(int handle);

    /// <summary>Returns a positive value while a submitted flip is still pending.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutIsFlipPending(int handle);

    /// <summary>Reads how far the output has got through the flips submitted to it.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutGetFlipStatus(int handle, SceVideoOutFlipStatus* status);

    /// <summary>Reads the vertical-blank counter and timing for the output.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutGetVblankStatus(int handle, SceVideoOutVblankStatus* status);

    /// <summary>
    /// Registers a flip-complete event on the given equeue. The event fires each time a
    /// submitted flip retires on the output.
    /// </summary>
    /// <param name="eq">An equeue handle from <c>sceKernelCreateEqueue</c>.</param>
    /// <param name="handle">The display handle from <see cref="sceVideoOutOpen"/>.</param>
    /// <param name="udata">User data pointer delivered with the event.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutAddFlipEvent(nint eq, int handle, void* udata);

    /// <summary>
    /// Registers a vertical-blank event on the given equeue. The event fires once per
    /// vertical blank on the output.
    /// </summary>
    /// <param name="eq">An equeue handle from <c>sceKernelCreateEqueue</c>.</param>
    /// <param name="handle">The display handle from <see cref="sceVideoOutOpen"/>.</param>
    /// <param name="udata">User data pointer delivered with the event.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutAddVblankEvent(nint eq, int handle, void* udata);

    /// <summary>Removes a flip event from an equeue.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutDeleteFlipEvent(nint eq, int handle);

    /// <summary>Removes a vertical-blank event from an equeue.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutDeleteVblankEvent(nint eq, int handle);

    /// <summary>Registers a pre-vblank-start event on the given equeue.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutAddPreVblankStartEvent(nint eq, int handle, void* udata);

    /// <summary>Removes a pre-vblank-start event from an equeue.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutDeletePreVblankStartEvent(nint eq, int handle);

    /// <summary>Configures the output settings for a display handle.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutConfigureOutput(int handle, void* config);

    /// <summary>Queries whether a specific output mode is supported.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoOutIsOutputSupported(int handle, void* outputMode);
}

/// <summary>
/// How far the output has got through the flips submitted to it. The field that says which flip is on
/// screen is <see cref="FlipArg"/>: it carries back the number handed to
/// <see cref="VideoOut.sceVideoOutSubmitFlip"/>, so a caller that numbers its frames can tell which one
/// is showing. Waiting a vertical blank tells nothing about this - a blank happens whether or not a
/// flip retired.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 128)]
public struct SceVideoOutFlipStatus
{
    /// <summary>How many flips have happened since the output was opened. Offset 0.</summary>
    public ulong Count;

    /// <summary>When the last flip happened. Offset 8.</summary>
    public ulong ProcessTime;

    private ulong _reserved0;

    /// <summary>The number submitted with the flip now on screen. Offset 24.</summary>
    public long FlipArg;

    private ulong _reserved1;

    /// <summary>The counter reading when the last flip happened. Offset 40.</summary>
    public ulong ProcessTimeCounter;

    /// <summary>How many submitted flips are still waiting on the graphics processor. Offset 48.</summary>
    public int GraphicsQueueCount;

    /// <summary>How many submitted flips have not finished at all. Offset 52.</summary>
    public int PendingCount;

    /// <summary>Which buffer is on screen. Offset 56.</summary>
    public int CurrentBuffer;

    private uint _reserved2;

    /// <summary>The counter reading when the last flip was asked for. Offset 64.</summary>
    public ulong SubmitProcessTimeCounter;
}

/// <summary>
/// Vertical-blank timing information for a display output. Returned by
/// <see cref="VideoOut.sceVideoOutGetVblankStatus"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct SceVideoOutVblankStatus
{
    /// <summary>How many vertical blanks have occurred since the output was opened.</summary>
    public ulong Count;

    /// <summary>Process-time of the last vertical blank.</summary>
    public ulong ProcessTime;

    /// <summary>TSC counter reading of the last vertical blank.</summary>
    public ulong ProcessTimeCounter;

    private ulong _reserved0;
    private ulong _reserved1;
    private ulong _reserved2;
    private ulong _reserved3;
    private ulong _reserved4;
}
