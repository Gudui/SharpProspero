using SharpProspero.Texture;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class ImageOpsTests
{
    // A width x height image where each pixel's channels are set from a per-pixel function returning R,G,B,A.
    private static DecodedImage Make(int width, int height, Func<int, int, (byte, byte, byte, byte)> pixel)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b, byte a) = pixel(x, y);
                int i = (y * width + x) * 4;
                rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
            }
        return new DecodedImage(width, height, rgba);
    }

    [Fact]
    public void Resize_ChangesTheDimensions()
    {
        DecodedImage r = ImageOps.Resize(Make(4, 4, (_, _) => (10, 20, 30, 255)), 8, 2);
        Assert.Equal(8, r.Width);
        Assert.Equal(2, r.Height);
    }

    [Fact]
    public void Resize_KeepsASolidColour()
    {
        DecodedImage r = ImageOps.Resize(Make(3, 3, (_, _) => (40, 80, 120, 200)), 7, 5);
        for (int i = 0; i < r.Rgba.Length; i += 4)
        {
            Assert.Equal(40, r.Rgba[i]);
            Assert.Equal(80, r.Rgba[i + 1]);
            Assert.Equal(120, r.Rgba[i + 2]);
            Assert.Equal(200, r.Rgba[i + 3]);
        }
    }

    [Fact]
    public void Crop_TakesTheRegion()
    {
        // Red byte encodes x, green encodes y.
        DecodedImage image = Make(4, 4, (x, y) => ((byte)x, (byte)y, 0, 255));
        DecodedImage c = ImageOps.Crop(image, 1, 2, 2, 1);
        Assert.Equal(2, c.Width);
        Assert.Equal(1, c.Height);
        Assert.Equal(1, c.Rgba[0]);  // x of the first pixel
        Assert.Equal(2, c.Rgba[1]);  // y
        Assert.Equal(2, c.Rgba[4]);  // x of the second pixel
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Crop(image, 3, 3, 2, 2));
    }

    [Fact]
    public void FlipVertical_ReversesRows()
    {
        DecodedImage image = Make(1, 3, (_, y) => ((byte)y, 0, 0, 255));
        DecodedImage f = ImageOps.FlipVertical(image);
        Assert.Equal(2, f.Rgba[0]);            // top row is now the old bottom
        Assert.Equal(0, f.Rgba[2 * 4]);         // bottom row is the old top
    }

    [Fact]
    public void FlipHorizontal_ReversesColumns()
    {
        DecodedImage image = Make(3, 1, (x, _) => ((byte)x, 0, 0, 255));
        DecodedImage f = ImageOps.FlipHorizontal(image);
        Assert.Equal(2, f.Rgba[0]);            // leftmost is now the old rightmost
        Assert.Equal(0, f.Rgba[2 * 4]);
    }
}
