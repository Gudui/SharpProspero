// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Audio;
using SharpProspero.Interop.Kernel;
using SharpProspero.Storage;
using SharpProspero.Threading;
using SharpProspero.Timing;
using System;
using System.IO;
using Xunit;

namespace SharpProspero.Tests;

// The pieces a large interactive application needs from the SDK that can be checked without a console:
// how a file-open request folds into the one flag word the file system takes, how a counter reading
// turns into a duration, and how a processor set is built.
public sealed unsafe class StreamingAndPacingTests
{
    // --- opening a file in pieces -------------------------------------------------------------

    [Theory]
    [InlineData(FileMode.Open, FileAccess.Read, KernelFile.ReadOnly)]
    [InlineData(FileMode.Open, FileAccess.ReadWrite, KernelFile.ReadWrite)]
    [InlineData(FileMode.Create, FileAccess.Write, KernelFile.WriteOnly | KernelFile.Create | KernelFile.Truncate)]
    [InlineData(FileMode.CreateNew, FileAccess.Write, KernelFile.WriteOnly | KernelFile.Create | KernelFile.Exclusive)]
    [InlineData(FileMode.OpenOrCreate, FileAccess.ReadWrite, KernelFile.ReadWrite | KernelFile.Create)]
    [InlineData(FileMode.Truncate, FileAccess.Write, KernelFile.WriteOnly | KernelFile.Truncate)]
    [InlineData(FileMode.Append, FileAccess.Write, KernelFile.WriteOnly | KernelFile.Create | KernelFile.Append)]
    public void ToOpenFlags_FoldsModeAndAccessIntoOneFlagWord(FileMode mode, FileAccess access, int expected)
        => Assert.Equal(expected, DeviceFileStream.ToOpenFlags(mode, access));

    // A mode that empties or creates a file cannot be served by a read-only descriptor. Letting it
    // through would open the file read-only and fail at the first write, far from the mistake.
    [Theory]
    [InlineData(FileMode.Create)]
    [InlineData(FileMode.CreateNew)]
    [InlineData(FileMode.OpenOrCreate)]
    [InlineData(FileMode.Truncate)]
    public void ToOpenFlags_RefusesAWritingModeWithoutWriteAccess(FileMode mode)
        => Assert.Throws<ArgumentException>(() => DeviceFileStream.ToOpenFlags(mode, FileAccess.Read));

    // Appending is a write-only mode: the file system offset is ignored for writes, so a reader sharing
    // the descriptor would read from wherever the last write left it.
    [Theory]
    [InlineData(FileAccess.Read)]
    [InlineData(FileAccess.ReadWrite)]
    public void ToOpenFlags_RefusesAppendingWithReadAccess(FileAccess access)
        => Assert.Throws<ArgumentException>(() => DeviceFileStream.ToOpenFlags(FileMode.Append, access));

    [Fact]
    public void ToOpenFlags_ReadOnlyIsZeroSoTheAccessBitsAreTheLowTwo()
    {
        // The three access values are 0, 1 and 2 rather than flags, so folding them in by OR only works
        // because the read-only case contributes nothing.
        Assert.Equal(0, KernelFile.ReadOnly);
        Assert.Equal(1, KernelFile.WriteOnly);
        Assert.Equal(2, KernelFile.ReadWrite);
    }

    // --- the fine-grained clock ---------------------------------------------------------------

    [Fact]
    public void ToTimeSpan_KeepsAFractionOfATickRatherThanRoundingItAway()
    {
        // A counter running far faster than the span's own hundred-nanosecond tick would round to
        // nothing if the division came first.
        const ulong frequency = 1_000_000_000;
        Assert.Equal(TimeSpan.FromTicks(1), PrecisionClock.ToTimeSpan(100, frequency));
        Assert.Equal(TimeSpan.FromMilliseconds(1), PrecisionClock.ToTimeSpan(1_000_000, frequency));
    }

    [Fact]
    public void ToTimeSpan_HandlesACountTooLargeToScaleDirectly()
    {
        // An hour at a nanosecond counter is 3.6e12; multiplying that by the span's ten million ticks a
        // second before dividing would overflow, so the whole seconds are split off first.
        const ulong frequency = 1_000_000_000;
        TimeSpan hour = PrecisionClock.ToTimeSpan(3_600UL * frequency, frequency);
        Assert.Equal(TimeSpan.FromHours(1), hour);
    }

    [Fact]
    public void ToMicroseconds_MatchesTheSpanConversion()
    {
        const ulong frequency = 24_000_000;
        ulong ticks = (frequency / 1000) * 7;             // seven milliseconds
        Assert.Equal(7000, PrecisionClock.ToMicroseconds(ticks, frequency));
        Assert.Equal(7.0, PrecisionClock.ToTimeSpan(ticks, frequency).TotalMilliseconds, 6);
    }

    [Fact]
    public void FromTimeSpan_IsTheInverseOfToTimeSpan()
    {
        const ulong frequency = 24_000_000;
        var duration = TimeSpan.FromMilliseconds(16.666);
        ulong ticks = PrecisionClock.FromTimeSpan(duration, frequency);
        Assert.Equal(duration.TotalMilliseconds, PrecisionClock.ToTimeSpan(ticks, frequency).TotalMilliseconds, 3);
    }

    [Fact]
    public void FromTimeSpan_TreatsAPastDeadlineAsNoWait()
        => Assert.Equal(0UL, PrecisionClock.FromTimeSpan(TimeSpan.FromSeconds(-1), 1000));

    [Fact]
    public void Conversions_RefuseAFrequencyOfZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrecisionClock.ToTimeSpan(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrecisionClock.ToMicroseconds(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrecisionClock.FromTimeSpan(TimeSpan.FromSeconds(1), 0));
    }

    [Fact]
    public void SleepNanoseconds_RefusesANegativeInterval()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PrecisionClock.SleepNanoseconds(-1));

    [Fact]
    public void Sleep_RefusesANegativeInterval()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PrecisionClock.Sleep(TimeSpan.FromSeconds(-1)));

    // --- placing work on a processor ----------------------------------------------------------

    [Fact]
    public void Only_SetsTheOneBitThatNamesTheProcessor()
    {
        Assert.Equal(1UL, Processor.Only(0));
        Assert.Equal(1UL << 4, Processor.Only(4));
        Assert.Equal(1UL << 12, Processor.Only(12));
    }

    [Fact]
    public void Only_RefusesAProcessorTheMachineDoesNotHave()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Processor.Only(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Processor.Only(Processor.Count));
    }

    [Fact]
    public void Mask_JoinsTheNamedProcessors()
        => Assert.Equal((1UL << 1) | (1UL << 5), Processor.Mask([1, 5]));

    [Fact]
    public void Range_NamesAHalfOpenBlock()
    {
        Assert.Equal(0b1110UL, Processor.Range(1, 4));
        Assert.Equal(0UL, Processor.Range(3, 3));
        Assert.Equal(SceKernelCpumask.All, Processor.Range(0, Processor.Count));
    }

    [Fact]
    public void Range_RefusesABlockOutsideTheMachine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Processor.Range(0, Processor.Count + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Processor.Range(4, 2));
    }

    // Every processor the mask type admits is one the helpers can name, so nothing addressable is out
    // of reach and nothing the helpers produce falls outside the mask.
    [Fact]
    public void EveryProcessorTheMaskAdmitsIsOneTheHelpersCanName()
        => Assert.Equal(SceKernelCpumask.All, Processor.Range(0, Processor.Count));

    // A set naming nothing would leave a thread with nowhere to run, which the platform reports as a
    // plain argument error; naming it here says which argument was wrong.
    [Fact]
    public void SetCurrentThreadAffinity_RefusesAnEmptySet()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Processor.SetCurrentThreadAffinity(0));

    [Fact]
    public void TrySetCurrentThreadAffinity_ReportsAnEmptySetRatherThanCallingThePlatform()
        => Assert.False(Processor.TrySetCurrentThreadAffinity(0));

    [Fact]
    public void PriorityBoundsAreTheOnesTheThreadLibraryPublishes()
    {
        // A smaller number is served first, so the most urgent is the smallest of the three.
        Assert.Equal(256, Processor.PriorityHighest);
        Assert.Equal(700, Processor.PriorityDefault);
        Assert.Equal(767, Processor.PriorityLowest);
        Assert.True(Processor.PriorityHighest < Processor.PriorityDefault);
        Assert.True(Processor.PriorityDefault < Processor.PriorityLowest);
    }

    // --- what a port says about itself --------------------------------------------------------

    [Fact]
    public void AudioOutPortState_IsThirtyTwoBytes()
        => Assert.Equal(32, sizeof(SceAudioOutPortState));

    [Fact]
    public void AudioOutPortState_PlacesItsFieldsWhereTheServiceWritesThem()
    {
        SceAudioOutPortState state = default;
        byte* raw = (byte*)&state;
        raw[0] = 0x05;                                    // output, low byte
        raw[2] = 6;                                       // channel
        raw[4] = 0x00; raw[5] = 0x40;                     // volume
        raw[6] = 0x11; raw[7] = 0x00;                     // reroute counter

        Assert.Equal((ushort)0x0005, state.Output);
        Assert.Equal((byte)6, state.Channel);
        Assert.Equal((short)0x4000, state.Volume);
        Assert.Equal((ushort)0x0011, state.RerouteCounter);
    }

    [Fact]
    public void AudioOutStateOutput_NamesTheBitsTheServiceSets()
    {
        Assert.Equal(1, (int)AudioOutStateOutput.Primary);
        Assert.Equal(2, (int)AudioOutStateOutput.Secondary);
        Assert.Equal(4, (int)AudioOutStateOutput.ControllerSpeaker);
        Assert.Equal(64, (int)AudioOutStateOutput.Headphone);
        Assert.Equal(128, (int)AudioOutStateOutput.External);
    }

    [Fact]
    public void AudioOutOutputParam_IsSixteenBytesSoThePointerIsAligned()
        => Assert.Equal(16, sizeof(SceAudioOutOutputParam));

    // The queued output refuses a grain the service would refuse, and says which argument was wrong
    // rather than passing a number down for a call several layers away to reject.
    [Theory]
    [InlineData(0u)]
    [InlineData(128u)]
    [InlineData(300u)]
    [InlineData(4096u)]
    public void AudioQueueDevice_RefusesAGrainTheOutputWouldRefuse(uint grain)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => SharpProspero.Audio.AudioQueueDevice.OpenStereo(grain));

    [Theory]
    [InlineData(44100u)]
    [InlineData(96000u)]
    public void AudioQueueDevice_RefusesASampleRateTheMainOutputDoesNotTake(uint rate)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => SharpProspero.Audio.AudioQueueDevice.OpenStereo(256, rate));

    [Fact]
    public void AudioQueueDevice_RefusesAQueueThatHoldsNothing()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => SharpProspero.Audio.AudioQueueDevice.OpenStereo(256, 48000, 0));
}
