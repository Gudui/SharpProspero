// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;

namespace SharpProspero.Timing;

/// <summary>
/// Fires on a steady beat — spawn an enemy every two seconds, tick a clock, poll on a schedule. Advance
/// it each frame with the time since the last, and it reports how many whole periods have passed, so a
/// long frame still fires the right number of times rather than dropping beats.
/// </summary>
/// <remarks>Creates a beat with the given <paramref name="periodSeconds"/> between fires.</remarks>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="periodSeconds"/> is not positive.</exception>
public sealed class Interval(float periodSeconds)
{
    private float _accumulated;
    private float _period = periodSeconds > 0f
        ? periodSeconds
        : throw new ArgumentOutOfRangeException(nameof(periodSeconds));

    /// <summary>The time between fires, in seconds. Must be positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public float Period
    {
        get => _period;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _period = value;
        }
    }

    /// <summary>
    /// Advances by <paramref name="deltaSeconds"/> and returns how many whole periods have elapsed since
    /// the last call — fire that many times. Test it against zero for the common "did a beat happen"
    /// case.
    /// </summary>
    public int Advance(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f || Period <= 0f)
            return 0;

        _accumulated += deltaSeconds;
        int count = 0;
        while (_accumulated >= Period)
        {
            _accumulated -= Period;
            count++;
        }
        return count;
    }

    /// <summary>Clears the progress toward the next fire.</summary>
    public void Reset() => _accumulated = 0f;
}
