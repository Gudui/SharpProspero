// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using Xunit;

namespace SharpProspero.Tests;

public sealed class UiWidgetsTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    private static UiInput Left => new(false, false, true, false, false, false);
    private static UiInput Right => new(false, false, false, true, false, false);
    private static UiInput Down => new(false, true, false, false, false, false);
    private static UiInput Confirm => new(false, false, false, false, true, false);

    [Fact]
    public void Slider_RightAndLeftAdjustAndClamp()
    {
        float last = 0;
        var slider = new Slider("Volume", 0, 10, 5, step: 2, changed: v => last = v);

        Assert.True(slider.HandleInput(Right, Theme));
        Assert.Equal(7, slider.Value);
        Assert.Equal(7, last);

        Assert.True(slider.HandleInput(Left, Theme));
        Assert.Equal(5, slider.Value);

        // Clamp at the maximum: repeated right stops at 10 and does not report a no-op change.
        slider.HandleInput(Right, Theme); // 7
        slider.HandleInput(Right, Theme); // 9
        slider.HandleInput(Right, Theme); // 10 (clamped from 11)
        Assert.Equal(10, slider.Value);
        last = -1;
        Assert.True(slider.HandleInput(Right, Theme)); // already at max
        Assert.Equal(10, slider.Value);
        Assert.Equal(-1, last); // no change reported
    }

    [Fact]
    public void Slider_DoesNotConsumeVerticalSoFocusCanLeave()
    {
        var slider = new Slider("Volume", 0, 10, 5, 1);
        Assert.False(slider.HandleInput(Down, Theme));
    }

    [Fact]
    public void OptionSelector_CyclesAndWraps()
    {
        int last = -1;
        var selector = new OptionSelector("Mode", ["Easy", "Normal", "Hard"], selected: 0, changed: i => last = i);

        Assert.True(selector.HandleInput(Right, Theme));
        Assert.Equal(1, selector.SelectedIndex);
        Assert.Equal("Normal", selector.SelectedOption);
        Assert.Equal(1, last);

        selector.HandleInput(Right, Theme); // Hard
        selector.HandleInput(Right, Theme); // wraps to Easy
        Assert.Equal(0, selector.SelectedIndex);

        Assert.True(selector.HandleInput(Left, Theme)); // wraps back to Hard
        Assert.Equal(2, selector.SelectedIndex);
    }

    [Fact]
    public void OptionSelector_DoesNotConsumeVertical()
    {
        var selector = new OptionSelector("Mode", ["A", "B"]);
        Assert.False(selector.HandleInput(Down, Theme));
    }

    [Fact]
    public void TextBox_ConfirmRaisesActivated()
    {
        TextBox? activated = null;
        var box = new TextBox("Name", "Ada") { Activated = b => activated = b };

        Assert.True(box.HandleInput(Confirm, Theme));
        Assert.Same(box, activated);

        Assert.False(box.HandleInput(Down, Theme));
    }
}
