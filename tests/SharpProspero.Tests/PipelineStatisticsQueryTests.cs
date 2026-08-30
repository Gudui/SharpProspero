// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class PipelineStatisticsQueryTests
{
    [Fact]
    public void ContractMatchesValidatedGfx103Layout()
    {
        Assert.Equal(11, PipelineStatisticsQuery.CounterCount);
        Assert.Equal(15, PipelineStatisticsQuery.SampleStrideQwords);
        Assert.Equal(0x19u, PipelineStatisticsQuery.StartEvent);
        Assert.Equal(0x1Au, PipelineStatisticsQuery.StopEvent);
        Assert.Equal(1, PipelineStatisticsQuery.ClipperPrimitivesIndex);
        Assert.Equal(2, PipelineStatisticsQuery.ClipperInvocationsIndex);
    }

    [Fact]
    public void SamplePacketUsesAddressedPipelineStatEncoding()
    {
        const uint bytes = 64;
        uint* memory = (uint*)NativeMemory.AllocZeroed(bytes);
        try
        {
            using var commandBuffer = new DrawCommandBuffer(memory, bytes);
            nint packet = commandBuffer.SamplePipelineStatistics((void*)0x1234_5678_9ABC_DEF0UL);

            Assert.Equal((nint)memory, packet);
            Assert.Equal(4u, commandBuffer.SubmitSizeDwords);
            Assert.Equal(0xC002_4600u, memory[0]);
            Assert.Equal(0x0000_021Eu, memory[1]);
            Assert.Equal(0x9ABC_DEF0u, memory[2]);
            Assert.Equal(0x1234_5678u, memory[3]);
        }
        finally
        {
            NativeMemory.Free(memory);
        }
    }

    [Fact]
    public void SamplePacketRejectsInvalidDestinationAndCapacity()
    {
        uint* memory = (uint*)NativeMemory.AllocZeroed(16);
        try
        {
            using var commandBuffer = new DrawCommandBuffer(memory, 16);
            Assert.Throws<ArgumentNullException>(() => commandBuffer.SamplePipelineStatistics(null));
            Assert.Throws<ArgumentException>(() => commandBuffer.SamplePipelineStatistics((void*)0x1001));
            commandBuffer.SamplePipelineStatistics((void*)0x1000);
            Assert.Throws<InvalidOperationException>(() => commandBuffer.SamplePipelineStatistics((void*)0x2000));
        }
        finally
        {
            NativeMemory.Free(memory);
        }
    }

    [Fact]
    public void CompleteSamplesExposeClipperDeltas()
    {
        ulong[] words = SentinelWords();
        for (int i = 0; i < PipelineStatisticsQuery.CounterCount; i++)
        {
            words[i] = (ulong)(100 + i);
            words[PipelineStatisticsQuery.SampleStrideQwords + i] = (ulong)(110 + i * 2);
        }

        PipelineStatisticsQueryResult result = PipelineStatisticsQuery.Parse(words);

        Assert.True(result.IsStructurallyValid);
        Assert.Equal(11UL, result.ClipperPrimitives);
        Assert.Equal(12UL, result.ClipperInvocations);
        Assert.All(result.BeginTail, value => Assert.Equal(PipelineStatisticsQuery.Sentinel, value));
        Assert.All(result.EndTail, value => Assert.Equal(PipelineStatisticsQuery.Sentinel, value));
    }

    [Fact]
    public void MissingCounterAndTailOverwriteAreStructuralErrors()
    {
        ulong[] words = SentinelWords();
        for (int i = 0; i < PipelineStatisticsQuery.CounterCount; i++)
        {
            words[i] = 0;
            words[PipelineStatisticsQuery.SampleStrideQwords + i] = 0;
        }
        words[4] = PipelineStatisticsQuery.Sentinel;
        words[PipelineStatisticsQuery.CounterCount] = 0;

        PipelineStatisticsQueryResult result = PipelineStatisticsQuery.Parse(words);

        Assert.False(result.IsStructurallyValid);
        Assert.False(result.BeginComplete);
        Assert.False(result.BeginTailIntact);
        Assert.True(result.EndComplete);
        Assert.True(result.EndTailIntact);
    }

    [Fact]
    public void DeltaWrapsAsUnsignedCounterArithmetic()
    {
        ulong[] words = SentinelWords();
        for (int i = 0; i < PipelineStatisticsQuery.CounterCount; i++)
        {
            words[i] = ulong.MaxValue - 2;
            words[PipelineStatisticsQuery.SampleStrideQwords + i] = 3;
        }

        PipelineStatisticsQueryResult result = PipelineStatisticsQuery.Parse(words);

        Assert.True(result.IsStructurallyValid);
        Assert.All(result.Delta, value => Assert.Equal(6UL, value));
    }

    [Fact]
    public void ParserRejectsWrongSizedStorage()
    {
        Assert.Throws<ArgumentException>(() => PipelineStatisticsQuery.Parse(new ulong[2]));
    }

    private static ulong[] SentinelWords()
    {
        var words = new ulong[PipelineStatisticsQuery.SampleStrideQwords * 2];
        Array.Fill(words, PipelineStatisticsQuery.Sentinel);
        return words;
    }
}
