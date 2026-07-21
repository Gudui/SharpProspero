// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using SharpProspero.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class DataTableTests
{
    private static DataTable Sample()
    {
        var table = new DataTable("name", "score");
        table.AddRow("Bob", "20");
        table.AddRow("Ann", "20");
        table.AddRow("Cy", "9");
        return table;
    }

    [Fact]
    public void AddRow_PadsShortRowsAndRejectsTooLong()
    {
        var table = new DataTable("a", "b", "c");
        table.AddRow("1"); // padded
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("1", table[0, "a"]);
        Assert.Equal(string.Empty, table[0, "c"]);
        Assert.Throws<ArgumentException>(() => table.AddRow("1", "2", "3", "4"));
    }

    [Fact]
    public void SortBy_IsStableAndCanDescend()
    {
        DataTable byScore = Sample().SortBy("score"); // ordinal: "20","20","9" -> "20","20","9"
        // Ordinal string order puts "20" before "9"; the two 20s keep their input order (Bob before Ann).
        Assert.Equal(["Bob", "Ann", "Cy"], byScore.Rows.Select(r => r["name"]).ToList());

        DataTable natural = Sample().SortBy("score", comparer: TextFormat.NaturalComparer);
        Assert.Equal(["Cy", "Bob", "Ann"], natural.Rows.Select(r => r["name"]).ToList()); // 9 < 20

        DataTable desc = Sample().SortBy("name", descending: true);
        Assert.Equal(["Cy", "Bob", "Ann"], desc.Rows.Select(r => r["name"]).ToList());
    }

    [Fact]
    public void Where_KeepsMatchingRows()
    {
        DataTable filtered = Sample().Where(r => r["score"] == "20");
        Assert.Equal(2, filtered.RowCount);
        Assert.All(filtered.Rows, r => Assert.Equal("20", r["score"]));
    }

    [Fact]
    public void GroupBy_SplitsByColumnValue()
    {
        Dictionary<string, DataTable> groups = Sample().GroupBy("score");
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups["20"].RowCount);
        Assert.Equal(1, groups["9"].RowCount);
    }

    [Fact]
    public void FromCsv_And_ToCsv_RoundTrip()
    {
        DataTable table = DataTable.FromCsv("name,score\nAnn,10\nBob,20");
        Assert.Equal(["name", "score"], table.Columns);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("Ann", table[0, "name"]);

        string csv = table.ToCsv();
        DataTable again = DataTable.FromCsv(csv);
        Assert.Equal("Bob", again[1, "name"]);
    }

    [Fact]
    public void FromCsv_WithoutHeaderNamesColumnsGenerically()
    {
        DataTable table = DataTable.FromCsv("a,b\nc,d", hasHeader: false);
        Assert.Equal(["col0", "col1"], table.Columns);
        Assert.Equal(2, table.RowCount);
    }

    [Fact]
    public void FromCsv_MakesDuplicateColumnNamesUnique()
    {
        DataTable table = DataTable.FromCsv("id,id\n1,2"); // duplicate header must not throw
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("id", table.Columns[0]);
        Assert.NotEqual(table.Columns[0], table.Columns[1]);
        Assert.Equal("2", table[0, table.Columns[1]]);
    }

    [Fact]
    public void DataRow_Default_ThrowsInvalidOperation()
    {
        DataRow row = default;
        Assert.Throws<InvalidOperationException>(() => row[0]);
        Assert.Throws<InvalidOperationException>(() => row["x"]);
    }

    [Fact]
    public void Constructor_RejectsEmptyOrDuplicateColumns()
    {
        Assert.Throws<ArgumentException>(() => new DataTable());
        Assert.Throws<ArgumentException>(() => new DataTable("x", "x"));
    }
}
