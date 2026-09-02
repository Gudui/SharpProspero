// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace SharpProspero.Tests;

/// <summary>
/// Slice 1 host enforcement (TASK-SHARPPROSPERO-RUNTIME-SLICE): the external-host runtime owns
/// service, display and input initialization/teardown with deterministic ordering and idempotent
/// disposal, and a minimal external host can initialize, poll and shut down without entering
/// <see cref="ProsperoApp.Run"/>. The recording platform keeps every test off-device.
/// </summary>
public sealed class ProsperoRuntimeTests
{
    private sealed class RecordingPlatform : IProsperoRuntimePlatform
    {
        public readonly List<string> Calls = new();
        public bool FailOpenGamePad;

        public void InitializeServices(bool hideSplashScreen) => Calls.Add($"Init:hideSplash={hideSplashScreen}");

        public void TerminateServices() => Calls.Add("Terminate");

        public DisplayDevice OpenDisplay(AppConfig config)
        {
            Calls.Add($"OpenDisplay:{config.Width}x{config.Height}x{config.BufferCount}");
            return (DisplayDevice)RuntimeHelpers.GetUninitializedObject(typeof(DisplayDevice));
        }

        public GamePad OpenGamePad(int userId)
        {
            Calls.Add($"OpenGamePad:{userId}");
            if (FailOpenGamePad)
                throw new ProsperoException("scePadOpen", -1);
            return (GamePad)RuntimeHelpers.GetUninitializedObject(typeof(GamePad));
        }

        public void DisposeDisplay(DisplayDevice display) => Calls.Add("DisposeDisplay");

        public void DisposePad(GamePad pad) => Calls.Add("DisposePad");

        public void PollServiceEvents() => Calls.Add("Poll");
    }

    [Fact]
    public void Initialize_Poll_Open_Shutdown_RecordsOrderedLifecycle()
    {
        var platform = new RecordingPlatform();
        using (ProsperoRuntime runtime = ProsperoRuntime.Initialize(null, platform))
        {
            Assert.Equal(1920, runtime.Config.Width);
            Assert.Null(runtime.Display);
            Assert.Null(runtime.GamePad);

            runtime.PollEvents();

            DisplayDevice display = runtime.OpenDisplay();
            Assert.Same(display, runtime.Display);
            Assert.True(runtime.TryOpenGamePad());
            Assert.NotNull(runtime.GamePad);
        }

        Assert.Equal(
        [
            "Init:hideSplash=True",
            "Poll",
            "OpenDisplay:1920x1080x2",
            $"OpenGamePad:{SceUser.Invalid}",
            "DisposePad",
            "DisposeDisplay",
            "Terminate",
        ], platform.Calls);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var platform = new RecordingPlatform();
        ProsperoRuntime runtime = ProsperoRuntime.Initialize(new AppConfig(), platform);
        runtime.OpenDisplay();
        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(1, platform.Calls.Count(call => call == "Terminate"));
        Assert.Equal(1, platform.Calls.Count(call => call == "DisposeDisplay"));
    }

    [Fact]
    public void Operations_AfterDispose_ThrowObjectDisposed()
    {
        var platform = new RecordingPlatform();
        ProsperoRuntime runtime = ProsperoRuntime.Initialize(new AppConfig(), platform);
        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runtime.OpenDisplay());
        Assert.Throws<ObjectDisposedException>(() => runtime.TryOpenGamePad());
        Assert.Throws<ObjectDisposedException>(() => runtime.TryOpenGamePadIfDue(999));
        Assert.Throws<ObjectDisposedException>(() => runtime.PollEvents());
    }

    [Fact]
    public void OpenDisplay_Twice_ThrowsInvalidOperation()
    {
        var platform = new RecordingPlatform();
        using ProsperoRuntime runtime = ProsperoRuntime.Initialize(new AppConfig(), platform);
        runtime.OpenDisplay();

        Assert.Throws<InvalidOperationException>(() => runtime.OpenDisplay());
        Assert.Equal(1, platform.Calls.Count(call => call.StartsWith("OpenDisplay:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TryOpenGamePad_Failure_Throttles_ThenRetriesWhenDue()
    {
        var platform = new RecordingPlatform { FailOpenGamePad = true };
        using ProsperoRuntime runtime = ProsperoRuntime.Initialize(new AppConfig(), platform);

        Assert.False(runtime.TryOpenGamePad());
        Assert.Null(runtime.GamePad);

        // The failed startup attempt schedules the next try 60 frames out; earlier frames must
        // not ask the service again.
        Assert.False(runtime.TryOpenGamePadIfDue(1));
        Assert.False(runtime.TryOpenGamePadIfDue(59));
        Assert.Equal(1, platform.Calls.Count(call => call.StartsWith("OpenGamePad:", StringComparison.Ordinal)));

        platform.FailOpenGamePad = false;
        Assert.True(runtime.TryOpenGamePadIfDue(60));
        Assert.NotNull(runtime.GamePad);
        Assert.Equal(2, platform.Calls.Count(call => call.StartsWith("OpenGamePad:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TryOpenGamePad_WhenDisabled_NeverAsksTheService()
    {
        var platform = new RecordingPlatform();
        using ProsperoRuntime runtime = ProsperoRuntime.Initialize(new AppConfig { OpenGamePad = false }, platform);

        Assert.False(runtime.TryOpenGamePadIfDue(0));
        Assert.False(runtime.TryOpenGamePadIfDue(10_000));
        Assert.Equal(0, platform.Calls.Count(call => call.StartsWith("OpenGamePad:", StringComparison.Ordinal)));
    }

    // This body is never executed. Compiling it is the Slice 1 exit-criterion check: a minimal
    // external host initializes, polls and shuts down through the public API without entering
    // ProsperoApp.Run().
    private static void CompileExternalHostWithoutRun()
    {
        using ProsperoRuntime runtime = ProsperoRuntime.Initialize();
        runtime.PollEvents();
        runtime.TryOpenGamePadIfDue(0);
        _ = runtime.Config.Width;
    }
}
