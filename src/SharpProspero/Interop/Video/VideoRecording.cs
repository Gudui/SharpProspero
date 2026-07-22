// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Video;

/// <summary>
/// System video recording: encode gameplay video to a file. Signatures from video_recording.h.
/// </summary>
public static unsafe partial class VideoRecording
{
    private const string Lib = "libSceVideoRecording";

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingGetStatus();

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingQueryMemSize(void* pParam);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingOpen(byte* pPath, void* pParam, void* pHeap, int heapSize);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingStart();

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingStop();

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceVideoRecordingClose(int discard);

}