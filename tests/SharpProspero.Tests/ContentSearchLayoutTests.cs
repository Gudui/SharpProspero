// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Content;
using Xunit;

namespace SharpProspero.Tests;

// The content-search result row and query structures, from content_search_types.h. The reserved
// fields in the row are the header's alignment padding, chosen so createdTime lands on an 8-byte
// boundary; the row is 2400 bytes.
public sealed unsafe class ContentSearchLayoutTests
{
    [Fact]
    public void ContentInfo_Is2400Bytes()
        => Assert.Equal(2400, sizeof(SceContentSearchContentInfo));

    [Fact]
    public void ContentInfo_FieldsSitWhereTheHeaderPutsThem()
    {
        SceContentSearchContentInfo info = default;
        byte* b = (byte*)&info;
        Assert.Equal(0x000, (int)((byte*)&info.ContentId - b));
        Assert.Equal(0x008, (int)((byte*)&info.Duration - b));
        Assert.Equal(0x00C, (int)((byte*)&info.MimeType - b));
        Assert.Equal(0x010, (int)((byte*)&info.ContentType - b));
        Assert.Equal(0x014, (int)((byte*)&info.GeneratorId - b));
        Assert.Equal(0x018, (int)((byte*)&info.ContentPath[0] - b));
        Assert.Equal(0x424, (int)((byte*)&info.Title[0] - b));
        Assert.Equal(0x52B, (int)((byte*)&info.IconPath[0] - b));
        Assert.Equal(0x934, (int)((byte*)&info.UploadStatus - b));
        Assert.Equal(0x938, (int)((byte*)&info.CreatedTime - b));
        Assert.Equal(0x940, (int)((byte*)&info.Size - b));
        Assert.Equal(0x948, (int)((byte*)&info.Status - b));
        Assert.Equal(0x94C, (int)((byte*)&info.Accounts[0] - b));
    }

    [Fact]
    public void QueryStructs_MatchTheHeaderSizes()
    {
        Assert.Equal(8, sizeof(SceContentSearchInitParam));
        Assert.Equal(16, sizeof(SceContentSearchColumnValue));
        Assert.Equal(24, sizeof(SceContentSearchContentColumn));
        Assert.Equal(16, sizeof(SceContentSearchContentColumnSet));
        Assert.Equal(8, sizeof(SceContentSearchContentOrderByCondition));
        Assert.Equal(16, sizeof(SceContentSearchMetadataValue));
    }
}
