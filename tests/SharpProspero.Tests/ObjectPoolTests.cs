// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Memory;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class ObjectPoolTests
{
    [Fact]
    public void Rent_MakesWhenEmptyThenReusesWhatWasReturned()
    {
        int made = 0;
        var pool = new ObjectPool<List<int>>(() => { made++; return []; });

        List<int> first = pool.Rent();
        Assert.Equal(1, made);
        Assert.Equal(0, pool.IdleCount);

        pool.Return(first);
        Assert.Equal(1, pool.IdleCount);

        List<int> second = pool.Rent();
        Assert.Same(first, second);  // the returned object came back out
        Assert.Equal(1, made);       // no new allocation
    }

    [Fact]
    public void Hooks_RunAsObjectsAreRentedAndReturned()
    {
        var rented = new List<int>();
        var returned = new List<int>();
        int next = 0;
        var pool = new ObjectPool<Box>(
            factory: () => new Box(next++),
            onRent: b => rented.Add(b.Id),
            onReturn: b => returned.Add(b.Id));

        Box box = pool.Rent();
        pool.Return(box);

        Assert.Equal([0], rented);
        Assert.Equal([0], returned);
    }

    [Fact]
    public void Return_DropsObjectsPastTheRetainedLimit()
    {
        var pool = new ObjectPool<object>(() => new object(), maxRetained: 2);
        pool.Return(new object());
        pool.Return(new object());
        pool.Return(new object()); // over the limit; dropped
        Assert.Equal(2, pool.IdleCount);
    }

    [Fact]
    public void Prewarm_FillsIdleButNotPastTheRetainedLimit()
    {
        var pool = new ObjectPool<object>(() => new object(), maxRetained: 3, prewarm: 10);
        Assert.Equal(3, pool.IdleCount);
    }

    [Fact]
    public void Clear_EmptiesTheIdleSet()
    {
        var pool = new ObjectPool<object>(() => new object(), prewarm: 2);
        Assert.Equal(2, pool.IdleCount);
        pool.Clear();
        Assert.Equal(0, pool.IdleCount);
    }

    [Fact]
    public void Constructor_And_Return_RejectBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ObjectPool<object>(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<object>(() => new object(), maxRetained: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<object>(() => new object(), prewarm: -1));

        var pool = new ObjectPool<object>(() => new object());
        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
    }

    private sealed class Box(int id)
    {
        public int Id { get; } = id;
    }
}
