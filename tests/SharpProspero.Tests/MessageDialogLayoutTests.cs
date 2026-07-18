// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Dialog;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The message dialog structures, recomputed from message_dialog.h for x86-64. The nested pointer
// fields sit on 8-byte boundaries with the enum-then-pad pattern the header spells out.
public sealed unsafe class MessageDialogLayoutTests
{
    [Fact]
    public void Param_IsOneHundredThirtySixBytes() => Assert.Equal(136, sizeof(SceMsgDialogParam));

    [Theory]
    [InlineData("BaseParam", 0)]
    [InlineData("Size", 48)]
    [InlineData("Mode", 56)]
    [InlineData("UserMsgParam", 64)]
    [InlineData("ProgBarParam", 72)]
    [InlineData("SysMsgParam", 80)]
    [InlineData("UserId", 88)]
    public void Param_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceMsgDialogParam>(field));

    [Fact]
    public void Result_IsFortyFourBytes() => Assert.Equal(44, sizeof(SceMsgDialogResult));

    [Theory]
    [InlineData("Mode", 0)]
    [InlineData("Result", 4)]
    [InlineData("ButtonId", 8)]
    public void Result_FieldsSitWhereTheHeaderPutsThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceMsgDialogResult>(field));

    [Fact]
    public void ProgressBarParam_IsEightyBytesWithTheMessagePastTheHole()
    {
        Assert.Equal(80, sizeof(SceMsgDialogProgressBarParam));
        Assert.Equal(0, (int)Marshal.OffsetOf<SceMsgDialogProgressBarParam>("BarType"));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceMsgDialogProgressBarParam>("Msg"));
    }

    [Fact]
    public void UserMessageParam_IsFortyEightBytes()
    {
        Assert.Equal(48, sizeof(SceMsgDialogUserMessageParam));
        Assert.Equal(0, (int)Marshal.OffsetOf<SceMsgDialogUserMessageParam>("ButtonType"));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceMsgDialogUserMessageParam>("Msg"));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceMsgDialogUserMessageParam>("ButtonsParam"));
    }

    [Fact]
    public void ButtonsParam_IsFortyEight() => Assert.Equal(48, sizeof(SceMsgDialogButtonsParam));

    [Fact]
    public void SystemMessageParam_IsThirtySix() => Assert.Equal(36, sizeof(SceMsgDialogSystemMessageParam));

    [Theory]
    [InlineData(MsgDialogMode.UserMessage, 1)]
    [InlineData(MsgDialogMode.ProgressBar, 2)]
    [InlineData(MsgDialogMode.SystemMessage, 3)]
    public void Mode_MatchesTheHeader(MsgDialogMode value, int expected) => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(MsgDialogButtonType.Ok, 0)]
    [InlineData(MsgDialogButtonType.YesNo, 1)]
    [InlineData(MsgDialogButtonType.OkCancel, 3)]
    [InlineData(MsgDialogButtonType.TwoButtons, 9)]
    public void ButtonType_MatchesTheHeader(MsgDialogButtonType value, int expected) => Assert.Equal(expected, (int)value);

    // The check value is derived from the block's own address; a wrong stamp makes the service reject it.
    [Fact]
    public void InitializeParam_StampsTheSizesAndAddressDerivedMagic()
    {
        SceMsgDialogParam param;
        MessageDialog.InitializeParam(&param);

        Assert.Equal(48ul, param.BaseParam.Size);
        Assert.Equal(136ul, param.Size);
        uint expected = unchecked((uint)(0xC0D1A109ul + (ulong)&param.BaseParam));
        Assert.Equal(expected, param.BaseParam.Magic);
    }
}
