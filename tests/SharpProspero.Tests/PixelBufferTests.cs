// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class PixelBufferTests
{
    [Fact]
    public void StartsTransparentAndDrawsThrough()
    {
        using var buffer = new PixelBuffer(4, 4);
        Assert.Equal(4, buffer.Width);
        Assert.Equal(4, buffer.Height);

        Surface surface = buffer.AsSurface();
        Assert.Equal(0u, surface.Pixels[0]); // zeroed on creation

        buffer.Clear(Color.Red);
        Assert.Equal(Color.Red.Value, surface.Pixels[0]);

        surface.SetPixel(1, 1, Color.White);
        Assert.Equal(Color.White.Value, buffer.AsSurface().Pixels[(1 * 4) + 1]);
    }

    [Fact]
    public void AsSurfaceThrowsAfterDispose()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { buffer.AsSurface(); });
        buffer.Dispose(); // a second dispose is harmless
    }

    [Fact]
    public void RejectsBadSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(4, -1));
    }
}
