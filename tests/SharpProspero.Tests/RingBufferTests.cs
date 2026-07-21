// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Buffers;
using System;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void Add_WithinCapacity_KeepsOldestAtIndexZero()
    {
        var ring = new RingBuffer<int>(4);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        Assert.Equal(3, ring.Count);
        Assert.False(ring.IsFull);
        Assert.Equal(1, ring[0]);
        Assert.Equal(3, ring[2]);
        Assert.Equal([1, 2, 3], ring.ToArray());
    }

    [Fact]
    public void Add_WhenFull_OverwritesOldest()
    {
        var ring = new RingBuffer<int>(3);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        ring.Add(4); // drops 1
        ring.Add(5); // drops 2
        Assert.True(ring.IsFull);
        Assert.Equal(3, ring.Count);
        Assert.Equal([3, 4, 5], ring.ToArray());
        Assert.Equal(3, ring.Peek());
    }

    [Fact]
    public void Remove_TakesOldestFirst()
    {
        var ring = new RingBuffer<int>(3);
        ring.Add(1);
        ring.Add(2);
        Assert.Equal(1, ring.Remove());
        Assert.Equal(2, ring.Remove());
        Assert.True(ring.IsEmpty);
        Assert.False(ring.TryRemove(out _));
        Assert.Throws<InvalidOperationException>(() => ring.Remove());
        Assert.Throws<InvalidOperationException>(() => ring.Peek());
    }

    [Fact]
    public void Indexer_RejectsOutOfRange()
    {
        var ring = new RingBuffer<int>(3);
        ring.Add(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[-1]);
    }

    [Fact]
    public void Clear_EmptiesTheRing()
    {
        var ring = new RingBuffer<int>(3);
        ring.Add(1);
        ring.Add(2);
        ring.Clear();
        Assert.True(ring.IsEmpty);
        Assert.Empty(ring);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
}

public sealed class ByteRingTests
{
    [Fact]
    public void WriteThenRead_MovesBytesInOrder()
    {
        var ring = new ByteRing(8);
        Assert.Equal(5, ring.Write([1, 2, 3, 4, 5]));
        Assert.Equal(5, ring.Count);
        Assert.Equal(3, ring.FreeSpace);

        byte[] dest = new byte[3];
        Assert.Equal(3, ring.Read(dest));
        Assert.Equal([1, 2, 3], dest);
        Assert.Equal(2, ring.Count);

        byte[] rest = new byte[10];
        Assert.Equal(2, ring.Read(rest)); // fewer than asked when the ring runs dry
        Assert.Equal([4, 5], rest[..2]);
        Assert.True(ring.IsEmpty);
    }

    [Fact]
    public void Write_TakesOnlyWhatFits()
    {
        var ring = new ByteRing(4);
        Assert.Equal(4, ring.Write([1, 2, 3, 4, 5, 6])); // only 4 fit
        Assert.Equal(0, ring.FreeSpace);
        Assert.Equal(0, ring.Write([9])); // full: nothing taken
    }

    [Fact]
    public void ReadAndWrite_WrapAroundTheEnd()
    {
        var ring = new ByteRing(4);
        ring.Write([1, 2, 3]);
        byte[] two = new byte[2];
        ring.Read(two);            // consumes 1,2; head now at index 2
        Assert.Equal(3, ring.Write([4, 5, 6])); // 3,(wrap)4,5,6 -> fills, wrapping past the end

        byte[] all = new byte[4];
        Assert.Equal(4, ring.Read(all));
        Assert.Equal([3, 4, 5, 6], all);
    }

    [Fact]
    public void Skip_DropsOldestBytes()
    {
        var ring = new ByteRing(8);
        ring.Write([1, 2, 3, 4]);
        Assert.Equal(2, ring.Skip(2));
        byte[] dest = new byte[2];
        ring.Read(dest);
        Assert.Equal([3, 4], dest);
    }
}
