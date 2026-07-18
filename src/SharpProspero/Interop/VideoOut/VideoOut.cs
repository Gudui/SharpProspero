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
}
