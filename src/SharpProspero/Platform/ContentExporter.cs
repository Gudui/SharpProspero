// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Content;
using SharpProspero.Interop.Sysmodule;
using System;
using System.Text;
using System.Text.Unicode;
using Native = SharpProspero.Interop.Content.ContentExport;

namespace SharpProspero.Platform;

/// <summary>
/// Copies a file a module produced — a screenshot, a rendered image, a recorded clip — into the
/// console content library, where the system shows it in the gallery. Open it, export one or more
/// files, dispose it.
/// </summary>
/// <example>
/// <code>
/// using var exporter = ContentExporter.Open();
/// string saved = exporter.Export("/data/shot.png", "My screenshot", ContentExport.FormatImagePng);
/// </code>
/// </example>
public sealed unsafe class ContentExporter : IDisposable
{
    private bool _disposed;

    private ContentExporter() { }

    /// <summary>Loads and starts the content-export service.</summary>
    /// <exception cref="ProsperoException">The service could not be loaded or started.</exception>
    public static ContentExporter Open()
    {
        SceResult.ThrowIfFailed(
            Sysmodule.sceSysmoduleLoadModule((ushort)SystemModuleId.ContentExport),
            "sceSysmoduleLoadModule(ContentExport)");

        // A zeroed parameter takes the service's own allocator and buffer size.
        var param = default(SceContentExportInitParam2);
        SceResult.ThrowIfFailed(Native.sceContentExportInit2(&param), nameof(Native.sceContentExportInit2));
        return new ContentExporter();
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into the content library with the given
    /// <paramref name="title"/> and <paramref name="contentType"/> (a media type such as
    /// <see cref="ContentExport.FormatImagePng"/>), and returns the path it was written to.
    /// </summary>
    /// <exception cref="ProsperoException">The export was rejected or failed.</exception>
    public string Export(string sourcePath, string title, string contentType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        title ??= string.Empty;

        int expId = Native.sceContentExportStart();
        SceResult.ThrowIfFailed(expId, nameof(Native.sceContentExportStart));
        try
        {
            int srcCount = Encoding.UTF8.GetByteCount(sourcePath);
            byte* src = stackalloc byte[srcCount + 1];
            Encoding.UTF8.GetBytes(sourcePath, new Span<byte>(src, srcCount));
            src[srcCount] = 0;

            byte* outPath = stackalloc byte[ContentExport.PathLength];
            new Span<byte>(outPath, ContentExport.PathLength).Clear();

            var param = default(SceContentExportParam);
            SceContentExportParam* p = &param;
            WriteCString(p->Title, SceContentExportParam.TitleLength + 1, title);
            WriteCString(p->ContentType, SceContentExportParam.ContentTypeLength + 1, contentType);
            int rc = Native.sceContentExportFromFile(expId, p, src, outPath, (nuint)ContentExport.PathLength);
            SceResult.ThrowIfFailed(rc, nameof(Native.sceContentExportFromFile));
            return ReadUtf8(outPath, ContentExport.PathLength);
        }
        finally
        {
            Native.sceContentExportFinish(expId);
        }
    }

    /// <summary>Stops the service and unloads the module.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Native.sceContentExportTerm();
        Sysmodule.sceSysmoduleUnloadModule((ushort)SystemModuleId.ContentExport);
    }

    private static void WriteCString(byte* dest, int capacity, string value)
    {
        var span = new Span<byte>(dest, capacity);
        span.Clear();
        if (!string.IsNullOrEmpty(value))
            Utf8.FromUtf16(value, span[..(capacity - 1)], out _, out _);
    }

    private static string ReadUtf8(byte* start, int maxLength)
    {
        int length = 0;
        while (length < maxLength && start[length] != 0)
            length++;
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(start, length);
    }
}
