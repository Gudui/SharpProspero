// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// The grid places children in cells; the checks read back where each landed, since that is what
// decides whether a page of tiles lines up and whether focus steps through it the expected way.
public sealed class GridTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    private static (Grid Grid, List<Button> Cells) WithCells(int count, int columns, int spacing)
    {
        var grid = new Grid { Columns = columns, ColumnSpacing = spacing, RowSpacing = spacing };
        var cells = new List<Button>();
        for (int i = 0; i < count; i++)
        {
            var b = new Button($"c{i}");
            cells.Add(b);
            grid.Add(b);
        }
        return (grid, cells);
    }

    [Fact]
    public void ColumnsAreEqualSharesOfTheWidth()
    {
        (Grid grid, List<Button> c) = WithCells(4, columns: 2, spacing: 10);
        grid.Arrange(new UiRect(0, 0, 210, 400), Theme);

        // 210 wide, one 10-pixel gap between two columns -> 100 per cell.
        Assert.Equal(0, c[0].Bounds.X);
        Assert.Equal(100, c[0].Bounds.Width);
        Assert.Equal(110, c[1].Bounds.X);
        // Same columns on the second row.
        Assert.Equal(0, c[2].Bounds.X);
        Assert.Equal(110, c[3].Bounds.X);
    }

    [Fact]
    public void ChildrenWrapToTheNextRow()
    {
        (Grid grid, List<Button> c) = WithCells(4, columns: 2, spacing: 0);
        grid.Arrange(new UiRect(0, 0, 200, 400), Theme);

        // First two share a row, next two drop below.
        Assert.Equal(c[0].Bounds.Y, c[1].Bounds.Y);
        Assert.Equal(c[2].Bounds.Y, c[3].Bounds.Y);
        Assert.True(c[2].Bounds.Y > c[0].Bounds.Y);
    }

    [Fact]
    public void APartlyFilledLastRowStillPlacesItsChildren()
    {
        (Grid grid, List<Button> c) = WithCells(3, columns: 2, spacing: 0);
        grid.Arrange(new UiRect(0, 0, 200, 400), Theme);

        // Third tile sits alone on the second row, at the first column.
        Assert.Equal(0, c[2].Bounds.X);
        Assert.True(c[2].Bounds.Y > c[0].Bounds.Y);
    }

    [Fact]
    public void HeightCountsEveryRowIncludingAPartialOne()
    {
        (Grid grid, _) = WithCells(3, columns: 2, spacing: 0);
        int rowHeight = new Button("x").Measure(100, Theme);
        // Three tiles in two columns is two rows.
        Assert.Equal(2 * rowHeight, grid.Measure(200, Theme));
    }

    [Fact]
    public void FocusWalksTheChildrenInOrder()
    {
        (Grid grid, List<Button> c) = WithCells(4, columns: 2, spacing: 0);
        var found = new List<UiElement>();
        grid.CollectFocusables(found);
        Assert.Equal<UiElement>(c, found);
    }

    [Fact]
    public void OneColumnIsTheFloor()
    {
        var grid = new Grid { Columns = 0 };
        Assert.Equal(1, grid.Columns);
    }
}
