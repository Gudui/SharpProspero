// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Media;

/// <summary>How much the player reports about what it is doing.</summary>
public enum AvPlayerDebugLevel
{
    /// <summary>Report nothing.</summary>
    None = 0,

    /// <summary>Report errors.</summary>
    Info = 1,

    /// <summary>Report errors and warnings.</summary>
    Warnings = 2,

    /// <summary>Report everything.</summary>
    All = 3,
}

/// <summary>The allocators the player calls. It has none of its own, so all four must be supplied.</summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public unsafe struct AvPlayerMemAllocator
{
    /// <summary>Passed back to each callback. Offset 0.</summary>
    public void* ObjectPointer;

    /// <summary>General allocation: (object, alignment, size) returning the block. Offset 8.</summary>
    public delegate* unmanaged<void*, uint, uint, void*> Allocate;

    /// <summary>General release: (object, block). Offset 16.</summary>
    public delegate* unmanaged<void*, void*, void> Deallocate;

    /// <summary>Frame-memory allocation: (object, alignment, size) returning the block. Offset 24.</summary>
    public delegate* unmanaged<void*, uint, uint, void*> AllocateTexture;

    /// <summary>Frame-memory release: (object, block). Offset 32.</summary>
    public delegate* unmanaged<void*, void*, void> DeallocateTexture;
}

/// <summary>
/// Optional file callbacks. Leave the whole block zero to let the player read the file itself, which
/// is what a plain path needs.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public unsafe struct AvPlayerFileReplacement
{
    /// <summary>Passed back to each callback. Offset 0.</summary>
    public void* ObjectPointer;

    /// <summary>Open: (object, path) returning zero on success. Offset 8.</summary>
    public delegate* unmanaged<void*, byte*, int> Open;

    /// <summary>Close: (object) returning zero on success. Offset 16.</summary>
    public delegate* unmanaged<void*, int> Close;

    /// <summary>Read: (object, buffer, position, length) returning the bytes read. Offset 24.</summary>
    public delegate* unmanaged<void*, byte*, ulong, uint, int> ReadOffset;

    /// <summary>Size: (object) returning the file length. Offset 32.</summary>
    public delegate* unmanaged<void*, ulong> Size;
}

/// <summary>Optional event callback. Leave zero to poll instead.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public unsafe struct AvPlayerEventReplacement
{
    /// <summary>Passed back to the callback. Offset 0.</summary>
    public void* ObjectPointer;

    /// <summary>Event: (object, eventId, sourceId, eventData). Offset 8.</summary>
    public delegate* unmanaged<void*, int, int, void*, void> EventCallback;
}

/// <summary>What the player starts with. Build one with <see cref="AvPlayer.InitializeData"/>.</summary>
[StructLayout(LayoutKind.Sequential, Size = 120)]
public unsafe struct AvPlayerInitData
{
    /// <summary>The allocators. Required. Offset 0.</summary>
    public AvPlayerMemAllocator MemoryReplacement;

    /// <summary>Optional file callbacks. Offset 40.</summary>
    public AvPlayerFileReplacement FileReplacement;

    /// <summary>Optional event callback. Offset 80.</summary>
    public AvPlayerEventReplacement EventReplacement;

    /// <summary>How much the player reports. Offset 96.</summary>
    public AvPlayerDebugLevel DebugLevel;

    /// <summary>Thread priority; zero takes the default of 700, otherwise 637 to 764. Offset 100.</summary>
    public uint BasePriority;

    /// <summary>Frame buffers to hold, 2 to 16; anything else takes 2. Offset 104.</summary>
    public int NumOutputVideoFrameBuffers;

    /// <summary>Whether playback begins without waiting for the callback. Offset 108.</summary>
    public byte AutoStart;

    private byte _reserved0;
    private byte _reserved1;
    private byte _reserved2;

    /// <summary>Optional default language for stream selection. Offset 112.</summary>
    public byte* DefaultLanguage;
}

/// <summary>The details of an audio frame.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct AvPlayerAudioDetails
{
    /// <summary>How many channels the frame carries. Offset 0.</summary>
    public ushort ChannelCount;

    private ushort _reserved;

    /// <summary>Samples per second. Offset 4.</summary>
    public uint SampleRate;

    /// <summary>The payload size in bytes. Offset 8.</summary>
    public uint Size;

    /// <summary>The language code. Offset 12.</summary>
    public uint LanguageCode;
}

/// <summary>The details of a video stream.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct AvPlayerVideoDetails
{
    /// <summary>Width in pixels. Offset 0.</summary>
    public uint Width;

    /// <summary>Height in pixels. Offset 4.</summary>
    public uint Height;

    /// <summary>Aspect ratio. Offset 8.</summary>
    public float AspectRatio;

    /// <summary>The language code. Offset 12.</summary>
    public uint LanguageCode;
}

/// <summary>The details of a frame; which member applies depends on the stream it came from.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct AvPlayerStreamDetails
{
    /// <summary>Read when the frame is audio.</summary>
    [FieldOffset(0)] public AvPlayerAudioDetails Audio;

    /// <summary>Read when the stream is video.</summary>
    [FieldOffset(0)] public AvPlayerVideoDetails Video;
}

/// <summary>One decoded frame: where the payload is, when it plays, and what it holds.</summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public unsafe struct AvPlayerFrameInfo
{
    /// <summary>The payload. Offset 0.</summary>
    public byte* Data;

    private uint _reserved;
    private uint _padding;

    /// <summary>When the frame plays, in milliseconds. Offset 16.</summary>
    public ulong TimeStamp;

    /// <summary>What the frame holds. Offset 24.</summary>
    public AvPlayerStreamDetails Details;
}

/// <summary>
/// One decoded video frame in extended form. The extended frame carries the pitch, which is needed to
/// address the NV12 planes the decoder writes. Only the fields the SDK reads are named; the rest of the
/// 80-byte details union is reserved.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 104)]
public unsafe struct AvPlayerFrameInfoEx
{
    /// <summary>The frame payload (NV12 planes). Offset 0.</summary>
    [FieldOffset(0)] public void* Data;

    /// <summary>When the frame plays, in milliseconds. Offset 16.</summary>
    [FieldOffset(16)] public ulong TimeStamp;

    /// <summary>Video width in pixels. Offset 24 (details.video.width).</summary>
    [FieldOffset(24)] public uint VideoWidth;

    /// <summary>Video height in pixels. Offset 28 (details.video.height).</summary>
    [FieldOffset(28)] public uint VideoHeight;

    /// <summary>Row pitch of the luma and chroma planes in bytes. Offset 60 (details.video.pitch).</summary>
    [FieldOffset(60)] public uint VideoPitch;
}

/// <summary>
/// Media-playback bindings. Start the player with allocators it can call, add a source, start it, and
/// pull decoded audio and video frames while it stays active.
/// </summary>
public static unsafe partial class AvPlayer
{
    private const string Lib = "libSceAvPlayer";

    /// <summary>The default thread priority the player takes when none is given.</summary>
    public const uint DefaultBasePriority = 700;

    /// <summary>Zeroes <paramref name="data"/> so only the fields a caller sets are non-zero.</summary>
    public static void InitializeData(AvPlayerInitData* data)
        => new System.Span<byte>(data, sizeof(AvPlayerInitData)).Clear();

    /// <summary>Starts a player. Returns the handle, or null when it could not start.</summary>
    [LibraryImport(Lib)]
    public static partial void* sceAvPlayerInit(AvPlayerInitData* data);

    /// <summary>Adds the file at <paramref name="path"/> as the source to play.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerAddSource(void* handle, byte* path);

    /// <summary>Begins playback.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerStart(void* handle);

    /// <summary>Stops playback.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerStop(void* handle);

    /// <summary>Pauses playback.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerPause(void* handle);

    /// <summary>Resumes paused playback.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerResume(void* handle);

    /// <summary>Whether the player still has something to play.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool sceAvPlayerIsActive(void* handle);

    /// <summary>Sets whether the source repeats.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerSetLooping(void* handle, [MarshalAs(UnmanagedType.U1)] bool loop);

    /// <summary>
    /// Takes the next decoded audio frame into <paramref name="frame"/>. Returns false when none is
    /// ready.
    /// </summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool sceAvPlayerGetAudioData(void* handle, AvPlayerFrameInfo* frame);

    /// <summary>
    /// Takes the next decoded video frame into <paramref name="frame"/>, in NV12 with the pitch filled.
    /// Returns false when none is ready.
    /// </summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool sceAvPlayerGetVideoDataEx(void* handle, AvPlayerFrameInfoEx* frame);

    /// <summary>The playback position in milliseconds.</summary>
    [LibraryImport(Lib)]
    public static partial ulong sceAvPlayerCurrentTime(void* handle);

    /// <summary>Moves playback to <paramref name="milliseconds"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerJumpToTime(void* handle, ulong milliseconds);

    /// <summary>How many streams the source carries.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerStreamCount(void* handle);

    /// <summary>Turns a stream on.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerEnableStream(void* handle, uint streamId);

    /// <summary>Shuts the player down and releases it.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAvPlayerClose(void* handle);
}
