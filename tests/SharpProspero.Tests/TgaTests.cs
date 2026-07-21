// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Interop;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class TgaTests
{
    private static readonly uint[] Colors =
    [
        0xFF112233u, 0xFF445566u, 0xFF778899u,
        0xFFAABBCCu, 0xFFDDEEFFu, 0xFF010203u,
    ];

    [Fact]
    public void Encode32ThenDecode_RoundTrips()
    {
        using var buffer = new PixelBuffer(3, 2);
        Surface source = buffer.AsSurface();
        for (int i = 0; i < Colors.Length; i++)
            source.Pixels[i] = Colors[i];

        byte[] tga = TgaEncoder.Encode(source, includeAlpha: true);
        using TgaImage image = TgaImage.Decode(tga);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);

        Surface decoded = image.AsSurface();
        for (int i = 0; i < Colors.Length; i++)
            Assert.Equal(Colors[i], decoded.Pixels[i]);
    }

    [Fact]
    public void Encode24_DropsAlphaToOpaque()
    {
        using var buffer = new PixelBuffer(2, 1);
        Surface source = buffer.AsSurface();
        source.Pixels[0] = 0x80112233u; // half alpha
        source.Pixels[1] = 0x40445566u;

        using TgaImage image = TgaImage.Decode(TgaEncoder.Encode(source, includeAlpha: false));
        Surface decoded = image.AsSurface();
        Assert.Equal(0xFF112233u, decoded.Pixels[0]); // colour kept, alpha now opaque
        Assert.Equal(0xFF445566u, decoded.Pixels[1]);
    }

    [Fact]
    public void Decode_BottomUpRawIsFlippedUpright()
    {
        // A 2x2 uncompressed 32-bit TGA with a bottom-left origin (descriptor 0), so the file's first row
        // is the image's bottom row.
        byte[] tga =
        [
            0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 32, 0,
            7, 8, 9, 255, 10, 11, 12, 255, // file row 0 = image bottom row
            1, 2, 3, 255, 4, 5, 6, 255,    // file row 1 = image top row
        ];

        using TgaImage image = TgaImage.Decode(tga);
        Surface s = image.AsSurface();
        Assert.Equal(0xFF030201u, s.Pixels[0]); // top-left
        Assert.Equal(0xFF060504u, s.Pixels[1]);
        Assert.Equal(0xFF090807u, s.Pixels[2]); // bottom-left
        Assert.Equal(0xFF0C0B0Au, s.Pixels[3]);
    }

    [Fact]
    public void Decode_RunLengthEncoded()
    {
        // A 4x1 top-to-bottom RLE TGA: a run of three, then one literal.
        byte[] tga =
        [
            0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 1, 0, 32, 0x20,
            0x82, 10, 20, 30, 255, // run packet: 3 x (B10,G20,R30)
            0x00, 40, 50, 60, 255, // raw packet: 1 x (B40,G50,R60)
        ];

        using TgaImage image = TgaImage.Decode(tga);
        Surface s = image.AsSurface();
        Assert.Equal(0xFF1E140Au, s.Pixels[0]);
        Assert.Equal(0xFF1E140Au, s.Pixels[1]);
        Assert.Equal(0xFF1E140Au, s.Pixels[2]);
        Assert.Equal(0xFF3C3228u, s.Pixels[3]);
    }

    [Fact]
    public void Decode_RejectsUnsupportedForms()
    {
        byte[] colorMapped = new byte[18];
        colorMapped[1] = 1; // colour-map type
        colorMapped[2] = 1;
        colorMapped[12] = 1; colorMapped[14] = 1; colorMapped[16] = 24;
        Assert.Throws<ProsperoException>(() => TgaImage.Decode(colorMapped));

        byte[] grayscale = new byte[18];
        grayscale[2] = 3; // grayscale image type
        grayscale[12] = 1; grayscale[14] = 1; grayscale[16] = 8;
        Assert.Throws<ProsperoException>(() => TgaImage.Decode(grayscale));
    }

    [Fact]
    public void Decode_TruncatedIdBlock_ThrowsProsperoException()
    {
        // The header claims a 200-byte image-id block that the 21-byte buffer cannot hold. The decoder
        // must reject it with its own exception, not let the slice throw ArgumentOutOfRangeException.
        byte[] tga = new byte[21];
        tga[0] = 200;  // id length
        tga[2] = 2;    // uncompressed true-colour
        tga[12] = 1;   // width 1
        tga[14] = 1;   // height 1
        tga[16] = 24;  // depth
        Assert.Throws<ProsperoException>(() => TgaImage.Decode(tga));
    }

    [Fact]
    public void Decode_OversizedDimensions_ThrowsProsperoException()
    {
        // 46341 x 46341 x 4 bytes exceeds a signed int; rejecting it up front avoids the pixel-count
        // math overflowing to a negative or wrapped value.
        byte[] tga = new byte[18];
        tga[2] = 2;
        tga[12] = 0x05; tga[13] = 0xB5; // width  46341
        tga[14] = 0x05; tga[15] = 0xB5; // height 46341
        tga[16] = 32;
        Assert.Throws<ProsperoException>(() => TgaImage.Decode(tga));
    }
}
