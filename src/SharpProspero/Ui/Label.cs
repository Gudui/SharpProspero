// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;

namespace SharpProspero.Ui;

/// <summary>A line of text. Not focusable; use it for titles, headings and read-only values.</summary>
/// <remarks>Creates a label showing <paramref name="text"/>.</remarks>
public sealed class Label(string text = "") : UiElement
{

    /// <summary>The text to show.</summary>
    public string Text { get; set; } = text ?? "";

    /// <summary>The text color, or null to use the theme's text color (the default).</summary>
    public Color? TextColor { get; set; }

    /// <summary>The text scale, or -1 to use the theme's scale (the default).</summary>
    public int Scale { get; set; } = -1;

    /// <summary>Whether to center the text within the label's width.</summary>
    public bool Centered { get; set; }

    /// <inheritdoc />
    public override int Measure(int width, UiTheme theme)
        => (Scale >= 1 ? Scale : theme.TextScale) * BitmapFont.GlyphSize;

    /// <inheritdoc />
    public override void Draw(Surface surface, UiTheme theme, UiElement? focused)
    {
        int scale = Scale >= 1 ? Scale : theme.TextScale;
        Color color = TextColor ?? theme.Text;
        if (Centered)
        {
            int textWidth = Surface.MeasureText(Text, scale);
            surface.DrawText(Text, Bounds.X + (Bounds.Width - textWidth) / 2, Bounds.Y, scale, color);
        }
        else
        {
            surface.DrawText(Text, Bounds.X, Bounds.Y, scale, color);
        }
    }
}
