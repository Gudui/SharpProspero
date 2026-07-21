// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// Text fitting is checked against the built-in font at scale one, where every character is exactly
// eight pixels wide, so the expected break points are exact rather than approximate.
public sealed class TextLayoutTests
{
    private static readonly BitmapTextFont Font = new(1);
    private const int CharWidth = 8;

    [Fact]
    public void Measure_IsEightPixelsPerCharacter()
    {
        Assert.Equal(3 * CharWidth, Font.MeasureText("abc"));
        Assert.Equal(CharWidth, Font.LineHeight);
    }

    [Fact]
    public void Wrap_KeepsTextThatFitsOnOneLine()
    {
        List<string> lines = TextLayout.Wrap(Font, "hello", 5 * CharWidth);
        Assert.Equal(["hello"], lines);
    }

    [Fact]
    public void Wrap_BreaksAtSpaces()
    {
        List<string> lines = TextLayout.Wrap(Font, "hello world", 5 * CharWidth);
        Assert.Equal(["hello", "world"], lines);
    }

    [Fact]
    public void Wrap_StartsANewLineAtALineBreak()
    {
        List<string> lines = TextLayout.Wrap(Font, "a\nb", 40 * CharWidth);
        Assert.Equal(["a", "b"], lines);
    }

    [Fact]
    public void Wrap_SplitsAWordTooWideToFit()
    {
        // Ten characters with room for five must split rather than overflow.
        List<string> lines = TextLayout.Wrap(Font, "abcdefghij", 5 * CharWidth);
        Assert.Equal(["abcde", "fghij"], lines);
    }

    [Fact]
    public void Wrap_ReturnsNothingForEmptyText()
    {
        Assert.Empty(TextLayout.Wrap(Font, "", 100));
        Assert.Empty(TextLayout.Wrap(Font, null, 100));
    }

    [Fact]
    public void Wrap_KeepsParagraphsWholeWithoutAUsableWidth()
    {
        List<string> lines = TextLayout.Wrap(Font, "hello world", 0);
        Assert.Equal(["hello world"], lines);
    }

    [Fact]
    public void MeasureWrapped_ReportsWidestLineAndTotalHeight()
    {
        (int width, int height) = TextLayout.MeasureWrapped(Font, "hello world", 5 * CharWidth);
        Assert.Equal(5 * CharWidth, width);
        Assert.Equal(2 * Font.LineHeight, height);
    }

    [Fact]
    public void Truncate_LeavesTextThatAlreadyFits()
    {
        Assert.Equal("hello", TextLayout.Truncate(Font, "hello", 10 * CharWidth));
    }

    [Fact]
    public void Truncate_ShortensAndMarksWhatWasDropped()
    {
        // Eight characters of room, three of which the marker takes.
        string result = TextLayout.Truncate(Font, "hello world", 8 * CharWidth);
        Assert.Equal("hello...", result);
        Assert.True(Font.MeasureText(result) <= 8 * CharWidth);
    }

    [Fact]
    public void Truncate_FallsBackToPlainTextWhenTheMarkerWillNotFit()
    {
        string result = TextLayout.Truncate(Font, "hello world", 2 * CharWidth);
        Assert.Equal("he", result);
    }

    [Fact]
    public void Truncate_ReturnsEmptyForNoRoom()
    {
        Assert.Equal("", TextLayout.Truncate(Font, "hello", 0));
    }
}
