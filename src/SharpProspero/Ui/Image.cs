// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;

namespace SharpProspero.Ui;

/// <summary>
/// A picture drawn from a <see cref="Surface"/>, for an image viewer or an icon. It draws the surface
/// at its top-left corner, optionally blending by alpha. Not focusable. The surface is a view over
/// pixels the caller owns, so keep those pixels (for example a decoded <c>PngImage</c>) alive while the
/// control is on screen.
/// </summary>
public sealed unsafe class Image : UiElement
{
    private Surface _content;

    /// <summary>Creates an image control showing <paramref name="content"/>.</summary>
    /// <param name="content">The pixels to draw.</param>
    /// <param name="blend">Whether to blend the source by its alpha (for a picture with transparency).</param>
    public Image(Surface content, bool blend = false)
    {
        _content = content;
        Blend = blend;
    }

    /// <summary>Whether the picture is blended over the background by its alpha.</summary>
    public bool Blend { get; set; }

    /// <summary>Replaces the pictures's pixels.</summary>
    public void SetContent(Surface content) => _content = content;

    /// <inheritdoc />
    public override int Measure(int width, UiTheme theme) => _content.Height;

    /// <inheritdoc />
    public override void Draw(Surface surface, UiTheme theme, UiElement? focused)
    {
        if (Blend)
            surface.BlitBlended(_content, Bounds.X, Bounds.Y);
        else
            surface.Blit(_content, Bounds.X, Bounds.Y);
    }
}
