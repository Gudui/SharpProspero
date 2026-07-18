// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using System.Collections.Generic;

namespace SharpProspero.Ui;

/// <summary>
/// One choice from a fixed set, cycled with left and right, for a setting such as a difficulty or a
/// resolution. It shows the label and the current option between arrows, and calls <see cref="Changed"/>
/// with the new index each time it moves. Choosing wraps around at the ends.
/// </summary>
public sealed class OptionSelector : UiElement
{
    private int _selectedIndex;

    /// <summary>Creates a selector labelled <paramref name="text"/> over <paramref name="options"/>.</summary>
    public OptionSelector(string text, IReadOnlyList<string> options, int selected = 0, Action<int>? changed = null)
    {
        Text = text ?? "";
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Count == 0)
            throw new ArgumentException("A selector needs at least one option.", nameof(options));
        _selectedIndex = Math.Clamp(selected, 0, options.Count - 1);
        Changed = changed;
    }

    /// <summary>The label shown at the left.</summary>
    public string Text { get; set; }

    /// <summary>The choices.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>The index of the chosen option.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => _selectedIndex = Math.Clamp(value, 0, Options.Count - 1);
    }

    /// <summary>The chosen option's text.</summary>
    public string SelectedOption => Options[_selectedIndex];

    /// <summary>Called with the new index each time the choice changes.</summary>
    public Action<int>? Changed { get; set; }

    /// <inheritdoc />
    public override bool IsFocusable => Visible;

    /// <inheritdoc />
    public override void Draw(Surface surface, UiTheme theme, UiElement? focused)
    {
        bool isFocused = ReferenceEquals(focused, this);
        surface.FillRect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, isFocused ? theme.PanelFocused : theme.Panel);
        if (isFocused)
            surface.DrawRect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, theme.Accent);

        int textY = CenterTextY(Bounds, theme.TextScale);
        surface.DrawText(Text, Bounds.X + theme.Padding, textY, theme.TextScale, theme.Text);

        string option = "\x11 " + SelectedOption + " \x10"; // arrows around the option
        int optionWidth = Surface.MeasureText(option, theme.TextScale);
        surface.DrawText(option, Bounds.X + Bounds.Width - theme.Padding - optionWidth, textY, theme.TextScale, theme.Text);
    }

    /// <inheritdoc />
    public override bool HandleInput(UiInput input, UiTheme theme)
    {
        if (input.Left)
        {
            Move(-1);
            return true;
        }
        if (input.Right)
        {
            Move(1);
            return true;
        }
        return false;
    }

    private void Move(int delta)
    {
        int count = Options.Count;
        _selectedIndex = ((_selectedIndex + delta) % count + count) % count;
        Changed?.Invoke(_selectedIndex);
    }
}
