// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Kernel;
using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Video;

/// <summary>Which compressed form the video decoder reads.</summary>
public enum Videodec2CodecType : uint
{
    /// <summary>H.264 / MPEG-4 Part 10.</summary>
    Avc = 1,

    /// <summary>H.265 High Efficiency Video Coding.</summary>
    Hevc = 974921,

    /// <summary>VP9.</summary>
    Vp9 = 2382845,
}

/// <summary>The H.264 profile a stream is coded at.</summary>
public enum Videodec2AvcProfile : uint
{
    /// <summary>Main profile.</summary>
    Main = 77,

    /// <summary>High profile, the usual one for a recorded file.</summary>
    High = 100,
}

/// <summary>What the decoder is built on.</summary>
public enum Videodec2ResourceType : uint
{
    /// <summary>Decoding runs on a compute queue.</summary>
    Compute = 1,
}

/// <summary>How the decoder is set up: what it decodes and how large a picture it must handle.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2DecoderConfigInfo
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>What the decoder is built on, from <see cref="Videodec2ResourceType"/>.</summary>
    public uint ResourceType;

    /// <summary>Which compressed form to read, from <see cref="Videodec2CodecType"/>.</summary>
    public uint CodecType;

    /// <summary>The profile the stream is coded at.</summary>
    public uint Profile;

    /// <summary>The highest level the decoder must handle.</summary>
    public uint MaxLevel;

    /// <summary>The widest picture, or -1 to let the decoder decide.</summary>
    public int MaxFrameWidth;

    /// <summary>The tallest picture, or -1 to let the decoder decide.</summary>
    public int MaxFrameHeight;

    /// <summary>How many pictures the decoder may hold back, or -1 to let it decide.</summary>
    public int MaxDpbFrameCount;

    /// <summary>How many inputs may be queued before one is taken.</summary>
    public uint DecodeInputQueueDepth;

    /// <summary>The compute queue the decoder runs on.</summary>
    public void* ComputeQueue;

    /// <summary>Which processors the decoder's threads may run on, or 0 to inherit.</summary>
    public ulong CpuAffinityMask;

    /// <summary>The priority of the decoder's threads, or -1 to inherit.</summary>
    public int CpuThreadPriority;

    /// <summary>Whether to favour progressive video.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool OptimizeProgressiveVideo;

    /// <summary>Whether the decoder checks what backs the memory it is given.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool CheckMemoryType;

    private byte _reserved0;
    private byte _reserved1;

    /// <summary>Extra settings, or null.</summary>
    public void* ExtraConfigInfo;
}

/// <summary>How much memory of each kind the decoder needs, and where it has been put.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2DecoderMemoryInfo
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>Bytes of ordinary memory the decoder needs.</summary>
    public nuint CpuMemorySize;

    /// <summary>Where that ordinary memory is.</summary>
    public void* CpuMemory;

    /// <summary>Bytes of graphics memory the decoder needs.</summary>
    public nuint GpuMemorySize;

    /// <summary>Where that graphics memory is.</summary>
    public void* GpuMemory;

    /// <summary>Bytes of memory both sides reach that the decoder needs.</summary>
    public nuint CpuGpuMemorySize;

    /// <summary>Where that shared memory is.</summary>
    public void* CpuGpuMemory;

    /// <summary>The largest picture buffer the decoder will ask for.</summary>
    public nuint MaxFrameBufferSize;

    /// <summary>The alignment a picture buffer must be made on.</summary>
    public uint FrameBufferAlignment;

    private uint _reserved0;
}

/// <summary>One compressed unit handed to the decoder.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2InputData
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>The compressed bytes.</summary>
    public void* AuData;

    /// <summary>How many compressed bytes there are.</summary>
    public nuint AuSize;

    /// <summary>When the picture should be shown.</summary>
    public ulong PresentationTime;

    /// <summary>When the unit should be decoded.</summary>
    public ulong DecodeTime;

    /// <summary>A value of the caller's own, handed back with the picture.</summary>
    public ulong AttachedData;
}

/// <summary>What came out of a decode call.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2OutputInfo
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>Whether a picture is present.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool IsValid;

    /// <summary>Whether the picture was decoded from damaged input.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool IsErrorFrame;

    /// <summary>How many pictures this output carries.</summary>
    public byte PictureCount;

    /// <summary>Whether the picture was dropped rather than shown.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool IsDiscardedFrame;

    /// <summary>Which compressed form produced it.</summary>
    public uint CodecType;

    /// <summary>The picture's width in pixels.</summary>
    public uint FrameWidth;

    /// <summary>The picture's row stride in pixels.</summary>
    public uint FramePitch;

    /// <summary>The picture's height in pixels.</summary>
    public uint FrameHeight;

    /// <summary>Where the picture is.</summary>
    public void* FrameBuffer;

    /// <summary>How large the picture buffer is.</summary>
    public nuint FrameBufferSize;

    /// <summary>The picture's pixel arrangement.</summary>
    public uint FrameFormat;

    /// <summary>The picture's row stride in bytes.</summary>
    public uint FramePitchInBytes;
}

/// <summary>A buffer offered to the decoder to write a picture into.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2FrameBuffer
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>Where the buffer is.</summary>
    public void* FrameBuffer;

    /// <summary>How large the buffer is.</summary>
    public nuint FrameBufferSize;

    /// <summary>Whether the decoder took the buffer.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool IsAccepted;
}

/// <summary>How much memory a compute queue needs, and where it has been put.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceVideodec2ComputeMemoryInfo
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>Bytes of memory both sides reach that the queue needs.</summary>
    public nuint CpuGpuMemorySize;

    /// <summary>Where that shared memory is.</summary>
    public void* CpuGpuMemory;
}

/// <summary>Which compute queue to take and whether its memory is checked.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceVideodec2ComputeConfigInfo
{
    /// <summary>The size of this structure in bytes.</summary>
    public nuint ThisSize;

    /// <summary>Which pipe the queue is on.</summary>
    public ushort ComputePipeId;

    /// <summary>Which queue on that pipe.</summary>
    public ushort ComputeQueueId;

    /// <summary>Whether the service checks what backs the memory it is given.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool CheckMemoryType;

    private byte _reserved0;
    private ushort _reserved1;
}

/// <summary>
/// Compressed video decoding. The caller provides every piece of memory: it asks how much a compute
/// queue needs and creates one, asks how much a decoder needs and creates it, then offers a buffer per
/// picture and is handed back the decoded frame.
/// </summary>
public static unsafe partial class Videodec2
{
    private const string Lib = "libSceVideodec2";

    /// <summary>Let the decoder settle a frame or buffer count itself.</summary>
    public const int AutoFrameSetting = -1;

    /// <summary>Run the decoder's threads on whichever processors the caller uses.</summary>
    public const ulong InheritAffinityMask = 0;

    /// <summary>Run the decoder's threads at the caller's priority.</summary>
    public const int InheritThreadPriority = -1;

    /// <summary>The usual pixel arrangement for a decoded picture.</summary>
    public const uint FrameFormatDefault = 0;

    /// <summary>The alignment every memory region the decoder is given must be made on.</summary>
    public const nuint MemoryAlignment = KernelMemory.PageSize;

    /// <summary>Asks how much memory a compute queue needs.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2QueryComputeMemoryInfo(SceVideodec2ComputeMemoryInfo* memoryInfo);

    /// <summary>Takes a compute queue using the memory named in <paramref name="memoryInfo"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2AllocateComputeQueue(
        SceVideodec2ComputeConfigInfo* configInfo, SceVideodec2ComputeMemoryInfo* memoryInfo, void** computeQueueOut);

    /// <summary>Gives a compute queue back.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2ReleaseComputeQueue(void* computeQueue);

    /// <summary>Asks how much memory a decoder with these settings needs.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2QueryDecoderMemoryInfo(
        SceVideodec2DecoderConfigInfo* configInfo, SceVideodec2DecoderMemoryInfo* memoryInfo);

    /// <summary>Creates a decoder from the settings and the memory provided.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2CreateDecoder(
        SceVideodec2DecoderConfigInfo* configInfo, SceVideodec2DecoderMemoryInfo* memoryInfo, void** decoderOut);

    /// <summary>Destroys a decoder.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2DeleteDecoder(void* decoder);

    /// <summary>Decodes one compressed unit into the offered picture buffer.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2Decode(
        void* decoder, SceVideodec2InputData* inputData, SceVideodec2FrameBuffer* frameBuffer,
        SceVideodec2OutputInfo* outputInfo);

    /// <summary>Pushes out any picture the decoder was still holding.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2Flush(
        void* decoder, SceVideodec2FrameBuffer* frameBuffer, SceVideodec2OutputInfo* outputInfo);

    /// <summary>Drops what the decoder was carrying, for a seek.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideodec2Reset(void* decoder);
}
