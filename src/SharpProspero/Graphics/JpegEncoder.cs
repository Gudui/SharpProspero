// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Image;
using SharpProspero.Storage;
using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics;

/// <summary>
/// Encodes a drawing surface to JPEG bytes, for a screenshot or a photo export. JPEG is far smaller
/// than PNG for photographic content, which makes it the format for capture and thumbnails. The surface
/// is the display's B8-G8-R8-A8 format, so a framebuffer encodes directly. Load the JPEG-encode module
/// (<c>SystemModule.Load(SystemModuleId.JpegEnc)</c>) before encoding.
/// </summary>
public static unsafe class JpegEncoder
{
    /// <summary>
    /// Encodes <paramref name="surface"/> to the bytes of a JPEG file.
    /// </summary>
    /// <param name="surface">The pixels to encode, in the display's B8-G8-R8-A8 format.</param>
    /// <param name="quality">Picture quality, 1 (smallest) to 100 (best). Default 90.</param>
    /// <exception cref="ProsperoException">Sizing, creating the encoder, or encoding failed.</exception>
    public static byte[] Encode(Surface surface, int quality = 90)
    {
        if (surface.Width <= 0 || surface.Height <= 0)
            throw new ArgumentException("The surface has no pixels to encode.", nameof(surface));

        // Map the friendly 1..100 quality to the encoder's inverted 0..255 ratio, where zero is best.
        int level = Math.Clamp(quality, 1, 100);
        byte compressionRatio = (byte)((100 - level) * 255 / 100);

        var create = new SceJpegEncCreateParam { ThisSize = (uint)sizeof(SceJpegEncCreateParam), Attribute = 0 };
        int workSize = JpegEnc.sceJpegEncQueryMemorySize(&create);
        SceResult.ThrowIfFailed(workSize, nameof(JpegEnc.sceJpegEncQueryMemorySize));

        // The source rows step by the surface stride in bytes (four per pixel); the buffer spans every
        // row. Compute in 64-bit so the products are exact and cannot wrap the size check.
        ulong pitch = (ulong)surface.Stride * 4;
        ulong imageBytes = pitch * (ulong)surface.Height;
        // A generous upper bound for the output: at the highest quality a JPEG can approach its source
        // size, so the buffer is sized above it with room for the container.
        ulong jpegCapacity = imageBytes + (imageBytes >> 1) + 0x10000;
        if (imageBytes > uint.MaxValue || jpegCapacity > uint.MaxValue)
            throw new ProsperoException("The image is too large to encode.", unchecked((int)0x80650102));

        void* work = NativeMemory.Alloc((nuint)workSize);
        void* handle = null;
        void* jpegBuffer = null;
        try
        {
            SceResult.ThrowIfFailed(JpegEnc.sceJpegEncCreate(&create, work, (uint)workSize, &handle), nameof(JpegEnc.sceJpegEncCreate));

            jpegBuffer = NativeMemory.Alloc((nuint)jpegCapacity);
            var encode = new SceJpegEncEncodeParam
            {
                ImageMemAddr = surface.Pixels,
                JpegMemAddr = jpegBuffer,
                ImageMemSize = (uint)imageBytes,
                JpegMemSize = (uint)jpegCapacity,
                ImageWidth = (uint)surface.Width,
                ImageHeight = (uint)surface.Height,
                ImagePitch = (uint)pitch,
                PixelFormat = (ushort)JpegEncPixelFormat.Bgra8,
                EncodeMode = (ushort)JpegEncMode.Normal,
                ColorSpace = (ushort)JpegEncColorSpace.Ycc,
                SamplingType = (byte)JpegEncSamplingType.Sub420,
                CompressionRatio = compressionRatio,
                RestartInterval = 0,
            };
            SceJpegEncOutputInfo info = default;
            SceResult.ThrowIfFailed(JpegEnc.sceJpegEncEncode(handle, &encode, &info), nameof(JpegEnc.sceJpegEncEncode));

            var output = new byte[info.DataSize];
            new ReadOnlySpan<byte>(jpegBuffer, (int)info.DataSize).CopyTo(output);
            return output;
        }
        finally
        {
            if (jpegBuffer != null)
                NativeMemory.Free(jpegBuffer);
            if (handle != null)
                JpegEnc.sceJpegEncDelete(handle);
            NativeMemory.Free(work);
        }
    }

    /// <summary>
    /// Encodes <paramref name="surface"/> and writes it to the file at <paramref name="path"/>, for
    /// saving a screenshot (for example <c>/data/screenshot.jpg</c>).
    /// </summary>
    /// <exception cref="ProsperoException">Encoding or writing the file failed.</exception>
    public static void Save(Surface surface, string path, int quality = 90)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FileSystem.WriteAllBytes(path, Encode(surface, quality));
    }
}
