// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class CsvTests
{
    [Fact]
    public void Parse_SimpleRows()
    {
        List<string[]> rows = Csv.Parse("a,b,c\n1,2,3\n"); // trailing newline adds no empty row
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void Parse_NoTrailingNewlineAndEmptyFields()
    {
        Assert.Equal(new[] { "a", "b" }, Assert.Single(Csv.Parse("a,b")));
        Assert.Equal(new[] { "a", "", "c" }, Assert.Single(Csv.Parse("a,,c")));
    }

    [Fact]
    public void Parse_QuotedFields()
    {
        // A field holding the separator, doubled quotes, or a line break survives the round.
        Assert.Equal(new[] { "a,b", "c" }, Assert.Single(Csv.Parse("\"a,b\",c")));
        Assert.Equal(new[] { "she said \"hi\"", "x" }, Assert.Single(Csv.Parse("\"she said \"\"hi\"\"\",x")));
        Assert.Equal(new[] { "line1\nline2", "x" }, Assert.Single(Csv.Parse("\"line1\nline2\",x")));
    }

    [Fact]
    public void Parse_HandlesCarriageReturns()
    {
        List<string[]> rows = Csv.Parse("a,b\r\n1,2\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_TabSeparated() =>
        Assert.Equal(new[] { "a", "b", "c" }, Assert.Single(Csv.Parse("a\tb\tc", separator: '\t')));

    [Fact]
    public void Write_QuotesOnlyWhereNeeded()
    {
        string text = Csv.Write(new[] { new[] { "plain", "has,comma", "has\"quote" } });
        Assert.Equal("plain,\"has,comma\",\"has\"\"quote\"\r\n", text);
    }

    [Fact]
    public void WriteThenParse_RoundTrips()
    {
        var rows = new List<string[]>
        {
            new[] { "name", "note" },
            new[] { "Ada", "a, b and \"c\"" },
            new[] { "line", "one\ntwo" },
        };
        List<string[]> back = Csv.Parse(Csv.Write(rows));
        Assert.Equal(rows.Count, back.Count);
        for (int i = 0; i < rows.Count; i++)
            Assert.Equal(rows[i], back[i]);
    }

    [Fact]
    public void Parse_EmptyTextIsNoRows() =>
        Assert.Empty(Csv.Parse(""));
}
