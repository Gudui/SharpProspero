// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Audio;

/// <summary>The sample form of a waveform an Ngs2 voice plays.</summary>
public enum Ngs2WaveformType : uint
{
    /// <summary>Signed 8-bit.</summary>
    PcmI8 = 0x10,
    /// <summary>Unsigned 8-bit.</summary>
    PcmU8 = 0x11,
    /// <summary>Signed 16-bit, little-endian.</summary>
    PcmI16L = 0x12,
    /// <summary>Signed 16-bit, big-endian.</summary>
    PcmI16B = 0x13,
    /// <summary>Signed 24-bit, little-endian.</summary>
    PcmI24L = 0x14,
    /// <summary>Signed 24-bit, big-endian.</summary>
    PcmI24B = 0x15,
    /// <summary>Signed 32-bit, little-endian.</summary>
    PcmI32L = 0x16,
    /// <summary>Signed 32-bit, big-endian.</summary>
    PcmI32B = 0x17,
    /// <summary>32-bit floating-point, little-endian.</summary>
    PcmF32L = 0x18,
    /// <summary>32-bit floating-point, big-endian.</summary>
    PcmF32B = 0x19,
}

/// <summary>The built-in Ngs2 rack kinds.</summary>
public enum Ngs2RackId : uint
{
    /// <summary>Plays waveforms as voices.</summary>
    Sampler = 0x1000,
    /// <summary>Mixes voices down.</summary>
    Submixer = 0x2000,
    /// <summary>Adds reverberation.</summary>
    Reverb = 0x2001,
    /// <summary>The final mastering step.</summary>
    Mastering = 0x3000,
    /// <summary>A custom rack.</summary>
    Custom = 0x4000,
    /// <summary>A custom sampler rack.</summary>
    CustomSampler = 0x4001,
    /// <summary>A custom submixer rack.</summary>
    CustomSubmixer = 0x4002,
    /// <summary>A custom mastering rack.</summary>
    CustomMastering = 0x4003,
    /// <summary>An extended custom rack.</summary>
    CustomEx = 0x4004,
}

/// <summary>Points the engine at the block of memory it works in, and reports how big it needs to be.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceNgs2ContextBufferInfo
{
    /// <summary>The buffer the engine uses.</summary>
    public void* HostBuffer;
    /// <summary>The buffer size in bytes.</summary>
    public nuint HostBufferSize;
    private nuint _reserved0;
    private nuint _reserved1;
    private nuint _reserved2;
    private nuint _reserved3;
    private nuint _reserved4;
    /// <summary>Caller data carried through.</summary>
    public nuint UserData;
}

/// <summary>Handlers the engine calls to allocate and free its own memory, for the allocator-driven create path.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceNgs2BufferAllocator
{
    /// <summary>An <c>int32(SceNgs2ContextBufferInfo*)</c> allocation callback.</summary>
    public nint AllocHandler;
    /// <summary>An <c>int32(SceNgs2ContextBufferInfo*)</c> free callback.</summary>
    public nint FreeHandler;
    /// <summary>Caller data passed to the handlers.</summary>
    public nuint UserData;
}

/// <summary>Where a render pass writes its output and in what form.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceNgs2RenderBufferInfo
{
    /// <summary>The output buffer.</summary>
    public void* Buffer;
    /// <summary>The output buffer size in bytes.</summary>
    public nuint BufferSize;
    /// <summary>The sample form, from <see cref="Ngs2WaveformType"/>.</summary>
    public uint WaveformType;
    /// <summary>The channel count (1, 2, 6, or 8).</summary>
    public uint NumChannels;
}

/// <summary>
/// The audio synthesis and mixing engine. A system owns racks (sampler, submixer, reverb, mastering); a
/// rack owns voices that play waveforms; a render pass mixes them into an output buffer. Handles are opaque
/// integers. This is the flat interface exactly as the engine headers declare it; the sized option and
/// command structures are passed as pointers the caller builds and initializes through the reset-option and
/// query-info calls.
/// </summary>
public static unsafe partial class Ngs2
{
    private const string Lib = "libSceNgs2";

    /// <summary>One (mono) channel.</summary>
    public const uint Channels1 = 1;
    /// <summary>Two (stereo) channels.</summary>
    public const uint Channels2 = 2;
    /// <summary>Six (5.1) channels.</summary>
    public const uint Channels51 = 6;
    /// <summary>Eight (7.1) channels.</summary>
    public const uint Channels71 = 8;

    // System

    /// <summary>Lists the live system handles.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemEnumHandles(nuint* outHandles, uint maxHandles);
    /// <summary>Fills an option structure with its defaults.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemResetOption(void* outOption);
    /// <summary>Reports the buffer a system with these options needs.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemQueryBufferSize(void* option, SceNgs2ContextBufferInfo* outBufferInfo);
    /// <summary>Creates a system in a caller-provided buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemCreate(void* option, SceNgs2ContextBufferInfo* bufferInfo, nuint* outHandle);
    /// <summary>Creates a system, letting the engine allocate through the given handlers.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemCreateWithAllocator(void* option, SceNgs2BufferAllocator* allocator, nuint* outHandle);
    /// <summary>Destroys a system and returns its buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemDestroy(nuint systemHandle, SceNgs2ContextBufferInfo* outBufferInfo);
    /// <summary>Runs a batch of system commands.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemRunCommands(nuint systemHandle, void* @params, nuint numParams);
    /// <summary>Reads a system information field.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemQueryInfo(nuint systemHandle, uint queryId, void* outInfo, nuint infoSize);
    /// <summary>Takes the system lock.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemLock(nuint systemHandle);
    /// <summary>Releases the system lock.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemUnlock(nuint systemHandle);
    /// <summary>Attaches caller data to a system.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemSetUserData(nuint systemHandle, nuint userData);
    /// <summary>Reads the caller data attached to a system.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemGetUserData(nuint systemHandle, nuint* outUserData);
    /// <summary>Reads the system information block.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemGetInfo(nuint systemHandle, void* outInfo, nuint infoSize);
    /// <summary>Sets the grain (block) size in samples.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemSetGrainSamples(nuint systemHandle, uint numGrainSamples);
    /// <summary>Sets the output sample rate.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemSetSampleRate(nuint systemHandle, uint sampleRate);
    /// <summary>Lists a system's rack handles.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemEnumRackHandles(nuint systemHandle, nuint* aOutHandle, uint maxHandles);
    /// <summary>Mixes the system into the render buffers - the call that produces audio.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2SystemRender(nuint systemHandle, SceNgs2RenderBufferInfo* aBufferInfo, uint numBufferInfo);

    // Rack

    /// <summary>Reports the buffer a rack of this kind needs.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackQueryBufferSize(uint rackId, void* option, SceNgs2ContextBufferInfo* outBufferInfo);
    /// <summary>Creates a rack in a caller-provided buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackCreate(nuint systemHandle, uint rackId, void* option, SceNgs2ContextBufferInfo* bufferInfo, nuint* outHandle);
    /// <summary>Creates a rack, letting the engine allocate through the given handlers.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackCreateWithAllocator(nuint systemHandle, uint rackId, void* option, SceNgs2BufferAllocator* allocator, nuint* outHandle);
    /// <summary>Destroys a rack and returns its buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackDestroy(nuint rackHandle, SceNgs2ContextBufferInfo* outBufferInfo);
    /// <summary>Runs a batch of rack commands.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackRunCommands(nuint rackHandle, void* @params, nuint numParams);
    /// <summary>Reads a rack information field.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackQueryInfo(nuint rackHandle, uint queryId, void* outInfo, nuint infoSize);
    /// <summary>Takes the rack lock.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackLock(nuint rackHandle);
    /// <summary>Releases the rack lock.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackUnlock(nuint rackHandle);
    /// <summary>Attaches caller data to a rack.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackSetUserData(nuint rackHandle, nuint userData);
    /// <summary>Reads the caller data attached to a rack.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackGetUserData(nuint rackHandle, nuint* outUserData);
    /// <summary>Reads the rack information block.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackGetInfo(nuint rackHandle, void* outInfo, nuint infoSize);
    /// <summary>Gets the handle of one voice in a rack.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2RackGetVoiceHandle(nuint rackHandle, uint voiceIndex, nuint* outHandle);

    // Voice

    /// <summary>Runs a batch of voice commands.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceRunCommands(nuint voiceHandle, void* @params, nuint numParams);
    /// <summary>Reads a voice information field.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceQueryInfo(nuint voiceHandle, uint queryId, void* outInfo, nuint infoSize);
    /// <summary>Applies a parameter list to a voice.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceControl(nuint voiceHandle, void* paramList);
    /// <summary>Reads a voice's state flags.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceGetStateFlags(nuint voiceHandle, uint* outStateFlags);
    /// <summary>Reads a voice's state block.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceGetState(nuint voiceHandle, void* outVoiceState, nuint voiceStateSize);
    /// <summary>Finds the rack and index that own a voice.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceGetOwner(nuint voiceHandle, nuint* outOwnerRack, uint* outVoiceIndex);
    /// <summary>Reads a voice's mixing-matrix information.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceGetMatrixInfo(nuint voiceHandle, uint matrixId, void* outInfo, nuint infoSize);
    /// <summary>Reads a voice's port information.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2VoiceGetPortInfo(nuint voiceHandle, uint portId, void* outInfo, nuint infoSize);

    // Stream

    /// <summary>Fills a stream option structure with its defaults.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamResetOption(void* outOption);
    /// <summary>Reports the buffer a stream with these options needs.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamQueryBufferSize(void* option, SceNgs2ContextBufferInfo* outBuffer);
    /// <summary>Creates a stream in a caller-provided buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamCreate(nuint systemHandle, void* option, SceNgs2ContextBufferInfo* buffer, nuint* outStreamHandle);
    /// <summary>Creates a stream, letting the engine allocate through the given handlers.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamCreateWithAllocator(nuint systemHandle, void* option, SceNgs2BufferAllocator* allocator, nuint* outStreamHandle);
    /// <summary>Destroys a stream and returns its buffer.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamDestroy(nuint streamHandle, SceNgs2ContextBufferInfo* outBuffer);
    /// <summary>Runs a batch of stream commands.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamRunCommands(nuint streamHandle, void* @params, nuint numParams);
    /// <summary>Reads a stream information field.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2StreamQueryInfo(nuint streamHandle, uint queryId, void* outInfo, nuint infoSize);

    // Waveform utilities

    /// <summary>Reads waveform information from a block of memory.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2ParseWaveformData(void* data, nuint dataSize, void* outInfo);
    /// <summary>Reads waveform information from a file.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2ParseWaveformFile(byte* path, ulong offset, void* outInfo);
    /// <summary>Reads waveform information through a caller-supplied read handler.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2ParseWaveformUser(nint handler, nuint userData, void* outInfo);
    /// <summary>Computes the byte layout of a run of samples in a waveform format.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2CalcWaveformBlock(void* format, uint samplePos, uint numSamples, void* outBlock);
    /// <summary>Reports the frame layout of a waveform format.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2GetWaveformFrameInfo(void* format, uint* outFrameSize, uint* outNumFrameSamples, uint* outUnitsPerFrame, uint* outNumDelaySamples);

    /// <summary>Fills a job-scheduler option structure with its defaults.</summary>
    [LibraryImport(Lib)] public static partial int sceNgs2JobSchedulerResetOption(void* outOption);
}
