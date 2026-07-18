// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Image;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The PNG-encode structures must match the module's layout exactly, or the encoder reads the wrong
// fields. The offsets are computed from the header for the x86-64 target.
public sealed class PngEncLayoutTests
{
    [Fact]
    public void CreateParam_Is16Bytes()
    {
        Assert.Equal(16, Marshal.SizeOf<ScePngEncCreateParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<ScePngEncCreateParam>(nameof(ScePngEncCreateParam.ThisSize)));
        Assert.Equal(4, (int)Marshal.OffsetOf<ScePngEncCreateParam>(nameof(ScePngEncCreateParam.Attribute)));
        Assert.Equal(8, (int)Marshal.OffsetOf<ScePngEncCreateParam>(nameof(ScePngEncCreateParam.MaxImageWidth)));
        Assert.Equal(12, (int)Marshal.OffsetOf<ScePngEncCreateParam>(nameof(ScePngEncCreateParam.MaxFilterNumber)));
    }

    [Fact]
    public void EncodeParam_MatchesTheHeaderOffsets()
    {
        Assert.Equal(48, Marshal.SizeOf<ScePngEncEncodeParam>());
        Assert.Equal(0, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ImageMemAddr)));
        Assert.Equal(8, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.PngMemAddr)));
        Assert.Equal(16, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ImageMemSize)));
        Assert.Equal(20, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.PngMemSize)));
        Assert.Equal(24, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ImageWidth)));
        Assert.Equal(28, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ImageHeight)));
        Assert.Equal(32, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ImagePitch)));
        Assert.Equal(36, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.PixelFormat)));
        Assert.Equal(38, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ColorSpace)));
        Assert.Equal(40, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.BitDepth)));
        Assert.Equal(42, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.ClutNumber)));
        Assert.Equal(44, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.FilterType)));
        Assert.Equal(46, (int)Marshal.OffsetOf<ScePngEncEncodeParam>(nameof(ScePngEncEncodeParam.CompressionLevel)));
    }

    [Fact]
    public void OutputInfo_Is8Bytes()
    {
        Assert.Equal(8, Marshal.SizeOf<ScePngEncOutputInfo>());
        Assert.Equal(0, (int)Marshal.OffsetOf<ScePngEncOutputInfo>(nameof(ScePngEncOutputInfo.DataSize)));
        Assert.Equal(4, (int)Marshal.OffsetOf<ScePngEncOutputInfo>(nameof(ScePngEncOutputInfo.ProcessedHeight)));
    }

    [Fact]
    public void PixelFormat_MatchesTheSurface()
    {
        // The display surface is B8-G8-R8-A8, so that is what a framebuffer encodes as.
        Assert.Equal(1, (int)PngEncPixelFormat.Bgra8);
        Assert.Equal(19, (int)PngEncColorSpace.Rgba);
    }
}
