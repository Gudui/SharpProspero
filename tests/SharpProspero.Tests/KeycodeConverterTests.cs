// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Input;
using Xunit;

namespace SharpProspero.Tests;

// The layout values are the ones the system publishes, so they are pinned here; the conversion calls
// themselves need the device and are exercised there.
public sealed class KeycodeConverterTests
{
    [Theory]
    [InlineData(KeyboardLayout.None, 0)]
    [InlineData(KeyboardLayout.German, 2)]
    [InlineData(KeyboardLayout.EnglishUs, 4)]
    [InlineData(KeyboardLayout.EnglishGb, 5)]
    [InlineData(KeyboardLayout.French, 9)]
    [InlineData(KeyboardLayout.JapaneseRoman, 22)]
    [InlineData(KeyboardLayout.Korean, 24)]
    [InlineData(KeyboardLayout.Czech, 32)]
    public void KeyboardLayout_HasThePublishedValues(KeyboardLayout layout, int value) =>
        Assert.Equal(value, (int)layout);

    [Fact]
    public void ModulePath_IsTheSystemLibrary() =>
        Assert.Equal("/system/common/lib/libSceConvertKeycode.sprx", KeycodeConverter.ModulePath);
}
