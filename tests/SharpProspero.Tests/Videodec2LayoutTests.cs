// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Kernel;
using SharpProspero.Interop.Video;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The decoder is handed these structures by pointer and each one carries its own size, so a wrong
// layout is rejected rather than silently misread. The values here come from the declarations the
// service publishes.
public sealed unsafe class Videodec2LayoutTests
{
    [Fact]
    public void DecoderConfigMatchesTheDeclaredLayout()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.ThisSize)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.ResourceType)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.CodecType)));
        Assert.Equal(28, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.MaxFrameHeight)));
        Assert.Equal(40, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.ComputeQueue)));
        Assert.Equal(48, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.CpuAffinityMask)));
        Assert.Equal(64, (int)Marshal.OffsetOf<SceVideodec2DecoderConfigInfo>(nameof(SceVideodec2DecoderConfigInfo.ExtraConfigInfo)));
        Assert.Equal(72, sizeof(SceVideodec2DecoderConfigInfo));
    }

    [Fact]
    public void DecoderMemoryMatchesTheDeclaredLayout()
    {
        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2DecoderMemoryInfo>(nameof(SceVideodec2DecoderMemoryInfo.CpuMemorySize)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceVideodec2DecoderMemoryInfo>(nameof(SceVideodec2DecoderMemoryInfo.CpuMemory)));
        Assert.Equal(40, (int)Marshal.OffsetOf<SceVideodec2DecoderMemoryInfo>(nameof(SceVideodec2DecoderMemoryInfo.CpuGpuMemorySize)));
        Assert.Equal(56, (int)Marshal.OffsetOf<SceVideodec2DecoderMemoryInfo>(nameof(SceVideodec2DecoderMemoryInfo.MaxFrameBufferSize)));
        Assert.Equal(64, (int)Marshal.OffsetOf<SceVideodec2DecoderMemoryInfo>(nameof(SceVideodec2DecoderMemoryInfo.FrameBufferAlignment)));
        Assert.Equal(72, sizeof(SceVideodec2DecoderMemoryInfo));
    }

    [Fact]
    public void InputAndOutputMatchTheDeclaredLayout()
    {
        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2InputData>(nameof(SceVideodec2InputData.AuData)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceVideodec2InputData>(nameof(SceVideodec2InputData.AuSize)));
        Assert.Equal(40, (int)Marshal.OffsetOf<SceVideodec2InputData>(nameof(SceVideodec2InputData.AttachedData)));
        Assert.Equal(48, sizeof(SceVideodec2InputData));

        // The four one-byte fields pack together before the next word.
        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2OutputInfo>(nameof(SceVideodec2OutputInfo.IsValid)));
        Assert.Equal(10, (int)Marshal.OffsetOf<SceVideodec2OutputInfo>(nameof(SceVideodec2OutputInfo.PictureCount)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SceVideodec2OutputInfo>(nameof(SceVideodec2OutputInfo.CodecType)));
        Assert.Equal(32, (int)Marshal.OffsetOf<SceVideodec2OutputInfo>(nameof(SceVideodec2OutputInfo.FrameBuffer)));
        Assert.Equal(48, (int)Marshal.OffsetOf<SceVideodec2OutputInfo>(nameof(SceVideodec2OutputInfo.FrameFormat)));
        Assert.Equal(56, sizeof(SceVideodec2OutputInfo));
    }

    [Fact]
    public void FrameBufferAndComputeStructuresMatchTheDeclaredLayout()
    {
        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2FrameBuffer>(nameof(SceVideodec2FrameBuffer.FrameBuffer)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceVideodec2FrameBuffer>(nameof(SceVideodec2FrameBuffer.FrameBufferSize)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceVideodec2FrameBuffer>(nameof(SceVideodec2FrameBuffer.IsAccepted)));

        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2ComputeMemoryInfo>(nameof(SceVideodec2ComputeMemoryInfo.CpuGpuMemorySize)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceVideodec2ComputeMemoryInfo>(nameof(SceVideodec2ComputeMemoryInfo.CpuGpuMemory)));
        Assert.Equal(24, sizeof(SceVideodec2ComputeMemoryInfo));

        Assert.Equal(8, (int)Marshal.OffsetOf<SceVideodec2ComputeConfigInfo>(nameof(SceVideodec2ComputeConfigInfo.ComputePipeId)));
        Assert.Equal(10, (int)Marshal.OffsetOf<SceVideodec2ComputeConfigInfo>(nameof(SceVideodec2ComputeConfigInfo.ComputeQueueId)));
        Assert.Equal(16, sizeof(SceVideodec2ComputeConfigInfo));
    }

    [Fact]
    public void CodecAndSettingValuesMatchTheDeclaredOnes()
    {
        Assert.Equal(1u, (uint)Videodec2CodecType.Avc);
        Assert.Equal(974921u, (uint)Videodec2CodecType.Hevc);
        Assert.Equal(2382845u, (uint)Videodec2CodecType.Vp9);

        Assert.Equal(77u, (uint)Videodec2AvcProfile.Main);
        Assert.Equal(100u, (uint)Videodec2AvcProfile.High);
        Assert.Equal(1u, (uint)Videodec2ResourceType.Compute);

        Assert.Equal(-1, Videodec2.AutoFrameSetting);
        Assert.Equal(0ul, Videodec2.InheritAffinityMask);
        Assert.Equal(-1, Videodec2.InheritThreadPriority);
    }

    [Fact]
    public void MemoryIsMadeOnAPageAndKeptToItself()
    {
        // Every region the decoder is given is page-aligned and mapped without being joined to a
        // neighbour, because the service inspects what backs it.
        Assert.Equal((nuint)16384, KernelMemory.PageSize);
        Assert.Equal((nuint)16384, Videodec2.MemoryAlignment);
        Assert.Equal(0x400000, KernelMemory.MapNoCoalesce);

        // The shared regions and the graphics-side region are different kinds.
        Assert.Equal(12, KernelMemory.MemoryTypeCachedShared);
        Assert.Equal(11, KernelMemory.MemoryTypeCached);
    }
}
