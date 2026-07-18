// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Keyboard;
using SharpProspero.Interop.Mouse;
using Xunit;

namespace SharpProspero.Tests;

// The keyboard and mouse read structures, recomputed from the headers for x86-64. The C bool fields
// are one byte, so a one-byte hole sits after each before the next aligned field; these tests lock
// the total size, which a four-byte managed bool would blow past.
public sealed unsafe class InputDeviceLayoutTests
{
    [Fact]
    public void KeyboardData_IsNinetySixBytes()
        => Assert.Equal(96, sizeof(SceKeyboardData));

    [Fact]
    public void KeyboardOpenParam_IsEightBytes()
        => Assert.Equal(8, sizeof(SceKeyboardOpenParam));

    [Fact]
    public void MouseData_IsFortyBytes()
        => Assert.Equal(40, sizeof(SceMouseData));

    [Fact]
    public void MouseOpenParam_IsEightBytes()
        => Assert.Equal(8, sizeof(SceMouseOpenParam));

    // The bool fields are read back correctly only if they occupy exactly one byte where the header
    // put them; a wider bool would read a neighbouring field's bytes.
    [Fact]
    public void KeyboardData_ReadsTheOneByteBoolsAtTheRightOffsets()
    {
        var data = new SceKeyboardData();
        byte* raw = (byte*)&data;
        raw[8] = 1;    // intercepted
        raw[16] = 1;   // connected
        Assert.True(data.Intercepted);
        Assert.True(data.Connected);

        raw[8] = 0;
        raw[16] = 0;
        Assert.False(data.Intercepted);
        Assert.False(data.Connected);
    }

    [Fact]
    public void KeyboardData_ReadsTheFieldsPastTheBoolHole()
    {
        var data = new SceKeyboardData();
        byte* raw = (byte*)&data;
        // length @20, led @24, modifierKey @28, keyCode @32.
        *(int*)(raw + 20) = 3;
        *(uint*)(raw + 28) = (uint)(KeyModifier.LeftControl | KeyModifier.RightShift);
        *(ushort*)(raw + 32) = 0x0004;

        Assert.Equal(3, data.Length);
        Assert.Equal(KeyModifier.LeftControl | KeyModifier.RightShift, data.ModifierKey);
        Assert.Equal(0x0004, data.KeyCode[0]);
    }

    [Fact]
    public void MouseData_ReadsButtonsAndAxesPastTheBoolHole()
    {
        var data = new SceMouseData();
        byte* raw = (byte*)&data;
        raw[8] = 1;                                        // connected @8
        *(uint*)(raw + 12) = (uint)MouseButton.Primary;   // buttons @12, past the 3-byte hole
        *(int*)(raw + 16) = -5;                            // xAxis @16
        *(int*)(raw + 20) = 7;                             // yAxis @20
        *(int*)(raw + 24) = 2;                             // wheel @24

        Assert.True(data.Connected);
        Assert.Equal(MouseButton.Primary, data.Buttons);
        Assert.Equal(-5, data.XAxis);
        Assert.Equal(7, data.YAxis);
        Assert.Equal(2, data.Wheel);
    }

    [Theory]
    [InlineData(MouseButton.Primary, 0x00000001u)]
    [InlineData(MouseButton.Secondary, 0x00000002u)]
    [InlineData(MouseButton.Intercepted, 0x80000000u)]
    public void MouseButton_MatchesTheHeader(MouseButton value, uint expected)
        => Assert.Equal(expected, (uint)value);

    [Theory]
    [InlineData(KeyModifier.LeftControl, 0x01u)]
    [InlineData(KeyModifier.LeftShift, 0x02u)]
    [InlineData(KeyModifier.RightGui, 0x80u)]
    public void KeyModifier_MatchesTheHeader(KeyModifier value, uint expected)
        => Assert.Equal(expected, (uint)value);
}
