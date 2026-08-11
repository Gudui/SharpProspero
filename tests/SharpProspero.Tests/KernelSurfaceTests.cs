// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using SharpProspero.Interop.Kernel;
using SharpProspero.Memory;
using SharpProspero.Threading;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The blocks the kernel fills and the numbers it compares against. A field read at the wrong offset
// returns an unrelated value and no call reports that it did, so the shapes are pinned here rather
// than trusted to a reading of the declaration.
public sealed unsafe class KernelSurfaceLayoutTests
{
    [Fact]
    public void Event_IsThirtyTwoBytes()
        => Assert.Equal(32, sizeof(SceKernelEvent));

    [Theory]
    [InlineData("Identifier", 0)]
    [InlineData("Filter", 8)]
    [InlineData("Flags", 10)]
    [InlineData("FilterFlags", 12)]
    [InlineData("Data", 16)]
    [InlineData("UserData", 24)]
    public void Event_FieldsSitWhereTheQueueWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceKernelEvent>(field));

    [Fact]
    public void VirtualQueryInfo_IsSeventyTwoBytes()
        => Assert.Equal(72, sizeof(SceKernelVirtualQueryInfo));

    [Theory]
    [InlineData("Start", 0)]
    [InlineData("End", 8)]
    [InlineData("Offset", 16)]
    [InlineData("Protection", 24)]
    [InlineData("MemoryType", 28)]
    [InlineData("Flags", 32)]
    [InlineData("Name", 33)]
    [InlineData("GpuMaskId", 65)]
    public void VirtualQueryInfo_FieldsSitWhereTheQueryWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceKernelVirtualQueryInfo>(field));

    // The seven kind bits share the single byte at offset 32, packed from the least significant end.
    // Reading one of them out of the wrong bit reports a range as backed by the wrong pool.
    [Theory]
    [InlineData(0x01, true, false, false, false, false)]
    [InlineData(0x02, false, true, false, false, false)]
    [InlineData(0x04, false, false, true, false, false)]
    [InlineData(0x08, false, false, false, true, false)]
    [InlineData(0x10, false, false, false, false, true)]
    public void VirtualQueryInfo_KindBitsDecodeFromTheirOwnBit(
        byte flags, bool flexible, bool direct, bool stack, bool pooled, bool committed)
    {
        var info = new SceKernelVirtualQueryInfo { Flags = flags };
        Assert.Equal(flexible, info.IsFlexibleMemory);
        Assert.Equal(direct, info.IsDirectMemory);
        Assert.Equal(stack, info.IsStack);
        Assert.Equal(pooled, info.IsPooledMemory);
        Assert.Equal(committed, info.IsCommitted);
    }

    [Fact]
    public void VirtualQueryInfo_HighKindBitsDecodeFromTheirOwnBit()
    {
        Assert.True(new SceKernelVirtualQueryInfo { Flags = 0x20 }.IsGpuPrt);
        Assert.True(new SceKernelVirtualQueryInfo { Flags = 0x40 }.IsAmmUsage);
        Assert.False(new SceKernelVirtualQueryInfo { Flags = 0x40 }.IsGpuPrt);
    }

    [Fact]
    public void DirectMemoryQueryInfo_IsTwentyFourBytes()
        => Assert.Equal(24, sizeof(SceKernelDirectMemoryQueryInfo));

    [Theory]
    [InlineData("Start", 0)]
    [InlineData("End", 8)]
    [InlineData("MemoryType", 16)]
    public void DirectMemoryQueryInfo_FieldsSitWhereTheQueryWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceKernelDirectMemoryQueryInfo>(field));

    [Fact]
    public void MemoryPoolBlockStats_IsSixteenBytes()
        => Assert.Equal(16, sizeof(SceKernelMemoryPoolBlockStats));

    [Fact]
    public void SchedParam_IsFourBytes()
        => Assert.Equal(4, sizeof(SceKernelSchedParam));

    [Fact]
    public void Uuid_IsSixteenBytes()
        => Assert.Equal(16, sizeof(SceKernelUuid));

    [Theory]
    [InlineData("TimeLow", 0)]
    [InlineData("TimeMid", 4)]
    [InlineData("TimeHighAndVersion", 6)]
    [InlineData("ClockSequenceHighAndReserved", 8)]
    [InlineData("ClockSequenceLow", 9)]
    [InlineData("Node", 10)]
    public void Uuid_FieldsSitWhereTheSystemWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceKernelUuid>(field));
}

// The numbers the platform compares against. Each one is a value an application has to send exactly,
// so they are pinned rather than left to a reading of the declaration.
public sealed class KernelSurfaceConstantTests
{
    [Fact]
    public void EventFilters_CarryTheNumbersTheQueueReports()
    {
        Assert.Equal(-1, (int)KernelEqueue.FilterRead);
        Assert.Equal(-2, (int)KernelEqueue.FilterWrite);
        Assert.Equal(-4, (int)KernelEqueue.FilterFile);
        Assert.Equal(-7, (int)KernelEqueue.FilterTimer);
        Assert.Equal(-11, (int)KernelEqueue.FilterUser);
        Assert.Equal(-13, (int)KernelEqueue.FilterVideoOut);
        Assert.Equal(-14, (int)KernelEqueue.FilterGraphicsCore);
        Assert.Equal(-15, (int)KernelEqueue.FilterHighResolutionTimer);
    }

    [Fact]
    public void EventReportFlags_CarryTheBitsTheQueueSets()
    {
        Assert.Equal(0x8000, (int)KernelEqueue.FlagEndOfFile);
        Assert.Equal(0x4000, (int)KernelEqueue.FlagError);
    }

    [Fact]
    public void FileWatchNotes_CarryTheirOwnBits()
    {
        Assert.Equal(0x0001u, KernelEqueue.NoteDelete);
        Assert.Equal(0x0002u, KernelEqueue.NoteWrite);
        Assert.Equal(0x0004u, KernelEqueue.NoteExtend);
        Assert.Equal(0x0008u, KernelEqueue.NoteAttrib);
        Assert.Equal(0x0020u, KernelEqueue.NoteRename);
        Assert.Equal(0x0040u, KernelEqueue.NoteRevoke);
        Assert.Equal(0x006Fu, KernelEqueue.NoteAll);
    }

    [Fact]
    public void EventFlagAttributes_CarryTheirOwnBits()
    {
        Assert.Equal(0x01u, KernelEventFlags.AttrThreadFifo);
        Assert.Equal(0x02u, KernelEventFlags.AttrThreadPriority);
        Assert.Equal(0x10u, KernelEventFlags.AttrSingle);
        Assert.Equal(0x20u, KernelEventFlags.AttrMulti);
    }

    [Fact]
    public void EventFlagWaitModes_CarryTheirOwnBits()
    {
        Assert.Equal(0x01u, KernelEventFlags.WaitModeAnd);
        Assert.Equal(0x02u, KernelEventFlags.WaitModeOr);
        Assert.Equal(0x10u, KernelEventFlags.WaitModeClearAll);
        Assert.Equal(0x20u, KernelEventFlags.WaitModeClearPattern);
    }

    [Fact]
    public void SemaphoreAttributes_CarryTheirOwnBits()
    {
        Assert.Equal(0x01u, KernelSemaphores.AttrThreadFifo);
        Assert.Equal(0x02u, KernelSemaphores.AttrThreadPriority);
    }

    // The scheduling policies are not numbered the way another system numbers them: the time-shared
    // policy is 2 here and round-robin is 3, so a value carried across from elsewhere selects the wrong
    // policy without failing.
    [Fact]
    public void SchedulingPolicies_CarryThisPlatformsNumbering()
    {
        Assert.Equal(1, KernelScheduling.SchedFifo);
        Assert.Equal(2, KernelScheduling.SchedOther);
        Assert.Equal(3, KernelScheduling.SchedRoundRobin);
    }

    [Fact]
    public void SchedulingPolicyEnum_MatchesTheRawNumbering()
    {
        Assert.Equal(KernelScheduling.SchedOther, (int)SchedulingPolicy.Default);
        Assert.Equal(KernelScheduling.SchedFifo, (int)SchedulingPolicy.FirstInFirstOut);
        Assert.Equal(KernelScheduling.SchedRoundRobin, (int)SchedulingPolicy.RoundRobin);
    }

    [Fact]
    public void ThreadAttributeStates_CarryTheirOwnValues()
    {
        Assert.Equal(0, KernelScheduling.CreateJoinable);
        Assert.Equal(1, KernelScheduling.CreateDetached);
        Assert.Equal(0, KernelScheduling.ExplicitSched);
        Assert.Equal(4, KernelScheduling.InheritSched);
    }

    // A smaller number is served first, so the most urgent priority is the smallest value.
    [Fact]
    public void ThreadPriorities_RunFromTheSmallestNumberFirst()
    {
        Assert.Equal(256, Processor.PriorityHighest);
        Assert.Equal(700, Processor.PriorityDefault);
        Assert.Equal(767, Processor.PriorityLowest);
        Assert.True(Processor.PriorityHighest < Processor.PriorityLowest);
    }

    [Fact]
    public void MemoryAdvice_CarriesItsOwnNumbering()
    {
        Assert.Equal(0, KernelMemory.AdviseNormal);
        Assert.Equal(1, KernelMemory.AdviseRandom);
        Assert.Equal(2, KernelMemory.AdviseSequential);
        Assert.Equal(3, KernelMemory.AdviseWillNeed);
        Assert.Equal(4, KernelMemory.AdviseDontNeed);
        Assert.Equal(8, KernelMemory.AdviseNoCore);
        Assert.Equal(9, KernelMemory.AdviseCore);
    }

    [Fact]
    public void MemorySyncModes_CarryTheirOwnNumbering()
    {
        Assert.Equal(0, KernelMemory.MsyncSynchronous);
        Assert.Equal(1, KernelMemory.MsyncAsynchronous);
        Assert.Equal(2, KernelMemory.MsyncInvalidate);
    }

    [Fact]
    public void MemoryLockScopes_CarryTheirOwnBits()
    {
        Assert.Equal(1, KernelMemory.MemoryLockCurrent);
        Assert.Equal(2, KernelMemory.MemoryLockFuture);
    }

    [Fact]
    public void MappingFlagsAndBounds_CarryTheirOwnValues()
    {
        Assert.Equal(0x0010, KernelMemory.MapFixed);
        Assert.Equal(0x0080, KernelMemory.MapNoOverwrite);
        Assert.Equal(0x1000000000UL, KernelMemory.MapAreaStart);
        Assert.Equal(0xfc00000000UL, KernelMemory.MapAreaEnd);
        Assert.Equal(32, KernelMemory.VirtualRangeNameSize);
        Assert.Equal(1, KernelMemory.QueryFindNext);
    }

    // The alignment request is the power of two shifted into the top byte of the flags word.
    [Theory]
    [InlineData(14, 14 << 24)]
    [InlineData(16, 16 << 24)]
    [InlineData(21, 21 << 24)]
    public void MapAligned_ShiftsThePowerIntoTheFlagsWord(int shift, int expected)
        => Assert.Equal(expected, KernelMemory.MapAligned(shift));

    [Fact]
    public void ProcessorMask_AdmitsThirteenProcessors()
    {
        Assert.Equal(0x1fffUL, SceKernelCpumask.All);
        Assert.Equal(13, System.Numerics.BitOperations.PopCount(SceKernelCpumask.All));
        Assert.Equal(1UL << 5, SceKernelCpumask.Only(5));
    }
}

// Decoding the platform hands back, and the arguments a caller hands in. All of it runs off the
// device, so a mistake here is caught by the suite rather than by a build that behaves oddly.
public sealed unsafe class KernelSurfaceDecodingTests
{
    [Theory]
    [InlineData(KernelEqueue.FilterRead, EventSource.Readable)]
    [InlineData(KernelEqueue.FilterWrite, EventSource.Writable)]
    [InlineData(KernelEqueue.FilterFile, EventSource.FileChanged)]
    [InlineData(KernelEqueue.FilterTimer, EventSource.Timer)]
    [InlineData(KernelEqueue.FilterHighResolutionTimer, EventSource.Timer)]
    [InlineData(KernelEqueue.FilterUser, EventSource.User)]
    [InlineData(KernelEqueue.FilterVideoOut, EventSource.VideoOut)]
    [InlineData(KernelEqueue.FilterGraphicsCore, EventSource.GraphicsCore)]
    [InlineData((short)-3, EventSource.Other)]
    public void QueuedEvent_NamesTheSourceItsFilterReports(short filter, EventSource expected)
        => Assert.Equal(expected, QueuedEvent.FromFilter(filter));

    [Fact]
    public void QueuedEvent_CarriesTheReportsFieldsThrough()
    {
        var raw = new SceKernelEvent
        {
            Identifier = 7,
            Filter = KernelEqueue.FilterTimer,
            Flags = KernelEqueue.FlagEndOfFile,
            FilterFlags = KernelEqueue.NoteWrite,
            Data = 4096,
        };

        QueuedEvent decoded = QueuedEvent.From(raw);
        Assert.Equal(EventSource.Timer, decoded.Source);
        Assert.Equal(KernelEqueue.FilterTimer, decoded.RawFilter);
        Assert.Equal((nuint)7, decoded.Identifier);
        Assert.Equal((nint)4096, decoded.Data);
        Assert.Equal(KernelEqueue.NoteWrite, decoded.FilterFlags);
        Assert.True(decoded.IsEndOfFile);
        Assert.False(decoded.IsError);
    }

    [Fact]
    public void QueuedEvent_ReportsAnErrorWhenTheQueueSetsThatBit()
    {
        var raw = new SceKernelEvent { Filter = KernelEqueue.FilterRead, Flags = KernelEqueue.FlagError, Data = 9 };
        QueuedEvent decoded = QueuedEvent.From(raw);
        Assert.True(decoded.IsError);
        Assert.Equal((nint)9, decoded.Data);
    }

    [Theory]
    [InlineData(EventFlagWait.All, EventFlagClear.None, 0x01u)]
    [InlineData(EventFlagWait.Any, EventFlagClear.None, 0x02u)]
    [InlineData(EventFlagWait.All, EventFlagClear.Requested, 0x21u)]
    [InlineData(EventFlagWait.Any, EventFlagClear.All, 0x12u)]
    public void EventFlagWaitMode_CombinesTheModeWithTheClearRule(
        EventFlagWait mode, EventFlagClear clear, uint expected)
        => Assert.Equal(expected, EventFlag.WaitMode(mode, clear));

    [Fact]
    public void MappedRange_NamesWhatBacksIt()
    {
        Assert.Equal(MappingBacking.Direct, Decode(0x02).Backing);
        Assert.Equal(MappingBacking.Flexible, Decode(0x01).Backing);
        Assert.Equal(MappingBacking.Pooled, Decode(0x08).Backing);
        Assert.Equal(MappingBacking.Unknown, Decode(0x00).Backing);
        // Both pools claimed at once is reported as direct, which is the one that constrains a caller.
        Assert.Equal(MappingBacking.Direct, Decode(0x03).Backing);

        static MappedRange Decode(byte flags)
            => MappedRange.From(new SceKernelVirtualQueryInfo { Flags = flags });
    }

    [Fact]
    public void MappedRange_ReadsTheBoundsAndProtectionBack()
    {
        var info = new SceKernelVirtualQueryInfo
        {
            Start = (void*)0x1000000000,
            End = (void*)0x1000010000,
            Offset = 0x40000,
            Protection = KernelMemory.ProtCpuReadWrite | KernelMemory.ProtGpuRead,
            MemoryType = KernelMemory.MemoryTypeCachedShared,
            Flags = 0x02 | 0x10,
        };

        MappedRange range = MappedRange.From(info);
        Assert.Equal(unchecked((nuint)0x1000000000), range.Start);
        Assert.Equal(unchecked((nuint)0x1000010000), range.End);
        Assert.Equal((nuint)0x10000, range.Size);
        Assert.Equal(0x40000, range.PhysicalOffset);
        Assert.True(range.CpuCanRead);
        Assert.True(range.CpuCanWrite);
        Assert.True(range.GpuCanRead);
        Assert.False(range.GpuCanWrite);
        Assert.True(range.IsCommitted);
        Assert.Equal(string.Empty, range.Name);
    }

    [Fact]
    public void MappedRange_ReadsTheNameUpToItsTerminator()
    {
        var info = new SceKernelVirtualQueryInfo();
        byte[] name = System.Text.Encoding.UTF8.GetBytes("frame buffers");
        for (int i = 0; i < name.Length; i++)
            info.Name[i] = name[i];
        info.Name[name.Length] = 0;
        // Rubbish past the terminator must not be read: a range whose name was overwritten by a shorter
        // one still has the tail of the longer one behind it.
        info.Name[name.Length + 1] = (byte)'X';

        Assert.Equal("frame buffers", MappedRange.From(info).Name);
    }

    [Fact]
    public void MappedRange_ReadsANameThatFillsTheFieldWithNoRoomForATerminator()
    {
        var info = new SceKernelVirtualQueryInfo();
        for (int i = 0; i < KernelMemory.VirtualRangeNameSize; i++)
            info.Name[i] = (byte)'a';

        Assert.Equal(new string('a', KernelMemory.VirtualRangeNameSize), MappedRange.From(info).Name);
    }

    // The platform holds the same three leading fields in the processor's own byte order, so they carry
    // across without being reversed; the eight bytes after them are plain and carry across as they are.
    [Fact]
    public void Uuid_ConvertsFieldForField()
    {
        var uuid = new SceKernelUuid
        {
            TimeLow = 0x01020304,
            TimeMid = 0x0506,
            TimeHighAndVersion = 0x4708,
            ClockSequenceHighAndReserved = 0x89,
            ClockSequenceLow = 0x0a,
        };
        byte[] node = [0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10];
        for (int i = 0; i < node.Length; i++)
            uuid.Node[i] = node[i];

        Guid expected = new(0x01020304, 0x0506, 0x4708, 0x89, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10);
        Assert.Equal(expected, ProcessInfo.ToGuid(uuid));
    }
}

// The microsecond field the waiting calls take is unsigned and 32-bit, so a caller's TimeSpan has to
// be checked before it is narrowed rather than silently wrapping into a short wait.
public sealed class WaitTimeoutTests
{
    [Fact]
    public void Zero_IsZero()
        => Assert.Equal(0u, WaitTimeout.ToMicroseconds(TimeSpan.Zero));

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(250, 250_000)]
    public void Milliseconds_BecomeMicroseconds(int milliseconds, uint expected)
        => Assert.Equal(expected, WaitTimeout.ToMicroseconds(TimeSpan.FromMilliseconds(milliseconds)));

    // A wait shorter than a microsecond is still a wait; rounding it down to zero would turn it into
    // "do not wait at all", which is the opposite of what the caller asked for.
    [Fact]
    public void ARemainderBelowAMicrosecondRoundsUp()
    {
        Assert.Equal(1u, WaitTimeout.ToMicroseconds(TimeSpan.FromTicks(1)));
        Assert.Equal(2u, WaitTimeout.ToMicroseconds(TimeSpan.FromTicks(11)));
    }

    [Fact]
    public void TheLongestWaitIsAccepted()
        => Assert.Equal(uint.MaxValue, WaitTimeout.ToMicroseconds(WaitTimeout.Maximum));

    [Fact]
    public void ANegativeWaitIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => WaitTimeout.ToMicroseconds(TimeSpan.FromMilliseconds(-1)));

    [Fact]
    public void AWaitPastTheFieldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => WaitTimeout.ToMicroseconds(WaitTimeout.Maximum + TimeSpan.FromTicks(10)));

    [Fact]
    public void TheLongestWaitIsALittleOverSeventyOneMinutes()
        => Assert.InRange(WaitTimeout.Maximum.TotalMinutes, 71.5, 71.7);
}

// A priority computed from an offset must not walk out of the range the platform accepts.
public sealed class ProcessorPriorityRangeTests
{
    [Theory]
    [InlineData(256, true)]
    [InlineData(700, true)]
    [InlineData(767, true)]
    [InlineData(255, false)]
    [InlineData(768, false)]
    [InlineData(0, false)]
    public void APriorityIsValidOnlyInsideTheRange(int priority, bool expected)
        => Assert.Equal(expected, Processor.IsValidPriority(priority));

    [Theory]
    [InlineData(0, 256)]
    [InlineData(500, 500)]
    [InlineData(9000, 767)]
    public void ClampBringsAPriorityBackIntoTheRange(int priority, int expected)
        => Assert.Equal(expected, Processor.ClampPriority(priority));

    [Fact]
    public void EveryClampedPriorityIsValid()
        => Assert.All(new[] { int.MinValue, -1, 0, 255, 256, 767, 768, int.MaxValue },
            p => Assert.True(Processor.IsValidPriority(Processor.ClampPriority(p))));
}

// Every name these bindings import has to be one the catalogue files under a library that publishes
// it, or the module carries an import nothing can bind and never reaches its first instruction. The
// general check lives with the catalogue tests; this pins the set added for the kernel surface.
public sealed class KernelSurfaceCatalogTests
{
    private static readonly string[] Expected =
    [
        "sceKernelCreateEqueue", "sceKernelDeleteEqueue", "sceKernelWaitEqueue",
        "sceKernelAddTimerEvent", "sceKernelAddHRTimerEvent", "sceKernelAddReadEvent",
        "sceKernelAddWriteEvent", "sceKernelAddFileEvent", "sceKernelAddUserEvent",
        "sceKernelAddUserEventEdge", "sceKernelTriggerUserEvent",
        "sceKernelCreateEventFlag", "sceKernelWaitEventFlag", "sceKernelSetEventFlag",
        "sceKernelCancelEventFlag",
        "sceKernelCreateSema", "sceKernelWaitSema", "sceKernelSignalSema", "sceKernelCancelSema",
        "scePthreadGetschedparam", "scePthreadSetschedparam", "scePthreadGetcpuclockid",
        "scePthreadAttrInit", "scePthreadAttrSetschedpolicy", "scePthreadAttrSetinheritsched",
        "sceKernelVirtualQuery", "sceKernelDirectMemoryQuery", "sceKernelIsStack",
        "sceKernelSetVirtualRangeName", "sceKernelGetPageTableStats", "sceKernelMsync",
        "getargc", "getargv", "sceKernelUuidCreate",
        "getpagesize", "getdtablesize", "sched_get_priority_max", "sched_get_priority_min",
        "mlockall", "munlockall",
    ];

    [Fact]
    public void EveryAddedNameIsNamedByACatalogEntry()
    {
        var provided = SharpProspero.Prx.StubCatalog.Core.SelectMany(e => e.Exports).ToHashSet();
        string[] missing = [.. Expected.Where(n => !provided.Contains(n)).Order()];
        Assert.True(missing.Length == 0, "Not named by any catalog entry:\n  " + string.Join("\n  ", missing));
    }
}
