// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Vision;
using Xunit;

namespace SharpProspero.Tests;

// The depth library runs on the device with the camera; off the device these pin the sizes and layouts of
// the parameter structures passed across the boundary and the constants transcribed from the header.
public sealed class Depth2Tests
{
    [Fact]
    public unsafe void ParameterStructsHaveTheHeaderLayout()
    {
        Assert.Equal(16, sizeof(SceDepth2QueryMemoryResult));      // size_t + size_t
        Assert.Equal(16, sizeof(SceDepth2MemoryInformation));      // void* + size_t
        Assert.Equal(12, sizeof(SceDepth2InputImageInformation));  // int + int + enum(int)
        Assert.Equal(28, sizeof(SceDepth2ProcessingInformation));  // seven 4-byte fields
        Assert.Equal(16, sizeof(SceDepth2PlatformInformation));    // int + int + void*
        Assert.Equal(64, sizeof(SceDepth2InitializeParameter));    // 4 + 12 + 28, padded to 8 before the 16-byte platform block
    }

    [Fact]
    public void EnumValuesMatchTheHeader()
    {
        Assert.Equal(0, (int)SceDepth2PixelFormat.Y8);
        Assert.Equal(1, (int)SceDepth2PixelFormat.Yuv422);
        Assert.Equal(2, (int)SceDepth2Profile.Profile15);
        Assert.Equal(3, (int)SceDepth2Profile.Profile20);
        Assert.Equal(4, (int)SceDepth2Profile.Profile16);
        Assert.Equal(1, (int)SceDepth2StereoCameraType.HdCamera);
        Assert.Equal(1, (int)SceDepth2ExecutionMode.DoNotCopySourceImage);
        Assert.Equal(0, (int)SceDepth2ImageType.Depth16Bit);
    }

    [Fact]
    public void InvalidDepthValueMatchesTheHeader()
    {
        Assert.Equal(0xffff, Depth2.InvalidDepthValue);
    }
}
