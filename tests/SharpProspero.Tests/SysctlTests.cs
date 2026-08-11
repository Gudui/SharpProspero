// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using System;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// The named-value reader checks its arguments before it reaches the platform, so a bad name is rejected
// the same way on any machine rather than turning into an unhelpful failure from the call underneath.
// Its one piece of decoding, turning a value block into text, is checked here as well.
public sealed class SysctlTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EveryReaderRejectsAnEmptyName(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.Exists(name!));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryGetSize(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadInt32(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadUInt32(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadInt64(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadUInt64(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadString(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadRaw(name!, out _));
        Assert.ThrowsAny<ArgumentException>(() => Sysctl.TryReadRaw(name!, new byte[4], out _));
    }

    // A name is handed over as text terminated by a NUL, so one embedded in the name would silently cut
    // the request short and ask for something else.
    [Fact]
    public void ANameCarryingANulIsRejected()
        => Assert.Throws<ArgumentException>(() => Sysctl.TryReadInt32("hw.\0ncpu", out _));

    [Fact]
    public void ReadingIntoAnEmptyBufferIsRejected()
        => Assert.Throws<ArgumentException>(() => Sysctl.TryReadRaw("hw.ncpu", Span<byte>.Empty, out _));

    [Fact]
    public void DecodeString_StopsAtTheTerminator()
        => Assert.Equal("FreeBSD", Sysctl.DecodeString(Encoding.UTF8.GetBytes("FreeBSD\0junk")));

    [Fact]
    public void DecodeString_TakesTheWholeBlockWhenNothingTerminatesIt()
        => Assert.Equal("11.020.000", Sysctl.DecodeString(Encoding.UTF8.GetBytes("11.020.000")));

    [Fact]
    public void DecodeString_TurnsAnEmptyBlockIntoAnEmptyString()
        => Assert.Equal(string.Empty, Sysctl.DecodeString([]));

    [Fact]
    public void DecodeString_ReadsTextBeyondAscii()
        => Assert.Equal("café", Sysctl.DecodeString(Encoding.UTF8.GetBytes("café\0")));

    [Fact]
    public void TheErrorNumbersNameWhatTheSystemReports()
    {
        Assert.Equal(2, Sysctl.NotPresentError);
        Assert.Equal(1, Sysctl.NotPermittedError);
        Assert.Equal(12, Sysctl.BufferTooSmallError);
    }
}
