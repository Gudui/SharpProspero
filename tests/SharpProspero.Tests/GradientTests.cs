// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class GradientTests
{
    private static readonly Color Black = Color.FromRgb(0, 0, 0);
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    [Fact]
    public void Sample_ReturnsEndpointsAndBlendsBetween()
    {
        var gradient = Gradient.TwoColor(Black, White);
        Assert.Equal(Black.Value, gradient.Sample(0f).Value);
        Assert.Equal(White.Value, gradient.Sample(1f).Value);

        Color middle = gradient.Sample(0.5f);
        Assert.InRange(middle.R, (byte)120, (byte)135); // roughly halfway
        Assert.Equal(middle.R, middle.G);
        Assert.Equal(middle.G, middle.B);
    }

    [Fact]
    public void Sample_ClampsOutsideZeroToOne()
    {
        var gradient = Gradient.TwoColor(Black, White);
        Assert.Equal(Black.Value, gradient.Sample(-5f).Value);
        Assert.Equal(White.Value, gradient.Sample(5f).Value);
    }

    [Fact]
    public void Constructor_SortsStopsSoOrderDoesNotMatter()
    {
        Color mid = Color.FromRgb(10, 20, 30);
        var gradient = new Gradient(
            new GradientStop(1f, White),
            new GradientStop(0.5f, mid),
            new GradientStop(0f, Black));

        Assert.Equal(3, gradient.StopCount);
        Assert.Equal(mid.Value, gradient.Sample(0.5f).Value); // exactly on the middle stop
        Assert.Equal(Black.Value, gradient.Sample(0f).Value);
    }

    [Fact]
    public void ToPalette_SamplesEvenlyAndKeepsEndpoints()
    {
        Palette palette = Gradient.TwoColor(Black, White).ToPalette(5);
        Assert.Equal(5, palette.Count);
        Assert.Equal(Black.Value, palette[0].Value);
        Assert.Equal(White.Value, palette[4].Value);
    }

    [Fact]
    public void Sample_NaN_MapsToTheLowEndInBothGradientAndPalette()
    {
        // A ratio like value/max is NaN when both are zero; the two samplers must agree, not diverge.
        var gradient = Gradient.TwoColor(Black, White);
        Assert.Equal(Black.Value, gradient.Sample(float.NaN).Value);

        Palette palette = gradient.ToPalette(4);
        Assert.Equal(palette[0].Value, palette.Sample(float.NaN).Value);
        Assert.Equal(Black.Value, palette.Sample(float.NaN).Value);
    }

    [Fact]
    public void NamedGradients_ProduceKnownEndpoints()
    {
        Assert.Equal(Color.FromRgb(0, 0, 0).Value, Gradient.Heat.Sample(0f).Value);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00).Value, Gradient.Rainbow.Sample(0f).Value);
    }
}

public sealed class PaletteTests
{
    private static readonly Color A = Color.FromRgb(1, 2, 3);
    private static readonly Color B = Color.FromRgb(4, 5, 6);
    private static readonly Color C = Color.FromRgb(7, 8, 9);

    [Fact]
    public void Indexer_ReturnsColorsAndRejectsOutOfRange()
    {
        var palette = new Palette(A, B, C);
        Assert.Equal(3, palette.Count);
        Assert.Equal(B.Value, palette[1].Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => palette[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => palette[-1]);
    }

    [Fact]
    public void Cycle_WrapsForAnyIndex()
    {
        var palette = new Palette(A, B, C);
        Assert.Equal(A.Value, palette.Cycle(0).Value);
        Assert.Equal(A.Value, palette.Cycle(3).Value);   // wraps
        Assert.Equal(C.Value, palette.Cycle(-1).Value);  // negative wraps
    }

    [Fact]
    public void Sample_MapsFractionToNearestEntry()
    {
        var palette = new Palette(A, B, C);
        Assert.Equal(A.Value, palette.Sample(0f).Value);
        Assert.Equal(C.Value, palette.Sample(1f).Value);
        Assert.Equal(B.Value, palette.Sample(0.5f).Value);
    }

    [Fact]
    public void Constructor_RejectsEmpty()
        => Assert.Throws<ArgumentException>(() => new Palette());
}
