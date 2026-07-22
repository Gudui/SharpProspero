// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ui;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class StepperAndCarouselTests
{
    private static readonly UiTheme Theme = UiTheme.Default;
    private static UiInput Left => new(false, false, true, false, false, false);
    private static UiInput Right => new(false, false, false, true, false, false);
    private static UiInput Down => new(false, true, false, false, false, false);
    private static UiInput Confirm => new(false, false, false, false, true, false);

    [Fact]
    public void Stepper_RightAndLeftMoveByStepAndClamp()
    {
        long last = -1;
        var stepper = new Stepper("Volume", value: 4, min: 0, max: 10, step: 3, changed: v => last = v);

        Assert.True(stepper.HandleInput(Right, Theme));
        Assert.Equal(7, stepper.Value);
        Assert.Equal(7, last);

        stepper.HandleInput(Right, Theme); // 10 (clamped from 13)
        Assert.Equal(10, stepper.Value);

        stepper.HandleInput(Left, Theme);  // 7
        Assert.Equal(7, stepper.Value);
    }

    [Fact]
    public void Stepper_DoesNotFireChangedWhenAlreadyAtBound()
    {
        int calls = 0;
        var stepper = new Stepper("N", value: 10, min: 0, max: 10, changed: _ => calls++);
        Assert.True(stepper.HandleInput(Right, Theme)); // consumed, but no change at max
        Assert.Equal(10, stepper.Value);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Stepper_DoesNotConsumeVerticalSoFocusCanLeave()
    {
        var stepper = new Stepper("N", 5, 0, 10);
        Assert.False(stepper.HandleInput(Down, Theme));
    }

    [Fact]
    public void Stepper_ValueSetterClampsAndConstructorValidates()
    {
        var stepper = new Stepper("N", 5, 0, 10) { Value = 999 };
        Assert.Equal(10, stepper.Value);
        stepper.Value = -999;
        Assert.Equal(0, stepper.Value);
        Assert.Throws<ArgumentException>(() => new Stepper("N", 0, 10, 0));   // min > max
        Assert.Throws<ArgumentException>(() => new Stepper("N", 0, 0, 10, step: 0));
    }

    [Fact]
    public void Stepper_DoesNotOverflowNearLongBounds()
    {
        var high = new Stepper("N", value: long.MaxValue - 1, min: long.MinValue, max: long.MaxValue, step: 5);
        high.HandleInput(Right, Theme); // +5 would overflow; must clamp to the maximum, not wrap to the minimum
        Assert.Equal(long.MaxValue, high.Value);

        var low = new Stepper("N", value: long.MinValue + 1, min: long.MinValue, max: long.MaxValue, step: 5);
        low.HandleInput(Left, Theme);
        Assert.Equal(long.MinValue, low.Value);
    }

    [Fact]
    public void Carousel_LeftAndRightWrapAndReportChange()
    {
        int last = -1;
        var carousel = new Carousel(["A", "B", "C"], selected: 0, changed: i => last = i);

        Assert.True(carousel.HandleInput(Right, Theme));
        Assert.Equal(1, carousel.SelectedIndex);
        Assert.Equal("B", carousel.SelectedItem);
        Assert.Equal(1, last);

        carousel.HandleInput(Left, Theme);  // back to A
        carousel.HandleInput(Left, Theme);  // wraps to C
        Assert.Equal("C", carousel.SelectedItem);
    }

    [Fact]
    public void Carousel_ConfirmActivatesTheMiddleItem()
    {
        int activated = -1;
        var carousel = new Carousel(["A", "B", "C"], selected: 2, activated: i => activated = i);
        Assert.True(carousel.HandleInput(Confirm, Theme));
        Assert.Equal(2, activated);
    }

    [Fact]
    public void Carousel_DoesNotConsumeVerticalAndValidatesItems()
    {
        var carousel = new Carousel(["only"]);
        Assert.False(carousel.HandleInput(Down, Theme));
        Assert.Equal("only", carousel.SelectedItem);
        Assert.Throws<ArgumentException>(() => new Carousel([]));
    }

    [Fact]
    public void Carousel_MeasuresThreeRowsTall()
    {
        var carousel = new Carousel(["A", "B"]);
        Assert.Equal(Theme.RowHeight * 3, carousel.Measure(800, Theme));
    }
}
