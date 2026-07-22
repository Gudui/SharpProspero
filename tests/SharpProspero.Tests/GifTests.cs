// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Interop;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class GifTests
{
    // A three-colour palette (RGB). Decoded pixels come back opaque as 0xFF_RRGGBB.
    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0x00, 0x00, 0x00), // 0 black
        (0xFF, 0x00, 0x00), // 1 red
        (0x00, 0xFF, 0x00), // 2 green
        (0x00, 0x00, 0xFF), // 3 blue
    ];

    private static uint Argb(int index)
        => 0xFF000000u | ((uint)Palette[index].R << 16) | ((uint)Palette[index].G << 8) | Palette[index].B;

    [Fact]
    public void Decode_StaticImage_ReturnsExactPixels()
    {
        byte[] indices =
        [
            0, 1, 2, 3,
            1, 1, 2, 2,
            3, 3, 3, 3,
            0, 2, 0, 2,
        ];
        byte[] gif = BuildGif(4, 4, indices);

        using GifImage image = GifImage.Decode(gif);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Single(image.Frames);

        Surface surface = image.First.AsSurface();
        for (int i = 0; i < indices.Length; i++)
            Assert.Equal(Argb(indices[i]), surface.Pixels[i]);
    }

    [Fact]
    public void Decode_InterlacedImage_MatchesTheProgressiveOne()
    {
        byte[] indices = new byte[8 * 8];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = (byte)(i % 4);

        using GifImage progressive = GifImage.Decode(BuildGif(8, 8, indices));
        using GifImage interlaced = GifImage.Decode(BuildGif(8, 8, indices, interlaced: true));

        Surface a = progressive.First.AsSurface();
        Surface b = interlaced.First.AsSurface();
        for (int i = 0; i < indices.Length; i++)
            Assert.Equal(a.Pixels[i], b.Pixels[i]);
    }

    [Fact]
    public void Decode_AnimationWithTransparencyAndDisposal_ComposesFrames()
    {
        // Frame 0 fills 2x2 with red (index 1). Frame 1 draws a single green pixel (index 2) at (0,0)
        // over a transparent surround, and uses "restore to background" disposal.
        var builder = new GifBuilder(2, 2, Palette) { LoopCount = 0 };
        builder.AddFrame([1, 1, 1, 1], delayMs: 100);
        builder.AddFrame(
            frameLeft: 0, frameTop: 0, frameWidth: 1, frameHeight: 1,
            indices: [2], delayMs: 100, disposal: 2, transparentIndex: 3);

        using GifImage image = GifImage.Decode(builder.Build());
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(0, image.LoopCount);

        Surface frame0 = image.Frames[0].AsSurface();
        Assert.Equal(Argb(1), frame0.Pixels[0]);
        Assert.Equal(Argb(1), frame0.Pixels[3]);
        Assert.Equal(100, image.Frames[0].DelayMilliseconds);

        // Frame 1: green at (0,0); the other three pixels keep frame 0's red (transparent surround).
        Surface frame1 = image.Frames[1].AsSurface();
        Assert.Equal(Argb(2), frame1.Pixels[0]);
        Assert.Equal(Argb(1), frame1.Pixels[1]);
        Assert.Equal(Argb(1), frame1.Pixels[3]);
    }

    [Fact]
    public void Decode_RejectsNonGifData()
        => Assert.Throws<ProsperoException>(() => GifImage.Decode([0x00, 0x01, 0x02, 0x03]));

    [Fact]
    public void Decode_RejectsACorruptLzwStreamWithoutCrashing()
    {
        // A crafted 5x1 GIF whose LZW codes (6, 6, 6) would build a self-referencing prefix chain; the
        // decoder must reject it with its own exception rather than fault walking the decode stack.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
            0x05, 0x00, 0x01, 0x00,             // 5x1 screen
            0x80, 0x00, 0x00,                   // global colour table, 2 entries
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, // the two colours
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, // image descriptor 5x1
            0x02,                               // LZW minimum code size
            0x02, 0xB6, 0x01, 0x00,             // one sub-block: codes 6, 6, 6
            0x3B,                               // trailer
        ];
        Assert.Throws<ProsperoException>(() => GifImage.Decode(gif));
    }

    [Fact]
    public void Decode_RejectsAnOversizedScreen()
    {
        // A 13-byte header declaring 32768 x 16384 (~536M pixels) must be refused, not allocated.
        byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x80, 0x00, 0x40, 0x00, 0x00, 0x00];
        Assert.Throws<ProsperoException>(() => GifImage.Decode(gif));
    }

    // --- test helpers: build a spec-correct GIF so the decoder is exercised end to end ---

    private static byte[] BuildGif(int width, int height, byte[] indices, bool interlaced = false)
    {
        var builder = new GifBuilder(width, height, Palette);
        builder.AddFrame(indices, interlaced: interlaced);
        return builder.Build();
    }

    private sealed class GifBuilder(int width, int height, (byte R, byte G, byte B)[] palette)
    {
        private readonly List<byte> _bytes = [];
        private bool _headerWritten;

        public int LoopCount { get; set; } = 1;

        public void AddFrame(byte[] indices, bool interlaced = false, int delayMs = 0)
            => AddFrame(0, 0, width, height, indices, interlaced, delayMs, disposal: 0, transparentIndex: -1);

        public void AddFrame(int frameLeft, int frameTop, int frameWidth, int frameHeight, byte[] indices,
            bool interlaced = false, int delayMs = 0, int disposal = 0, int transparentIndex = -1)
        {
            EnsureHeader();

            if (delayMs > 0 || disposal > 0 || transparentIndex >= 0)
            {
                _bytes.Add(0x21);
                _bytes.Add(0xF9);
                _bytes.Add(0x04);
                _bytes.Add((byte)((disposal << 2) | (transparentIndex >= 0 ? 0x01 : 0)));
                int delay = delayMs / 10;
                _bytes.Add((byte)(delay & 0xFF));
                _bytes.Add((byte)(delay >> 8));
                _bytes.Add((byte)(transparentIndex >= 0 ? transparentIndex : 0));
                _bytes.Add(0x00);
            }

            _bytes.Add(0x2C);
            AddU16(frameLeft);
            AddU16(frameTop);
            AddU16(frameWidth);
            AddU16(frameHeight);
            _bytes.Add((byte)(interlaced ? 0x40 : 0x00));

            int minCodeSize = Math.Max(2, TableBits());
            _bytes.Add((byte)minCodeSize);
            byte[] lzw = LzwEncode(indices, minCodeSize);
            for (int offset = 0; offset < lzw.Length; offset += 255)
            {
                int size = Math.Min(255, lzw.Length - offset);
                _bytes.Add((byte)size);
                for (int i = 0; i < size; i++)
                    _bytes.Add(lzw[offset + i]);
            }

            _bytes.Add(0x00);
        }

        public byte[] Build()
        {
            EnsureHeader();
            _bytes.Add(0x3B);
            return [.. _bytes];
        }

        private void EnsureHeader()
        {
            if (_headerWritten)
                return;
            _headerWritten = true;

            _bytes.AddRange("GIF89a"u8.ToArray());
            AddU16(width);
            AddU16(height);
            int bits = TableBits();
            _bytes.Add((byte)(0x80 | (bits - 1))); // global colour table, size 2^bits
            _bytes.Add(0x00);
            _bytes.Add(0x00);

            int entries = 1 << bits;
            for (int i = 0; i < entries; i++)
            {
                (byte r, byte g, byte b) = i < palette.Length ? palette[i] : ((byte)0, (byte)0, (byte)0);
                _bytes.Add(r);
                _bytes.Add(g);
                _bytes.Add(b);
            }

            if (LoopCount != 1)
            {
                _bytes.Add(0x21);
                _bytes.Add(0xFF);
                _bytes.Add(0x0B);
                _bytes.AddRange("NETSCAPE2.0"u8.ToArray());
                _bytes.Add(0x03);
                _bytes.Add(0x01);
                AddU16(LoopCount);
                _bytes.Add(0x00);
            }
        }

        private int TableBits()
        {
            int bits = 1;
            while ((1 << bits) < palette.Length)
                bits++;
            return bits;
        }

        private void AddU16(int value)
        {
            _bytes.Add((byte)(value & 0xFF));
            _bytes.Add((byte)((value >> 8) & 0xFF));
        }

        // A minimal spec-correct GIF LZW encoder whose code-size growth mirrors a compliant decoder.
        private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
        {
            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int available = clearCode + 2;
            var table = new Dictionary<(int, int), int>();
            var writer = new BitWriter();

            writer.Write(clearCode, codeSize);
            int current = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int next = pixels[i];
                if (table.TryGetValue((current, next), out int combined))
                {
                    current = combined;
                }
                else
                {
                    writer.Write(current, codeSize);
                    if (available < 4096)
                    {
                        table[(current, next)] = available;
                        available++;
                        // The decoder learns each new entry one code later (it has no suffix until the next
                        // code), so it bumps its own code size one entry after the encoder would. Bump one
                        // entry later here to stay in lockstep with that lagging decoder.
                        if (available == (1 << codeSize) + 1 && codeSize < 12)
                            codeSize++;
                    }

                    current = next;
                }
            }

            writer.Write(current, codeSize);
            writer.Write(endCode, codeSize);
            return writer.Finish();
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _bytes = [];
            private int _buffer;
            private int _count;

            public void Write(int code, int size)
            {
                _buffer |= code << _count;
                _count += size;
                while (_count >= 8)
                {
                    _bytes.Add((byte)(_buffer & 0xFF));
                    _buffer >>= 8;
                    _count -= 8;
                }
            }

            public byte[] Finish()
            {
                if (_count > 0)
                    _bytes.Add((byte)(_buffer & 0xFF));
                return [.. _bytes];
            }
        }
    }
}
