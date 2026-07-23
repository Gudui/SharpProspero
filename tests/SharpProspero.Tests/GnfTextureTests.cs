using SharpProspero.Texture;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace SharpProspero.Tests;

public class GnfTextureTests
{
    private static byte[] SolidRgba(int width, int height, byte r, byte g, byte b, byte a)
    {
        byte[] px = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = a;
        }
        return px;
    }

    private static uint Word(byte[] gnf, int textureIndex, int wordIndex)
        => BinaryPrimitives.ReadUInt32LittleEndian(gnf.AsSpan(16 + textureIndex * 32 + wordIndex * 4, 4));

    [Fact]
    public void Build_WritesContainerHeader()
    {
        byte[] gnf = GnfWriter.BuildLinear2D(SolidRgba(64, 32, 1, 2, 3, 4), 64, 32);

        Assert.Equal(0x20464E47u, BinaryPrimitives.ReadUInt32LittleEndian(gnf));   // "GNF "
        uint contentsSize = BinaryPrimitives.ReadUInt32LittleEndian(gnf.AsSpan(4));
        Assert.Equal(248u, contentsSize);                                          // pixel data starts at 256
        Assert.Equal(4, gnf[8]);                                                   // current console version
        Assert.Equal(1, gnf[9]);                                                   // one texture
        Assert.Equal(8, gnf[10]);                                                  // log2(256) global alignment
        Assert.Equal(0, gnf[11]);
        uint streamSize = BinaryPrimitives.ReadUInt32LittleEndian(gnf.AsSpan(12));
        Assert.Equal((uint)gnf.Length, streamSize);
        // 64 is a multiple of 64, so the pitch equals the width: 256 + 64*32*4.
        Assert.Equal(256 + 64 * 32 * 4, gnf.Length);
    }

    [Fact]
    public void Build_DescriptorCarriesShapeAndFileMetadata()
    {
        const int w = 100, h = 40; // width not a multiple of 64 -> padded pitch
        byte[] gnf = GnfWriter.BuildLinear2D(SolidRgba(w, h, 10, 20, 30, 40), w, h);

        uint w0 = Word(gnf, 0, 0), w1 = Word(gnf, 0, 1), w2 = Word(gnf, 0, 2), w3 = Word(gnf, 0, 3), w4 = Word(gnf, 0, 4), w7 = Word(gnf, 0, 7);

        Assert.Equal(0u, w0);                                   // pixel offset (single texture)
        Assert.Equal(8u, w1 & 0xFF);                            // log2 per-texture alignment
        Assert.Equal(56u, (w1 >> 20) & 0x1FF);                  // nine-bit format k8_8_8_8UNorm

        // Width-1 is split: low two bits in word1 bits 30-31, high twelve bits in word2 bits 0-11.
        uint widthLo = (w1 >> 30) & 0x3;
        uint widthHi = w2 & 0xFFF;
        Assert.Equal((uint)(w - 1), widthLo | (widthHi << 2));
        Assert.Equal((uint)(h - 1), (w2 >> 14) & 0x3FFF);       // height - 1

        Assert.Equal(4u, w3 & 0x7);                             // dst X <- Red
        Assert.Equal(5u, (w3 >> 3) & 0x7);                      // dst Y <- Green
        Assert.Equal(6u, (w3 >> 6) & 0x7);                      // dst Z <- Blue
        Assert.Equal(7u, (w3 >> 9) & 0x7);                      // dst W <- Alpha
        Assert.Equal(0u, (w3 >> 20) & 0x1F);                    // linear tiling
        Assert.Equal(9u, (w3 >> 28) & 0x0F);                    // 2D texture

        Assert.Equal(0u, w4);                                   // single-slice 2D: no depth, array, or pitch field

        int pitch = ((w + 63) / 64) * 64;                       // 128
        Assert.Equal((uint)(pitch * h * 4), w7);                // pixel size in bytes
    }

    [Fact]
    public void Build_LaysPixelsAtPaddedPitchWithZeroPadding()
    {
        const int w = 3, h = 2;
        byte[] rgba =
        [
            1,2,3,4,   5,6,7,8,   9,10,11,12,       // row 0
            13,14,15,16, 17,18,19,20, 21,22,23,24,  // row 1
        ];
        byte[] gnf = GnfWriter.BuildLinear2D(rgba, w, h);

        int pitchTexels = 64;              // 3 padded up to 64
        int pitchBytes = pitchTexels * 4;
        int pixelStart = 256;

        // Row 0 pixels are copied verbatim.
        for (int i = 0; i < w * 4; i++)
            Assert.Equal(rgba[i], gnf[pixelStart + i]);
        // The row padding after the real pixels is zero.
        Assert.Equal(0, gnf[pixelStart + w * 4]);
        // Row 1 sits one pitch later.
        for (int i = 0; i < w * 4; i++)
            Assert.Equal(rgba[w * 4 + i], gnf[pixelStart + pitchBytes + i]);
    }

    [Fact]
    public void GnfReader_ReadsBackTheHeader()
    {
        byte[] gnf = GnfWriter.BuildLinear2D(SolidRgba(200, 150, 0, 0, 0, 255), 200, 150);
        GnfInfo info = GnfReader.Read(gnf);

        Assert.Equal(4, info.Version);
        Assert.Equal(1, info.TextureCount);
        Assert.Equal(256, info.Alignment);
        Assert.Equal(gnf.Length, info.StreamSize);
        Assert.Equal(200, info.Width);
        Assert.Equal(150, info.Height);
        Assert.Equal(56, info.DataFormat);   // k8_8_8_8UNorm
        Assert.Equal(0, info.TileMode);
        int pitch = ((200 + 63) / 64) * 64;
        Assert.Equal((long)pitch * 150 * 4, info.PixelSize);
    }

    [Fact]
    public void Srgb_SelectsSrgbFormat()
    {
        byte[] gnf = GnfWriter.BuildLinear2D(SolidRgba(64, 64, 1, 1, 1, 1), 64, 64, srgb: true);
        uint w1 = Word(gnf, 0, 1);
        Assert.Equal(130u, (w1 >> 20) & 0x1FF); // k8_8_8_8Srgb
    }

    // --- Decoder round-trips ---

    private static byte[] MakePng(int w, int h, byte colorType, byte[] samples, int channels, byte[]? trns = null)
    {
        static void WriteChunk(MemoryStream s, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            s.Write(len);
            foreach (char c in type) s.WriteByte((byte)c);
            s.Write(data);
            s.Write(stackalloc byte[4]); // CRC placeholder (the decoder does not check it)
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = colorType; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);
        if (trns is not null) WriteChunk(ms, "tRNS", trns);

        // Filtered scanlines: filter 0 (none) + row samples.
        int stride = w * channels;
        byte[] raw = new byte[(stride + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(samples, y * stride, raw, y * (stride + 1) + 1, stride);
        }
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        WriteChunk(ms, "IDAT", comp.ToArray());
        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    [Fact]
    public void Png_Rgba_RoundTrips()
    {
        byte[] samples = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160];
        byte[] png = MakePng(2, 2, colorType: 6, samples, channels: 4);
        DecodedImage img = DecodedImage.Decode(png);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(samples, img.Rgba);
    }

    [Fact]
    public void Png_Rgb_ExpandsAlphaToOpaque()
    {
        byte[] samples = [10, 20, 30, 40, 50, 60];
        byte[] png = MakePng(2, 1, colorType: 2, samples, channels: 3);
        DecodedImage img = DecodedImage.Decode(png);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }, img.Rgba);
    }

    [Fact]
    public void Png_Grayscale_ReplicatesChannels()
    {
        byte[] samples = [128, 200];
        byte[] png = MakePng(2, 1, colorType: 0, samples, channels: 1);
        DecodedImage img = DecodedImage.Decode(png);
        Assert.Equal(new byte[] { 128, 128, 128, 255, 200, 200, 200, 255 }, img.Rgba);
    }

    [Fact]
    public void Png_Grayscale_ColorKeyIsTransparent()
    {
        // Two gray pixels; tRNS marks gray 128 fully transparent (stored as a 16-bit sample: 0x0080).
        byte[] samples = [128, 200];
        byte[] png = MakePng(2, 1, colorType: 0, samples, channels: 1, trns: [0x00, 0x80]);
        DecodedImage img = DecodedImage.Decode(png);
        Assert.Equal(new byte[] { 128, 128, 128, 0, 200, 200, 200, 255 }, img.Rgba);
    }

    [Fact]
    public void Bmp_32Bit_ZeroReservedByteIsOpaque()
    {
        // 1x1, 32-bit BI_RGB with a zero reserved byte must decode opaque, not transparent.
        byte[] file = new byte[54 + 4];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 32);
        file[54] = 30; file[55] = 20; file[56] = 10; file[57] = 0; // B,G,R,reserved=0
        DecodedImage img = DecodedImage.Decode(file);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, img.Rgba);
    }

    [Fact]
    public void Bmp_24Bit_RoundTrips()
    {
        // 2x1, 24-bit, bottom-up. Row padded to 4 bytes. BGR order in file.
        int w = 2, h = 1;
        int stride = ((w * 3) + 3) & ~3; // 8
        byte[] file = new byte[54 + stride * h];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 54);      // pixel offset
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), 40);      // header size
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), h);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 24);
        // pixel 0 = B,G,R = 30,20,10 ; pixel 1 = 60,50,40
        file[54] = 30; file[55] = 20; file[56] = 10;
        file[57] = 60; file[58] = 50; file[59] = 40;
        DecodedImage img = DecodedImage.Decode(file);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }, img.Rgba);
    }

    [Fact]
    public void Tga_Uncompressed32_RoundTrips()
    {
        // 2x1, uncompressed true colour, 32-bit, top origin. BGRA in file.
        byte[] file = new byte[18 + 2 * 4];
        file[2] = 2;                                    // uncompressed true colour
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 2); // width
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(14), 1); // height
        file[16] = 32;                                  // depth
        file[17] = 0x20;                                // top origin
        // pixel 0 = B,G,R,A ; pixel 1
        file[18] = 30; file[19] = 20; file[20] = 10; file[21] = 200;
        file[22] = 60; file[23] = 50; file[24] = 40; file[25] = 255;
        DecodedImage img = DecodedImage.Decode(file);
        Assert.Equal(new byte[] { 10, 20, 30, 200, 40, 50, 60, 255 }, img.Rgba);
    }

    [Fact]
    public void Build_RejectsOutOfRangeDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GnfWriter.BuildLinear2D(new byte[4], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GnfWriter.BuildLinear2D(new byte[4], 20000, 1));
    }
}
