// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.AppContent;
using SharpProspero.Interop.PlayGo;
using SharpProspero.Prx;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// PlayGo and AppContent layouts, recomputed from their headers.
public sealed unsafe class PlayGoAppContentLayoutTests
{
    [Fact] public void PlayGoInitParams_IsSixteen() => Assert.Equal(16, sizeof(ScePlayGoInitParams));
    [Fact] public void PlayGoProgress_IsSixteen() => Assert.Equal(16, sizeof(ScePlayGoProgress));
    [Fact] public void AppContentInitParam_IsThirtyTwo() => Assert.Equal(32, sizeof(SceAppContentInitParam));
    [Fact] public void AppContentBootParam_IsForty() => Assert.Equal(40, sizeof(SceAppContentBootParam));

    [Theory]
    [InlineData("BufAddr", 0)]
    [InlineData("BufSize", 8)]
    public void PlayGoInitParams_Fields(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<ScePlayGoInitParams>(field));

    [Theory]
    [InlineData("ProgressSize", 0)]
    [InlineData("TotalSize", 8)]
    public void PlayGoProgress_Fields(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<ScePlayGoProgress>(field));

    [Fact]
    public void AppContentBootParam_AttrAtFour()
        => Assert.Equal(4, (int)Marshal.OffsetOf<SceAppContentBootParam>("Attr"));

    [Theory]
    [InlineData(PlayGoLocus.NotDownloaded, 0)]
    [InlineData(PlayGoLocus.LocalSlow, 2)]
    [InlineData(PlayGoLocus.LocalFast, 3)]
    public void Locus_MatchesTheHeader(PlayGoLocus value, int expected)
        => Assert.Equal(expected, (int)value);

    // PlayGo publishes module version 1.0; a 1.1 stub would install and then fail to bind.
    [Fact]
    public void PlayGo_StubRecordsModuleVersionOnePointZero()
    {
        StubCatalog.Entry entry = StubCatalog.Core.Single(e => e.Library == "libScePlayGo");
        Assert.Equal((ushort)0x0100, entry.ModuleVersion);
    }
}
