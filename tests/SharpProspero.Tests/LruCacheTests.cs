// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Memory;
using System;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class LruCacheTests
{
    [Fact]
    public void EvictsTheLeastRecentlyUsedWhenFull()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // "a" is the oldest and drops out

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out int b));
        Assert.Equal(2, b);
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void ReadingAnEntryKeepsItFromBeingEvicted()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _)); // "a" is now the most recently used
        cache.Set("c", 3);                     // so "b" is evicted instead

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Evicted_FiresWithTheDroppedEntry()
    {
        var cache = new LruCache<string, int>(1);
        (string Key, int Value)? dropped = null;
        cache.Evicted += (k, v) => dropped = (k, v);
        cache.Set("a", 10);
        cache.Set("b", 20);
        Assert.Equal(("a", 10), dropped);
    }

    [Fact]
    public void SetReplacesAndRefreshesAnExistingKey()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 99); // update "a" and make it most recently used
        cache.Set("c", 3);  // evicts "b"

        Assert.True(cache.TryGet("a", out int a));
        Assert.Equal(99, a);
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void GetOrAdd_BuildsOnceThenReadsFromCache()
    {
        var cache = new LruCache<int, string>(4);
        int calls = 0;
        string First() { calls++; return cache.GetOrAdd(7, k => $"built-{k}"); }
        Assert.Equal("built-7", First());
        Assert.Equal("built-7", First());
        Assert.Equal(2, calls);              // the local ran twice
        Assert.Equal(1, cache.Count);        // but the factory only produced one entry
    }

    [Fact]
    public void ContainsKeyDoesNotCountAsUse()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.ContainsKey("a")); // must not refresh "a"
        cache.Set("c", 3);                   // "a" is still the oldest and drops
        Assert.False(cache.ContainsKey("a"));
        Assert.True(cache.ContainsKey("b"));
    }

    [Fact]
    public void RemoveAndClear_Work()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.Remove("a"));
        Assert.False(cache.Remove("a"));
        Assert.Equal(1, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Keys_AreMostRecentlyUsedFirst()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);
        cache.TryGet("a", out _); // move "a" to the front
        Assert.Equal(new[] { "a", "c", "b" }, cache.Keys.ToArray());
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
}
