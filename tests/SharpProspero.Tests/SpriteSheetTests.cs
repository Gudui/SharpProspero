// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

// A sprite sheet reads frames as views onto one image. The checks fill each frame's cell with a
// distinct colour and confirm the frame view lands on the right cell, and that drawing a frame copies
// the right pixels.
public sealed unsafe class SpriteSheetTests
{
    // A 4x2 grid of 2x2-pixel frames: each frame filled with its own index as the colour.
    private static void WithSheet(Action<SpriteSheet, uint[]> action)
    {
        const int fw = 2, fh = 2, cols = 4, rows = 2;
        int w = fw * cols, h = fh * rows;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, w, h);
            for (int index = 0; index < cols * rows; index++)
            {
                int fx = (index % cols) * fw;
                int fy = (index / cols) * fh;
                surface.FillRect(fx, fy, fw, fh, Color.FromRgb((byte)(index + 1), 0, 0));
            }
            action(new SpriteSheet(surface, fw, fh), pixels);
        }
    }

    [Fact]
    public void CountsFramesFromTheSheetAndFrameSize()
    {
        WithSheet((sheet, _) =>
        {
            Assert.Equal(4, sheet.Columns);
            Assert.Equal(2, sheet.Rows);
            Assert.Equal(8, sheet.Count);
        });
    }

    [Fact]
    public void EachFrameViewHasTheFrameSizeAndItsOwnPixels()
    {
        WithSheet((sheet, _) =>
        {
            // Frame 5 is column 1 of row 1; its cell was filled with colour index+1 = 6.
            Surface frame = sheet.Frame(5);
            Assert.Equal(2, frame.Width);
            Assert.Equal(2, frame.Height);
            Assert.Equal(Color.FromRgb(6, 0, 0).Value, frame.Pixels[0]);
        });
    }

    [Fact]
    public void DrawCopiesTheChosenFrame()
    {
        WithSheet((sheet, _) =>
        {
            uint[] dest = new uint[4];   // a 2x2 target
            fixed (uint* d = dest)
            {
                var target = new Surface(d, 2, 2);
                sheet.Draw(target, index: 2, x: 0, y: 0);   // frame 2 was filled with colour 3
                Assert.All(dest, px => Assert.Equal(Color.FromRgb(3, 0, 0).Value, px));
            }
        });
    }

    [Fact]
    public void AFrameOutsideTheRangeIsRejected()
    {
        WithSheet((sheet, _) =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Frame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Frame(8));
        });
    }

    [Fact]
    public void AFrameLargerThanTheSheetIsRejected()
    {
        uint[] pixels = new uint[4];
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, 2, 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteSheet(surface, 4, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteSheet(surface, 2, 0));
        }
    }
}
