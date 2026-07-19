// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Interop;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class ImageCodecTests
{
    [Fact]
    public void Bmp_RoundTripsColorsAndDimensions()
    {
        const int w = 5, h = 3;
        uint[] pixels = new uint[w * h];
        byte[] encoded;
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, w, h);
            surface.Clear(Color.FromRgb(10, 20, 30));
            surface.SetPixel(0, 0, Color.FromRgb(255, 0, 0));
            surface.SetPixel(4, 0, Color.FromRgb(0, 255, 0));
            surface.SetPixel(0, 2, Color.FromRgb(0, 0, 255));
            surface.SetPixel(4, 2, Color.FromRgb(1, 2, 3));
            encoded = BmpEncoder.Encode(surface);
        }

        // "BM" magic and a plausible size.
        Assert.Equal((byte)'B', encoded[0]);
        Assert.Equal((byte)'M', encoded[1]);

        using BmpImage decoded = BmpImage.Decode(encoded);
        Assert.Equal(w, decoded.Width);
        Assert.Equal(h, decoded.Height);

        Surface ds = decoded.AsSurface();
        Assert.Equal(0xFFFF0000u, ds.Pixels[0]);              // red, opaque
        Assert.Equal(0xFF00FF00u, ds.Pixels[4]);              // green
        Assert.Equal(0xFF0000FFu, ds.Pixels[2 * w]);          // blue
        Assert.Equal(0xFF010203u, ds.Pixels[2 * w + 4]);      // arbitrary color
        Assert.Equal(0xFF0A141Eu, ds.Pixels[w + 2]);          // background
    }

    [Fact]
    public void Bmp_RejectsNonBmp()
    {
        Assert.Throws<ProsperoException>(() => BmpImage.Decode([1, 2, 3, 4]));
        Assert.Throws<ProsperoException>(() => BmpImage.Decode(new byte[100]));
    }

    [Fact]
    public void Bmp_Decodes32BitAsOpaque()
    {
        // A 1x1 32-bit BMP whose reserved byte is zero must decode fully opaque, not transparent.
        byte[] bmp = new byte[54 + 4];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54); // pixel offset
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40); // DIB size
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);  // width
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);  // height
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1); // planes
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 32);// bit count
        // Pixel bytes: blue=0, green=0, red=255, reserved=0.
        bmp[54] = 0; bmp[55] = 0; bmp[56] = 255; bmp[57] = 0;

        using BmpImage decoded = BmpImage.Decode(bmp);
        Assert.Equal(0xFFFF0000u, decoded.AsSurface().Pixels[0]); // opaque red, not transparent
    }

    [Fact]
    public void Bmp_RejectsOverflowingDimensions()
    {
        // A crafted header with huge width/height must be rejected with ProsperoException, not crash
        // with an out-of-memory or overflow.
        byte[] bmp = new byte[54];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), int.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), int.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 32);

        Assert.Throws<ProsperoException>(() => BmpImage.Decode(bmp));
    }
}
