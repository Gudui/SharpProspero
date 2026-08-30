// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PixelPipeOcclusionQueryTests
{
    [Fact]
    public void ContractMatchesValidatedFirmwareAndGfx10Layout()
    {
        Assert.Equal(0x39u, PixelPipeOcclusionQuery.DumpEvent);
        Assert.Equal(0x0001u, PixelPipeOcclusionQuery.DbCountControlOffset);
        Assert.Equal(0x11000106u, PixelPipeOcclusionQuery.PreciseOneSampleDbCountControl);
        Assert.Equal(32, PixelPipeOcclusionQuery.MaximumPairs);
        Assert.Equal(16, PixelPipeOcclusionQuery.PairStrideBytes);
    }

    [Fact]
    public void CompletePackedPairsProduceNonzeroSum()
    {
        ulong[] words = EmptyWords();
        words[0] = Valid(100);
        words[1] = Valid(125);
        words[2] = Valid(7);
        words[3] = Valid(12);

        PixelPipeQueryResult result = PixelPipeOcclusionQuery.Parse(words);

        Assert.True(result.IsStructurallyValid);
        Assert.Equal(2, result.CompletePairs);
        Assert.Equal(0, result.PartialPairs);
        Assert.Equal(0, result.NonConsecutivePairs);
        Assert.Equal(30UL, result.Sum);
        Assert.Equal(25UL, result.Pairs[0].Delta);
        Assert.Equal(5UL, result.Pairs[1].Delta);
    }

    [Fact]
    public void CompleteZeroIsDistinctFromMissingValidity()
    {
        ulong[] completeZero = EmptyWords();
        completeZero[0] = Valid(42);
        completeZero[1] = Valid(42);

        PixelPipeQueryResult zero = PixelPipeOcclusionQuery.Parse(completeZero);
        PixelPipeQueryResult missing = PixelPipeOcclusionQuery.Parse(EmptyWords());

        Assert.True(zero.IsStructurallyValid);
        Assert.Equal(0UL, zero.Sum);
        Assert.False(missing.IsStructurallyValid);
        Assert.Equal(0, missing.CompletePairs);
        Assert.Equal(0UL, missing.Sum);
    }

    [Fact]
    public void PartialAndNonConsecutivePairsAreStructuralErrors()
    {
        ulong[] words = EmptyWords();
        words[0] = Valid(1);
        words[1] = 2;
        words[4] = Valid(10);
        words[5] = Valid(20);

        PixelPipeQueryResult result = PixelPipeOcclusionQuery.Parse(words);

        Assert.False(result.IsStructurallyValid);
        Assert.Equal(1, result.PartialPairs);
        Assert.Equal(1, result.NonConsecutivePairs);
        Assert.Equal(1, result.CompletePairs);
    }

    [Fact]
    public void CounterDifferenceWrapsWithinSixtyThreeBits()
    {
        ulong[] words = EmptyWords();
        words[0] = Valid(PixelPipeOcclusionQuery.CounterMask - 2);
        words[1] = Valid(3);

        PixelPipeQueryResult result = PixelPipeOcclusionQuery.Parse(words);

        Assert.True(result.IsStructurallyValid);
        Assert.Equal(6UL, result.Sum);
    }

    [Fact]
    public void ParserRejectsWrongSizedStorage()
    {
        Assert.Throws<ArgumentException>(() => PixelPipeOcclusionQuery.Parse(new ulong[2]));
    }

    private static ulong[] EmptyWords() => new ulong[PixelPipeOcclusionQuery.MaximumPairs * 2];

    private static ulong Valid(ulong count)
        => PixelPipeOcclusionQuery.ValidBit | (count & PixelPipeOcclusionQuery.CounterMask);
}
