// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Storage;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics;

/// <summary>
/// A decoded BMP image held as B8-G8-R8-A8 pixels, the same layout the display surface uses, so it
/// blits straight onto a framebuffer. BMP is an uncompressed format the SDK reads and writes on its own,
/// with no system module, which makes it a dependable interchange format for a file browser or an
/// editor. Decode once, draw its <see cref="AsSurface"/> as often as needed, dispose it to release the
/// pixels.
/// </summary>
public sealed unsafe class BmpImage : IDisposable
{
    private void* _pixels;
    private bool _disposed;

    private BmpImage(void* pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Views the decoded pixels as a drawing surface.</summary>
    public Surface AsSurface() => new((uint*)_pixels, Width, Height);

    /// <summary>Loads and decodes the BMP file at <paramref name="path"/>.</summary>
    /// <exception cref="ProsperoException">The file could not be read or is not a supported BMP.</exception>
    public static BmpImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Decode(FileSystem.ReadAllBytes(path));
    }

    /// <summary>
    /// Decodes an uncompressed 24- or 32-bit BMP into a B8-G8-R8-A8 image. A 24-bit source is read as
    /// fully opaque. Top-down and bottom-up row orders are both handled.
    /// </summary>
    /// <exception cref="ProsperoException">The data is not a BMP this reader supports.</exception>
    public static BmpImage Decode(ReadOnlySpan<byte> bmp)
    {
        // File header (14) + at least the core BITMAPINFOHEADER (40).
        if (bmp.Length < 54 || bmp[0] != (byte)'B' || bmp[1] != (byte)'M')
            throw new ProsperoException("Not a BMP file.", -1);

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bmp[10..]);
        int dibSize = BinaryPrimitives.ReadInt32LittleEndian(bmp[14..]);
        if (dibSize < 40)
            throw new ProsperoException("Unsupported BMP header.", -1);

        int width = BinaryPrimitives.ReadInt32LittleEndian(bmp[18..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(bmp[22..]);
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(bmp[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(bmp[30..]);

        bool topDown = rawHeight < 0;
        int height = topDown ? -rawHeight : rawHeight;

        if (width <= 0 || height <= 0)
            throw new ProsperoException("Invalid BMP dimensions.", -1);
        if (compression != 0 || (bitCount != 24 && bitCount != 32))
            throw new ProsperoException("Only uncompressed 24- or 32-bit BMP is supported.", -1);

        int bytesPerPixel = bitCount / 8;
        // Rows are padded to a four-byte boundary.
        long rowStride = ((long)width * bytesPerPixel + 3) & ~3L;
        // Compare by division so a crafted width/height cannot overflow the product and slip past the
        // check; height is already known positive and the span length bounds the pixel region.
        long available = (long)bmp.Length - pixelOffset;
        if (pixelOffset < 54 || available < 0 || rowStride > available / height)
            throw new ProsperoException("BMP pixel data is truncated.", -1);

        ulong imageBytes = (ulong)width * 4 * (ulong)height;
        void* image = NativeMemory.Alloc((nuint)imageBytes);
        try
        {
            uint* pixels = (uint*)image;
            for (int y = 0; y < height; y++)
            {
                int srcRow = topDown ? y : height - 1 - y;
                ReadOnlySpan<byte> row = bmp.Slice(pixelOffset + (int)(srcRow * rowStride), (int)(width * (long)bytesPerPixel));
                uint* dst = pixels + (long)y * width;
                if (bitCount == 32)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = x * 4;
                        // Stored blue, green, red, then a reserved byte: an uncompressed BMP carries no
                        // alpha, so the fourth byte is dropped and the pixel is read fully opaque.
                        dst[x] = 0xFF000000u | ((uint)row[i + 2] << 16) | ((uint)row[i + 1] << 8) | row[i];
                    }
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = x * 3;
                        dst[x] = 0xFF000000u | ((uint)row[i + 2] << 16) | ((uint)row[i + 1] << 8) | row[i];
                    }
                }
            }
        }
        catch
        {
            NativeMemory.Free(image);
            throw;
        }
        return new BmpImage(image, width, height);
    }

    /// <summary>Releases the decoded pixels.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_pixels != null)
        {
            NativeMemory.Free(_pixels);
            _pixels = null;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the pixels if the image was dropped without a <see cref="Dispose"/> call.</summary>
    ~BmpImage() => Dispose();
}

/// <summary>
/// Writes a drawing surface to an uncompressed BMP, a dependable interchange format that needs no system
/// module. It writes a 24-bit opaque image, which every image tool reads. Use it to export a screenshot
/// or a picture without loading an encoder.
/// </summary>
public static unsafe class BmpEncoder
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    /// <summary>Encodes <paramref name="surface"/> to the bytes of a 24-bit BMP file.</summary>
    public static byte[] Encode(Surface surface)
    {
        int width = surface.Width, height = surface.Height;
        if (width <= 0 || height <= 0)
            throw new ArgumentException("The surface is empty.", nameof(surface));

        int rowStride = (width * 3 + 3) & ~3;
        int pixelBytes = rowStride * height;
        int pixelOffset = FileHeaderSize + InfoHeaderSize;
        int fileSize = pixelOffset + pixelBytes;

        byte[] output = new byte[fileSize];
        Span<byte> span = output;

        // File header.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], pixelOffset);

        // BITMAPINFOHEADER: 24-bit, bottom-up (positive height), uncompressed.
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], 1);   // planes
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], 24);  // bit count
        BinaryPrimitives.WriteInt32LittleEndian(span[34..], pixelBytes);

        uint* pixels = surface.Pixels;
        int stride = surface.Stride;
        for (int y = 0; y < height; y++)
        {
            // BMP rows run bottom-up.
            uint* srcRow = pixels + (long)(height - 1 - y) * stride;
            int rowStart = pixelOffset + y * rowStride;
            for (int x = 0; x < width; x++)
            {
                uint p = srcRow[x];
                int o = rowStart + x * 3;
                span[o] = (byte)p;            // blue
                span[o + 1] = (byte)(p >> 8); // green
                span[o + 2] = (byte)(p >> 16);// red
            }
        }
        return output;
    }

    /// <summary>Encodes <paramref name="surface"/> and writes it to the file at <paramref name="path"/>.</summary>
    public static void Save(Surface surface, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FileSystem.WriteAllBytes(path, Encode(surface));
    }
}
