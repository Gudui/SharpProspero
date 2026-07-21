// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Input;
using SharpProspero.Interop.Pad;

namespace SharpProspero.Ui;

/// <summary>
/// Turns held controller directions into repeated moves, the way holding a key repeats it. Where
/// <see cref="UiInput.From(GamePadState, GamePadState)"/> reports each press once, this reports the first
/// press at once, then — while the direction is still held — again after a short wait and then at a
/// steady rate. Use it in place of <c>UiInput.From</c> so scrolling a long list or dragging a slider does
/// not need a press per step. Confirm and cancel are still reported once, so they never repeat.
/// </summary>
/// <remarks>
/// Keep one per screen (it holds the hold timers between frames) and pass this frame's controller sample
/// and the time since the last frame. Call <see cref="Reset"/> when focus jumps somewhere new so a held
/// direction does not carry a repeat into it.
/// </remarks>
/// <example>
/// <code>
/// screen.Update(repeater.Update(context.Input, (float)context.DeltaSeconds));
/// </code>
/// </example>
public sealed class UiRepeater
{
    private bool _upHeld, _downHeld, _leftHeld, _rightHeld, _confirmHeld, _cancelHeld;
    private float _upTimer, _downTimer, _leftTimer, _rightTimer;

    /// <summary>How long a direction is held before the first repeat, in seconds. Default 0.4.</summary>
    public float InitialDelay { get; set; } = 0.4f;

    /// <summary>The time between repeats once they start, in seconds. Default 0.09.</summary>
    public float RepeatInterval { get; set; } = 0.09f;

    /// <summary>
    /// Reads the navigation intent from this frame's controller sample, repeating held directions after
    /// <see cref="InitialDelay"/> and then every <see cref="RepeatInterval"/>.
    /// </summary>
    public UiInput Update(GamePadState current, float deltaSeconds)
    {
        bool up = Direction(current.IsPressed(ScePadButton.Up), ref _upHeld, ref _upTimer, deltaSeconds);
        bool down = Direction(current.IsPressed(ScePadButton.Down), ref _downHeld, ref _downTimer, deltaSeconds);
        bool left = Direction(current.IsPressed(ScePadButton.Left), ref _leftHeld, ref _leftTimer, deltaSeconds);
        bool right = Direction(current.IsPressed(ScePadButton.Right), ref _rightHeld, ref _rightTimer, deltaSeconds);
        bool confirm = Edge(current.IsPressed(ScePadButton.Cross), ref _confirmHeld);
        bool cancel = Edge(current.IsPressed(ScePadButton.Circle), ref _cancelHeld);
        return new UiInput(up, down, left, right, confirm, cancel);
    }

    /// <summary>Forgets which buttons were held, so the next press starts fresh with no pending repeat.</summary>
    public void Reset()
    {
        _upHeld = _downHeld = _leftHeld = _rightHeld = _confirmHeld = _cancelHeld = false;
        _upTimer = _downTimer = _leftTimer = _rightTimer = 0f;
    }

    // Fires on the frame the direction is first pressed, then once the initial delay has passed while it
    // is still held, then every interval after that. At most one repeat is reported per frame.
    private bool Direction(bool held, ref bool wasHeld, ref float timer, float deltaSeconds)
    {
        bool fire = false;
        if (held)
        {
            if (!wasHeld)
            {
                fire = true;
                timer = InitialDelay;
            }
            else if (deltaSeconds > 0f)
            {
                timer -= deltaSeconds;
                if (timer <= 0f)
                {
                    fire = true;
                    timer += RepeatInterval;
                }
            }
        }
        wasHeld = held;
        return fire;
    }

    // Confirm and cancel fire only as the button becomes pressed, so they are never repeated.
    private static bool Edge(bool held, ref bool wasHeld)
    {
        bool fire = held && !wasHeld;
        wasHeld = held;
        return fire;
    }
}
