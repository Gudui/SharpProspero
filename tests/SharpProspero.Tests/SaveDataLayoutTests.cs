// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.SaveData;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The save-data structures, recomputed from save_data_defs.h for x86-64. The blocks fields are 64-bit
// and the pointers sit on 8-byte boundaries, which sets the alignment holes.
public sealed unsafe class SaveDataLayoutTests
{
    [Fact] public void TitleId_IsSixteen() => Assert.Equal(16, sizeof(SceSaveDataTitleId));
    [Fact] public void DirName_IsThirtyTwo() => Assert.Equal(32, sizeof(SceSaveDataDirName));
    [Fact] public void MountPoint_IsSixteen() => Assert.Equal(16, sizeof(SceSaveDataMountPoint));
    [Fact] public void Param_IsThirteenTwentyEight() => Assert.Equal(1328, sizeof(SceSaveDataParam));
    [Fact] public void Mount3_IsEighty() => Assert.Equal(80, sizeof(SceSaveDataMount3));
    [Fact] public void MountResult_IsSixtyFour() => Assert.Equal(64, sizeof(SceSaveDataMountResult));
    [Fact] public void MountInfo_IsFortyEight() => Assert.Equal(48, sizeof(SceSaveDataMountInfo));
    [Fact] public void Delete_IsSixtyFour() => Assert.Equal(64, sizeof(SceSaveDataDelete));
    [Fact] public void SearchCond_IsSixtyFour() => Assert.Equal(64, sizeof(SceSaveDataDirNameSearchCond));
    [Fact] public void SearchResult_IsFiftySix() => Assert.Equal(56, sizeof(SceSaveDataDirNameSearchResult));

    [Theory]
    [InlineData("UserId", 0)]
    [InlineData("DirName", 8)]
    [InlineData("Blocks", 16)]
    [InlineData("SystemBlocks", 24)]
    [InlineData("MountMode", 32)]
    [InlineData("Resource", 40)]
    public void Mount3_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceSaveDataMount3>(field));

    [Theory]
    [InlineData("UserParam", 1280)]
    [InlineData("Mtime", 1288)]
    public void Param_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceSaveDataParam>(field));

    [Theory]
    [InlineData("HitNum", 0)]
    [InlineData("DirNames", 8)]
    [InlineData("DirNamesNum", 16)]
    [InlineData("SetNum", 20)]
    [InlineData("Params", 24)]
    [InlineData("Infos", 32)]
    public void SearchResult_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceSaveDataDirNameSearchResult>(field));

    [Theory]
    [InlineData(SaveDataMountMode.ReadOnly, 1u)]
    [InlineData(SaveDataMountMode.ReadWrite, 2u)]
    [InlineData(SaveDataMountMode.Create, 4u)]
    public void MountMode_MatchesTheHeader(SaveDataMountMode value, uint expected)
        => Assert.Equal(expected, (uint)value);
}
