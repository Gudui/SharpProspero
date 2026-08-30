// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Kernel;
using SharpProspero.Memory;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>One render backend's 63-bit begin/end occlusion counters and their validity bits.</summary>
public readonly record struct PixelPipeQueryPair(
    int Index, ulong Begin, ulong End, bool BeginValid, bool EndValid)
{
    /// <summary>Whether both samples were written by the pixel pipe.</summary>
    public bool IsComplete => BeginValid && EndValid;

    /// <summary>The modulo-2^63 counter difference, or zero while the pair is incomplete.</summary>
    public ulong Delta => IsComplete
        ? (End - Begin) & PixelPipeOcclusionQuery.CounterMask
        : 0;
}

/// <summary>A post-retirement snapshot of all pixel-pipe occlusion counter slots.</summary>
public sealed record PixelPipeQueryResult(
    PixelPipeQueryPair[] Pairs,
    int CompletePairs,
    int PartialPairs,
    int NonConsecutivePairs,
    ulong Sum)
{
    /// <summary>Whether the snapshot can classify a completed zero or nonzero query.</summary>
    public bool IsStructurallyValid => CompletePairs > 0 && PartialPairs == 0 && NonConsecutivePairs == 0;
}

/// <summary>
/// A firmware-generated GFX10 mode-0 pixel-pipe occlusion query. Begin and end each append one
/// addressed <c>PIXEL_PIPE_STAT_DUMP</c> event. The graphics queue preamble owns the active render-
/// backend mask and 128-bit stride, so callers neither guess nor program hardware instances.
/// </summary>
/// <remarks>
/// Call <see cref="Prepare"/>, record <see cref="Begin"/>, the draw and <see cref="End"/>, submit,
/// wait for exact retirement, and only then call <see cref="ReadAfterRetirement"/>. The class cannot
/// prove retirement itself and intentionally exposes no in-flight read method.
/// </remarks>
public sealed unsafe class PixelPipeOcclusionQuery : IDisposable
{
    /// <summary>GFX9+ PAL architecture maximum used for an oversized, mask-independent allocation.</summary>
    public const int MaximumPairs = 32;
    /// <summary>Bytes occupied by one begin/end pair.</summary>
    public const int PairStrideBytes = 16;
    /// <summary>Firmware AGC event type for <c>PIXEL_PIPE_STAT_DUMP</c>.</summary>
    public const uint DumpEvent = 0x39;
    /// <summary>Context offset of <c>DB_COUNT_CONTROL</c>.</summary>
    public const uint DbCountControlOffset = 0x0001;
    /// <summary>One-sample precise GFX10 Z-pass counting state.</summary>
    public const uint PreciseOneSampleDbCountControl = 0x11000106;
    /// <summary>Mask selecting the 63-bit counter below its validity bit.</summary>
    public const ulong CounterMask = 0x7FFF_FFFF_FFFF_FFFFUL;
    /// <summary>Bit written by hardware when a counter sample is complete.</summary>
    public const ulong ValidBit = 0x8000_0000_0000_0000UL;

    private DirectMemoryRegion? _region;
    private bool _prepared;
    private bool _active;
    private bool _ended;

    private PixelPipeOcclusionQuery(DirectMemoryRegion region) => _region = region;

    /// <summary>Allocates an aligned cached-shared result region reachable by the CPU and GPU.</summary>
    public static PixelPipeOcclusionQuery Allocate()
        => new(DirectMemoryRegion.Allocate(
            MaximumPairs * PairStrideBytes,
            KernelMemory.PageSize,
            KernelMemory.MemoryTypeCachedShared,
            KernelMemory.ProtCpuReadWrite | KernelMemory.ProtGpuReadWrite));

    /// <summary>Clears and synchronously writes back the entire mapping before a new submission.</summary>
    public void Prepare()
    {
        DirectMemoryRegion region = Region;
        if (_active)
            throw new InvalidOperationException("Cannot prepare an active pixel-pipe query.");

        new Span<byte>(region.Pointer, checked((int)region.Size)).Clear();
        SceResult.ThrowIfFailed(
            KernelMemory.sceKernelMsync(region.Pointer, region.Size, KernelMemory.MsyncSynchronous),
            nameof(KernelMemory.sceKernelMsync));
        _prepared = true;
        _ended = false;
    }

    /// <summary>Records the mode-0 begin dump and enables precise one-sample Z-pass counting.</summary>
    public void Begin(DrawCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (!_prepared || _active || _ended)
            throw new InvalidOperationException("Prepare the pixel-pipe query exactly once before Begin.");

        DirectMemoryRegion region = Region;
        RequirePacket(commandBuffer.EventWrite(DumpEvent, (ulong)region.Pointer), "begin dump");
        RequirePacket(
            commandBuffer.SetContextRegister(DbCountControlOffset, PreciseOneSampleDbCountControl),
            "DB_COUNT_CONTROL enable");
        _active = true;
    }

    /// <summary>Records the mode-0 end dump and restores the firmware's context-table value.</summary>
    public void End(DrawCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (!_active)
            throw new InvalidOperationException("Begin the pixel-pipe query before End.");

        DirectMemoryRegion region = Region;
        RequirePacket(commandBuffer.EventWrite(DumpEvent, (ulong)region.Pointer + sizeof(ulong)), "end dump");
        uint restoreValue = RegisterDefaults.GetContextValue((ushort)DbCountControlOffset);
        RequirePacket(commandBuffer.SetContextRegister(DbCountControlOffset, restoreValue), "DB_COUNT_CONTROL restore");
        _active = false;
        _ended = true;
    }

    /// <summary>
    /// Invalidates the CPU mapping and parses the result. The caller must have already proven exact GPU
    /// retirement; calling this method is the caller's explicit assertion that no submission is in flight.
    /// </summary>
    public PixelPipeQueryResult ReadAfterRetirement()
    {
        if (!_prepared || _active || !_ended)
            throw new InvalidOperationException("Readback requires a prepared, ended and retired query.");

        DirectMemoryRegion region = Region;
        SceResult.ThrowIfFailed(
            KernelMemory.sceKernelMsync(region.Pointer, region.Size, KernelMemory.MsyncInvalidate),
            nameof(KernelMemory.sceKernelMsync));

        var words = new ReadOnlySpan<ulong>(region.Pointer, MaximumPairs * 2);
        PixelPipeQueryResult result = Parse(words);
        _prepared = false;
        _ended = false;
        return result;
    }

    /// <summary>Parses alternating begin/end words. Exposed internally for host verification.</summary>
    internal static PixelPipeQueryResult Parse(ReadOnlySpan<ulong> words)
    {
        if (words.Length != MaximumPairs * 2)
            throw new ArgumentException($"Expected {MaximumPairs * 2} counter words.", nameof(words));

        var pairs = new PixelPipeQueryPair[MaximumPairs];
        int complete = 0;
        int partial = 0;
        int nonConsecutive = 0;
        ulong sum = 0;
        bool gapSeen = false;

        for (int i = 0; i < MaximumPairs; i++)
        {
            ulong beginWord = words[i * 2];
            ulong endWord = words[i * 2 + 1];
            bool beginValid = (beginWord & ValidBit) != 0;
            bool endValid = (endWord & ValidBit) != 0;
            var pair = new PixelPipeQueryPair(
                i, beginWord & CounterMask, endWord & CounterMask, beginValid, endValid);
            pairs[i] = pair;

            if (pair.IsComplete)
            {
                if (gapSeen)
                    nonConsecutive++;
                complete++;
                sum = unchecked(sum + pair.Delta);
            }
            else
            {
                if (beginValid != endValid)
                    partial++;
                gapSeen = true;
            }
        }

        return new PixelPipeQueryResult(pairs, complete, partial, nonConsecutive, sum);
    }

    /// <summary>Releases the query allocation after all GPU work and readback have completed.</summary>
    public void Dispose()
    {
        if (_active)
            throw new InvalidOperationException("Cannot dispose an active pixel-pipe query.");
        _region?.Dispose();
        _region = null;
        GC.SuppressFinalize(this);
    }

    private DirectMemoryRegion Region
        => _region ?? throw new ObjectDisposedException(nameof(PixelPipeOcclusionQuery));

    private static void RequirePacket(nint packet, string operation)
    {
        if (packet == 0)
            throw new InvalidOperationException("AGC could not record pixel-pipe query " + operation + ".");
    }
}
