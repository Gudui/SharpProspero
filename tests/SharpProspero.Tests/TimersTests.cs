// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Timing;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class TimersTests
{
    // --- Cooldown ---

    [Fact]
    public void Cooldown_StartsReadyAndGatesUse()
    {
        var cooldown = new Cooldown(2f);
        Assert.True(cooldown.IsReady);

        Assert.True(cooldown.TryUse());   // ready, so it fires
        Assert.False(cooldown.IsReady);
        Assert.Equal(1f, cooldown.Fraction, 4);
        Assert.False(cooldown.TryUse());  // cold, so it does not

        cooldown.Advance(0.5f);
        Assert.Equal(1.5f, cooldown.Remaining, 4);
        Assert.Equal(0.75f, cooldown.Fraction, 4);

        cooldown.Advance(5f);             // clamps at zero
        Assert.Equal(0f, cooldown.Remaining, 4);
        Assert.True(cooldown.IsReady);
        Assert.True(cooldown.TryUse());   // ready again
    }

    [Fact]
    public void Cooldown_ResetAndStart()
    {
        var cooldown = new Cooldown(1f);
        cooldown.Start();
        Assert.False(cooldown.IsReady);
        cooldown.Reset();
        Assert.True(cooldown.IsReady);
    }

    // --- Interval ---

    [Fact]
    public void Interval_FiresOnTheBeatAndCountsCatchUp()
    {
        var interval = new Interval(1f);
        Assert.Equal(0, interval.Advance(0.5f)); // not yet
        Assert.Equal(1, interval.Advance(0.5f)); // reaches one period
        Assert.Equal(2, interval.Advance(2.5f)); // a long frame fires twice, keeping the remainder
        Assert.Equal(0, interval.Advance(0f));
    }

    [Fact]
    public void Interval_Reset()
    {
        var interval = new Interval(1f);
        interval.Advance(0.9f);
        interval.Reset();
        Assert.Equal(0, interval.Advance(0.5f)); // progress was cleared
    }

    // --- Countdown ---

    [Fact]
    public void Countdown_FiresOnceAtZero()
    {
        var countdown = new Countdown(1f);
        Assert.True(countdown.IsRunning);
        Assert.Equal(0f, countdown.Progress, 4);

        Assert.False(countdown.Advance(0.4f));
        Assert.Equal(0.6f, countdown.Remaining, 4);
        Assert.Equal(0.4f, countdown.Progress, 4);

        Assert.True(countdown.Advance(0.6f)); // reaches zero this frame
        Assert.True(countdown.IsElapsed);
        Assert.Equal(1f, countdown.Progress, 4);

        Assert.False(countdown.Advance(1f)); // stays elapsed, fires no more
    }

    [Fact]
    public void Countdown_Restart()
    {
        var countdown = new Countdown(1f);
        countdown.Advance(2f);
        Assert.True(countdown.IsElapsed);

        countdown.Restart();
        Assert.True(countdown.IsRunning);
        Assert.Equal(1f, countdown.Remaining, 4);

        countdown.Restart(3f);
        Assert.Equal(3f, countdown.Duration, 4);
        Assert.Equal(3f, countdown.Remaining, 4);
    }

    [Fact]
    public void Timers_RejectBadDurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cooldown(-1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Interval(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Countdown(-1f));
    }

    [Fact]
    public void Timers_SettersValidateLikeTheConstructor()
    {
        var cooldown = new Cooldown(1f);
        cooldown.Duration = 2f;
        Assert.Equal(2f, cooldown.Duration);
        Assert.Throws<ArgumentOutOfRangeException>(() => cooldown.Duration = -1f);

        var interval = new Interval(1f);
        interval.Period = 2f;
        Assert.Equal(2f, interval.Period);
        Assert.Throws<ArgumentOutOfRangeException>(() => interval.Period = 0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => interval.Period = -1f);
    }
}
