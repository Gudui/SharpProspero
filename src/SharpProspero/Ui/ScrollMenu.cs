// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using System;
using System.Collections.Generic;

namespace SharpProspero.Ui;

/// <summary>
/// A scrolling column of controls the user moves through with up and down, where more controls are
/// stacked than fit in the window. Unlike <see cref="ScrollView"/>, which scrolls read-only content as a
/// block, this keeps its controls individually focusable: up and down move a highlight from one control
/// to the next, the window scrolls to keep the focused control in view, and the confirm and adjust
/// presses reach whichever control is focused. Use it for a settings form or any panel with more buttons,
/// checkboxes and sliders than the screen has room for. Up on the first control and down on the last are
/// left unused, so focus can move to a control outside the window.
/// </summary>
/// <remarks>
/// The window holds focus as one unit in the screen and drives its own controls, so add the leaf controls
/// (buttons, checkboxes, sliders, selectors) directly rather than wrapping them in another container. The
/// controls are placed in the window's own coordinates and drawn through <see cref="Surface.Region"/>,
/// which clips them to the window.
/// </remarks>
public sealed class ScrollMenu : UiElement
{
    private readonly List<UiElement> _children = [];
    private readonly List<int> _tops = []; // each child's top in content coordinates, filled by the layout pass
    private int _focusIndex;
    private int _scroll;
    private int _contentHeight;

    /// <summary>How tall the window is. Default 240 pixels.</summary>
    public int ViewHeight { get; set; } = 240;

    /// <summary>The gap between controls, or -1 to use the theme's spacing (the default).</summary>
    public int Spacing { get; set; } = -1;

    /// <summary>Whether to draw a bar on the right showing the position. Default true.</summary>
    public bool ShowScrollBar { get; set; } = true;

    /// <summary>The controls, in order.</summary>
    public IReadOnlyList<UiElement> Children => _children;

    /// <summary>Adds <paramref name="child"/> to the bottom and returns this menu, so calls can chain.</summary>
    public ScrollMenu Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
        return this;
    }

    /// <summary>How far down the content is scrolled, in pixels.</summary>
    public int ScrollOffset => _scroll;

    /// <summary>How far the content can scroll, in pixels. Zero when everything already fits.</summary>
    public int MaxScroll => Math.Max(0, _contentHeight - ViewHeight);

    /// <summary>The control the window currently highlights, or null when it has no focusable control.</summary>
    public UiElement? FocusedChild
        => _focusIndex >= 0 && _focusIndex < _children.Count && _children[_focusIndex].IsFocusable ? _children[_focusIndex] : null;

    /// <summary>The menu takes focus when it has at least one focusable control.</summary>
    public override bool IsFocusable => Visible && FirstFocusable(0, +1) >= 0;

    /// <inheritdoc />
    public override int Measure(int width, UiTheme theme) => ViewHeight;

    /// <inheritdoc />
    internal override void Arrange(UiRect bounds, UiTheme theme)
    {
        Bounds = bounds;
        // Keep the highlight on a focusable control, then place the children with the current scroll.
        if (_focusIndex < 0 || _focusIndex >= _children.Count || !_children[_focusIndex].IsFocusable)
        {
            int first = FirstFocusable(0, +1);
            if (first >= 0)
                _focusIndex = first;
        }
        LayoutChildren(theme);
    }

    /// <inheritdoc />
    internal override void CollectFocusables(List<UiElement> into)
    {
        // The window is one focus stop for the screen; it drives its own controls from there.
        if (Visible && IsFocusable)
            into.Add(this);
    }

    /// <inheritdoc />
    public override bool HandleInput(UiInput input, UiTheme theme)
    {
        UiElement? child = FocusedChild;
        // Offer the input to the focused control first, so it can confirm, toggle or adjust itself.
        if (child is not null && child.HandleInput(input, theme))
            return true;

        // Otherwise up and down move the highlight to the next focusable control and scroll it into view;
        // at the first or last control they are left for the screen so focus can leave the window.
        if (input.Up)
            return MoveFocus(-1, theme);
        if (input.Down)
            return MoveFocus(+1, theme);
        return false;
    }

    /// <inheritdoc />
    public override void Draw(Surface surface, UiTheme theme, UiElement? focused)
    {
        if (!Visible)
            return;

        bool menuFocused = ReferenceEquals(focused, this);
        UiElement? childFocus = menuFocused ? FocusedChild : null;

        Surface view = surface.Region(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);
        foreach (UiElement child in _children)
        {
            if (child.Visible)
                child.Draw(view, theme, childFocus);
        }

        if (!ShowScrollBar || MaxScroll <= 0)
            return;

        const int barWidth = 4;
        int trackX = Bounds.Right - barWidth;
        surface.FillRect(trackX, Bounds.Y, barWidth, Bounds.Height, theme.Border);
        int thumbHeight = Math.Max(theme.Spacing, (int)((long)Bounds.Height * Bounds.Height / Math.Max(1, _contentHeight)));
        int travel = Bounds.Height - thumbHeight;
        int thumbY = Bounds.Y + (int)((long)travel * _scroll / MaxScroll);
        surface.FillRect(trackX, thumbY, barWidth, thumbHeight, menuFocused ? theme.Accent : theme.TextMuted);
    }

    // Places the children top to bottom in the window's own coordinates, shifted up by the scroll, and
    // records each child's top so the scroll can bring a focused control into view.
    private void LayoutChildren(UiTheme theme)
    {
        int gap = Spacing >= 0 ? Spacing : theme.Spacing;
        _tops.Clear();
        int y = 0;
        bool first = true;
        foreach (UiElement child in _children)
        {
            if (!child.Visible)
            {
                _tops.Add(y);
                continue;
            }
            if (!first)
                y += gap;
            first = false;
            _tops.Add(y);
            int height = child.Measure(Bounds.Width, theme);
            child.Arrange(new UiRect(0, y - _scroll, Bounds.Width, height), theme);
            y += height;
        }
        _contentHeight = y;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    // Moves the highlight to the next focusable control in the given step direction. Returns false at the
    // edge so the input falls through to the screen and focus can move to a neighbor.
    private bool MoveFocus(int step, UiTheme theme)
    {
        int next = FirstFocusable(_focusIndex + step, step);
        if (next < 0)
            return false;
        _focusIndex = next;
        ScrollToChild(next, theme);
        return true;
    }

    // Adjusts the scroll so the child at the given index sits fully within the window, then re-lays the
    // children so the change shows on this frame rather than the next.
    private void ScrollToChild(int index, UiTheme theme)
    {
        if (index < 0 || index >= _tops.Count)
            return;
        int top = _tops[index];
        int height = _children[index].Visible ? _children[index].Measure(Bounds.Width, theme) : 0;
        if (top < _scroll)
            _scroll = top;
        else if (top + height > _scroll + ViewHeight)
            _scroll = top + height - ViewHeight;
        LayoutChildren(theme);
    }

    // The index of the first focusable, visible child at or after `start` stepping by `step`, or -1.
    private int FirstFocusable(int start, int step)
    {
        for (int i = start; i >= 0 && i < _children.Count; i += step)
            if (_children[i].Visible && _children[i].IsFocusable)
                return i;
        return -1;
    }
}
