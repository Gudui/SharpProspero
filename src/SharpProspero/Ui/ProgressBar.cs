// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using SharpProspero.Graphics;

namespace SharpProspero.Ui;

/// <summary>A horizontal bar that fills from the left to show a fraction from 0 to 1. Not focusable.</summary>
public sealed class ProgressBar : UiElement
{
    private float _value;

    /// <summary>Creates a progress bar at <paramref name="value"/> (0 to 1).</summary>
    public ProgressBar(float value = 0f) => Value = value;

    /// <summary>How full the bar is, from 0 (empty) to 1 (full). Values outside the range are clamped.</summary>
    public float Value
    {
        get => _value;
        set => _value = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>The color of the filled part, or null to use the theme's accent (the default).</summary>
    public Color? FillColor { get; set; }

    /// <inheritdoc />
    public override int Measure(int width, UiTheme theme) => theme.LineHeight;

    /// <inheritdoc />
    public override void Draw(Surface surface, UiTheme theme, UiElement? focused)
    {
        surface.FillRect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, theme.Border);
        int fill = (int)(Bounds.Width * _value + 0.5f);
        if (fill > 0)
            surface.FillRect(Bounds.X, Bounds.Y, fill, Bounds.Height, FillColor ?? theme.Accent);
    }
}
