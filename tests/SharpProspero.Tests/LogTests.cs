// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Diagnostics;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

// The logging facade filters by level, fans out to every sink, and never lets a sink failure escape.
// The concrete file and console sinks call the kernel, so only the level/routing logic is tested here.
public sealed class LogTests
{
    private sealed class CapturingSink : ILogSink
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public void Write(LogLevel level, string message) => Entries.Add((level, message));
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(LogLevel level, string message) => throw new System.InvalidOperationException("boom");
    }

    private static CapturingSink FreshLog(LogLevel minimum)
    {
        Log.ClearSinks();
        Log.MinimumLevel = minimum;
        var sink = new CapturingSink();
        Log.AddSink(sink);
        return sink;
    }

    [Fact]
    public void Write_DropsMessagesBelowTheMinimum()
    {
        CapturingSink sink = FreshLog(LogLevel.Warning);
        Log.Information("skipped");
        Log.Debug("skipped");
        Log.Warning("kept");
        Log.Error("kept too");

        Assert.Equal(2, sink.Entries.Count);
        Assert.Equal(LogLevel.Warning, sink.Entries[0].Level);
        Assert.Equal("kept", sink.Entries[0].Message);
        Assert.Equal("kept too", sink.Entries[1].Message);

        Log.ClearSinks();
    }

    [Fact]
    public void Write_FansOutToEverySink()
    {
        Log.ClearSinks();
        Log.MinimumLevel = LogLevel.Trace;
        var a = new CapturingSink();
        var b = new CapturingSink();
        Log.AddSink(a);
        Log.AddSink(b);

        Log.Information("hello");

        Assert.Single(a.Entries);
        Assert.Single(b.Entries);
        Assert.Equal("hello", a.Entries[0].Message);

        Log.ClearSinks();
    }

    [Fact]
    public void Write_SwallowsSinkExceptions()
    {
        Log.ClearSinks();
        Log.MinimumLevel = LogLevel.Trace;
        Log.AddSink(new ThrowingSink());
        var good = new CapturingSink();
        Log.AddSink(good);

        Log.Error("still delivered");   // must not throw

        Assert.Single(good.Entries);
        Log.ClearSinks();
    }

    [Fact]
    public void Write_WithNoSinkDoesNothing()
    {
        Log.ClearSinks();
        Log.MinimumLevel = LogLevel.Trace;
        Log.Information("nowhere");    // must not throw
    }

    [Fact]
    public void NoneLevel_TurnsLoggingOff()
    {
        CapturingSink sink = FreshLog(LogLevel.None);
        Log.Error("dropped");
        Assert.Empty(sink.Entries);
        Log.ClearSinks();
    }
}
