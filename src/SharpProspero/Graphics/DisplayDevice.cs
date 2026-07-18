// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.VideoOut;
using SharpProspero.Memory;
using System;

namespace SharpProspero.Graphics;

/// <summary>
/// A double- or triple-buffered display attached to the main output. The device opens the output,
/// allocates its framebuffers from direct memory, registers them, and presents frames on the
/// vertical blank. Draw into <see cref="BackBuffer"/>, then call <see cref="Present"/>.
/// </summary>
public sealed unsafe class DisplayDevice : IDisposable
{
    private const int BytesPerPixel = 4;
    private const nuint Alignment = 2 * 1024 * 1024;

    private readonly int _handle;
    private readonly DirectMemoryRegion[] _regions;
    private int _index;
    private long _frame;
    private bool _disposed;

    private DisplayDevice(int handle, int width, int height, DirectMemoryRegion[] regions)
    {
        _handle = handle;
        Width = width;
        Height = height;
        _regions = regions;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Number of framebuffers in the swap chain.</summary>
    public int BufferCount => _regions.Length;

    /// <summary>The framebuffer to draw the next frame into.</summary>
    public Surface BackBuffer => _regions[_index].AsSurface(Width, Height);

    /// <summary>
    /// Opens the main output and builds a swap chain of <paramref name="bufferCount"/> framebuffers.
    /// The row stride equals <paramref name="width"/>, so use a width that is a multiple of 64 to
    /// match the linear pitch the output derives; the standard 1920 and 1280 widths already are.
    /// </summary>
    /// <exception cref="ProsperoException">Opening the output or registering the buffers failed.</exception>
    public static DisplayDevice Open(int width = 1920, int height = 1080, int bufferCount = 2, int userId = SceUser.System)
    {
        if (bufferCount < 2)
            throw new ArgumentOutOfRangeException(nameof(bufferCount), "At least two buffers are required.");
        if (width <= 0 || (width & 63) != 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be a positive multiple of 64 so the row pitch matches the allocated framebuffer.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        int handle = VideoOut.sceVideoOutOpen(userId, (int)VideoOutBusType.Main, 0, null);
        SceResult.ThrowIfFailed(handle, nameof(VideoOut.sceVideoOutOpen));
        VideoOut.sceVideoOutSetFlipRate(handle, 0);

        var regions = new DirectMemoryRegion[bufferCount];
        try
        {
            nuint frameBytes = (nuint)((long)width * height * BytesPerPixel);
            SceVideoOutBuffers* addresses = stackalloc SceVideoOutBuffers[bufferCount];
            for (int i = 0; i < bufferCount; i++)
            {
                regions[i] = DirectMemoryRegion.Allocate(frameBytes, Alignment);
                addresses[i] = default;
                addresses[i].Data = regions[i].Pointer;
            }

            SceVideoOutBufferAttribute2 attribute = default;
            VideoOut.sceVideoOutSetBufferAttribute2(
                &attribute, VideoOutPixelFormat.Bgra8Srgb, (uint)VideoOutTilingMode.Linear,
                (uint)width, (uint)height, VideoOutBufferAttributeOption.None, 0, 0);
            // Pin the row pitch to the width so the registered pitch, the allocation, and the drawing
            // stride all agree. A width that is a multiple of 64 is a valid linear pitch.
            attribute.PitchInPixel = (uint)width;

            int rc = VideoOut.sceVideoOutRegisterBuffers2(
                handle, 0, 0, addresses, bufferCount, &attribute, (int)VideoOutBufferCategory.Uncompressed, null);
            SceResult.ThrowIfFailed(rc, nameof(VideoOut.sceVideoOutRegisterBuffers2));
        }
        catch
        {
            foreach (DirectMemoryRegion? region in regions)
                region?.Dispose();
            VideoOut.sceVideoOutClose(handle);
            throw;
        }

        return new DisplayDevice(handle, width, height, regions);
    }

    /// <summary>
    /// Presents <see cref="BackBuffer"/>, waits for the vertical blank, and advances to the next
    /// framebuffer. Returns the presented frame index.
    /// </summary>
    public long Present(VideoOutFlipMode mode = VideoOutFlipMode.VSync)
    {
        int rc = VideoOut.sceVideoOutSubmitFlip(_handle, _index, (uint)mode, _frame);
        SceResult.ThrowIfFailed(rc, nameof(VideoOut.sceVideoOutSubmitFlip));
        VideoOut.sceVideoOutWaitVblank(_handle);
        long presented = _frame;
        _index = (_index + 1) % _regions.Length;
        _frame++;
        return presented;
    }

    /// <summary>Unregisters the buffers, releases their memory and closes the output.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        VideoOut.sceVideoOutUnregisterBuffers(_handle, 0);
        foreach (DirectMemoryRegion region in _regions)
            region.Dispose();
        VideoOut.sceVideoOutClose(_handle);
    }
}
