// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;
using SharpProspero.Interop;
using SharpProspero.Interop.Image;

namespace SharpProspero.Graphics;

/// <summary>
/// A decoded JPEG image held as B8-G8-R8-A8 pixels, the layout the display surface uses, so it blits
/// straight onto a framebuffer. Decode once, draw its <see cref="AsSurface"/> as often as needed, and
/// dispose it to release the pixels. Load the JPEG-decode module
/// (<c>SystemModule.Load(SystemModuleId.JpegDec)</c>) before decoding.
/// </summary>
public sealed unsafe class JpegImage : IDisposable
{
    private void* _pixels;
    private bool _disposed;

    private JpegImage(void* pixels, int width, int height)
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

    /// <summary>Decodes <paramref name="jpeg"/> into a B8-G8-R8-A8 image.</summary>
    /// <exception cref="ProsperoException">Parsing, sizing, or decoding failed.</exception>
    public static JpegImage Decode(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.IsEmpty)
            throw new ArgumentException("The JPEG data is empty.", nameof(jpeg));

        fixed (byte* source = jpeg)
        {
            SceJpegDecImageInfo info = default;
            var parse = new SceJpegDecParseParam { JpegMemAddr = source, JpegMemSize = (uint)jpeg.Length, DecodeMode = 0, DownScale = 1 };
            SceResult.ThrowIfFailed(JpegDec.sceJpegDecParseHeader(&parse, &info), nameof(JpegDec.sceJpegDecParseHeader));

            ulong width = info.OutputImageWidth;
            ulong height = info.OutputImageHeight;
            ulong imageBytes = width * 4 * height;
            if (width == 0 || height == 0 || imageBytes > uint.MaxValue)
                throw new ProsperoException(nameof(JpegDec.sceJpegDecParseHeader), unchecked((int)0x80650020));

            // Standard sampling: the decoder handles the common YCbCr layouts (4:4:4, 4:2:2, 4:2:0)
            // a typical file uses. Zero is not one of the attribute values, so it is not the default.
            var create = new SceJpegDecCreateParam { ThisSize = (uint)sizeof(SceJpegDecCreateParam), Attribute = 1, MaxImageWidth = info.ImageWidth };
            int workSize = JpegDec.sceJpegDecQueryMemorySize(&create);
            SceResult.ThrowIfFailed(workSize, nameof(JpegDec.sceJpegDecQueryMemorySize));

            void* work = NativeMemory.Alloc((nuint)workSize);
            void* handle = null;
            void* image = null;
            void* coefficient = null;
            try
            {
                SceResult.ThrowIfFailed(JpegDec.sceJpegDecCreate(&create, work, (uint)workSize, &handle), nameof(JpegDec.sceJpegDecCreate));

                image = NativeMemory.Alloc((nuint)imageBytes);
                if (info.CoefficientMemSize > 0)
                    coefficient = NativeMemory.Alloc(info.CoefficientMemSize);

                SceJpegDecImageInfo outInfo = default;
                var decode = new SceJpegDecDecodeParam
                {
                    JpegMemAddr = source,
                    ImageMemAddr = image,
                    CoefficientMemAddr = coefficient,
                    JpegMemSize = (uint)jpeg.Length,
                    ImageMemSize = (uint)imageBytes,
                    CoefficientMemSize = info.CoefficientMemSize,
                    DecodeMode = 0,
                    DownScale = 1,
                    PixelFormat = (ushort)JpegPixelFormat.Bgra8,
                    AlphaValue = 255,
                    ImagePitch = (uint)(width * 4),
                };
                SceResult.ThrowIfFailed(JpegDec.sceJpegDecDecode(handle, &decode, &outInfo), nameof(JpegDec.sceJpegDecDecode));
            }
            catch
            {
                if (coefficient != null)
                    NativeMemory.Free(coefficient);
                if (image != null)
                    NativeMemory.Free(image);
                if (handle != null)
                    JpegDec.sceJpegDecDelete(handle);
                NativeMemory.Free(work);
                throw;
            }

            if (coefficient != null)
                NativeMemory.Free(coefficient);
            JpegDec.sceJpegDecDelete(handle);
            NativeMemory.Free(work);
            return new JpegImage(image, (int)width, (int)height);
        }
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
    ~JpegImage() => Dispose();
}
