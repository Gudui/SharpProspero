// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Dialog;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The error dialog parameter block, recomputed from the header: four 32-bit fields, 16 bytes.
public sealed unsafe class ErrorDialogLayoutTests
{
    [Fact]
    public void Param_IsSixteenBytes()
        => Assert.Equal(16, sizeof(SceErrorDialogParam));

    [Theory]
    [InlineData("Size", 0)]
    [InlineData("ErrorCode", 4)]
    [InlineData("UserId", 8)]
    public void Param_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceErrorDialogParam>(field));

    [Fact]
    public void InitializeParam_SetsTheSize()
    {
        SceErrorDialogParam param;
        byte* raw = (byte*)&param;
        for (int i = 0; i < sizeof(SceErrorDialogParam); i++)
            raw[i] = 0xAB;

        ErrorDialog.InitializeParam(&param);

        Assert.Equal(16, param.Size);
        Assert.Equal(0, param.ErrorCode);
        // The dialog refuses a user of nought, so clearing the block cannot leave it there. This
        // asserted the value the dialog rejects.
        Assert.Equal(SceUser.System, param.UserId);
    }
}
