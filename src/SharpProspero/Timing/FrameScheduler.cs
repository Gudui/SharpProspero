// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;

namespace SharpProspero.Timing;

/// <summary>
/// Runs callbacks after a delay or on a repeat, driven by the frame delta. Where <see cref="Cooldown"/>,
/// <see cref="Interval"/> and <see cref="Countdown"/> are values a frame polls, this invokes a callback
/// on your behalf when its time comes. Call <see cref="Update"/> once per frame with the seconds elapsed;
/// callbacks run on that thread, in the order they came due. Schedule and cancel from inside a callback
/// safely: work added during a tick is considered from the next tick, not the current one.
/// </summary>
/// <example>
/// <code>
/// var scheduler = new FrameScheduler();
/// scheduler.After(1.5, () =&gt; ShowHint());          // once, in 1.5 s
/// int spawn = scheduler.Every(0.75, SpawnEnemy);    // repeating, every 0.75 s
/// // each frame:
/// scheduler.Update(context.DeltaSeconds);
/// // later:
/// scheduler.Cancel(spawn);
/// </code>
/// </example>
public sealed class FrameScheduler
{
    private sealed class Entry
    {
        public int Id;
        public double Remaining;
        public double Interval;
        public bool Repeating;
        public Action Callback = null!;
        public bool Cancelled;
    }

    private readonly List<Entry> _entries = [];
    private int _nextId = 1;

    /// <summary>The number of callbacks currently scheduled.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Schedules <paramref name="callback"/> to run once after <paramref name="seconds"/>. A value of
    /// zero or less runs it on the next <see cref="Update"/>. Returns a handle for <see cref="Cancel"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    public int After(double seconds, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Add(seconds, 0, repeating: false, callback);
    }

    /// <summary>
    /// Schedules <paramref name="callback"/> to run every <paramref name="seconds"/>, starting one
    /// interval from now. Returns a handle for <see cref="Cancel"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is zero or negative.</exception>
    public int Every(double seconds, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        return Add(seconds, seconds, repeating: true, callback);
    }

    /// <summary>
    /// Cancels the callback with the given <paramref name="handle"/>. Returns false when nothing with that
    /// handle is scheduled. Safe to call from inside a callback, including on the running callback itself.
    /// </summary>
    public bool Cancel(int handle)
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Id == handle && !entry.Cancelled)
            {
                entry.Cancelled = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Cancels every scheduled callback.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Advances every schedule by <paramref name="deltaSeconds"/> and runs the callbacks that come due.
    /// A repeating callback that falls more than one interval behind (after a long pause) fires once and
    /// re-arms one interval out rather than firing repeatedly to catch up.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (deltaSeconds > 0)
        {
            foreach (Entry entry in _entries)
            {
                if (!entry.Cancelled)
                    entry.Remaining -= deltaSeconds;
            }
        }

        // Collect the entries due at entry to this tick, so a callback that schedules more work does not
        // see its own additions fire this same tick. Iterate by index over the snapshot count.
        int due = _entries.Count;
        for (int i = 0; i < due; i++)
        {
            Entry entry = _entries[i];
            if (entry.Cancelled || entry.Remaining > 0)
                continue;

            entry.Callback();

            if (entry.Repeating && !entry.Cancelled)
            {
                entry.Remaining += entry.Interval;
                if (entry.Remaining <= 0)
                    entry.Remaining = entry.Interval;
            }
            else
            {
                entry.Cancelled = true;
            }
        }

        _entries.RemoveAll(static e => e.Cancelled);
    }

    private int Add(double seconds, double interval, bool repeating, Action callback)
    {
        int id = _nextId++;
        _entries.Add(new Entry
        {
            Id = id,
            Remaining = seconds,
            Interval = interval,
            Repeating = repeating,
            Callback = callback,
        });
        return id;
    }
}
