// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using Xunit;

namespace SharpProspero.Tests;

// The window's job is to move content that does not fit and to hand the input back at either end, so
// focus can leave it instead of getting stuck.
public sealed class ScrollViewTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    // A panel of rows tall enough to need scrolling in a short window.
    private static ScrollView WithRows(int rows, int viewHeight)
    {
        var panel = new StackPanel();
        for (int i = 0; i < rows; i++)
            panel.Add(new Label($"row {i}"));
        var view = new ScrollView(panel) { ViewHeight = viewHeight, ScrollStep = 10 };
        view.Measure(400, Theme);
        return view;
    }

    [Fact]
    public void ContentThatFitsDoesNotScrollOrTakeFocus()
    {
        ScrollView view = WithRows(1, 1000);
        Assert.Equal(0, view.MaxScroll);
        Assert.False(view.IsFocusable);
        Assert.False(view.ScrollBy(50));
        Assert.Equal(0, view.ScrollOffset);
    }

    [Fact]
    public void ContentThatOverflowsScrollsAndTakesFocus()
    {
        ScrollView view = WithRows(20, 100);
        Assert.True(view.MaxScroll > 0);
        Assert.True(view.IsFocusable);

        Assert.True(view.ScrollBy(10));
        Assert.Equal(10, view.ScrollOffset);
    }

    [Fact]
    public void ScrollingStopsAtEitherEnd()
    {
        ScrollView view = WithRows(20, 100);

        Assert.False(view.ScrollBy(-10));           // already at the top
        Assert.Equal(0, view.ScrollOffset);

        Assert.True(view.ScrollBy(100000));         // clamped to the bottom
        Assert.Equal(view.MaxScroll, view.ScrollOffset);
        Assert.False(view.ScrollBy(10));            // nothing left to move
    }

    [Fact]
    public void DownAndUpMoveTheContent()
    {
        ScrollView view = WithRows(20, 100);
        var down = new UiInput(false, true, false, false, false, false);
        var up = new UiInput(true, false, false, false, false, false);

        Assert.True(view.HandleInput(down, Theme));
        Assert.Equal(10, view.ScrollOffset);

        Assert.True(view.HandleInput(up, Theme));
        Assert.Equal(0, view.ScrollOffset);
    }

    [Fact]
    public void TheInputIsLeftUnusedAtEitherEnd()
    {
        ScrollView view = WithRows(20, 100);
        var up = new UiInput(true, false, false, false, false, false);
        var down = new UiInput(false, true, false, false, false, false);

        // At the top, up belongs to the screen so focus can move away.
        Assert.False(view.HandleInput(up, Theme));

        view.ScrollBy(view.MaxScroll);
        Assert.False(view.HandleInput(down, Theme));
    }

    [Fact]
    public void ScrollToTopReturnsToTheStart()
    {
        ScrollView view = WithRows(20, 100);
        view.ScrollBy(view.MaxScroll);
        Assert.True(view.ScrollOffset > 0);

        view.ScrollToTop();
        Assert.Equal(0, view.ScrollOffset);
    }

    [Fact]
    public void TheWindowIsAsTallAsItWasAskedToBe()
    {
        ScrollView view = WithRows(20, 150);
        Assert.Equal(150, view.Measure(400, Theme));
    }
}
