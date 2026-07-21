// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Timing;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class FixedTimestepTests
{
    [Fact]
    public void Advance_ReturnsDueStepsAndLeavesInterpolationAlpha()
    {
        // 0.25-second steps and clean binary deltas keep the arithmetic exact.
        var clock = new FixedTimestep(0.25) { MaxFrameTime = 10 };
        Assert.Equal(2, clock.Advance(0.625)); // two steps, 0.125 left over
        Assert.Equal(0.5, clock.Alpha, 6);     // 0.125 / 0.25

        Assert.Equal(1, clock.Advance(0.25));  // 0.125 + 0.25 = 0.375 -> one step
        Assert.Equal(0.5, clock.Alpha, 6);
    }

    [Fact]
    public void Advance_IgnoresNegativeAndNonFiniteDeltas()
    {
        var clock = new FixedTimestep(0.25) { MaxFrameTime = 10 };
        Assert.Equal(0, clock.Advance(-1));
        Assert.Equal(0, clock.Advance(double.NaN));
        Assert.Equal(0, clock.Advance(double.PositiveInfinity));
        Assert.Equal(0, clock.Advance(0));
    }

    [Fact]
    public void Advance_ClampsToMaxFrameTimeToPreventTheSpiralOfDeath()
    {
        var clock = new FixedTimestep(0.25); // MaxFrameTime defaults to 0.25
        Assert.Equal(1, clock.Advance(100.0)); // one step, not four hundred
    }

    [Fact]
    public void Advance_CallbackRunsOncePerStep()
    {
        var clock = new FixedTimestep(0.25) { MaxFrameTime = 10 };
        int steps = 0;
        clock.Advance(1.0, () => steps++);
        Assert.Equal(4, steps);
    }

    [Fact]
    public void Reset_ClearsLeftoverTime()
    {
        var clock = new FixedTimestep(0.25) { MaxFrameTime = 10 };
        clock.Advance(0.3);
        clock.Reset();
        Assert.Equal(0.0, clock.Alpha, 6);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveStep()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestep(0));

    [Fact]
    public void MaxFrameTime_RejectsNonPositiveOrNonFiniteValues()
    {
        var clock = new FixedTimestep(0.1);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.MaxFrameTime = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.MaxFrameTime = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.MaxFrameTime = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.MaxFrameTime = double.PositiveInfinity);
    }

    [Fact]
    public void Advance_WithATinyStepDoesNotHangOrOverflow()
    {
        // A tiny step implies billions of due steps; the count is capped and computed without a loop.
        var clock = new FixedTimestep(1e-10);
        int steps = clock.Advance(0.25);
        Assert.InRange(steps, 0, 1_000_000); // capped and positive, never a wrapped negative
        Assert.Equal(0.0, clock.Alpha, 6);   // the backlog was dropped, not carried forward
    }

    [Fact]
    public void Advance_Callback_ThatThrowsRestoresTheStepTime()
    {
        var clock = new FixedTimestep(0.25) { MaxFrameTime = 10 };
        Assert.Throws<InvalidOperationException>(
            () => clock.Advance(1.0, () => throw new InvalidOperationException()));

        // Four steps were due; the first threw, so all four steps' time is given back and due again.
        Assert.Equal(4, clock.Advance(0.0));
    }
}
