// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// The row divides the width it is given into equal columns; the checks below read back where each
// child was placed, since that is what decides whether a pair of buttons lines up.
public sealed class RowTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    [Fact]
    public void PlacesChildrenSideBySideWithAGapBetween()
    {
        var left = new Button("left");
        var right = new Button("right");
        var row = new Row { Spacing = 10 }.Add(left).Add(right);

        row.Arrange(new UiRect(0, 0, 210, 40), Theme);

        // 210 wide with one 10-pixel gap leaves 100 per column.
        Assert.Equal(0, left.Bounds.X);
        Assert.Equal(100, left.Bounds.Width);
        Assert.Equal(110, right.Bounds.X);
        Assert.Equal(0, left.Bounds.Y);
        Assert.Equal(0, right.Bounds.Y);
    }

    [Fact]
    public void TheLastColumnTakesWhatIsLeftOver()
    {
        var a = new Button("a");
        var b = new Button("b");
        var c = new Button("c");
        var row = new Row { Spacing = 0 }.Add(a).Add(b).Add(c);

        // 100 across three columns does not divide evenly.
        row.Arrange(new UiRect(0, 0, 100, 40), Theme);

        Assert.Equal(100, c.Bounds.Right);
        Assert.Equal(100, a.Bounds.Width + b.Bounds.Width + c.Bounds.Width);
    }

    [Fact]
    public void HiddenChildrenTakeNoRoom()
    {
        var shown = new Button("shown");
        var hidden = new Button("hidden") { Visible = false };
        var row = new Row { Spacing = 0 }.Add(shown).Add(hidden);

        row.Arrange(new UiRect(0, 0, 100, 40), Theme);

        Assert.Equal(100, shown.Bounds.Width);
    }

    [Fact]
    public void TheRowIsAsTallAsItsTallestChild()
    {
        var short_ = new Label("short");
        var tall = new TextBlock("a longer piece of text that wraps onto several lines when narrow");
        var row = new Row().Add(short_).Add(tall);

        int height = row.Measure(120, Theme);

        Assert.True(height >= short_.Measure(60, Theme));
        Assert.True(height >= tall.Measure(60, Theme) - 1);
    }

    [Fact]
    public void FocusMovesAlongTheRowInOrder()
    {
        var left = new Button("left");
        var right = new Button("right");
        var row = new Row().Add(left).Add(right);

        var found = new List<UiElement>();
        row.CollectFocusables(found);

        Assert.Equal([left, right], found);
    }

    [Fact]
    public void AnEmptyRowTakesNoHeight()
    {
        Assert.Equal(0, new Row().Measure(200, Theme));
    }
}
