// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Threading;
using System;
using System.Diagnostics;

namespace SharpProspero.Application;

/// <summary>
/// Base class for an application module. Derive from it, override <see cref="OnFrame"/>, and call
/// <see cref="Run"/> from the module entry point. The base owns a <see cref="ProsperoRuntime"/>
/// that opens the display and controller, drives a vertical-blank-paced loop, and tears everything
/// down on exit.
/// </summary>
/// <remarks>Creates the application with <paramref name="config"/>, or the defaults when null.</remarks>
public abstract class ProsperoApp(AppConfig? config = null) : IDisposable
{
    private readonly FrameContext _context = new();
    private ProsperoRuntime? _runtime;
    private bool _disposed;

    /// <summary>The startup settings.</summary>
    public AppConfig Config { get; } = config ?? new AppConfig();

    /// <summary>The display, available after <see cref="Run"/> has started.</summary>
    protected DisplayDevice Display => _runtime?.Display ?? throw new InvalidOperationException("The display is available only while running.");

    /// <summary>The controller, or null when none was opened.</summary>
    protected GamePad? GamePad => _runtime?.GamePad;

    /// <summary>
    /// The hand-off point back to the frame thread, drained once per frame before <see cref="OnFrame"/>.
    /// Post work here from a worker thread to apply a background result to the drawing state safely.
    /// </summary>
    protected Dispatcher Dispatcher => _context.Dispatcher;

    /// <summary>
    /// Initializes the runtime, opens the display and controller, and runs the frame loop until an
    /// override requests exit. The call returns after teardown.
    /// </summary>
    public void Run()
    {
        _runtime = ProsperoRuntime.Initialize(Config);

        DisplayDevice display = _runtime.OpenDisplay();
        if (Config.OpenGamePad)
            _runtime.TryOpenGamePad();

        OnLoad();

        // Once loading has run, its teardown must run too, whether the loop ends by request or because a
        // frame threw. The exception still propagates so the caller sees it (and Dispose releases the
        // display and controller), but the override's own cleanup is not skipped on the way out.
        try
        {
            long previous = Stopwatch.GetTimestamp();
            double frequency = Stopwatch.Frequency;
            _context.Input = GamePadState.Neutral;

            while (true)
            {
                long now = Stopwatch.GetTimestamp();
                _context.DeltaSeconds = (now - previous) / frequency;
                _context.TotalSeconds += _context.DeltaSeconds;
                previous = now;

                _context.Surface = display.BackBuffer;
                _context.PreviousInput = _context.Input;

                // A controller that was not there at start-up is looked for again now and then. Without
                // this a module that started a moment before the user signed in, or before the pad was
                // paired, stays deaf to it for as long as it runs.
                _runtime.TryOpenGamePadIfDue(_context.FrameIndex);

                _context.Input = _runtime.GamePad?.Read() ?? GamePadState.Neutral;

                // Apply anything a worker thread handed back before the frame draws.
                _context.Dispatcher.RunPending();

                OnFrame(_context);

                display.Present(Config.FlipMode);
                _context.FrameIndex++;

                if (_context.ExitRequested)
                    break;
            }
        }
        finally
        {
            OnUnload();
        }
    }

    /// <summary>Called once after the display opens and before the first frame. Load resources here.</summary>
    protected virtual void OnLoad()
    {
    }

    /// <summary>Called once per frame. Draw into <see cref="FrameContext.Surface"/>.</summary>
    protected abstract void OnFrame(FrameContext context);

    /// <summary>Called once after the loop ends and before teardown. Release resources here.</summary>
    protected virtual void OnUnload()
    {
    }

    /// <summary>Tears down the runtime, releasing the controller, the display, and services.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _runtime?.Dispose();
        GC.SuppressFinalize(this);
    }
}
