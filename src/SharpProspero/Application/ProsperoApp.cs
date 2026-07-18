// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop;
using SharpProspero.Interop.SystemService;
using SharpProspero.Interop.UserService;

namespace SharpProspero.Application;

/// <summary>
/// Base class for an application module. Derive from it, override <see cref="OnFrame"/>, and call
/// <see cref="Run"/> from the module entry point. The base opens the display and controller, drives
/// a vertical-blank-paced loop, and tears everything down on exit.
/// </summary>
public abstract class ProsperoApp : IDisposable
{
    private readonly FrameContext _context = new();
    private DisplayDevice? _display;
    private GamePad? _gamePad;
    private bool _disposed;

    /// <summary>Creates the application with <paramref name="config"/>, or the defaults when null.</summary>
    protected ProsperoApp(AppConfig? config = null) => Config = config ?? new AppConfig();

    /// <summary>The startup settings.</summary>
    public AppConfig Config { get; }

    /// <summary>The display, available after <see cref="Run"/> has started.</summary>
    protected DisplayDevice Display => _display ?? throw new InvalidOperationException("The display is available only while running.");

    /// <summary>The controller, or null when none was opened.</summary>
    protected GamePad? GamePad => _gamePad;

    /// <summary>
    /// Opens the display and controller and runs the frame loop until an override requests exit. The
    /// call returns after teardown.
    /// </summary>
    public void Run()
    {
        InitializeServices();

        _display = DisplayDevice.Open(Config.Width, Config.Height, Config.BufferCount, Config.UserId);
        if (Config.OpenGamePad)
        {
            try { _gamePad = GamePad.Open(Config.UserId); }
            catch (ProsperoException) { _gamePad = null; }
        }

        OnLoad();

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
            _context.Input = _gamePad?.Read() ?? GamePadState.Neutral;

            OnFrame(_context);

            _display.Present(Config.FlipMode);
            _context.FrameIndex++;

            if (_context.ExitRequested)
                break;
        }

        OnUnload();
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
