// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Content;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The content-delete init parameter, from content_delete.h: a 4-byte reserved gap, an 8-byte heap
// size on the next 8-byte boundary, then a 32-byte reserved tail.
public sealed unsafe class ContentDeleteLayoutTests
{
    [Fact]
    public void InitParam_IsFortyEightBytes()
        => Assert.Equal(48, sizeof(SceContentDeleteInitParam));

    [Fact]
    public void InitParam_HeapSizeSitsAtEight()
        => Assert.Equal(8, (int)Marshal.OffsetOf<SceContentDeleteInitParam>("HeapSize"));
}
