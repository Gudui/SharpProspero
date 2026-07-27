// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Interop.Media;
using SharpProspero.Media;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class MediaVideoTests
{
    [Fact]
    public void FrameInfoEx_MatchesTheHeader()
    {
        // pData@0, timeStamp@16, then the video details union at 24: width@24, height@28, the four
        // crop insets at 44/48/52/56, and pitch@60.
        Assert.Equal(104, Marshal.SizeOf<AvPlayerFrameInfoEx>());
        Assert.Equal(0, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.Data)));
        Assert.Equal(16, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.TimeStamp)));
        Assert.Equal(24, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.VideoWidth)));
        Assert.Equal(28, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.VideoHeight)));
        Assert.Equal(44, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.CropLeft)));
        Assert.Equal(48, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.CropRight)));
        Assert.Equal(52, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.CropTop)));
        Assert.Equal(56, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.CropBottom)));
        Assert.Equal(60, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.VideoPitch)));
    }

    [Fact]
    public void RenderTo_ConvertsNeutralNv12ToGray()
    {
        // A 2x2 NV12 frame with luma 126 and neutral chroma (128) converts to mid gray under the
        // limited-range BT.601 matrix: (298*(126-16)+128)>>8 = 128 for each channel.
        byte[] frameData = [126, 126, 126, 126, 128, 128]; // Y plane (pitch 2 x height 2), then one UV row
        uint[] dest = new uint[4];
        fixed (byte* fp = frameData)
        fixed (uint* dp = dest)
        {
            var frame = new VideoFrame(fp, width: 2, height: 2, pitch: 2, timeStamp: 0,
                cropLeft: 0, cropRight: 0, cropTop: 0, cropBottom: 0);
            var surface = new Surface(dp, 2, 2);
            frame.RenderTo(surface, 0, 0);
        }
        foreach (uint pixel in dest)
            Assert.Equal(0xFF808080u, pixel);
    }

    [Fact]
    public void RenderTo_ClipsToDestinationBounds()
    {
        byte[] frameData = [200, 200, 200, 200, 128, 128];
        uint[] dest = new uint[4]; // 2x2 surface
        fixed (byte* fp = frameData)
        fixed (uint* dp = dest)
        {
            var frame = new VideoFrame(fp, 2, 2, 2, 0, 0, 0, 0, 0);
            var surface = new Surface(dp, 2, 2);
            // Placed mostly off-screen; only the top-left destination pixel is written.
            frame.RenderTo(surface, 1, 1);
        }
        Assert.Equal(0u, dest[0]);       // (0,0) untouched
        Assert.NotEqual(0u, dest[3]);    // (1,1) written
    }

    [Fact]
    public void RenderTo_ReadsOnlyThePictureInsideTheBuffer()
    {
        // A 4x4 buffer whose picture is the middle 2x2: one column cropped either side and one row top
        // and bottom. The picture is dark (luma 16 -> black) and everything around it is bright, so a
        // render that ignored the insets would put something other than black on the surface.
        byte[] frameData = new byte[4 * 4 + 4 * 2];
        for (int i = 0; i < frameData.Length; i++)
            frameData[i] = 255;
        for (int row = 1; row <= 2; row++)
            for (int col = 1; col <= 2; col++)
                frameData[row * 4 + col] = 16;
        // Neutral chroma throughout, so what the pixels come out as is decided by the luma alone.
        for (int i = 16; i < frameData.Length; i++)
            frameData[i] = 128;

        uint[] dest = new uint[4];
        fixed (byte* fp = frameData)
        fixed (uint* dp = dest)
        {
            var frame = new VideoFrame(fp, width: 4, height: 4, pitch: 4, timeStamp: 0,
                cropLeft: 1, cropRight: 1, cropTop: 1, cropBottom: 1);
            Assert.Equal(2, frame.VisibleWidth);
            Assert.Equal(2, frame.VisibleHeight);
            var surface = new Surface(dp, 2, 2);
            frame.RenderTo(surface, 0, 0);
        }
        foreach (uint pixel in dest)
            Assert.Equal(0xFF000000u, pixel);
    }
}
