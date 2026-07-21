// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop.Pad;
using SharpProspero.Ui;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class DiagnosticsAndControlsTests
{
    private static readonly UiTheme Theme = UiTheme.Default;

    private static GamePadState Held(ScePadButton button) => new() { Buttons = button };

    // --- Gauge ---

    [Fact]
    public void Gauge_ClampsAndReportsGeometry()
    {
        Assert.Equal(1f, new Gauge(2f).Value);
        Assert.Equal(0f, new Gauge(-1f).Value);

        var gauge = new Gauge { Diameter = 80 };
        Assert.False(gauge.IsFocusable);
        Assert.Equal(80, gauge.Measure(300, Theme));
    }

    [Fact]
    public void Gauge_FillsTheSweptSideAndLeavesTheHole()
    {
        var gauge = new Gauge(0.5f)
        {
            Diameter = 80,
            ShowPercent = false,
            FillColor = Color.White,
            TrackColor = Color.FromRgb(40, 40, 40),
        };
        gauge.Bounds = new UiRect(0, 0, 120, 80);

        const int w = 120, h = 80;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
            gauge.Draw(new Surface(p, w, h), Theme, null);

        int cx = 60, cy = 40;
        // Half a turn from the top sweeps the right half, so the east point is filled and the west is the
        // unfilled track; the middle is inside the ring's hole.
        Assert.Equal(Color.White.Value, pixels[(cy * w) + (cx + 35)]);
        Assert.Equal(Color.FromRgb(40, 40, 40).Value, pixels[(cy * w) + (cx - 35)]);
        Assert.Equal(0u, pixels[(cy * w) + cx]);
    }

    // --- FrameStats ---

    [Fact]
    public void FrameStats_ReportsRateAndFrameTimes()
    {
        var stats = new FrameStats(window: 8);
        stats.Record(0.010f);
        stats.Record(0.020f);
        stats.Record(0.030f);

        Assert.Equal(30f, stats.LastMs, 3);
        Assert.Equal(20f, stats.AvgMs, 3);
        Assert.Equal(10f, stats.MinMs, 3);
        Assert.Equal(30f, stats.MaxMs, 3);
        Assert.Equal(50f, stats.Fps, 2);      // 1000 / 20ms
        Assert.Equal(3, stats.SampleCount);

        // A non-positive delta is ignored.
        stats.Record(0f);
        Assert.Equal(3, stats.SampleCount);
    }

    [Fact]
    public void FrameStats_WindowDropsOldFrames()
    {
        var stats = new FrameStats(window: 2);
        stats.Record(0.010f);
        stats.Record(0.020f);
        stats.Record(0.030f); // pushes the 10ms frame out of the window

        Assert.Equal(2, stats.SampleCount);
        Assert.Equal(25f, stats.AvgMs, 3);
        Assert.Equal(20f, stats.MinMs, 3);
        Assert.Equal(30f, stats.MaxMs, 3);

        stats.Reset();
        Assert.Equal(0, stats.SampleCount);
        Assert.Equal(0f, stats.Fps);
    }

    [Fact]
    public void FrameStats_DrawAndGraphDoNotThrow()
    {
        var stats = new FrameStats(window: 4);
        for (int i = 0; i < 6; i++)
            stats.Record(0.016f + (i * 0.001f));

        const int w = 160, h = 40;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
        {
            var surface = new Surface(p, w, h);
            stats.Draw(surface, 2, 2, 1, Color.White);
            stats.DrawGraph(surface, 2, 16, 120, 20, Color.FromRgb(90, 160, 255), Color.FromRgb(60, 60, 60));
        }
        Assert.Contains(pixels, px => px != 0u);
    }

    // --- UiRepeater ---

    [Fact]
    public void UiRepeater_FiresOnPressThenRepeatsAfterTheDelay()
    {
        var repeater = new UiRepeater { InitialDelay = 0.4f, RepeatInterval = 0.09f };

        Assert.True(repeater.Update(Held(ScePadButton.Down), 0f).Down);      // first press
        Assert.False(repeater.Update(Held(ScePadButton.Down), 0.10f).Down);  // still within the delay
        Assert.True(repeater.Update(Held(ScePadButton.Down), 0.40f).Down);   // the delay has now passed
    }

    [Fact]
    public void UiRepeater_ReleaseThenPressFiresAgain()
    {
        var repeater = new UiRepeater();
        Assert.True(repeater.Update(Held(ScePadButton.Left), 0f).Left);
        Assert.False(repeater.Update(GamePadState.Neutral, 0.016f).Left);    // released
        Assert.True(repeater.Update(Held(ScePadButton.Left), 0.016f).Left);  // pressed again is a fresh edge
    }

    [Fact]
    public void UiRepeater_ConfirmAndCancelNeverRepeat()
    {
        var repeater = new UiRepeater();
        Assert.True(repeater.Update(Held(ScePadButton.Cross), 0f).Confirm);
        // Held for a long time, but confirm is edge-only, so it does not repeat.
        Assert.False(repeater.Update(Held(ScePadButton.Cross), 10f).Confirm);

        Assert.True(repeater.Update(Held(ScePadButton.Circle), 0f).Cancel);
        Assert.False(repeater.Update(Held(ScePadButton.Circle), 10f).Cancel);
    }

    [Fact]
    public void UiRepeater_ResetClearsHeldState()
    {
        var repeater = new UiRepeater();
        repeater.Update(Held(ScePadButton.Up), 0f);
        repeater.Reset();
        // After a reset the same held direction reads as a fresh press.
        Assert.True(repeater.Update(Held(ScePadButton.Up), 0f).Up);
    }

    // --- DrawTextOutlined ---

    [Fact]
    public void DrawTextOutlined_DrawsBothTheOutlineAndTheFill()
    {
        const int w = 40, h = 16;
        uint[] pixels = new uint[w * h];
        fixed (uint* p = pixels)
            new Surface(p, w, h).DrawTextOutlined("A", 4, 4, 1, Color.White, Color.FromRgb(200, 0, 0));

        Assert.Contains(pixels, px => px == Color.White.Value);          // the fill
        Assert.Contains(pixels, px => px == Color.FromRgb(200, 0, 0).Value); // the outline around it
    }
}
