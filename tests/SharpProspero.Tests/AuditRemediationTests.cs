// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Ai;
using SharpProspero.Diagnostics;
using SharpProspero.Graphics;
using SharpProspero.Numerics;
using SharpProspero.Storage;
using SharpProspero.Threading;
using SharpProspero.Ui;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class GameRandomUniformityTests
{
    [Fact]
    public void NextStaysWithinRangeAndCoversEveryBucket()
    {
        var random = new GameRandom(12345);
        var counts = new int[7];
        for (int i = 0; i < 70000; i++)
        {
            int v = random.Next(3, 10); // [3, 10)
            Assert.InRange(v, 3, 9);
            counts[v - 3]++;
        }
        // With the modulo bias removed every bucket should sit near the 10000 expected; a biased range
        // reduction would skew the low buckets well past this band.
        foreach (int count in counts)
            Assert.InRange(count, 9400, 10600);
    }

    [Fact]
    public void ShuffleIsAPermutation()
    {
        var random = new GameRandom(999);
        int[] items = new int[64];
        for (int i = 0; i < items.Length; i++)
            items[i] = i;
        random.Shuffle(items);
        Array.Sort(items);
        for (int i = 0; i < items.Length; i++)
            Assert.Equal(i, items[i]);
    }

    [Fact]
    public void DegenerateRangeReturnsTheLowerBound()
    {
        var random = new GameRandom(1);
        Assert.Equal(5, random.Next(5, 5));
        Assert.Equal(5, random.Next(5, 4));
    }
}

public sealed class Vector2HelpersTests
{
    [Fact]
    public void MoveTowardsStopsExactlyOnTheTarget()
    {
        var from = new Vector2(0, 0);
        var to = new Vector2(3, 4); // length 5
        Assert.Equal(to, Vector2.MoveTowards(from, to, 10f));       // overshoot clamps to target
        Vector2 half = Vector2.MoveTowards(from, to, 2.5f);
        Assert.Equal(2.5f, half.Length, 3);
    }

    [Fact]
    public void ClampLengthShortensOnlyWhenTooLong()
    {
        var v = new Vector2(6, 8); // length 10
        Assert.Equal(5f, v.ClampLength(5f).Length, 3);
        Assert.Equal(v, v.ClampLength(20f));
    }

    [Fact]
    public void CrossSignsByTurnDirection()
    {
        Assert.True(Vector2.Cross(Vector2.UnitX, Vector2.UnitY) > 0f);
        Assert.True(Vector2.Cross(Vector2.UnitY, Vector2.UnitX) < 0f);
        Assert.Equal(0f, Vector2.Cross(Vector2.UnitX, new Vector2(2, 0)));
    }

    [Fact]
    public void PerpendicularIsAQuarterTurn()
    {
        Vector2 p = new Vector2(1, 0).Perpendicular();
        Assert.Equal(0f, p.X, 5);
        Assert.Equal(1f, p.Y, 5);
    }

    [Fact]
    public void AngleRoundTrips()
    {
        Vector2 v = Vector2.FromAngle(0.9f, 3f);
        Assert.Equal(3f, v.Length, 3);
        Assert.Equal(0.9f, v.ToAngle(), 4);
    }
}

public sealed class CollisionSegmentTests
{
    [Fact]
    public void CrossingSegmentsMeetAtTheExpectedPoint()
    {
        bool hit = Collision.SegmentIntersection(new Vector2(0, 0), new Vector2(4, 4), new Vector2(0, 4), new Vector2(4, 0), out Vector2 point);
        Assert.True(hit);
        Assert.Equal(2f, point.X, 3);
        Assert.Equal(2f, point.Y, 3);
    }

    [Fact]
    public void ParallelAndMissingSegmentsDoNotMeet()
    {
        Assert.False(Collision.SegmentsIntersect(new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 1), new Vector2(4, 1)));   // parallel
        Assert.False(Collision.SegmentsIntersect(new Vector2(0, 0), new Vector2(1, 1), new Vector2(3, 0), new Vector2(4, 1)));   // apart
    }

    [Fact]
    public void SegmentThroughARectangleIsDetected()
    {
        var rect = new RectF(2, 2, 4, 4);
        Assert.True(Collision.SegmentIntersectsRect(new Vector2(0, 4), new Vector2(8, 4), rect)); // crosses
        Assert.True(Collision.SegmentIntersectsRect(new Vector2(3, 3), new Vector2(4, 4), rect)); // inside
        Assert.False(Collision.SegmentIntersectsRect(new Vector2(0, 0), new Vector2(1, 0), rect)); // clear
    }
}

public sealed class WeightedPathfindingTests
{
    [Fact]
    public void ThePathAvoidsExpensiveCells()
    {
        // A 3-wide corridor: the middle column costs a lot, so a route from the top-left to the
        // bottom-left should hug the left column rather than cut across the middle.
        var finder = new GridPathfinder(3, 3);
        float Cost(int c, int r) => c == 1 ? 50f : 1f;
        List<(int Column, int Row)> path = finder.FindPath((0, 0), (0, 2), Cost);
        Assert.NotEmpty(path);
        Assert.All(path, cell => Assert.NotEqual(1, cell.Column));
    }

    [Fact]
    public void ZeroCostCellsAreImpassable()
    {
        var finder = new GridPathfinder(3, 1);
        float Cost(int c, int r) => c == 1 ? 0f : 1f; // wall in the middle
        Assert.Empty(finder.FindPath((0, 0), (2, 0), Cost));
    }

    [Fact]
    public void TheBooleanOverloadStillWorks()
    {
        var finder = new GridPathfinder(3, 1);
        List<(int Column, int Row)> path = finder.FindPath((0, 0), (2, 0), (c, r) => true);
        Assert.Equal(3, path.Count);
    }
}

public sealed class FrameStatsPercentileTests
{
    [Fact]
    public void PercentilesTrackTheSlowFrames()
    {
        var stats = new FrameStats(100);
        for (int i = 1; i <= 100; i++)
            stats.Record(i / 1000f); // 1ms .. 100ms

        Assert.Equal(100f, stats.PercentileMs(100f), 3); // the slowest
        Assert.Equal(1f, stats.PercentileMs(1f), 0);     // near the fastest
        Assert.True(stats.PercentileMs(95f) >= stats.PercentileMs(50f));
        // The one-percent-low frame rate comes from the ninety-ninth-percentile frame time (about 99ms).
        Assert.InRange(stats.OnePercentLowFps, 9f, 12f);
    }

    [Fact]
    public void AnEmptyWindowReportsZero()
    {
        var stats = new FrameStats(8);
        Assert.Equal(0f, stats.PercentileMs(90f));
        Assert.Equal(0f, stats.OnePercentLowFps);
    }
}

public sealed class ScrollMenuTests
{
    private sealed class FocusStub : UiElement
    {
        public bool GotConfirm;
        public override bool IsFocusable => true;
        public override int Measure(int width, UiTheme theme) => 40;
        public override bool HandleInput(UiInput input, UiTheme theme)
        {
            if (input.Confirm) { GotConfirm = true; return true; }
            return false;
        }
        public override void Draw(Surface surface, UiTheme theme, UiElement? focused) { }
    }

    private static readonly UiInput Down = new(false, true, false, false, false, false);
    private static readonly UiInput Confirm = new(false, false, false, false, true, false);

    private static (ScrollMenu Menu, FocusStub[] Stubs) Build()
    {
        var menu = new ScrollMenu { ViewHeight = 100, Spacing = 0 };
        var stubs = new FocusStub[10];
        for (int i = 0; i < stubs.Length; i++)
        {
            stubs[i] = new FocusStub();
            menu.Add(stubs[i]);
        }
        // The screen runs the internal layout so the child positions and scroll are known.
        new UiScreen(menu).Layout(new UiRect(0, 0, 400, 100));
        return (menu, stubs);
    }

    [Fact]
    public void DownMovesFocusAndScrollsToKeepItInView()
    {
        (ScrollMenu menu, FocusStub[] stubs) = Build();
        Assert.True(menu.IsFocusable);
        Assert.Same(stubs[0], menu.FocusedChild);
        Assert.Equal(0, menu.ScrollOffset);

        Assert.True(menu.HandleInput(Down, UiTheme.Default)); // -> child 1, still in view
        Assert.True(menu.HandleInput(Down, UiTheme.Default)); // -> child 2, bottom 120 > 100 -> scroll 20
        Assert.True(menu.HandleInput(Down, UiTheme.Default)); // -> child 3, bottom 160 -> scroll 60

        Assert.Same(stubs[3], menu.FocusedChild);
        Assert.Equal(60, menu.ScrollOffset);
    }

    [Fact]
    public void ConfirmReachesTheFocusedControl()
    {
        (ScrollMenu menu, FocusStub[] stubs) = Build();
        menu.HandleInput(Down, UiTheme.Default); // focus child 1
        Assert.True(menu.HandleInput(Confirm, UiTheme.Default));
        Assert.True(stubs[1].GotConfirm);
        Assert.False(stubs[0].GotConfirm);
    }

    [Fact]
    public void DownAtTheLastControlIsLeftForTheScreen()
    {
        (ScrollMenu menu, FocusStub[] stubs) = Build();
        for (int i = 0; i < 20; i++)
            menu.HandleInput(Down, UiTheme.Default); // saturate at the last control
        Assert.Same(stubs[9], menu.FocusedChild);
        Assert.False(menu.HandleInput(Down, UiTheme.Default)); // nothing below, so the screen gets it
    }
}

public sealed class AssetManagerTests
{
    // A minimal ustar archive of regular files, enough for TarArchive to read back.
    private static byte[] BuildTar(params (string Name, string Content)[] files)
    {
        var output = new List<byte>();
        foreach ((string name, string content) in files)
        {
            byte[] data = Encoding.ASCII.GetBytes(content);
            byte[] header = new byte[512];
            void Write(int offset, string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                Array.Copy(bytes, 0, header, offset, bytes.Length);
            }
            Write(0, name);
            Write(100, "0000644\0");
            Write(108, "0000000\0");
            Write(116, "0000000\0");
            Write(124, Convert.ToString(data.Length, 8).PadLeft(11, '0') + "\0");
            Write(136, "00000000000\0");
            header[156] = (byte)'0'; // regular file
            Write(257, "ustar\0");
            Write(263, "00");
            for (int i = 148; i < 156; i++)
                header[i] = (byte)' ';
            int sum = 0;
            foreach (byte b in header)
                sum += b;
            Write(148, Convert.ToString(sum, 8).PadLeft(6, '0'));
            header[154] = 0;
            header[155] = (byte)' ';
            output.AddRange(header);
            output.AddRange(data);
            int pad = (512 - (data.Length % 512)) % 512;
            output.AddRange(new byte[pad]);
        }
        output.AddRange(new byte[1024]); // end-of-archive
        return output.ToArray();
    }

    [Fact]
    public void ReadsAnInMemoryAssetAndCachesTheDecode()
    {
        var assets = new AssetManager();
        assets.AddFile("ui/title.txt", Encoding.UTF8.GetBytes("hello"));

        Assert.True(assets.Exists("ui/title.txt"));
        Assert.Equal("hello", Encoding.UTF8.GetString(assets.ReadBytes("ui/title.txt")));

        int decodes = 0;
        string Decode(byte[] bytes) { decodes++; return Encoding.UTF8.GetString(bytes); }
        Assert.Equal("hello", assets.Load("ui/title.txt", Decode));
        Assert.Equal("hello", assets.Load("ui/title.txt", Decode));
        Assert.Equal(1, decodes); // decoded once, then served from the cache
    }

    [Fact]
    public void ReadsFromAMountedArchiveUnderItsPrefix()
    {
        var assets = new AssetManager();
        assets.MountArchive(BuildTar(("world1.dat", "abc")), prefix: "levels");

        Assert.True(assets.Exists("levels/world1.dat"));
        Assert.Equal("abc", Encoding.UTF8.GetString(assets.ReadBytes("levels/world1.dat")));
        Assert.False(assets.Exists("world1.dat")); // only under the prefix
    }

    [Fact]
    public void ALaterMountCoversAnEarlierOne()
    {
        var assets = new AssetManager();
        assets.MountArchive(BuildTar(("data.txt", "base")));
        assets.MountArchive(BuildTar(("data.txt", "patch")));
        Assert.Equal("patch", Encoding.UTF8.GetString(assets.ReadBytes("data.txt")));
    }

    [Fact]
    public void ReplacingAnAssetInvalidatesTheCache()
    {
        var assets = new AssetManager();
        assets.AddFile("a.txt", Encoding.UTF8.GetBytes("one"));
        Assert.Equal("one", Encoding.UTF8.GetString(assets.ReadBytes("a.txt")));
        assets.AddFile("a.txt", Encoding.UTF8.GetBytes("two"));
        Assert.Equal("two", Encoding.UTF8.GetString(assets.ReadBytes("a.txt")));
    }

    [Fact]
    public void AMissingAssetThrows()
    {
        var assets = new AssetManager();
        Assert.False(assets.TryReadBytes("nope", out _));
        Assert.Throws<System.IO.FileNotFoundException>(() => assets.ReadBytes("nope"));
    }
}

public sealed class WorkItemTests
{
    [Fact]
    public void APooledJobReturnsItsResult()
    {
        using var queue = new WorkQueue(2, "test");
        WorkItem<int> item = queue.Enqueue(() => 21 * 2);
        Assert.Equal(42, item.Result);
        Assert.True(item.IsComplete);
        Assert.False(item.Failed);
    }

    [Fact]
    public void APooledJobSurfacesItsException()
    {
        using var queue = new WorkQueue(1, "test");
        WorkItem<int> item = queue.Enqueue<int>(() => throw new InvalidOperationException("boom"));
        Assert.True(item.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(item.Failed);
        Assert.Throws<InvalidOperationException>(() => _ = item.Result);
    }
}
