// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Content;

/// <summary>The parameters the content-export service is initialized with.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceContentExportInitParam2
{
    /// <summary>An allocator the service calls, or null for the default.</summary>
    public nint MallocFunc;

    /// <summary>The matching free function, or null for the default.</summary>
    public nint FreeFunc;

    /// <summary>A pointer passed back to the allocator, or null.</summary>
    public nint UserData;

    /// <summary>The working buffer size the service uses, or zero for the default.</summary>
    public nuint BufSize;

    private long _reserved0;
    private long _reserved1;
}

/// <summary>The title and media type an exported item is written with.</summary>
[StructLayout(LayoutKind.Sequential, Size = 579)]
public unsafe struct SceContentExportParam
{
    /// <summary>Title length limit, including the terminator.</summary>
    public const int TitleLength = 256;

    /// <summary>Content-type length limit, including the terminator.</summary>
    public const int ContentTypeLength = 64;

    /// <summary>The title shown for the exported item (null-terminated UTF-8).</summary>
    public fixed byte Title[TitleLength + 1];

    private fixed byte _reserved[257];

    /// <summary>The media type of the item (null-terminated UTF-8), such as <c>image/jpeg</c>.</summary>
    public fixed byte ContentType[ContentTypeLength + 1];
}

/// <summary>
/// Content-export bindings: copy a file into the console content library. The service is loaded as a
/// system module and paired with an export session id from <see cref="sceContentExportStart"/>.
/// </summary>
public static unsafe partial class ContentExport
{
    private const string Lib = "libSceContentExport";

    /// <summary>Media type for a JPEG image.</summary>
    public const string FormatImageJpeg = "image/jpeg";

    /// <summary>Media type for a PNG image.</summary>
    public const string FormatImagePng = "image/png";

    /// <summary>Media type for a GIF image.</summary>
    public const string FormatImageGif = "image/gif";

    /// <summary>Media type for an MP4 video.</summary>
    public const string FormatVideoMp4 = "video/mp4";

    /// <summary>Media type for a WebM video.</summary>
    public const string FormatVideoWebm = "video/webm";

    /// <summary>The largest length an exported path is written with, including the terminator.</summary>
    public const int PathLength = 1024;

    /// <summary>Starts the content-export service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportInit2(SceContentExportInitParam2* initParam);

    /// <summary>Stops the content-export service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportTerm();

    /// <summary>Opens an export session and returns its id, or a negative error code.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportStart();

    /// <summary>Closes the export session <paramref name="expId"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportFinish(int expId);

    /// <summary>
    /// Copies the file at <paramref name="srcPath"/> into the content library, writing the destination
    /// path into <paramref name="path"/> (capacity <paramref name="pathBufferLength"/>).
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportFromFile(
        int expId, SceContentExportParam* param, byte* srcPath, byte* path, nuint pathBufferLength);

    /// <summary>Cancels the export running under <paramref name="expId"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportCancel(int expId);

    /// <summary>Reads the progress of the export under <paramref name="expId"/>, as a percentage.</summary>
    [LibraryImport(Lib)]
    public static partial int sceContentExportGetProgress(int expId);
}
