// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Text;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class TextFormatTests
{
    [Theory]
    [InlineData(0, true, "0 B")]
    [InlineData(1023, true, "1023 B")]
    [InlineData(1024, true, "1 KiB")]
    [InlineData(1536, true, "1.5 KiB")]
    [InlineData(1048576, true, "1 MiB")]
    [InlineData(1000, false, "1 KB")]
    [InlineData(1500, false, "1.5 KB")]
    public void ByteSize_FormatsBinaryAndDecimal(long bytes, bool binary, string expected)
        => Assert.Equal(expected, TextFormat.ByteSize(bytes, binary));

    [Fact]
    public void ByteSize_HandlesNegativeAndExtremes()
    {
        Assert.Equal("-1 KiB", TextFormat.ByteSize(-1024));
        Assert.StartsWith("-", TextFormat.ByteSize(long.MinValue)); // no overflow
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3661, "1:01:01")]
    [InlineData(-10, "0:00")]
    public void Duration_FormatsMinutesAndHours(double seconds, string expected)
        => Assert.Equal(expected, TextFormat.Duration(seconds));

    [Fact]
    public void CompareNatural_SortsEmbeddedNumbersByValue()
    {
        Assert.True(TextFormat.CompareNatural("file2", "file10") < 0);
        Assert.True(TextFormat.CompareNatural("file10", "file2") > 0);
        Assert.Equal(0, TextFormat.CompareNatural("File10", "file10")); // case-insensitive
        Assert.True(TextFormat.CompareNatural("img9", "img09") != 0);   // leading zeros break the tie deterministically

        var items = new List<string> { "img10", "img2", "img1" };
        items.Sort(TextFormat.NaturalComparer);
        Assert.Equal(["img1", "img2", "img10"], items);
    }

    [Fact]
    public void ByteSize_PromotesWhenRoundingReachesTheBoundary()
    {
        Assert.Equal("1 MiB", TextFormat.ByteSize(1048575));            // 1023.99 KiB rounds up, so promote
        Assert.Equal("1 MB", TextFormat.ByteSize(999999, binary: false));
    }

    [Fact]
    public void Duration_ClampsNonFiniteAndHugeValues()
    {
        Assert.Equal("0:00", TextFormat.Duration(double.PositiveInfinity));
        Assert.Equal("0:00", TextFormat.Duration(double.NaN));
        Assert.Equal("99999:59:59", TextFormat.Duration(1e19)); // capped rather than a 16-digit hours field
    }

    [Fact]
    public void NaturalComparer_StaysConsistentWithOtherScriptDigits()
    {
        // Mixing ASCII and non-ASCII digits must not make the comparer non-transitive, which would make
        // Sort throw "IComparer.Compare() method returns inconsistent results".
        var items = new List<string> { "file10", "file2", "٩x", "3", "３", "file1" };
        items.Sort(TextFormat.NaturalComparer); // must not throw
        Assert.True(items.IndexOf("file2") < items.IndexOf("file10"));
    }

    [Fact]
    public void HexDump_ShowsOffsetHexAndAscii()
    {
        string dump = TextFormat.HexDump("Hi"u8.ToArray());
        Assert.StartsWith("00000000  48 69 ", dump);
        Assert.EndsWith("Hi\n", dump);
        Assert.Equal(string.Empty, TextFormat.HexDump([]));
    }

    [Fact]
    public void Columns_AlignsRaggedRows()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "a", "bb" },
            new[] { "ccc", "d" },
        };
        Assert.Equal("a    bb\nccc  d", TextFormat.Columns(rows));
    }
}
