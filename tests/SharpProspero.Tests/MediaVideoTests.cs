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
        // pData@0, timeStamp@16, then the video details union at 24: width@24, height@28, pitch@60.
        Assert.Equal(104, Marshal.SizeOf<AvPlayerFrameInfoEx>());
        Assert.Equal(0, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.Data)));
        Assert.Equal(16, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.TimeStamp)));
        Assert.Equal(24, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.VideoWidth)));
        Assert.Equal(28, (int)Marshal.OffsetOf<AvPlayerFrameInfoEx>(nameof(AvPlayerFrameInfoEx.VideoHeight)));
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
            var frame = new VideoFrame(fp, width: 2, height: 2, pitch: 2, timeStamp: 0);
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
            var frame = new VideoFrame(fp, 2, 2, 2, 0);
            var surface = new Surface(dp, 2, 2);
            // Placed mostly off-screen; only the top-left destination pixel is written.
            frame.RenderTo(surface, 1, 1);
        }
        Assert.Equal(0u, dest[0]);       // (0,0) untouched
        Assert.NotEqual(0u, dest[3]);    // (1,1) written
    }
}
