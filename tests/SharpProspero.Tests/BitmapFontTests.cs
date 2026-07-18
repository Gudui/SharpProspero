// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using SharpProspero.Graphics;
using Xunit;

namespace SharpProspero.Tests;

public sealed class BitmapFontTests
{
    [Fact]
    public void GetGlyph_ReturnsEightRows()
    {
        Assert.Equal(BitmapFont.GlyphSize, BitmapFont.GetGlyph('A').Length);
    }

    [Fact]
    public void GetGlyph_Space_IsBlank()
    {
        foreach (byte row in BitmapFont.GetGlyph(' '))
            Assert.Equal(0, row);
    }

    [Fact]
    public void GetGlyph_OutOfRange_FallsBackToSpace()
    {
        ReadOnlySpan<byte> control = BitmapFont.GetGlyph('');
        ReadOnlySpan<byte> space = BitmapFont.GetGlyph(' ');
        Assert.True(control.SequenceEqual(space));
    }

    [Fact]
    public void GetGlyph_UpperA_MatchesTable()
    {
        byte[] expected = [0x0C, 0x1E, 0x33, 0x33, 0x3F, 0x33, 0x33, 0x00];
        Assert.True(BitmapFont.GetGlyph('A').SequenceEqual(expected));
    }

    [Fact]
    public void GetGlyph_Underscore_HasBottomRowSet()
    {
        ReadOnlySpan<byte> glyph = BitmapFont.GetGlyph('_');
        Assert.Equal(0xFF, glyph[7]);
    }
}
