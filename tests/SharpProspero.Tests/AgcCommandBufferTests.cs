// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics.Agc;
using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The command buffer's book-keeping (capacity, the write cursor, remaining space) and the register
// packing are pure host-side arithmetic, so they are tested directly. The recording calls themselves
// reach the graphics module and only run on the device, so they are not exercised here.
public sealed unsafe class AgcCommandBufferTests
{
    [Fact]
    public void RegisterPackingPutsOffsetLowAndValueHigh()
    {
        // The direct register-write packet reads the offset from the low sixteen bits and the value from
        // the high thirty-two bits of the packed word.
        Assert.Equal(0xDEADBEEF_0000_1234UL, DrawCommandBuffer.Pack(0x1234, 0xDEADBEEF));
        Assert.Equal(0UL, DrawCommandBuffer.Pack(0, 0));
        Assert.Equal(0x00000001_0000_FFFFUL, DrawCommandBuffer.Pack(0xFFFF, 1));
    }

    [Fact]
    public void RegisterPackingMasksTheOffsetToSixteenBits()
    {
        // A register offset is sixteen bits; anything above is dropped, matching the packet builder.
        Assert.Equal(0x00000000_0000_2345UL, DrawCommandBuffer.Pack(0x12345, 0));
    }

    [Fact]
    public void DmaFillContractIsSynchronizedImmediateDataToIncrementingL2Memory()
    {
        Assert.Equal((byte)0, DrawCommandBuffer.DmaFillEngine);
        Assert.Equal(3u, DrawCommandBuffer.DmaFillDestinationSelector);
        Assert.Equal((byte)0, DrawCommandBuffer.DmaFillDestinationCachePolicy);
        Assert.Equal(2u, DrawCommandBuffer.DmaFillSourceSelector);
        Assert.Equal((byte)0, DrawCommandBuffer.DmaFillSourceCachePolicy);
        Assert.Equal((byte)0, DrawCommandBuffer.DmaFillRawWait);
        Assert.Equal((byte)0, DrawCommandBuffer.DmaFillDisableWriteConfirm);
        Assert.Equal((byte)1, DrawCommandBuffer.DmaFillSync);
        Assert.Equal(0x03FF_FFFFu, DrawCommandBuffer.MaximumFillByteCount);
    }

    [Fact]
    public void DmaFillValidationAcceptsWholeAlignedRangeAndRejectsInvalidRanges()
    {
        DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1000, 4);
        DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1000, 0x03FF_FFFCu);

        Assert.Throws<ArgumentNullException>(() => DrawCommandBuffer.ValidateDmaFillArguments(null, 4));
        Assert.Throws<ArgumentException>(() => DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1001, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1000, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawCommandBuffer.ValidateDmaFillArguments((void*)0x1000, 0x0400_0000));
    }

    [Fact]
    public void FreshBufferIsEmptyWithFullCapacity()
    {
        const uint bytes = 4096;
        void* mem = NativeMemory.Alloc(bytes);
        try
        {
            using var dcb = new DrawCommandBuffer(mem, bytes);
            Assert.Equal(1024u, dcb.CapacityDwords);
            Assert.Equal(0u, dcb.SubmitSizeDwords);
            Assert.Equal(0u, dcb.SubmitSizeBytes);
            Assert.Equal(1024u, dcb.RemainingDwords);
            Assert.True(dcb.Handle != null);
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void SizeRoundsDownToWholeWords()
    {
        const uint bytes = 4098; // 1024 words plus two bytes
        void* mem = NativeMemory.Alloc(bytes);
        try
        {
            using var dcb = new DrawCommandBuffer(mem, bytes);
            Assert.Equal(1024u, dcb.CapacityDwords);
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void ResetRestoresAnEmptyBuffer()
    {
        const uint bytes = 256;
        void* mem = NativeMemory.Alloc(bytes);
        try
        {
            using var dcb = new DrawCommandBuffer(mem, bytes);
            dcb.Reset();
            Assert.Equal(0u, dcb.SubmitSizeDwords);
            Assert.Equal(dcb.CapacityDwords, dcb.RemainingDwords);
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void RejectsNullBufferAndTinySize()
    {
        Assert.Throws<ArgumentNullException>(() => new DrawCommandBuffer(null, 64));
        void* mem = NativeMemory.Alloc(4);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DrawCommandBuffer(mem, 3));
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void UseAfterDisposeThrowsObjectDisposedNotAccessViolation()
    {
        const uint bytes = 4096;
        void* mem = NativeMemory.Alloc(bytes);
        try
        {
            var dcb = new DrawCommandBuffer(mem, bytes);
            dcb.Dispose();

            // Every path that would otherwise dereference the freed state block must throw a catchable
            // ObjectDisposedException rather than fault on a null pointer.
            Assert.Throws<ObjectDisposedException>(() => dcb.SubmitSizeDwords);
            Assert.Throws<ObjectDisposedException>(() => dcb.RemainingDwords);
            Assert.Throws<ObjectDisposedException>(() => dcb.Reset());
            Assert.Throws<ObjectDisposedException>(() =>
            {
                void* _ = dcb.Handle;
            });
            Assert.Throws<ObjectDisposedException>(() => dcb.SetContextRegister(0, 0));
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        const uint bytes = 256;
        void* mem = NativeMemory.Alloc(bytes);
        try
        {
            var dcb = new DrawCommandBuffer(mem, bytes);
            dcb.Dispose();
            dcb.Dispose(); // second call must not double-free or throw
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }
}
