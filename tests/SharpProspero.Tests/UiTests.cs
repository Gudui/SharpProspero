// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Collections.Generic;
using SharpProspero.Input;
using SharpProspero.Interop.Pad;
using SharpProspero.Ui;
using Xunit;

namespace SharpProspero.Tests;

// The interface toolkit's logic: rectangles, input edges, spatial focus, stacking layout, list movement,
// and how a screen routes input to the focused control. All of this runs without a display.
public sealed class UiTests
{
    [Fact]
    public void UiRect_ReportsEdgesCentersAndContainment()
    {
        var rect = new UiRect(10, 20, 100, 40);
        Assert.Equal(110, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.Equal(60, rect.CenterX);
        Assert.Equal(40, rect.CenterY);
        Assert.True(rect.Contains(10, 20));
        Assert.False(rect.Contains(110, 20));   // right edge is exclusive
        Assert.False(rect.Contains(9, 20));
        Assert.Equal(new UiRect(15, 25, 90, 30), rect.Inset(5));
    }

    [Fact]
    public void UiInput_ReadsButtonEdges()
    {
        GamePadState resting = GamePadState.Neutral;
        GamePadState down = GamePadState.Neutral with { Buttons = ScePadButton.Down };

        UiInput pressed = UiInput.From(down, resting);
        Assert.True(pressed.Down);
        Assert.Equal(UiDirection.Down, pressed.Direction);

        // Holding the button is not an edge, so it moves once, not every frame.
        UiInput held = UiInput.From(down, down);
        Assert.False(held.Down);
        Assert.Null(held.Direction);
    }

    [Fact]
    public void UiInput_DirectionIsNullWhenTwoAreSetAtOnce()
    {
        var both = new UiInput(Up: true, Down: false, Left: true, Right: false, Confirm: false, Cancel: false);
        Assert.True(both.HasDirection);
        Assert.Null(both.Direction);
    }

    [Fact]
    public void FocusNavigator_PicksTheNeighborInTheDirection()
    {
        // A 2x2 grid: A top-left, B top-right, C bottom-left, D bottom-right.
        var a = Placed(0, 0);
        var b = Placed(200, 0);
        var c = Placed(0, 200);
        var d = Placed(200, 200);
        var all = new List<UiElement> { a, b, c, d };

        Assert.Same(b, FocusNavigator.Next(all, a, UiDirection.Right));  // straight ahead beats the diagonal
        Assert.Same(c, FocusNavigator.Next(all, a, UiDirection.Down));
        Assert.Same(d, FocusNavigator.Next(all, b, UiDirection.Down));
        Assert.Same(d, FocusNavigator.Next(all, c, UiDirection.Right));
        Assert.Null(FocusNavigator.Next(all, a, UiDirection.Left));      // nothing to the left of A
        Assert.Null(FocusNavigator.Next(all, a, UiDirection.Up));
        Assert.Same(a, FocusNavigator.Next(all, null, UiDirection.Down)); // no current -> the first
    }

    [Fact]
    public void StackPanel_StacksChildrenWithSpacingAndMeasuresTheTotal()
    {
        UiTheme theme = UiTheme.Default;
        var label = new Label("Title");
        var first = new Button("One");
        var second = new Button("Two");
        var panel = new StackPanel().Add(label).Add(first).Add(second);

        panel.Arrange(new UiRect(10, 20, 300, 1000), theme);

        int labelHeight = theme.TextScale * 8;
        Assert.Equal(new UiRect(10, 20, 300, labelHeight), label.Bounds);
        Assert.Equal(new UiRect(10, 20 + labelHeight + theme.Spacing, 300, theme.RowHeight), first.Bounds);
        Assert.Equal(new UiRect(10, 20 + labelHeight + theme.Spacing + theme.RowHeight + theme.Spacing, 300, theme.RowHeight), second.Bounds);

        Assert.Equal(labelHeight + theme.RowHeight * 2 + theme.Spacing * 2, panel.Measure(300, theme));
    }

    [Fact]
    public void StackPanel_CollectsOnlyFocusableChildren()
    {
        var label = new Label("Title");
        var button = new Button("Go");
        var disabled = new Button("Off") { Enabled = false };
        var panel = new StackPanel().Add(label).Add(button).Add(disabled);

        var focusables = new List<UiElement>();
        panel.CollectFocusables(focusables);

        Assert.Equal(new UiElement[] { button }, focusables);
    }

    [Fact]
    public void Button_ActivatesOnConfirmOnly()
    {
        int calls = 0;
        var button = new Button("Go", () => calls++);

        Assert.False(button.HandleInput(UiInput.None with { Down = true }, UiTheme.Default));
        Assert.Equal(0, calls);
        Assert.True(button.HandleInput(UiInput.None with { Confirm = true }, UiTheme.Default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Checkbox_TogglesAndReportsTheNewState()
    {
        bool? last = null;
        var box = new Checkbox("Fullscreen", changed: v => last = v);

        Assert.True(box.HandleInput(UiInput.None with { Confirm = true }, UiTheme.Default));
        Assert.True(box.Checked);
        Assert.True(last);

        box.HandleInput(UiInput.None with { Confirm = true }, UiTheme.Default);
        Assert.False(box.Checked);
        Assert.False(last);
    }

    [Fact]
    public void ListView_MovesSelectionAndEscapesAtTheEdges()
    {
        int activated = -1;
        var list = new ListView(["A", "B", "C"]) { Activated = i => activated = i };
        UiTheme theme = UiTheme.Default;

        // Up at the top is not consumed, so focus can leave the list upward.
        Assert.False(list.HandleInput(UiInput.None with { Up = true }, theme));
        Assert.Equal(0, list.SelectedIndex);

        Assert.True(list.HandleInput(UiInput.None with { Down = true }, theme));
        Assert.Equal(1, list.SelectedIndex);
        Assert.True(list.HandleInput(UiInput.None with { Down = true }, theme));
        Assert.Equal(2, list.SelectedIndex);

        // Down at the bottom is not consumed either.
        Assert.False(list.HandleInput(UiInput.None with { Down = true }, theme));
        Assert.Equal(2, list.SelectedIndex);

        Assert.True(list.HandleInput(UiInput.None with { Confirm = true }, theme));
        Assert.Equal(2, activated);
    }

    [Fact]
    public void ListView_EmptyIsNotFocusable()
    {
        var list = new ListView();
        Assert.False(list.IsFocusable);
        Assert.False(list.HandleInput(UiInput.None with { Confirm = true }, UiTheme.Default));
        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void UiScreen_StartsFocusedAndMovesFocusWithTheDirection()
    {
        var one = new Button("One");
        var two = new Button("Two");
        var three = new Button("Three");
        var screen = new UiScreen(new StackPanel().Add(one).Add(two).Add(three));

        screen.Layout(new UiRect(0, 0, 400, 400));
        Assert.Same(one, screen.Focused);

        screen.Update(UiInput.None with { Down = true });
        Assert.Same(two, screen.Focused);
        screen.Update(UiInput.None with { Down = true });
        Assert.Same(three, screen.Focused);
        screen.Update(UiInput.None with { Down = true });   // nothing below the last one
        Assert.Same(three, screen.Focused);
        screen.Update(UiInput.None with { Up = true });
        Assert.Same(two, screen.Focused);
    }

    [Fact]
    public void UiScreen_ActivatesTheFocusedControlAndRaisesCancel()
    {
        int started = 0, cancelled = 0;
        var start = new Button("Start", () => started++);
        var screen = new UiScreen(new StackPanel().Add(start)) { Cancelled = () => cancelled++ };
        screen.Layout(new UiRect(0, 0, 400, 400));

        screen.Update(UiInput.None with { Confirm = true });
        Assert.Equal(1, started);

        screen.Update(UiInput.None with { Cancel = true });
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void UiScreen_KeepsFocusInAListUntilItsEdge()
    {
        var list = new ListView(["A", "B"]);
        var below = new Button("Below");
        var screen = new UiScreen(new StackPanel().Add(list).Add(below));
        screen.Layout(new UiRect(0, 0, 400, 400));
        Assert.Same(list, screen.Focused);

        // Down moves the list's own selection, not focus.
        screen.Update(UiInput.None with { Down = true });
        Assert.Same(list, screen.Focused);
        Assert.Equal(1, list.SelectedIndex);

        // At the list's bottom, down leaves the list and focuses the button below it.
        screen.Update(UiInput.None with { Down = true });
        Assert.Same(below, screen.Focused);
    }

    // A focusable control placed at a fixed position, for the focus-navigation tests.
    private static Button Placed(int x, int y)
    {
        var button = new Button("x") { Bounds = new UiRect(x, y, 100, 50) };
        return button;
    }
}
