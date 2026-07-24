using SharpProspero.Texture;
using Xunit;

namespace SharpProspero.Tests;

public sealed class QoiImageTests
{
    private static DecodedImage Gradient(int w, int h)
    {
        byte[] rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                rgba[i] = (byte)(x * 7 + y);        // varied so RGB/DIFF/LUMA all get exercised
                rgba[i + 1] = (byte)(y * 3);
                rgba[i + 2] = (byte)(x ^ y);
                rgba[i + 3] = (byte)(x % 5 == 0 ? 200 : 255); // some alpha variation
            }
        return new DecodedImage(w, h, rgba);
    }

    [Fact]
    public void EncodeDecode_RoundTripsExactly()
    {
        DecodedImage original = Gradient(37, 19);
        DecodedImage back = QoiImage.Decode(QoiImage.Encode(original));
        Assert.Equal(original.Width, back.Width);
        Assert.Equal(original.Height, back.Height);
        Assert.Equal(original.Rgba, back.Rgba);
    }

    [Fact]
    public void SolidColour_EncodesWithRuns_AndRoundTrips()
    {
        byte[] rgba = new byte[64 * 64 * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 30; rgba[i + 1] = 60; rgba[i + 2] = 90; rgba[i + 3] = 255; }
        var solid = new DecodedImage(64, 64, rgba);
        byte[] qoi = QoiImage.Encode(solid);
        Assert.True(qoi.Length < rgba.Length / 4, "a run-length image compresses well");
        Assert.Equal(rgba, QoiImage.Decode(qoi).Rgba);
    }

    [Fact]
    public void IsQoi_DetectsTheSignature_AndDecodeRejectsOthers()
    {
        Assert.True(QoiImage.IsQoi(QoiImage.Encode(Gradient(4, 4))));
        Assert.False(QoiImage.IsQoi([1, 2, 3, 4]));
        Assert.Throws<ImageFormatException>(() => QoiImage.Decode([1, 2, 3, 4]));
    }

    [Fact]
    public void DecodedImage_AutoDetectsQoi()
    {
        DecodedImage original = Gradient(9, 9);
        DecodedImage back = DecodedImage.Decode(QoiImage.Encode(original));
        Assert.Equal(original.Rgba, back.Rgba);
    }
}
