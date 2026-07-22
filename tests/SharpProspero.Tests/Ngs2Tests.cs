// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Audio;
using Xunit;

namespace SharpProspero.Tests;

// The engine runs on the device; off the device these pin the sizes of the structures passed across the
// boundary (a wrong layout would corrupt the engine's state) and the constants transcribed from the header.
public sealed class Ngs2Tests
{
    [Fact]
    public unsafe void ContextBufferInfoHasTheHeaderLayout()
    {
        // void* + size_t + uintptr_t[5] + uintptr_t = 8 + 8 + 40 + 8 on a 64-bit target.
        Assert.Equal(64, sizeof(SceNgs2ContextBufferInfo));
    }

    [Fact]
    public unsafe void BufferAllocatorHasTheHeaderLayout()
    {
        // two function pointers + uintptr_t.
        Assert.Equal(24, sizeof(SceNgs2BufferAllocator));
    }

    [Fact]
    public unsafe void RenderBufferInfoHasTheHeaderLayout()
    {
        // void* + size_t + uint32 + uint32.
        Assert.Equal(24, sizeof(SceNgs2RenderBufferInfo));
    }

    [Fact]
    public void WaveformTypeValuesMatchTheHeader()
    {
        Assert.Equal(0x12u, (uint)Ngs2WaveformType.PcmI16L);
        Assert.Equal(0x13u, (uint)Ngs2WaveformType.PcmI16B);
        Assert.Equal(0x18u, (uint)Ngs2WaveformType.PcmF32L);
        Assert.Equal(0x19u, (uint)Ngs2WaveformType.PcmF32B);
    }

    [Fact]
    public void RackIdValuesMatchTheHeader()
    {
        Assert.Equal(0x1000u, (uint)Ngs2RackId.Sampler);
        Assert.Equal(0x2000u, (uint)Ngs2RackId.Submixer);
        Assert.Equal(0x2001u, (uint)Ngs2RackId.Reverb);
        Assert.Equal(0x3000u, (uint)Ngs2RackId.Mastering);
    }

    [Fact]
    public void ChannelCountsMatchTheHeader()
    {
        Assert.Equal(1u, Ngs2.Channels1);
        Assert.Equal(2u, Ngs2.Channels2);
        Assert.Equal(6u, Ngs2.Channels51);
        Assert.Equal(8u, Ngs2.Channels71);
    }
}
