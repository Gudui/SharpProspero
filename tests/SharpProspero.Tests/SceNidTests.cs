// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

public sealed class SceNidTests
{
    [Theory]
    [InlineData("memcpy", "Q3VBxCXhUHs")]
    [InlineData("memset", "8zTFvBIAIN8")]
    [InlineData("malloc", "gQX+4GDQjpM")]
    [InlineData("free", "tIhsqj0qsFE")]
    [InlineData("calloc", "2X5agFjKxMc")]
    [InlineData("abort", "L1SBTkC+Cvw")]
    [InlineData("exit", "uMei1W9uyNo")]
    public void Compute_MatchesKnownIdentifiers(string name, string expected)
    {
        Assert.Equal(expected, SceNid.Compute(name));
    }

    [Fact]
    public void Compute_HasFixedLength()
    {
        Assert.Equal(SceNid.Length, SceNid.Compute("sceUserServiceGetInitialUser").Length);
    }

    [Fact]
    public void ComputeBytes_ReturnsEightBytes()
    {
        Assert.Equal(8, SceNid.ComputeBytes("memcpy").Length);
    }
}
