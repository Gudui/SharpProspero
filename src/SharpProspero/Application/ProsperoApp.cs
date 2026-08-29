// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop;
using SharpProspero.Interop.SystemService;
using SharpProspero.Interop.UserService;
using SharpProspero.Threading;
using System;
using System.Diagnostics;

namespace SharpProspero.Application;

/// <summary>
/// Base class for an application module. Derive from it, override <see cref="OnFrame"/>, and call
/// <see cref="Run"/> from the module entry point. The base opens the display and controller, drives
/// a vertical-blank-paced loop, and tears everything down on exit.
/// </summary>
/// <remarks>Creates the application with <paramref name="config"/>, or the defaults when null.</remarks>
public abstract class ProsperoApp(AppConfig? config = null) : IDisposable
{
    private readonly FrameContext _context = new();
    private DisplayDevice? _display;
    private GamePad? _gamePad;
    private bool _disposed;

    /// <summary>The startup settings.</summary>
    public AppConfig Config { get; } = config ?? new AppConfig();

    /// <summary>The display, available after <see cref="Run"/> has started.</summary>
    protected DisplayDevice Display => _display ?? throw new InvalidOperationException("The display is available only while running.");

    /// <summary>The controller, or null when none was opened.</summary>
    protected GamePad? GamePad => _gamePad;

    /// <summary>
    /// The hand-off point back to the frame thread, drained once per frame before <see cref="OnFrame"/>.
    /// Post work here from a worker thread to apply a background result to the drawing state safely.
    /// </summary>
    protected Dispatcher Dispatcher => _context.Dispatcher;

    /// <summary>
    /// Opens the display and controller and runs the frame loop until an override requests exit. The
    /// call returns after teardown.
    /// </summary>
    public void Run()
    {
        InitializeServices();

        // The display is opened for the system rather than for a user. An application of this kind
        // owns the whole output and the call takes only that, so passing a user here would refuse the
        // open for anyone who set one; the user matters to the controller, which is where it is used.
        _display = DisplayDevice.Open(Config.Width, Config.Height, Config.BufferCount, SceUser.System);
        if (Config.OpenGamePad)
            TryOpenGamePad();

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

                _context.Surface = _display.BackBuffer;
                _context.PreviousInput = _context.Input;

                // A controller that was not there at start-up is looked for again now and then. Without
                // this a module that started a moment before the user signed in, or before the pad was
                // paired, stays deaf to it for as long as it runs.
                if (_gamePad is null && Config.OpenGamePad && _context.FrameIndex >= _nextGamePadAttempt)
                    TryOpenGamePad();

                _context.Input = _gamePad?.Read() ?? GamePadState.Neutral;

                // Apply anything a worker thread handed back before the frame draws.
                _context.Dispatcher.RunPending();

                OnFrame(_context);

                _display.Present(Config.FlipMode);
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

    // How many frames pass before a missing controller is looked for again, and the frame the next
    // attempt is allowed on. Once a second is often enough to pick one up without asking the service
    // on every frame for something that is usually not there.
    private const long GamePadRetryFrames = 60;
    private long _nextGamePadAttempt;

    /// <summary>
    /// Opens the controller for <see cref="AppConfig.UserId"/>, resolving the launching user when that
    /// is <see cref="SceUser.Invalid"/>. A failure leaves the controller unopened and is recorded
    /// rather than passed on: a module that draws is more use than one that ends because a pad was not
    /// ready, and the next attempt follows shortly.
    /// </summary>
    private void TryOpenGamePad()
    {
        _nextGamePadAttempt = _context.FrameIndex + GamePadRetryFrames;
        try
        {
            // Open resolves the launching user itself when the setting names none.
            _gamePad = GamePad.Open(Config.UserId);
        }
        catch (ProsperoException failure)
        {
            _gamePad = null;
            Log.Warning($"The controller could not be opened: {failure.Message}");
        }
    }

    private void InitializeServices()
    {
        // The service calls are tolerant: a module launched by the system may find them already
        // started, which the return code reports without preventing the loop from running.
        unsafe
        {
            int priority = 700;
            UserService.sceUserServiceInitialize(&priority);
        }

        if (Config.HideSplashScreen)
            SystemService.sceSystemServiceHideSplashScreen();
    }

    /// <summary>Tears down the controller and display.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _gamePad?.Dispose();
        _display?.Dispose();
        UserService.sceUserServiceTerminate();
        GC.SuppressFinalize(this);
    }
}
