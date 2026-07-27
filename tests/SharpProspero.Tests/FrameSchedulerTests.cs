// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Timing;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class FrameSchedulerTests
{
    [Fact]
    public void After_FiresOnceWhenTheDelayElapses()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        scheduler.After(1.0, () => fired++);

        scheduler.Update(0.5);
        Assert.Equal(0, fired); // not yet due

        scheduler.Update(0.5);
        Assert.Equal(1, fired); // due at exactly 1.0
        Assert.Equal(0, scheduler.Count); // a one-shot is gone after it runs

        scheduler.Update(1.0);
        Assert.Equal(1, fired); // never fires again
    }

    [Fact]
    public void After_ZeroDelayFiresOnTheNextUpdate()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        scheduler.After(0, () => fired++);

        scheduler.Update(0.016);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Every_RepeatsEachInterval()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        scheduler.Every(0.5, () => fired++);

        scheduler.Update(0.5);
        scheduler.Update(0.5);
        scheduler.Update(0.5);
        Assert.Equal(3, fired);
        Assert.Equal(1, scheduler.Count); // still scheduled
    }

    [Fact]
    public void Every_LongPauseFiresOnceNotOncePerMissedInterval()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        scheduler.Every(0.1, () => fired++);

        // One second passes in a single frame: a naive catch-up would fire ten times.
        scheduler.Update(1.0);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Cancel_StopsARepeatingCallbackAndReportsUnknownHandles()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        int handle = scheduler.Every(0.1, () => fired++);

        scheduler.Update(0.1);
        Assert.Equal(1, fired);

        Assert.True(scheduler.Cancel(handle));
        Assert.False(scheduler.Cancel(handle));   // already cancelled
        Assert.False(scheduler.Cancel(9999));      // never existed

        scheduler.Update(0.1);
        Assert.Equal(1, fired); // did not fire again
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void Callback_MayCancelItself()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        int handle = 0;
        handle = scheduler.Every(0.1, () =>
        {
            fired++;
            scheduler.Cancel(handle);
        });

        scheduler.Update(0.1);
        scheduler.Update(0.1);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void WorkScheduledInsideACallbackWaitsForTheNextUpdate()
    {
        var scheduler = new FrameScheduler();
        int inner = 0;
        scheduler.After(0, () => scheduler.After(0, () => inner++));

        scheduler.Update(0.1);
        Assert.Equal(0, inner); // the newly scheduled callback does not run in the same pass

        scheduler.Update(0.1);
        Assert.Equal(1, inner);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var scheduler = new FrameScheduler();
        int fired = 0;
        scheduler.After(0.1, () => fired++);
        scheduler.Every(0.1, () => fired++);
        Assert.Equal(2, scheduler.Count);

        scheduler.Clear();
        Assert.Equal(0, scheduler.Count);
        scheduler.Update(1.0);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Every_RejectsANonPositiveInterval()
    {
        var scheduler = new FrameScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Every(0, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Every(-1.0, () => { }));
    }

    [Fact]
    public void NullCallback_IsRejected()
    {
        var scheduler = new FrameScheduler();
        Assert.Throws<ArgumentNullException>(() => scheduler.After(1.0, null!));
        Assert.Throws<ArgumentNullException>(() => scheduler.Every(1.0, null!));
    }
    [Fact]
    public void Clear_FromInsideACallbackDoesNotEndTheTick()
    {
        // Emptying the list outright while it is being walked leaves the walk indexing past its end,
        // and the exception that follows leaves the frame loop entirely - the module stops.
        var scheduler = new FrameScheduler();
        int ran = 0;
        scheduler.Every(1, () => { ran++; scheduler.Clear(); });
        scheduler.Every(1, () => ran++);

        scheduler.Update(1);

        Assert.Equal(1, ran);
        scheduler.Update(1);
        Assert.Equal(1, ran);
    }

}
