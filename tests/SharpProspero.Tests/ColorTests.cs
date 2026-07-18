// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using Xunit;

namespace SharpProspero.Tests;

public sealed class ColorTests
{
    [Fact]
    public void FromRgb_PacksRedGreenBlueIntoExpectedBitsAndIsOpaque()
    {
        Color color = Color.FromRgb(0x12, 0x34, 0x56);
        Assert.Equal(0xFF123456u, color.Value);
    }

    [Fact]
    public void FromArgb_PlacesAlphaInTopByte()
    {
        Color color = Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        Assert.Equal(0xFF123456u, color.Value);
    }

    [Fact]
    public void WhiteAndBlack_AreOpaque()
    {
        Assert.Equal(0xFFFFFFFFu, Color.White.Value);
        Assert.Equal(0xFF000000u, Color.Black.Value);
    }

    [Fact]
    public void ImplicitConversion_ReturnsPackedValue()
    {
        uint value = Color.FromRgb(0x2E, 0x8B, 0xE6);
        Assert.Equal(0xFF2E8BE6u, value);
    }

    [Fact]
    public void Components_ReadBackTheChannels()
    {
        Color color = Color.FromArgb(0x12, 0x34, 0x56, 0x78);
        Assert.Equal(0x12, color.A);
        Assert.Equal(0x34, color.R);
        Assert.Equal(0x56, color.G);
        Assert.Equal(0x78, color.B);
    }

    [Fact]
    public void Lerp_ClampsAndBlends()
    {
        Assert.Equal(Color.Black.Value, Color.Lerp(Color.Black, Color.White, -1f).Value);
        Assert.Equal(Color.White.Value, Color.Lerp(Color.Black, Color.White, 2f).Value);

        Color mid = Color.Lerp(Color.Black, Color.White, 0.5f);
        Assert.Equal(128, mid.R);
        Assert.Equal(128, mid.G);
        Assert.Equal(128, mid.B);
    }

    [Theory]
    [InlineData(0f, 0xFF, 0x00, 0x00)]     // red
    [InlineData(120f, 0x00, 0xFF, 0x00)]   // green
    [InlineData(240f, 0x00, 0x00, 0xFF)]   // blue
    [InlineData(360f, 0xFF, 0x00, 0x00)]   // wraps back to red
    public void FromHsv_MapsPrimaryHues(float hue, int r, int g, int b)
    {
        Color color = Color.FromHsv(hue, 1f, 1f);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
        Assert.Equal(0xFF, color.A);
    }

    [Fact]
    public void FromHsv_ZeroValueIsBlackAndZeroSaturationIsGray()
    {
        Assert.Equal(Color.Black.Value, Color.FromHsv(200f, 1f, 0f).Value);

        Color gray = Color.FromHsv(200f, 0f, 1f);
        Assert.Equal(0xFF, gray.R);
        Assert.Equal(0xFF, gray.G);
        Assert.Equal(0xFF, gray.B);
    }
}
