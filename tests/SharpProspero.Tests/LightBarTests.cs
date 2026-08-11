// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop.Pad;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class LightBarTests
{
    [Fact]
    public void Solid_HoldsColorAndReportsFinished()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.Red);

        Assert.Equal(LightBarMode.Solid, animator.Mode);
        Assert.Equal(Color.Red.Value, animator.Current.Value);
        Assert.True(animator.IsFinished);

        animator.Update(1f);
        Assert.Equal(Color.Red.Value, animator.Current.Value);
    }

    [Fact]
    public void Off_IsBlack()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.White);
        animator.Off();

        Assert.Equal(0u, (uint)animator.Current.R);
        Assert.Equal(0u, (uint)animator.Current.G);
        Assert.Equal(0u, (uint)animator.Current.B);
    }

    [Fact]
    public void Pulse_StartsDarkReachesFullAndReturns()
    {
        var animator = new LightBarAnimator();
        animator.Pulse(Color.FromRgb(200, 100, 50), periodSeconds: 2f);

        // The cycle starts at the low end.
        Assert.Equal(0, animator.Current.R);

        // Half a period in, the high end.
        animator.Update(1f);
        Assert.Equal(200, animator.Current.R);
        Assert.Equal(100, animator.Current.G);
        Assert.Equal(50, animator.Current.B);

        // A full period returns to the low end.
        animator.Update(1f);
        Assert.Equal(0, animator.Current.R);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void Pulse_HonoursBrightnessRange()
    {
        var animator = new LightBarAnimator();
        animator.Pulse(Color.FromRgb(100, 100, 100), periodSeconds: 4f, minBrightness: 0.25f, maxBrightness: 0.75f);

        Assert.Equal(25, animator.Current.R);

        animator.Update(2f);
        Assert.Equal(75, animator.Current.R);
    }

    [Fact]
    public void Pulse_NeverLeavesItsEnds()
    {
        var animator = new LightBarAnimator();
        animator.Pulse(Color.FromRgb(255, 255, 255), periodSeconds: 1f);

        for (int i = 0; i < 240; i++)
        {
            animator.Update(1f / 60f);
            Assert.InRange(animator.Current.R, 0, 255);
            Assert.Equal(animator.Current.R, animator.Current.G);
        }
    }

    [Fact]
    public void Pulse_RejectsBadArguments()
    {
        var animator = new LightBarAnimator();
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Pulse(Color.Red, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Pulse(Color.Red, 1f, minBrightness: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Pulse(Color.Red, 1f, maxBrightness: 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Pulse(Color.Red, 1f, minBrightness: 0.9f, maxBrightness: 0.2f));
    }

    [Fact]
    public void Blink_TogglesOnThePeriod()
    {
        var animator = new LightBarAnimator();
        animator.Blink(Color.Green, Color.Black, periodSeconds: 0.5f);

        Assert.Equal(Color.Green.Value, animator.Current.Value);

        animator.Update(0.25f);
        Assert.Equal(Color.Black.Value, animator.Current.Value);

        animator.Update(0.25f);
        Assert.Equal(Color.Green.Value, animator.Current.Value);

        animator.Update(0.25f);
        Assert.Equal(Color.Black.Value, animator.Current.Value);

        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void Blink_DutyCycleShortensTheOnPhase()
    {
        var animator = new LightBarAnimator();
        animator.Blink(Color.Blue, Color.Black, periodSeconds: 1f, dutyCycle: 0.25f);

        animator.Update(0.2f);
        Assert.Equal(Color.Blue.Value, animator.Current.Value);

        animator.Update(0.1f);
        Assert.Equal(Color.Black.Value, animator.Current.Value);
    }

    [Fact]
    public void Blink_RejectsBadArguments()
    {
        var animator = new LightBarAnimator();
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Blink(Color.Red, Color.Black, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Blink(Color.Red, Color.Black, 1f, dutyCycle: 2f));
    }

    [Fact]
    public void Ramp_InterpolatesAndHoldsTheEnd()
    {
        var animator = new LightBarAnimator();
        animator.Ramp(Color.Black, Color.White, durationSeconds: 2f);

        Assert.Equal(0, animator.Current.R);

        animator.Update(1f);
        Assert.Equal(128, animator.Current.R);
        Assert.False(animator.IsFinished);

        animator.Update(1f);
        Assert.Equal(255, animator.Current.R);
        Assert.True(animator.IsFinished);

        animator.Update(5f);
        Assert.Equal(255, animator.Current.R);
    }

    [Fact]
    public void Ramp_InterpolatesEachChannel()
    {
        var animator = new LightBarAnimator();
        animator.Ramp(Color.FromRgb(0, 200, 100), Color.FromRgb(200, 0, 100), durationSeconds: 1f);

        animator.Update(0.5f);
        Assert.Equal(100, animator.Current.R);
        Assert.Equal(100, animator.Current.G);
        Assert.Equal(100, animator.Current.B);
    }

    [Fact]
    public void Ramp_LoopingRestarts()
    {
        var animator = new LightBarAnimator();
        animator.Ramp(Color.Black, Color.White, durationSeconds: 1f, loop: true);

        animator.Update(1.5f);
        Assert.Equal(128, animator.Current.R);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void Ramp_RejectsBadDuration()
    {
        var animator = new LightBarAnimator();
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Ramp(Color.Black, Color.White, 0f));
    }

    [Fact]
    public void Sequence_RunsStepsInOrder()
    {
        var animator = new LightBarAnimator();
        animator.Sequence(
        [
            LightBarStep.Hold(Color.Red, 1f),
            LightBarStep.Hold(Color.Green, 1f),
            LightBarStep.Hold(Color.Blue, 1f),
        ]);

        Assert.Equal(Color.Red.Value, animator.Current.Value);

        animator.Update(0.5f);
        Assert.Equal(Color.Red.Value, animator.Current.Value);

        animator.Update(1f);
        Assert.Equal(Color.Green.Value, animator.Current.Value);

        animator.Update(1f);
        Assert.Equal(Color.Blue.Value, animator.Current.Value);
        Assert.False(animator.IsFinished);

        animator.Update(1f);
        Assert.Equal(Color.Blue.Value, animator.Current.Value);
        Assert.True(animator.IsFinished);
    }

    [Fact]
    public void Sequence_FadingStepTravelsFromThePreviousColor()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.Black);
        animator.Sequence([LightBarStep.FadeTo(Color.White, 2f)]);

        Assert.Equal(0, animator.Current.R);

        animator.Update(1f);
        Assert.Equal(128, animator.Current.R);

        animator.Update(1f);
        Assert.Equal(255, animator.Current.R);
        Assert.True(animator.IsFinished);
    }

    [Fact]
    public void Sequence_LoopsBackToTheFirstStep()
    {
        var animator = new LightBarAnimator();
        animator.Sequence([LightBarStep.Hold(Color.Red, 1f), LightBarStep.Hold(Color.Green, 1f)], loop: true);

        animator.Update(1f);
        Assert.Equal(Color.Green.Value, animator.Current.Value);

        animator.Update(1f);
        Assert.Equal(Color.Red.Value, animator.Current.Value);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void Sequence_LongStepSkipsWholeSteps()
    {
        var animator = new LightBarAnimator();
        animator.Sequence(
        [
            LightBarStep.Hold(Color.Red, 0.1f),
            LightBarStep.Hold(Color.Green, 0.1f),
            LightBarStep.Hold(Color.Blue, 10f),
        ]);

        animator.Update(0.25f);
        Assert.Equal(Color.Blue.Value, animator.Current.Value);
    }

    [Fact]
    public void Sequence_RejectsEmptyAndZeroLengthSteps()
    {
        var animator = new LightBarAnimator();
        Assert.Throws<ArgumentNullException>(() => animator.Sequence(null!));
        Assert.Throws<ArgumentException>(() => animator.Sequence(new List<LightBarStep>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightBarStep.Hold(Color.Red, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightBarStep.FadeTo(Color.Red, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Sequence([default]));
    }

    [Fact]
    public void Update_RejectsNegativeDelta()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.Red);
        Assert.Throws<ArgumentOutOfRangeException>(() => animator.Update(-0.001f));
    }

    [Fact]
    public void TryTakeChangedColor_ReportsOnlyOnChange()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.Red);

        Assert.True(animator.TryTakeChangedColor(out Color first));
        Assert.Equal(Color.Red.Value, first.Value);

        Assert.False(animator.TryTakeChangedColor(out Color again));
        Assert.Equal(Color.Red.Value, again.Value);

        animator.Solid(Color.Blue);
        Assert.True(animator.TryTakeChangedColor(out Color changed));
        Assert.Equal(Color.Blue.Value, changed.Value);
    }

    [Fact]
    public void TryTakeChangedColor_StaysQuietWhileABlinkHoldsItsPhase()
    {
        var animator = new LightBarAnimator();
        animator.Blink(Color.Green, Color.Black, periodSeconds: 1f);

        Assert.True(animator.TryTakeChangedColor(out _));

        // Still inside the on phase: nothing to write.
        animator.Update(0.2f);
        Assert.False(animator.TryTakeChangedColor(out _));

        // Crossing into the off phase changes the color once.
        animator.Update(0.4f);
        Assert.True(animator.TryTakeChangedColor(out Color offColor));
        Assert.Equal(Color.Black.Value, offColor.Value);

        animator.Update(0.2f);
        Assert.False(animator.TryTakeChangedColor(out _));
    }

    [Fact]
    public void InvalidateTakenColor_ForcesTheNextReport()
    {
        var animator = new LightBarAnimator();
        animator.Solid(Color.Red);
        Assert.True(animator.TryTakeChangedColor(out _));
        Assert.False(animator.TryTakeChangedColor(out _));

        animator.InvalidateTakenColor();
        Assert.True(animator.TryTakeChangedColor(out _));
    }

    [Theory]
    [InlineData(ScePadError.InvalidArg, -2137915391)]
    [InlineData(ScePadError.InvalidPort, -2137915390)]
    [InlineData(ScePadError.InvalidHandle, -2137915389)]
    [InlineData(ScePadError.AlreadyOpened, -2137915388)]
    [InlineData(ScePadError.NotInitialized, -2137915387)]
    [InlineData(ScePadError.InvalidLightBarSetting, -2137915386)]
    [InlineData(ScePadError.DeviceNotConnected, -2137915385)]
    [InlineData(ScePadError.NoHandle, -2137915384)]
    [InlineData(ScePadError.Fatal, -2137915137)]
    public void PadErrorCodes_MatchTheService(int actual, int expected)
        => Assert.Equal(expected, actual);
}
