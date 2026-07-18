// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;
using SharpProspero.Interop.Dialog;
using Xunit;

namespace SharpProspero.Tests;

// The dialog service rejects a parameter block whose sizes and field placement do not match what it
// expects, and it does so silently. These lock the layout to the one the service is built against.
public sealed unsafe class DialogLayoutTests
{
    [Fact]
    public void BaseParam_MatchesTheExpectedSize()
    {
        Assert.Equal(48, sizeof(CommonDialogBaseParam));
        Assert.Equal(0, (int)Marshal.OffsetOf<CommonDialogBaseParam>(nameof(CommonDialogBaseParam.Size)));
        Assert.Equal(44, (int)Marshal.OffsetOf<CommonDialogBaseParam>(nameof(CommonDialogBaseParam.Magic)));
    }

    [Fact]
    public void BrowserParam_MatchesTheExpectedSize()
    {
        Assert.Equal(328, sizeof(WebBrowserDialogParam));
    }

    [Theory]
    [InlineData(nameof(WebBrowserDialogParam.BaseParam), 0)]
    [InlineData(nameof(WebBrowserDialogParam.Size), 48)]
    [InlineData(nameof(WebBrowserDialogParam.Mode), 56)]
    [InlineData(nameof(WebBrowserDialogParam.UserId), 60)]
    [InlineData(nameof(WebBrowserDialogParam.Url), 64)]
    [InlineData(nameof(WebBrowserDialogParam.CallbackInitParam), 72)]
    [InlineData(nameof(WebBrowserDialogParam.Width), 80)]
    [InlineData(nameof(WebBrowserDialogParam.Height), 82)]
    [InlineData(nameof(WebBrowserDialogParam.PositionX), 84)]
    [InlineData(nameof(WebBrowserDialogParam.PositionY), 86)]
    [InlineData(nameof(WebBrowserDialogParam.Parts), 88)]
    [InlineData(nameof(WebBrowserDialogParam.HeaderWidth), 92)]
    [InlineData(nameof(WebBrowserDialogParam.HeaderPositionX), 94)]
    [InlineData(nameof(WebBrowserDialogParam.HeaderPositionY), 96)]
    [InlineData(nameof(WebBrowserDialogParam.Control), 100)]
    [InlineData(nameof(WebBrowserDialogParam.ImeParam), 104)]
    [InlineData(nameof(WebBrowserDialogParam.WebViewParam), 112)]
    [InlineData(nameof(WebBrowserDialogParam.Animation), 120)]
    public void BrowserParam_FieldsLandWhereTheServiceExpects(string field, int offset)
    {
        Assert.Equal(offset, (int)Marshal.OffsetOf<WebBrowserDialogParam>(field));
    }

    [Fact]
    public void InitializeParam_SetsSizesAndDerivesTheCheckValueFromTheAddress()
    {
        WebBrowserDialogParam param;
        WebBrowserDialog.InitializeParam(&param);

        Assert.Equal(48ul, param.BaseParam.Size);
        Assert.Equal(328ul, param.Size);

        // The check value is the constant plus the block's own address, truncated to 32 bits.
        uint expected = unchecked((uint)(WebBrowserDialog.MagicNumber + (ulong)&param.BaseParam));
        Assert.Equal(expected, param.BaseParam.Magic);
    }

    [Fact]
    public void InitializeParam_ZeroesTheBlock()
    {
        WebBrowserDialogParam param;
        // Dirty every byte first, so a field the initializer forgets shows up as non-zero.
        new System.Span<byte>(&param, sizeof(WebBrowserDialogParam)).Fill(0xAB);
        WebBrowserDialog.InitializeParam(&param);

        Assert.Equal(0, param.UserId);
        Assert.True(param.Url == null);
        Assert.Equal(0u, param.Parts);
        Assert.Equal(0u, param.Control);
        Assert.True(param.ImeParam == null);
        Assert.Equal(0u, param.Animation);
    }
}
