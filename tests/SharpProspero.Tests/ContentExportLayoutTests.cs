// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Content;
using Xunit;

namespace SharpProspero.Tests;

// The content-export structures, from content_export.h: the init parameter is six 8-byte fields, and
// the export parameter is title[257] + reserved[257] + contenttype[65] = 579 bytes.
public sealed unsafe class ContentExportLayoutTests
{
    [Fact]
    public void InitParam_IsFortyEightBytes()
        => Assert.Equal(48, sizeof(SceContentExportInitParam2));

    [Fact]
    public void ExportParam_Is579Bytes()
        => Assert.Equal(579, sizeof(SceContentExportParam));

    [Fact]
    public void ExportParam_FieldsSitWhereTheHeaderPutsThem()
    {
        SceContentExportParam param = default;
        byte* b = (byte*)&param;
        Assert.Equal(0x000, (int)((byte*)&param.Title[0] - b));
        Assert.Equal(514, (int)((byte*)&param.ContentType[0] - b)); // after title[257] + reserved[257]
    }
}
