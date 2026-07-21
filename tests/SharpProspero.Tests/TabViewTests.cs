// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using Xunit;

namespace SharpProspero.Tests;

// The tab row's job is to pick which page is shown and to hand left and right back when it cannot move
// further, so focus can leave it.
public sealed class TabViewTests
{
    private static TabView TwoTabs()
    {
        var tabs = new TabView();
        tabs.Add("First", new Label("one"));
        tabs.Add("Second", new Label("two"));
        return tabs;
    }

    [Fact]
    public void StartsOnTheFirstTab()
    {
        TabView tabs = TwoTabs();
        Assert.Equal(2, tabs.Count);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal(["First", "Second"], tabs.Titles);
    }

    [Fact]
    public void RightAndLeftChangeThePage()
    {
        TabView tabs = TwoTabs();
        UiTheme theme = UiTheme.Default;

        Assert.True(tabs.HandleInput(new UiInput(false, false, false, true, false, false), theme));
        Assert.Equal(1, tabs.SelectedIndex);

        Assert.True(tabs.HandleInput(new UiInput(false, false, true, false, false, false), theme));
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void LeavesTheInputUnusedAtEitherEnd()
    {
        TabView tabs = TwoTabs();
        UiTheme theme = UiTheme.Default;

        // Already on the first tab, so left is for the screen to use.
        Assert.False(tabs.HandleInput(new UiInput(false, false, true, false, false, false), theme));

        tabs.SelectedIndex = 1;
        Assert.False(tabs.HandleInput(new UiInput(false, false, false, true, false, false), theme));
    }

    [Fact]
    public void ReportsTheChosenPage()
    {
        TabView tabs = TwoTabs();
        tabs.SelectedIndex = 1;
        Assert.NotNull(tabs.SelectedContent);
        Assert.Same(tabs.SelectedContent, tabs.SelectedContent);
    }

    [Fact]
    public void ClampsAnIndexOutsideTheRange()
    {
        TabView tabs = TwoTabs();
        tabs.SelectedIndex = 99;
        Assert.Equal(1, tabs.SelectedIndex);
        tabs.SelectedIndex = -5;
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void AnnouncesAChangeOnce()
    {
        TabView tabs = TwoTabs();
        int changes = 0, last = -1;
        tabs.SelectionChanged = index => { changes++; last = index; };

        tabs.SelectedIndex = 1;
        tabs.SelectedIndex = 1;   // same page again raises nothing

        Assert.Equal(1, changes);
        Assert.Equal(1, last);
    }

    [Fact]
    public void ASingleTabTakesNoFocus()
    {
        var tabs = new TabView();
        tabs.Add("Only", new Label("one"));
        Assert.False(tabs.IsFocusable);
        Assert.False(tabs.HandleInput(new UiInput(false, false, false, true, false, false), UiTheme.Default));
    }

    [Fact]
    public void ClearRemovesEveryTab()
    {
        TabView tabs = TwoTabs();
        tabs.SelectedIndex = 1;
        tabs.Clear();
        Assert.Equal(0, tabs.Count);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Null(tabs.SelectedContent);
    }
}
