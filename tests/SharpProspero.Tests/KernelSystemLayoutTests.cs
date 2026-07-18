// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Kernel;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The version block the kernel fills: an 8-byte size, a 28-byte printable string, then the packed
// value. The whole block is 40 bytes.
public sealed unsafe class KernelSystemLayoutTests
{
    [Fact]
    public void SwVersion_IsFortyBytes()
        => Assert.Equal(40, sizeof(SceKernelSwVersion));

    [Theory]
    [InlineData("Size", 0)]
    [InlineData("VersionString", 8)]
    [InlineData("Version", 36)]
    public void SwVersion_FieldsSitWhereTheKernelWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceKernelSwVersion>(field));
}
