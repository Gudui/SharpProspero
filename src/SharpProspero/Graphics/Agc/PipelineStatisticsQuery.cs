// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Kernel;
using SharpProspero.Memory;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>A validated begin/end snapshot of the eleven GFX10 pipeline-statistics counters.</summary>
public sealed record PipelineStatisticsQueryResult(
    ulong[] Begin,
    ulong[] End,
    ulong[] Delta,
    bool BeginComplete,
    bool EndComplete,
    bool BeginTailIntact,
    bool EndTailIntact)
{
    /// <summary>Whether both hardware samples overwrote every counter and stayed within their slots.</summary>
    public bool IsStructurallyValid => BeginComplete && EndComplete && BeginTailIntact && EndTailIntact;

    /// <summary>Number of primitives accepted by the clipper.</summary>
    public ulong ClipperPrimitives => Delta[PipelineStatisticsQuery.ClipperPrimitivesIndex];

    /// <summary>Number of clipper invocations.</summary>
    public ulong ClipperInvocations => Delta[PipelineStatisticsQuery.ClipperInvocationsIndex];
}

/// <summary>
/// An explicit, bounded GFX10 pipeline-statistics query. It owns sentinel-initialized GPU-visible
/// storage for one begin and one end sample and exposes readback only after caller-proven retirement.
/// </summary>
public sealed unsafe class PipelineStatisticsQuery : IDisposable
{
    /// <summary>Hardware counters written by one GFX10.3 sample.</summary>
    public const int CounterCount = 11;
    /// <summary>Allocated words per sample, including a four-word overrun sentinel tail.</summary>
    public const int SampleStrideQwords = 15;
    /// <summary>Pipeline-statistics start event.</summary>
    public const uint StartEvent = 0x19;
    /// <summary>Pipeline-statistics stop event.</summary>
    public const uint StopEvent = 0x1A;
    /// <summary>Hardware layout index of clipper primitives.</summary>
    public const int ClipperPrimitivesIndex = 1;
    /// <summary>Hardware layout index of clipper invocations.</summary>
    public const int ClipperInvocationsIndex = 2;
    /// <summary>Sentinel used to distinguish missing writes and detect tail corruption.</summary>
    public const ulong Sentinel = ulong.MaxValue;

    private DirectMemoryRegion? _region;
    private bool _prepared;
    private bool _active;
    private bool _ended;

    private PipelineStatisticsQuery(DirectMemoryRegion region) => _region = region;

    /// <summary>Allocates aligned cached-shared storage reachable by the CPU and GPU.</summary>
    public static PipelineStatisticsQuery Allocate()
        => new(DirectMemoryRegion.Allocate(
            SampleStrideQwords * 2 * sizeof(ulong),
            KernelMemory.PageSize,
            KernelMemory.MemoryTypeCachedShared,
            KernelMemory.ProtCpuReadWrite | KernelMemory.ProtGpuReadWrite));

    /// <summary>Initializes both samples and their guard tails, then writes them back synchronously.</summary>
    public void Prepare()
    {
        DirectMemoryRegion region = Region;
        if (_active)
            throw new InvalidOperationException("Cannot prepare an active pipeline-statistics query.");

        new Span<ulong>(region.Pointer, SampleStrideQwords * 2).Fill(Sentinel);
        SceResult.ThrowIfFailed(
            KernelMemory.sceKernelMsync(region.Pointer, region.Size, KernelMemory.MsyncSynchronous),
            nameof(KernelMemory.sceKernelMsync));
        _prepared = true;
        _ended = false;
    }

    /// <summary>Starts pipeline counting and records the begin sample.</summary>
    public void Begin(DrawCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (!_prepared || _active || _ended)
            throw new InvalidOperationException("Prepare the pipeline-statistics query exactly once before Begin.");

        RequirePacket(commandBuffer.EventWrite(StartEvent), "start event");
        RequirePacket(commandBuffer.SamplePipelineStatistics(Region.Pointer), "begin sample");
        _active = true;
    }

    /// <summary>Records the end sample and stops pipeline counting.</summary>
    public void End(DrawCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (!_active)
            throw new InvalidOperationException("Begin the pipeline-statistics query before End.");

        byte* end = (byte*)Region.Pointer + SampleStrideQwords * sizeof(ulong);
        RequirePacket(commandBuffer.SamplePipelineStatistics(end), "end sample");
        RequirePacket(commandBuffer.EventWrite(StopEvent), "stop event");
        _active = false;
        _ended = true;
    }

    /// <summary>
    /// Invalidates and parses the result after the caller has proven exact GPU retirement.
    /// </summary>
    public PipelineStatisticsQueryResult ReadAfterRetirement()
    {
        if (!_prepared || _active || !_ended)
            throw new InvalidOperationException("Readback requires a prepared, ended and retired query.");

        DirectMemoryRegion region = Region;
        SceResult.ThrowIfFailed(
            KernelMemory.sceKernelMsync(region.Pointer, region.Size, KernelMemory.MsyncInvalidate),
            nameof(KernelMemory.sceKernelMsync));

        PipelineStatisticsQueryResult result = Parse(
            new ReadOnlySpan<ulong>(region.Pointer, SampleStrideQwords * 2));
        _prepared = false;
        _ended = false;
        return result;
    }

    /// <summary>Parses two fixed-stride samples. Exposed internally for host verification.</summary>
    internal static PipelineStatisticsQueryResult Parse(ReadOnlySpan<ulong> words)
    {
        if (words.Length != SampleStrideQwords * 2)
            throw new ArgumentException($"Expected {SampleStrideQwords * 2} query words.", nameof(words));

        ulong[] begin = words[..CounterCount].ToArray();
        ulong[] end = words.Slice(SampleStrideQwords, CounterCount).ToArray();
        var delta = new ulong[CounterCount];
        bool beginComplete = true;
        bool endComplete = true;
        for (int i = 0; i < CounterCount; i++)
        {
            beginComplete &= begin[i] != Sentinel;
            endComplete &= end[i] != Sentinel;
            delta[i] = unchecked(end[i] - begin[i]);
        }

        bool beginTailIntact = TailIsSentinel(words.Slice(CounterCount, SampleStrideQwords - CounterCount));
        bool endTailIntact = TailIsSentinel(words.Slice(
            SampleStrideQwords + CounterCount, SampleStrideQwords - CounterCount));
        return new PipelineStatisticsQueryResult(
            begin, end, delta, beginComplete, endComplete, beginTailIntact, endTailIntact);
    }

    /// <summary>Releases the allocation after all GPU work and readback have completed.</summary>
    public void Dispose()
    {
        if (_active)
            throw new InvalidOperationException("Cannot dispose an active pipeline-statistics query.");
        _region?.Dispose();
        _region = null;
        GC.SuppressFinalize(this);
    }

    private DirectMemoryRegion Region
        => _region ?? throw new ObjectDisposedException(nameof(PipelineStatisticsQuery));

    private static bool TailIsSentinel(ReadOnlySpan<ulong> tail)
    {
        foreach (ulong word in tail)
        {
            if (word != Sentinel)
                return false;
        }
        return true;
    }

    private static void RequirePacket(nint packet, string operation)
    {
        if (packet == 0)
            throw new InvalidOperationException("AGC could not record pipeline-statistics query " + operation + ".");
    }
}
