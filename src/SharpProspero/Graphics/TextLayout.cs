// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;

namespace SharpProspero.Graphics;

/// <summary>Where a line of text sits within the width it is given.</summary>
public enum TextAlignment
{
    /// <summary>Against the left edge.</summary>
    Left,

    /// <summary>Centred in the width.</summary>
    Center,

    /// <summary>Against the right edge.</summary>
    Right,
}

/// <summary>
/// Fits text to a width: breaking a paragraph into lines, shortening a label that will not fit, and
/// drawing either one aligned in a rectangle. Everything measures through an <see cref="ITextFont"/>,
/// so the same layout serves the built-in text and a loaded outline font.
/// </summary>
public static class TextLayout
{
    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/>, splitting at
    /// spaces. A line break in the text starts a new line. A single word too wide to fit is split across
    /// lines rather than overflowing.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static List<string> Wrap(ITextFont font, string? text, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(font);
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
            return lines;

        // Without a usable width there is nothing to fit to, so each paragraph stays whole.
        if (maxWidth <= 0)
        {
            foreach (string whole in text.Split('\n'))
                lines.Add(whole.TrimEnd('\r'));
            return lines;
        }

        foreach (string paragraph in text.Split('\n'))
            WrapParagraph(font, paragraph.TrimEnd('\r'), maxWidth, lines);
        return lines;
    }

    private static void WrapParagraph(ITextFont font, string paragraph, int maxWidth, List<string> lines)
    {
        ReadOnlySpan<char> span = paragraph;
        if (span.Length == 0)
        {
            lines.Add(string.Empty);
            return;
        }

        int lineStart = 0, lineEnd = 0, i = 0;
        while (i < span.Length)
        {
            int wordStart = i;
            while (wordStart < span.Length && span[wordStart] == ' ')
                wordStart++;
            if (wordStart >= span.Length)
                break;

            int wordEnd = wordStart;
            while (wordEnd < span.Length && span[wordEnd] != ' ')
                wordEnd++;

            if (font.MeasureText(span[lineStart..wordEnd]) <= maxWidth)
            {
                lineEnd = wordEnd;
                i = wordEnd;
                continue;
            }

            // The word pushes the line past the width. Close the line if it already holds something,
            // otherwise the word alone is too wide and is split so the layout still makes progress.
            if (lineEnd > lineStart)
            {
                lines.Add(span[lineStart..lineEnd].ToString());
                lineStart = lineEnd = wordStart;
                continue;
            }

            int fit = LargestPrefix(font, span[wordStart..wordEnd], maxWidth);
            lines.Add(span.Slice(wordStart, fit).ToString());
            lineStart = lineEnd = i = wordStart + fit;
        }

        if (lineEnd > lineStart)
            lines.Add(span[lineStart..lineEnd].ToString());
        else if (lines.Count == 0)
            lines.Add(string.Empty);
    }

    // The longest run from the start of the text that fits, never less than one character so a caller
    // splitting a word always advances.
    private static int LargestPrefix(ITextFont font, ReadOnlySpan<char> text, int maxWidth)
    {
        int low = 1, high = text.Length, best = 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (font.MeasureText(text[..mid]) <= maxWidth)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return best;
    }

    /// <summary>
    /// The size the wrapped block of <paramref name="text"/> occupies: the widest line and the total
    /// height of every line.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static (int Width, int Height) MeasureWrapped(ITextFont font, string? text, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(font);
        List<string> lines = Wrap(font, text, maxWidth);
        int widest = 0;
        foreach (string line in lines)
        {
            int width = font.MeasureText(line);
            if (width > widest)
                widest = width;
        }
        return (widest, lines.Count * font.LineHeight);
    }

    /// <summary>
    /// Draws <paramref name="text"/> wrapped to <paramref name="width"/> starting at
    /// (<paramref name="x"/>, <paramref name="y"/>), each line placed by <paramref name="alignment"/>.
    /// </summary>
    /// <returns>The height, in pixels, the drawn block occupies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static int DrawWrapped(
        Surface surface, ITextFont font, string? text, int x, int y, int width,
        Color color, TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(font);
        List<string> lines = Wrap(font, text, width);
        int lineY = y;
        foreach (string line in lines)
        {
            DrawAligned(surface, font, line, x, lineY, width, color, alignment);
            lineY += font.LineHeight;
        }
        return lines.Count * font.LineHeight;
    }

    /// <summary>
    /// Draws one line of <paramref name="text"/> placed by <paramref name="alignment"/> within
    /// <paramref name="width"/> starting at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static void DrawAligned(
        Surface surface, ITextFont font, string? text, int x, int y, int width,
        Color color, TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
            return;
        int textWidth = font.MeasureText(text);
        int drawX = alignment switch
        {
            TextAlignment.Center => x + ((width - textWidth) / 2),
            TextAlignment.Right => x + width - textWidth,
            _ => x,
        };
        font.DrawText(surface, text, drawX, y, color);
    }

    /// <summary>
    /// Shortens <paramref name="text"/> so it fits <paramref name="maxWidth"/>, ending it with
    /// <paramref name="ellipsis"/> when anything was dropped. Text that already fits is returned as it is.
    /// Use this for a name in a list rather than letting it run past its column.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static string Truncate(ITextFont font, string? text, int maxWidth, string ellipsis = "...")
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (maxWidth <= 0)
            return string.Empty;
        if (font.MeasureText(text) <= maxWidth)
            return text;

        ellipsis ??= string.Empty;
        int ellipsisWidth = font.MeasureText(ellipsis);

        // No room for even the marker, so fall back to as much of the text as fits.
        if (ellipsisWidth >= maxWidth)
            return text[..LargestPrefix(font, text, maxWidth)];

        int room = maxWidth - ellipsisWidth;
        ReadOnlySpan<char> span = text;
        int low = 0, high = text.Length, best = 0;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (font.MeasureText(span[..mid]) <= room)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return string.Concat(span[..best].TrimEnd(), ellipsis);
    }
}
