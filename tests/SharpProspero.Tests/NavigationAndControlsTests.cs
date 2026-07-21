// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Ui;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class NavigationAndControlsTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    private static UiInput Up => new(true, false, false, false, false, false);
    private static UiInput Down => new(false, true, false, false, false, false);
    private static UiInput Confirm => new(false, false, false, false, true, false);
    private static UiInput Cancel => new(false, false, false, false, false, true);

    private static List<UiElement> Reachable(UiElement element)
    {
        var found = new List<UiElement>();
        element.CollectFocusables(found);
        return found;
    }

    [Fact]
    public void RadioGroup_UpAndDownMoveTheChoiceAndReportIt()
    {
        int last = -1;
        var group = new RadioGroup(["Low", "Medium", "High"], selected: 0, changed: i => last = i);

        Assert.True(group.HandleInput(Down, Theme));
        Assert.Equal(1, group.SelectedIndex);
        Assert.Equal("Medium", group.SelectedOption);
        Assert.Equal(1, last);

        Assert.True(group.HandleInput(Up, Theme));
        Assert.Equal(0, group.SelectedIndex);
    }

    [Fact]
    public void RadioGroup_LeavesTheEndsUnusedSoFocusCanEscape()
    {
        var group = new RadioGroup(["One", "Two"]);
        Assert.False(group.HandleInput(Up, Theme));       // already at the top
        group.SelectedIndex = 1;
        Assert.False(group.HandleInput(Down, Theme));     // already at the bottom
    }

    [Fact]
    public void RadioGroup_RejectsNoOptions() =>
        Assert.Throws<System.ArgumentException>(() => new RadioGroup([]));

    [Fact]
    public void Spinner_IsNotFocusableAndDrawsAHollowRing()
    {
        var spinner = new Spinner { Diameter = 24 };
        Assert.False(spinner.IsFocusable);
        Assert.Equal(24, spinner.Measure(200, Theme));

        // A negative or zero delta must not move it; a positive one is accepted without throwing.
        spinner.Advance(-1f);
        spinner.Advance(0.016f);

        const int w = 32, h = 32;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, w, h);
            spinner.Bounds = new UiRect(0, 0, w, 24);
            spinner.Draw(surface, Theme, null);
        }
        // The centre sits in the ring's hole, so it stays clear.
        Assert.Equal(0u, pixels[(12 * w) + 16]);
        // Some pixel of the track ring was drawn.
        Assert.Contains(pixels, px => px != 0u);
    }

    [Fact]
    public void ScreenStack_PushAndPopMoveBetweenScreens()
    {
        var first = new UiScreen(new StackPanel().Add(new Button("a")));
        var second = new UiScreen(new StackPanel().Add(new Button("b")));
        var nav = new ScreenStack(first);

        Assert.Equal(1, nav.Count);
        Assert.Same(first, nav.Current);

        nav.Push(second);
        Assert.Equal(2, nav.Count);
        Assert.Same(second, nav.Current);

        Assert.True(nav.Pop());
        Assert.Same(first, nav.Current);

        Assert.False(nav.Pop()); // the first screen is never popped
        Assert.Equal(1, nav.Count);
    }

    [Fact]
    public void ScreenStack_CancelOnAPushedScreenGoesBack()
    {
        var first = new UiScreen(new StackPanel().Add(new Button("a")));
        var second = new UiScreen(new StackPanel().Add(new Button("b")));
        var nav = new ScreenStack(first);
        nav.Push(second);

        // The pushed screen was given a cancel handler that pops the stack.
        nav.Update(Cancel);
        Assert.Equal(1, nav.Count);
        Assert.Same(first, nav.Current);
    }

    [Fact]
    public void ScreenStack_RespectsAnExistingCancelHandler()
    {
        var first = new UiScreen(new StackPanel().Add(new Button("a")));
        int handled = 0;
        var second = new UiScreen(new StackPanel().Add(new Button("b"))) { Cancelled = () => handled++ };
        var nav = new ScreenStack(first);
        nav.Push(second);

        nav.Update(Cancel);
        Assert.Equal(1, handled);
        Assert.Equal(2, nav.Count); // the screen's own handler ran instead of a pop
    }

    [Fact]
    public void ScreenStack_ReplaceAndPopToRoot()
    {
        var first = new UiScreen(new StackPanel().Add(new Button("a")));
        var nav = new ScreenStack(first);
        nav.Push(new UiScreen(new StackPanel().Add(new Button("b"))));
        nav.Push(new UiScreen(new StackPanel().Add(new Button("c"))));

        var replacement = new UiScreen(new StackPanel().Add(new Button("d")));
        nav.Replace(replacement);
        Assert.Same(replacement, nav.Current);
        Assert.Equal(3, nav.Count);

        nav.PopToRoot();
        Assert.Equal(1, nav.Count);
        Assert.Same(first, nav.Current);
    }

    [Fact]
    public void MessageBox_AlertOpensAndTheButtonCloses()
    {
        var host = new ModalHost(new StackPanel().Add(new Button("behind")));
        int closed = 0;
        MessageBox.Alert(host, "Done", "The file was saved.", "OK", () => closed++);

        Assert.True(host.IsOpen);
        List<UiElement> reachable = Reachable(host);
        var ok = Assert.IsType<Button>(reachable[0]);

        ok.HandleInput(Confirm, Theme);
        Assert.False(host.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void MessageBox_ConfirmRunsTheChosenBranchAndCloses()
    {
        var host = new ModalHost(new StackPanel().Add(new Button("behind")));
        int yes = 0, no = 0;
        MessageBox.Confirm(host, "Delete?", "This cannot be undone.", () => yes++, () => no++);

        List<UiElement> reachable = Reachable(host);
        Assert.Equal(2, reachable.Count);
        var confirm = Assert.IsType<Button>(reachable[0]);
        var cancel = Assert.IsType<Button>(reachable[1]);

        // Take the cancel branch: it closes the panel and runs only the cancel action.
        cancel.HandleInput(Confirm, Theme);
        Assert.False(host.IsOpen);
        Assert.Equal(0, yes);
        Assert.Equal(1, no);
    }
}
