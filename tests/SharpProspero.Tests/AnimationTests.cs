// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Animation;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class EasingTests
{
    // Every curve must pin its ends: start at 0, finish at 1. A curve that drifts at the ends would
    // make a motion jump when it starts or stops.
    [Theory]
    [InlineData(Ease.Linear)]
    [InlineData(Ease.InQuad)]
    [InlineData(Ease.OutQuad)]
    [InlineData(Ease.InOutQuad)]
    [InlineData(Ease.InCubic)]
    [InlineData(Ease.OutCubic)]
    [InlineData(Ease.InOutCubic)]
    [InlineData(Ease.InSine)]
    [InlineData(Ease.OutSine)]
    [InlineData(Ease.InOutSine)]
    [InlineData(Ease.OutBack)]
    [InlineData(Ease.OutBounce)]
    public void EveryCurvePinsItsEnds(Ease ease)
    {
        Assert.Equal(0f, Easing.Apply(ease, 0f), 3);
        Assert.Equal(1f, Easing.Apply(ease, 1f), 3);
    }

    [Fact]
    public void InputIsClampedToTheUnitRange()
    {
        Assert.Equal(0f, Easing.Apply(Ease.Linear, -1f), 3);
        Assert.Equal(1f, Easing.Apply(Ease.Linear, 2f), 3);
    }

    [Fact]
    public void TheInOutCurvesAreSymmetricAtTheMiddle()
    {
        Assert.Equal(0.5f, Easing.Apply(Ease.InOutQuad, 0.5f), 3);
        Assert.Equal(0.5f, Easing.Apply(Ease.InOutCubic, 0.5f), 3);
        Assert.Equal(0.5f, Easing.Apply(Ease.InOutSine, 0.5f), 3);
    }

    [Fact]
    public void OutBackOvershootsPastTheEndBeforeSettling()
    {
        // Near the finish the springy curve goes above 1 before landing on 1.
        Assert.True(Easing.Apply(Ease.OutBack, 0.8f) > 1f);
        Assert.Equal(1f, Easing.Apply(Ease.OutBack, 1f), 3);
    }

    [Fact]
    public void InterpolateMapsOntoTheValueRange()
    {
        Assert.Equal(10f, Easing.Interpolate(10f, 20f, 0f), 3);
        Assert.Equal(20f, Easing.Interpolate(10f, 20f, 1f), 3);
        Assert.Equal(15f, Easing.Interpolate(10f, 20f, 0.5f, Ease.Linear), 3);
    }
}

public sealed class TweenTests
{
    [Fact]
    public void ANewTweenSitsAtItsStart()
    {
        var t = new Tween(0f, 100f, 1f);
        Assert.Equal(0f, t.Value, 3);
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void ItReachesTheEndAfterItsDurationAndReportsComplete()
    {
        var t = new Tween(0f, 100f, 1f);
        t.Update(0.5f);
        Assert.Equal(50f, t.Value, 2);   // linear, halfway
        Assert.False(t.IsComplete);

        t.Update(0.6f);                  // past the end
        Assert.Equal(100f, t.Value, 3);
        Assert.True(t.IsComplete);
    }

    [Fact]
    public void OnceDoesNotOvershootPastTheEnd()
    {
        var t = new Tween(0f, 100f, 1f);
        t.Update(5f);
        Assert.Equal(100f, t.Value, 3);
        Assert.Equal(1f, t.Progress, 3);
    }

    [Fact]
    public void RestartReturnsItToTheStart()
    {
        var t = new Tween(0f, 100f, 1f);
        t.Update(1f);
        Assert.True(t.IsComplete);

        t.Restart();
        Assert.Equal(0f, t.Value, 3);
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void ANegativeOrZeroDeltaDoesNotMoveIt()
    {
        var t = new Tween(0f, 100f, 1f);
        t.Update(0.5f);
        float held = t.Value;
        t.Update(0f);
        t.Update(-2f);
        Assert.Equal(held, t.Value, 3);
    }

    [Fact]
    public void LoopWrapsAndNeverCompletes()
    {
        var t = new Tween(0f, 100f, 1f, Ease.Linear, TweenMode.Loop);
        t.Update(1.25f);                 // a quarter into the second run
        Assert.Equal(25f, t.Value, 2);
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void PingPongRunsOutThenBack()
    {
        var t = new Tween(0f, 100f, 1f, Ease.Linear, TweenMode.PingPong);
        t.Update(1f);                    // at the far end
        Assert.Equal(100f, t.Value, 2);
        t.Update(1f);                    // back at the start after a full there-and-back
        Assert.Equal(0f, t.Value, 2);
        Assert.False(t.IsComplete);
    }

    [Fact]
    public void ADurationMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween(0f, 1f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween(0f, 1f, -1f));
    }
}
