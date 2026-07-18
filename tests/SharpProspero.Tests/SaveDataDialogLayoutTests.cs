// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Dialog;
using Xunit;

namespace SharpProspero.Tests;

// The save-data dialog structures, from save_data_dialog.h. The anonymous 32-bit padding members in
// the header are modelled as explicit padding so the pointer members land on their 8-byte offsets.
public sealed unsafe class SaveDataDialogLayoutTests
{
    [Fact]
    public void Param_Is152Bytes() => Assert.Equal(152, sizeof(SceSaveDataDialogParam));

    [Fact]
    public void Items_Is96Bytes() => Assert.Equal(96, sizeof(SceSaveDataDialogItems));

    [Fact]
    public void Result_Is72Bytes() => Assert.Equal(72, sizeof(SceSaveDataDialogResult));

    [Fact]
    public void CloseParam_Is36Bytes() => Assert.Equal(36, sizeof(SceSaveDataDialogCloseParam));

    [Fact]
    public void Param_FieldsSitAfterTheCommonBase()
    {
        SceSaveDataDialogParam p = default;
        byte* b = (byte*)&p;
        Assert.Equal(48, (int)((byte*)&p.Size - b));   // after the 48-byte common base
        Assert.Equal(52, (int)((byte*)&p.Mode - b));
        Assert.Equal(56, (int)((byte*)&p.DispType - b));
        Assert.Equal(64, (int)((byte*)&p.AnimParam - b));
        Assert.Equal(72, (int)((byte*)&p.Items - b));
        Assert.Equal(112, (int)((byte*)&p.UserData - b));
        Assert.Equal(128, (int)((byte*)&p.WizardParam - b));
    }

    [Fact]
    public void Items_FieldsSitWhereTheHeaderPutsThem()
    {
        SceSaveDataDialogItems i = default;
        byte* b = (byte*)&i;
        Assert.Equal(0, (int)((byte*)&i.UserId - b));
        Assert.Equal(8, (int)((byte*)&i.TitleId - b));
        Assert.Equal(16, (int)((byte*)&i.DirName - b));
        Assert.Equal(24, (int)((byte*)&i.DirNameNum - b));
        Assert.Equal(32, (int)((byte*)&i.NewItem - b));
        Assert.Equal(40, (int)((byte*)&i.FocusPos - b));
        Assert.Equal(48, (int)((byte*)&i.FocusPosDirName - b));
        Assert.Equal(56, (int)((byte*)&i.ItemStyle - b));
    }

    [Fact]
    public void Result_FieldsSitWhereTheHeaderPutsThem()
    {
        SceSaveDataDialogResult r = default;
        byte* b = (byte*)&r;
        Assert.Equal(0, (int)((byte*)&r.Mode - b));
        Assert.Equal(4, (int)((byte*)&r.Result - b));
        Assert.Equal(8, (int)((byte*)&r.ButtonId - b));
        Assert.Equal(16, (int)((byte*)&r.DirName - b));
        Assert.Equal(24, (int)((byte*)&r.Param - b));
        Assert.Equal(32, (int)((byte*)&r.UserData - b));
    }
}
