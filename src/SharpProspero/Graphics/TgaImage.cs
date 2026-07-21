// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Storage;
using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics;

/// <summary>
/// A decoded TGA (Targa) image held as B8-G8-R8-A8 pixels, the layout the display uses, so it blits
/// straight onto a framebuffer. TGA is a simple lossless format that tools and asset pipelines commonly
/// export, and unlike BMP it carries a proper alpha channel and an optional run-length compression.
/// Decode once, draw its <see cref="AsSurface"/> as often as needed, and dispose it to release the
/// pixels. It needs no system module.
/// </summary>
public sealed unsafe class TgaImage : IDisposable
{
    private void* _pixels;
    private bool _disposed;

    private TgaImage(void* pixels, int width, int height)
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

    /// <summary>Loads and decodes the TGA file at <paramref name="path"/>.</summary>
    /// <exception cref="ProsperoException">The file could not be read or is not a supported TGA.</exception>
    public static TgaImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Decode(FileSystem.ReadAllBytes(path));
    }

    /// <summary>
    /// Decodes a true-colour TGA — 24- or 32-bit, uncompressed or run-length encoded — into a
    /// B8-G8-R8-A8 image. Colour-mapped and other forms are rejected.
    /// </summary>
    /// <exception cref="ProsperoException">The data is not a supported TGA.</exception>
    public static TgaImage Decode(ReadOnlySpan<byte> tga)
    {
        if (tga.Length < 18)
            throw new ProsperoException("Not a TGA file.", -1);

        int idLength = tga[0];
        int colorMapType = tga[1];
        int imageType = tga[2];
        int width = tga[12] | (tga[13] << 8);
        int height = tga[14] | (tga[15] << 8);
        int depth = tga[16];
        bool topToBottom = (tga[17] & 0x20) != 0;

        if (colorMapType != 0)
            throw new ProsperoException("Colour-mapped TGA is not supported.", -1);
        if (imageType != 2 && imageType != 10)
            throw new ProsperoException("Only uncompressed or run-length true-colour TGA is supported.", -1);
        if (depth != 24 && depth != 32)
            throw new ProsperoException("Only 24- or 32-bit TGA is supported.", -1);
        if (width <= 0 || height <= 0)
            throw new ProsperoException("The TGA has no image.", -1);

        // The pixel count is bounded by two 16-bit fields, so the byte total fits comfortably in 64 bits.
        ulong imageBytes = (ulong)width * 4 * (ulong)height;
        int bytesPerPixel = depth / 8;
        // Keep the buffer within a signed int so the pixel-count and offset math in the decoders cannot
        // overflow, and reject a header whose id block runs past the end of the data.
        if (imageBytes > int.MaxValue)
            throw new ProsperoException("The TGA image is too large.", -1);
        if (18 + idLength > tga.Length)
            throw new ProsperoException("The TGA data is truncated.", -1);
        ReadOnlySpan<byte> data = tga[(18 + idLength)..];

        void* image = NativeMemory.Alloc((nuint)imageBytes);
        try
        {
            uint* pixels = (uint*)image;
            if (imageType == 2)
                DecodeRaw(data, pixels, width, height, bytesPerPixel, topToBottom);
            else
                DecodeRle(data, pixels, width, height, bytesPerPixel, topToBottom);
        }
        catch
        {
            NativeMemory.Free(image);
            throw;
        }

        return new TgaImage(image, width, height);
    }

    private static void DecodeRaw(ReadOnlySpan<byte> data, uint* pixels, int width, int height, int bytesPerPixel, bool topToBottom)
    {
        int count = width * height;
        if (data.Length < count * bytesPerPixel)
            throw new ProsperoException("The TGA pixel data is truncated.", -1);
        for (int i = 0; i < count; i++)
            Store(pixels, i, Pixel(data, i * bytesPerPixel, bytesPerPixel), width, height, topToBottom);
    }

    private static void DecodeRle(ReadOnlySpan<byte> data, uint* pixels, int width, int height, int bytesPerPixel, bool topToBottom)
    {
        int total = width * height;
        int produced = 0, position = 0;
        while (produced < total)
        {
            if (position >= data.Length)
                throw new ProsperoException("The TGA pixel data is truncated.", -1);
            byte packet = data[position++];
            int runLength = (packet & 0x7F) + 1;

            if ((packet & 0x80) != 0)
            {
                // A run packet: one pixel repeated.
                if (position + bytesPerPixel > data.Length)
                    throw new ProsperoException("The TGA pixel data is truncated.", -1);
                uint value = Pixel(data, position, bytesPerPixel);
                position += bytesPerPixel;
                for (int k = 0; k < runLength && produced < total; k++)
                    Store(pixels, produced++, value, width, height, topToBottom);
            }
            else
            {
                // A raw packet: literal pixels.
                for (int k = 0; k < runLength && produced < total; k++)
                {
                    if (position + bytesPerPixel > data.Length)
                        throw new ProsperoException("The TGA pixel data is truncated.", -1);
                    Store(pixels, produced++, Pixel(data, position, bytesPerPixel), width, height, topToBottom);
                    position += bytesPerPixel;
                }
            }
        }
    }

    private static uint Pixel(ReadOnlySpan<byte> data, int offset, int bytesPerPixel)
    {
        byte b = data[offset];
        byte g = data[offset + 1];
        byte r = data[offset + 2];
        byte a = bytesPerPixel == 4 ? data[offset + 3] : (byte)255;
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    // TGA rows run from the origin corner; when the origin is at the bottom the file's first row is the
    // image's last, so the destination row is flipped unless the top-to-bottom flag is set.
    private static void Store(uint* pixels, int index, uint value, int width, int height, bool topToBottom)
    {
        int sourceRow = index / width;
        int column = index % width;
        int destinationRow = topToBottom ? sourceRow : height - 1 - sourceRow;
        pixels[(destinationRow * width) + column] = value;
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
    ~TgaImage() => Dispose();
}

/// <summary>
/// Encodes a drawing surface to the bytes of an uncompressed TGA file, keeping the alpha channel by
/// default. Use it to save a drawing, a screenshot, or a generated texture in a format editors read.
/// </summary>
public static unsafe class TgaEncoder
{
    /// <summary>
    /// Encodes <paramref name="surface"/> to TGA bytes. With <paramref name="includeAlpha"/> set (the
    /// default) it writes 32-bit BGRA; otherwise 24-bit BGR.
    /// </summary>
    public static byte[] Encode(Surface surface, bool includeAlpha = true)
    {
        int width = surface.Width, height = surface.Height;
        int bytesPerPixel = includeAlpha ? 4 : 3;
        byte[] output = new byte[18 + (width * height * bytesPerPixel)];

        output[2] = 2; // uncompressed true-colour
        output[12] = (byte)(width & 0xFF);
        output[13] = (byte)(width >> 8);
        output[14] = (byte)(height & 0xFF);
        output[15] = (byte)(height >> 8);
        output[16] = (byte)(bytesPerPixel * 8);
        // Alpha-channel depth in the low bits, and the top-to-bottom flag so the rows are written in order.
        output[17] = (byte)((includeAlpha ? 0x08 : 0x00) | 0x20);

        int offset = 18;
        for (int y = 0; y < height; y++)
        {
            uint* row = surface.Pixels + ((long)y * surface.Stride);
            for (int x = 0; x < width; x++)
            {
                uint pixel = row[x];
                output[offset++] = (byte)pixel;         // B
                output[offset++] = (byte)(pixel >> 8);  // G
                output[offset++] = (byte)(pixel >> 16); // R
                if (includeAlpha)
                    output[offset++] = (byte)(pixel >> 24); // A
            }
        }
        return output;
    }

    /// <summary>Encodes <paramref name="surface"/> and writes it to the file at <paramref name="path"/>.</summary>
    public static void Save(Surface surface, string path, bool includeAlpha = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FileSystem.WriteAllBytes(path, Encode(surface, includeAlpha));
    }
}
