// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Kernel;
using SharpProspero.Timing;
using Xunit;

namespace SharpProspero.Tests;

// A sleep that comes back before its interval has run reports how much was left, but only when it was
// cut short by something the process was told about. Every other refusal leaves that block exactly as
// the caller had it, which is whatever the stack held at that address. Asking again on one of those
// sleeps for an interval nobody chose - a very long one, or none at all in a loop that never ends - so
// the retry turns on the one code that writes a remainder and on nothing else.
public sealed class PrecisionSleepTests
{
    [Fact]
    public void AFinishedSleepIsNotAskedForAgain()
    {
        Assert.False(PrecisionClock.ShouldSleepAgain(0, new KernelTimespec { Seconds = 5 }));
    }

    [Fact]
    public void AnInterruptedSleepIsAskedForAgainWithWhatWasLeft()
    {
        Assert.True(PrecisionClock.ShouldSleepAgain(
            PrecisionClock.InterruptedError, new KernelTimespec { Nanoseconds = 1 }));
        Assert.True(PrecisionClock.ShouldSleepAgain(
            PrecisionClock.InterruptedError, new KernelTimespec { Seconds = 2, Nanoseconds = 500 }));
    }

    [Fact]
    public void AnInterruptedSleepWithNothingLeftEndsRatherThanLooping()
    {
        Assert.False(PrecisionClock.ShouldSleepAgain(PrecisionClock.InterruptedError, default));
    }

    [Fact]
    public void AnyOtherRefusalEndsTheSleepRatherThanRepeatingAnUnwrittenRemainder()
    {
        // The block is left untouched by these, so the values below stand for whatever the stack held.
        // Repeating on them is the defect this pins: a wait for an interval nobody asked for.
        var leftovers = new KernelTimespec { Seconds = 1_000_000, Nanoseconds = 999_999_999 };

        Assert.False(PrecisionClock.ShouldSleepAgain(unchecked((int)0x80020016), leftovers)); // refused argument
        Assert.False(PrecisionClock.ShouldSleepAgain(unchecked((int)0x8002000E), leftovers)); // refused address
        Assert.False(PrecisionClock.ShouldSleepAgain(-1, leftovers));
    }
}
