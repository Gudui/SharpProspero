// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using Xunit;

namespace SharpProspero.Tests;

// A kernel call that fails adds the system error number to a fixed high half and returns that. A
// caller that only learns "it failed" cannot tell a folder that is not there from one it may not open,
// which is the difference between a wrong path and a wrong permission. These pin the split.
public sealed class SceResultErrorTests
{
    [Fact]
    public void KernelFacilityIsTheHighHalfAFailedKernelCallStamps()
        => Assert.Equal(unchecked((int)0x80020000), SceResult.KernelFacility);

    [Theory]
    [InlineData(1)]     // not permitted
    [InlineData(2)]     // no such path
    [InlineData(13)]    // permission denied
    [InlineData(20)]    // not a directory
    [InlineData(22)]    // invalid argument
    [InlineData(63)]    // name too long
    public void ErrorNumberReadsTheSystemErrorOutOfAKernelCode(int number)
    {
        int code = unchecked(SceResult.KernelFacility + number);

        Assert.True(SceResult.Failed(code));
        Assert.True(SceResult.IsKernelError(code));
        Assert.Equal(number, SceResult.ErrorNumber(code));
    }

    [Fact]
    public void ACodeFromSomewhereElseCarriesNoSystemErrorNumber()
    {
        // A different high half belongs to a different reporter, and its low half means something else.
        const int elsewhere = unchecked((int)0x809F0002);

        Assert.False(SceResult.IsKernelError(elsewhere));
        Assert.Equal(0, SceResult.ErrorNumber(elsewhere));
    }

    [Fact]
    public void DescribeNamesTheReasonWhenTheCodeCarriesOne()
    {
        Assert.Equal("no such path (2)", SceResult.Describe(unchecked(SceResult.KernelFacility + 2)));
        Assert.Equal("permission denied (13)", SceResult.Describe(unchecked(SceResult.KernelFacility + 13)));
        Assert.Equal("not a directory (20)", SceResult.Describe(unchecked(SceResult.KernelFacility + 20)));
    }

    [Fact]
    public void DescribeFallsBackToTheRawCodeRatherThanGuessing()
    {
        // 0x2000 is not a number the header names, and an unnamed number reported as a name would be a
        // fabrication a reader could act on.
        string described = SceResult.Describe(unchecked(SceResult.KernelFacility + 0x2000));
        Assert.Equal("failed (0x80022000)", described);

        Assert.Equal("failed (0x809F0002)", SceResult.Describe(unchecked((int)0x809F0002)));
    }

    [Fact]
    public void DescribeReportsSuccessForANonNegativeCode()
    {
        Assert.Equal("succeeded", SceResult.Describe(SceResult.Ok));
        Assert.Equal("succeeded", SceResult.Describe(7));
    }
}
