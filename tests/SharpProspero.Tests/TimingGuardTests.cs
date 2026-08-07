// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Timing;
using SharpProspero.Ui;
using System;
using System.Threading;
using Xunit;

namespace SharpProspero.Tests;

// Every per-frame accumulator takes its delta straight from the frame loop, so it has to survive the
// two deltas a frame loop can produce and a caller cannot prevent: one that is not a number, and one
// far longer than a frame after a pause. A delta that is not a number poisons an accumulator for good;
// a long one against a short period used to be drained by subtracting in a loop, which either runs
// unbounded or, once the accumulator is large enough that subtracting the period does not change it,
// never finishes at all.
public sealed class TimingGuardTests
{
    [Fact]
    public void Interval_SmallPeriodAgainstAnOrdinaryFrameTerminatesAndIsBounded()
    {
        var beat = new Interval(1e-9f);
        int fired = RunWithTimeout(() => beat.Advance(0.016f));
        Assert.InRange(fired, 0, 1_000_000);
    }

    [Fact]
    public void Interval_ALongFrameDoesNotFireUnbounded()
    {
        var beat = new Interval(0.001f);
        int fired = RunWithTimeout(() => beat.Advance(3600f));
        // An hour of a millisecond beat is 3.6 million fires; the cap is what keeps that answerable.
        Assert.InRange(fired, 1, Interval.MaxFiresPerAdvance);
    }

    [Fact]
    public void Interval_HugeDeltaStillTerminates()
    {
        var beat = new Interval(2f);
        Assert.InRange(RunWithTimeout(() => beat.Advance(float.MaxValue)), 0, 1_000_000);
    }

    [Fact]
    public void Interval_ADeltaThatIsNotANumberLeavesItUsable()
    {
        var beat = new Interval(0.5f);
        Assert.Equal(0, beat.Advance(float.NaN));
        Assert.Equal(1, beat.Advance(0.5f));      // still counting after it
    }

    [Fact]
    public void AnimatedSprite_HighRateAgainstAnOrdinaryFrameTerminates()
    {
        AnimatedSprite sprite = Sprite(1e9f);
        RunWithTimeout(() => { sprite.Update(0.016f); return 0; });
    }

    [Fact]
    public void AnimatedSprite_ADeltaThatIsNotANumberLeavesItUsable()
    {
        AnimatedSprite sprite = Sprite(10f);
        sprite.Update(float.NaN);
        int before = sprite.CurrentFrame;
        sprite.Update(0.2f);
        Assert.NotEqual(before, sprite.CurrentFrame);
    }

    [Theory]
    [MemberData(nameof(Accumulators))]
    public void ADeltaThatIsNotANumberIsIgnoredRatherThanStored(string name, Action<float> advance, Func<bool> stillWorks)
    {
        advance(float.NaN);
        Assert.True(stillWorks(), $"{name} stopped responding after a delta that is not a number");
    }

    public static TheoryData<string, Action<float>, Func<bool>> Accumulators()
    {
        var data = new TheoryData<string, Action<float>, Func<bool>>();

        var stats = new FrameStats();
        data.Add("FrameStats", d => stats.Record(d), () => { stats.Record(0.016f); return float.IsFinite(stats.AvgMs) && stats.AvgMs > 0f; });

        var spinner = new Spinner();
        data.Add("Spinner", d => spinner.Advance(d), () => { spinner.Advance(0.1f); return true; });

        return data;
    }

    // A four-frame sheet over a small surface; the pixels are never read, only the frame index.
    private static unsafe AnimatedSprite Sprite(float fps)
    {
        uint* pixels = stackalloc uint[16];
        return new AnimatedSprite(new SpriteSheet(new Surface(pixels, 4, 4), 2, 2), fps);
    }

    // Runs the work on a thread so a construction that does not terminate fails the test rather than
    // hanging the run.
    private static int RunWithTimeout(Func<int> work)
    {
        int result = 0;
        var thread = new Thread(() => result = work()) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "the call did not terminate");
        return result;
    }
}
