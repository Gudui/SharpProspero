// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Graphics;

/// <summary>What an animation does when it reaches its last frame.</summary>
public enum SpriteMode
{
    /// <summary>Returns to the first frame and plays on, without end.</summary>
    Loop,

    /// <summary>Stops on the last frame and reports itself complete.</summary>
    Once,

    /// <summary>Plays to the last frame, then back to the first, then forward again, without end.</summary>
    PingPong,
}

/// <summary>
/// Plays a run of frames from a <see cref="SpriteSheet"/> over time — a walk cycle, a spinning coin, an
/// explosion. Advance it each frame with the time since the last, then draw it; it tracks which frame is
/// showing. Point it at all of a sheet's frames or a range within one, so several animations can share a
/// sheet.
/// </summary>
/// <example>
/// <code>
/// var run = new AnimatedSprite(sheet, framesPerSecond: 12, firstFrame: 0, frameCount: 8);
/// // each frame:
/// run.Update((float)context.DeltaSeconds);
/// run.Draw(context.Surface, x, y);
/// </code>
/// </example>
public sealed class AnimatedSprite
{
    private readonly SpriteSheet _sheet;
    private readonly int _first;
    private readonly int _count;
    private float _time;
    private int _index;
    private int _direction = 1;
    private bool _complete;

    /// <summary>Creates an animation over frames of <paramref name="sheet"/>.</summary>
    /// <param name="sheet">The sheet the frames come from.</param>
    /// <param name="framesPerSecond">How many frames play a second.</param>
    /// <param name="firstFrame">The sheet frame the run starts at.</param>
    /// <param name="frameCount">How many frames the run covers, or -1 for the rest of the sheet (the default).</param>
    /// <param name="mode">What happens at the last frame. Default loop.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="firstFrame"/> is outside the sheet.</exception>
    public AnimatedSprite(SpriteSheet sheet, float framesPerSecond = 12f, int firstFrame = 0, int frameCount = -1, SpriteMode mode = SpriteMode.Loop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(firstFrame, sheet.Count);

        int available = sheet.Count - firstFrame;
        _sheet = sheet;
        _first = firstFrame;
        _count = frameCount < 0 ? available : Math.Max(1, Math.Min(frameCount, available));
        FramesPerSecond = framesPerSecond;
        Mode = mode;

        // A single-frame run set to play once is already at its last frame, so it reads as complete.
        _complete = mode == SpriteMode.Once && _count <= 1;
    }

    /// <summary>How many frames play a second. Change it to speed the animation up or slow it down.</summary>
    public float FramesPerSecond { get; set; }

    /// <summary>What the animation does at its last frame.</summary>
    public SpriteMode Mode { get; set; }

    /// <summary>How many frames the run covers.</summary>
    public int FrameCount => _count;

    /// <summary>The frame within the run currently showing, from 0 to <see cref="FrameCount"/> minus one.</summary>
    public int LocalFrame => _index;

    /// <summary>The sheet frame currently showing, ready to pass to <see cref="SpriteSheet.Frame"/>.</summary>
    public int CurrentFrame => _first + _index;

    /// <summary>True once a <see cref="SpriteMode.Once"/> animation has reached its last frame.</summary>
    public bool IsComplete => _complete;

    /// <summary>Returns the animation to its first frame.</summary>
    public void Reset()
    {
        _time = 0f;
        _index = 0;
        _direction = 1;
        _complete = Mode == SpriteMode.Once && _count <= 1;
    }

    /// <summary>Advances the animation by <paramref name="deltaSeconds"/>, stepping frames as time passes.</summary>
    public void Update(float deltaSeconds)
    {
        if (deltaSeconds <= 0f || FramesPerSecond <= 0f || _count <= 1 || _complete)
            return;

        float frameDuration = 1f / FramesPerSecond;
        _time += deltaSeconds;
        while (_time >= frameDuration)
        {
            _time -= frameDuration;
            Step();
            if (_complete)
            {
                _time = 0f;
                break;
            }
        }
    }

    /// <summary>Draws the current frame onto <paramref name="destination"/> at (<paramref name="x"/>, <paramref name="y"/>), blended by alpha.</summary>
    public void Draw(Surface destination, int x, int y) => _sheet.Draw(destination, CurrentFrame, x, y);

    /// <summary>Draws the current frame scaled into the rectangle at (<paramref name="x"/>, <paramref name="y"/>), blended by alpha.</summary>
    public void DrawScaled(Surface destination, int x, int y, int width, int height)
        => _sheet.DrawScaled(destination, CurrentFrame, x, y, width, height);

    private void Step()
    {
        switch (Mode)
        {
            case SpriteMode.Once:
                if (_index < _count - 1)
                    _index++;
                else
                    _complete = true;
                break;

            case SpriteMode.PingPong:
                _index += _direction;
                if (_index >= _count - 1)
                {
                    _index = _count - 1;
                    _direction = -1;
                }
                else if (_index <= 0)
                {
                    _index = 0;
                    _direction = 1;
                }
                break;

            default:
                _index = (_index + 1) % _count;
                break;
        }
    }
}
