// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop;
using SharpProspero.Interop.SystemService;
using SharpProspero.Interop.UserService;
using System;

namespace SharpProspero.Application;

/// <summary>
/// External-host runtime for a SharpProspero application (engine-substrate Slice 1, HOST-01/ADR-006).
/// It owns service initialization and termination, display opening and teardown, and controller
/// discovery, without starting a loop, measuring engine time, or presenting frames. An external
/// engine initializes a runtime, opens its configured devices, polls service events on its own
/// thread, and disposes the runtime at shutdown; <see cref="ProsperoApp"/> remains a convenience
/// loop composed over the same runtime.
/// </summary>
/// <remarks>
/// Frame-thread owned and not thread-safe (P0, ADR-009 proposed). Disposal is idempotent and
/// releases the controller, then the display, then the user service, mirroring the order
/// <see cref="ProsperoApp"/> historically tore down in.
/// </remarks>
public sealed class ProsperoRuntime : IDisposable
{
    // How many frames pass before a missing controller is looked for again. Matches the throttle
    // ProsperoApp applied before the extraction, where frame zero is runtime startup.
    private const long GamePadRetryFrames = 60;

    private readonly AppConfig _config;
    private readonly IProsperoRuntimePlatform _platform;
    private DisplayDevice? _display;
    private GamePad? _gamePad;
    private long _nextGamePadAttempt;
    private bool _disposed;

    /// <summary>The startup settings this runtime was initialized with.</summary>
    public AppConfig Config => _config;

    /// <summary>The opened display, or null before <see cref="OpenDisplay"/> has succeeded.</summary>
    public DisplayDevice? Display => _display;

    /// <summary>The opened controller, or null when none was opened.</summary>
    public GamePad? GamePad => _gamePad;

    private ProsperoRuntime(AppConfig config, IProsperoRuntimePlatform platform)
    {
        _config = config;
        _platform = platform;
    }

    /// <summary>
    /// Initializes the user service, hides the boot splash when configured, and returns a runtime
    /// the caller owns. Open devices explicitly with <see cref="OpenDisplay"/> and
    /// <see cref="TryOpenGamePad"/>; no loop is started.
    /// </summary>
    public static ProsperoRuntime Initialize(AppConfig? config = null) =>
        Initialize(config, new ProsperoRuntimePlatform());

    internal static ProsperoRuntime Initialize(AppConfig? config, IProsperoRuntimePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var runtime = new ProsperoRuntime(config ?? new AppConfig(), platform);
        runtime._platform.InitializeServices(runtime._config.HideSplashScreen);
        return runtime;
    }

    /// <summary>
    /// Opens the display for the system user with the configured size and buffer count.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The runtime was disposed.</exception>
    /// <exception cref="InvalidOperationException">A display is already open on this runtime.</exception>
    public DisplayDevice OpenDisplay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_display is not null)
            throw new InvalidOperationException("A display is already open on this runtime.");
        _display = _platform.OpenDisplay(_config);
        return _display;
    }

    /// <summary>
    /// Opens the controller for <see cref="AppConfig.UserId"/>, resolving the launching user when
    /// that is <see cref="SceUser.Invalid"/>. A failure leaves the controller unopened, records a
    /// warning, and schedules the next attempt <see cref="GamePadRetryFrames"/> frames after frame
    /// zero; the loop then retries through <see cref="TryOpenGamePadIfDue"/>. Returns true when a
    /// controller is open.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The runtime was disposed.</exception>
    public bool TryOpenGamePad()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_gamePad is not null)
            return true;
        return AttemptOpenGamePad(frameIndex: 0);
    }

    /// <summary>
    /// Opens the controller when one is configured, none is open yet, and <paramref name="frameIndex"/>
    /// has reached the next allowed attempt. A module that started before the pad was paired stays
    /// able to pick it up without asking the service on every frame.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The runtime was disposed.</exception>
    public bool TryOpenGamePadIfDue(long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_gamePad is not null)
            return true;
        if (!_config.OpenGamePad || frameIndex < _nextGamePadAttempt)
            return false;
        return AttemptOpenGamePad(frameIndex);
    }

    /// <summary>
    /// Drains pending user-service notifications (sign-in/sign-out) so the queue cannot grow for as
    /// long as the runtime lives. An external host calls this on its own tick; the
    /// <see cref="ProsperoApp"/> compatibility loop preserves its historical frame behavior and does
    /// not call it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The runtime was disposed.</exception>
    public void PollEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.PollServiceEvents();
    }

    private bool AttemptOpenGamePad(long frameIndex)
    {
        _nextGamePadAttempt = frameIndex + GamePadRetryFrames;
        try
        {
            // Open resolves the launching user itself when the setting names none.
            _gamePad = _platform.OpenGamePad(_config.UserId);
            return true;
        }
        catch (ProsperoException failure)
        {
            _gamePad = null;
            Log.Warning($"The controller could not be opened: {failure.Message}");
            return false;
        }
    }

    /// <summary>
    /// Tears down the controller, the display, and the user service, in that order. References are
    /// retained (as <see cref="ProsperoApp"/> historically retained them) so post-teardown property
    /// reads observe the same objects; every operation still rejects use after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_gamePad is not null)
            _platform.DisposePad(_gamePad);
        if (_display is not null)
            _platform.DisposeDisplay(_display);
        _platform.TerminateServices();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The native boundary behind <see cref="ProsperoRuntime"/>. Production calls the platform;
/// host tests substitute a recording fake so lifecycle ordering and idempotence are verifiable
/// off-device.
/// </summary>
internal interface IProsperoRuntimePlatform
{
    void InitializeServices(bool hideSplashScreen);
    void TerminateServices();
    DisplayDevice OpenDisplay(AppConfig config);
    GamePad OpenGamePad(int userId);
    void DisposeDisplay(DisplayDevice display);
    void DisposePad(GamePad pad);
    void PollServiceEvents();
}

/// <summary>Production <see cref="IProsperoRuntimePlatform"/> that talks to the console services.</summary>
internal sealed class ProsperoRuntimePlatform : IProsperoRuntimePlatform
{
    public void InitializeServices(bool hideSplashScreen)
    {
        // The service calls are tolerant: a module launched by the system may find them already
        // started, which the return code reports without preventing the loop from running.
        unsafe
        {
            int priority = 700;
            UserService.sceUserServiceInitialize(&priority);
        }

        if (hideSplashScreen)
            SystemService.sceSystemServiceHideSplashScreen();
    }

    public void TerminateServices() => UserService.sceUserServiceTerminate();

    public DisplayDevice OpenDisplay(AppConfig config) =>
        // The display is opened for the system rather than for a user. An application of this kind
        // owns the whole output and the call takes only that, so passing a user here would refuse the
        // open for anyone who set one; the user matters to the controller, which is where it is used.
        DisplayDevice.Open(config.Width, config.Height, config.BufferCount, SceUser.System);

    public GamePad OpenGamePad(int userId) => GamePad.Open(userId);

    public void DisposeDisplay(DisplayDevice display) => display.Dispose();

    public void DisposePad(GamePad pad) => pad.Dispose();

    public void PollServiceEvents()
    {
        try
        {
            unsafe
            {
                SceUserServiceEvent @event;
                while (UserService.sceUserServiceGetEvent(&@event) == 0)
                {
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            // Off-device the service library is absent; polling is then a no-op so host harnesses
            // can exercise the external-host shape without hardware.
        }
    }
}
