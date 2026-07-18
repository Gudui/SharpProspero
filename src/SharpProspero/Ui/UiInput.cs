// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Input;
using SharpProspero.Interop.Pad;

namespace SharpProspero.Ui;

/// <summary>
/// One frame of navigation intent for the interface: the direction the user moved and whether they
/// confirmed or cancelled. Each field is edge-triggered, true only on the frame the button becomes
/// pressed, so holding a button moves once. Build it from two controller samples with
/// <see cref="From(GamePadState, GamePadState)"/> and hand it to <see cref="UiScreen.Update"/>.
/// </summary>
/// <param name="Up">Move focus up (d-pad up pressed this frame).</param>
/// <param name="Down">Move focus down.</param>
/// <param name="Left">Move focus left.</param>
/// <param name="Right">Move focus right.</param>
/// <param name="Confirm">Activate the focused control (cross pressed this frame).</param>
/// <param name="Cancel">Go back (circle pressed this frame).</param>
public readonly record struct UiInput(bool Up, bool Down, bool Left, bool Right, bool Confirm, bool Cancel)
{
    /// <summary>No input this frame.</summary>
    public static UiInput None => default;

    /// <summary>True when any of the four directions is set this frame.</summary>
    public bool HasDirection => Up || Down || Left || Right;

    /// <summary>The single direction this frame, or null when none (or more than one) is set.</summary>
    public UiDirection? Direction
    {
        get
        {
            int count = (Up ? 1 : 0) + (Down ? 1 : 0) + (Left ? 1 : 0) + (Right ? 1 : 0);
            if (count != 1)
                return null;
            if (Up) return UiDirection.Up;
            if (Down) return UiDirection.Down;
            if (Left) return UiDirection.Left;
            return UiDirection.Right;
        }
    }

    /// <summary>
    /// Reads the navigation intent from this frame's controller sample and the previous one, so each
    /// button counts once when it becomes pressed. The d-pad drives the directions, cross confirms and
    /// circle cancels.
    /// </summary>
    public static UiInput From(GamePadState current, GamePadState previous)
    {
        bool Edge(ScePadButton button) => current.IsPressed(button) && !previous.IsPressed(button);
        return new UiInput(
            Up: Edge(ScePadButton.Up),
            Down: Edge(ScePadButton.Down),
            Left: Edge(ScePadButton.Left),
            Right: Edge(ScePadButton.Right),
            Confirm: Edge(ScePadButton.Cross),
            Cancel: Edge(ScePadButton.Circle));
    }
}
