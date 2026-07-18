// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;
using SharpProspero.Interop.Dialog;
using Xunit;

namespace SharpProspero.Tests;

// The keyboard parameter block, recomputed from the header for x86-64. It has no shared base-param
// block; it starts with the user id. The pointer fields land on 8-byte boundaries, which is where
// the implicit padding after the enum pairs goes.
public sealed unsafe class ImeDialogLayoutTests
{
    [Fact]
    public void DialogParam_IsNinetySixBytes()
        => Assert.Equal(96, sizeof(SceImeDialogParam));

    [Theory]
    [InlineData("UserId", 0)]
    [InlineData("Type", 4)]
    [InlineData("SupportedLanguages", 8)]
    [InlineData("EnterLabel", 16)]
    [InlineData("InputMethod", 20)]
    [InlineData("Filter", 24)]
    [InlineData("Option", 32)]
    [InlineData("MaxTextLength", 36)]
    [InlineData("InputTextBuffer", 40)]
    [InlineData("PosX", 48)]
    [InlineData("PosY", 52)]
    [InlineData("HorizontalAlignment", 56)]
    [InlineData("VerticalAlignment", 60)]
    [InlineData("Placeholder", 64)]
    [InlineData("Title", 72)]
    public void DialogParam_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceImeDialogParam>(field));

    [Fact]
    public void DialogResult_IsSixteenBytes()
        => Assert.Equal(16, sizeof(SceImeDialogResult));

    [Fact]
    public void DialogResult_EndStatusIsFirst()
        => Assert.Equal(0, (int)Marshal.OffsetOf<SceImeDialogResult>("EndStatus"));

    [Theory]
    [InlineData(ImeType.Default, 0)]
    [InlineData(ImeType.BasicLatin, 1)]
    [InlineData(ImeType.Url, 2)]
    [InlineData(ImeType.Mail, 3)]
    [InlineData(ImeType.Number, 4)]
    public void Type_MatchesTheHeader(ImeType value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(ImeDialogStatus.None, 0)]
    [InlineData(ImeDialogStatus.Running, 1)]
    [InlineData(ImeDialogStatus.Finished, 2)]
    public void Status_MatchesTheHeader(ImeDialogStatus value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(ImeDialogEndStatus.Ok, 0)]
    [InlineData(ImeDialogEndStatus.UserCanceled, 1)]
    [InlineData(ImeDialogEndStatus.Aborted, 2)]
    public void EndStatus_MatchesTheHeader(ImeDialogEndStatus value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(ImeOption.Multiline, 0x00000001u)]
    [InlineData(ImeOption.Password, 0x00000004u)]
    [InlineData(ImeOption.NoAutoCapitalization, 0x00000002u)]
    [InlineData(ImeOption.FixedPosition, 0x00000040u)]
    public void Option_MatchesTheHeader(ImeOption value, uint expected)
        => Assert.Equal(expected, (uint)value);

    // The initializer zeroes the block and sets the user to the invalid default, matching the
    // service's own initializer, so a caller only fills the fields it supplies.
    [Fact]
    public void InitializeParam_ZeroesAndSetsTheInvalidUser()
    {
        SceImeDialogParam param;
        // Dirty every byte first so a field the initializer forgets would show.
        byte* raw = (byte*)&param;
        for (int i = 0; i < sizeof(SceImeDialogParam); i++)
            raw[i] = 0xAB;

        ImeDialog.InitializeParam(&param);

        Assert.Equal(-1, param.UserId);
        Assert.Equal(ImeType.Default, param.Type);
        Assert.Equal(0ul, param.SupportedLanguages);
        Assert.Equal(ImeOption.None, param.Option);
        Assert.Equal(0u, param.MaxTextLength);
        Assert.True(param.InputTextBuffer == null);
        Assert.True(param.Title == null);
        Assert.True(param.Placeholder == null);
        Assert.True(param.Filter == null);
    }
}
