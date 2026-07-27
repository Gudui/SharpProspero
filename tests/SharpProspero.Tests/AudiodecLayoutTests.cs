// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Audio;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The decoder is handed these structures by pointer, so their sizes and field positions have to match
// the ones the service expects. The values here come from the declarations the service publishes.
public sealed unsafe class AudiodecLayoutTests
{
    [Fact]
    public void AuInfoMatchesTheDeclaredLayout()
    {
        // uint32, then a pointer aligned to eight, then uint32 with tail padding.
        Assert.Equal(24, sizeof(SceAudiodecAuInfo));
        Assert.Equal(0, (int)Marshal.OffsetOf<SceAudiodecAuInfo>(nameof(SceAudiodecAuInfo.Size)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecAuInfo>(nameof(SceAudiodecAuInfo.Address)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecAuInfo>(nameof(SceAudiodecAuInfo.Length)));
    }

    [Fact]
    public void PcmItemMatchesTheDeclaredLayout()
    {
        Assert.Equal(24, sizeof(SceAudiodecPcmItem));
        Assert.Equal(0, (int)Marshal.OffsetOf<SceAudiodecPcmItem>(nameof(SceAudiodecPcmItem.Size)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecPcmItem>(nameof(SceAudiodecPcmItem.Address)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecPcmItem>(nameof(SceAudiodecPcmItem.Length)));
    }

    [Fact]
    public void ControlIsFourPointers()
    {
        Assert.Equal(32, sizeof(SceAudiodecCtrl));
        Assert.Equal(0, (int)Marshal.OffsetOf<SceAudiodecCtrl>(nameof(SceAudiodecCtrl.Param)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecCtrl>(nameof(SceAudiodecCtrl.StreamInfo)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecCtrl>(nameof(SceAudiodecCtrl.AuInfo)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SceAudiodecCtrl>(nameof(SceAudiodecCtrl.PcmItem)));
    }

    [Fact]
    public void LayerThreeStructuresMatchTheDeclaredLayout()
    {
        Assert.Equal(8, sizeof(SceAudiodecParamMp3));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceAudiodecParamMp3>(nameof(SceAudiodecParamMp3.WordSize)));

        Assert.Equal(20, sizeof(SceAudiodecMp3Info));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceAudiodecMp3Info>(nameof(SceAudiodecMp3Info.Header)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecMp3Info>(nameof(SceAudiodecMp3Info.Crc)));
        Assert.Equal(13, (int)Marshal.OffsetOf<SceAudiodecMp3Info>(nameof(SceAudiodecMp3Info.Emphasis)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecMp3Info>(nameof(SceAudiodecMp3Info.Result)));
    }

    [Fact]
    public void AdvancedCodingStructuresMatchTheDeclaredLayout()
    {
        Assert.Equal(24, sizeof(SceAudiodecParamM4aac));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceAudiodecParamM4aac>(nameof(SceAudiodecParamM4aac.WordSize)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecParamM4aac>(nameof(SceAudiodecParamM4aac.ConfigNumber)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecParamM4aac>(nameof(SceAudiodecParamM4aac.MaxChannels)));
        Assert.Equal(20, (int)Marshal.OffsetOf<SceAudiodecParamM4aac>(nameof(SceAudiodecParamM4aac.EnableHeAac)));

        Assert.Equal(20, sizeof(SceAudiodecM4aacInfo));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceAudiodecM4aacInfo>(nameof(SceAudiodecM4aacInfo.SamplingFrequency)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceAudiodecM4aacInfo>(nameof(SceAudiodecM4aacInfo.ChannelCount)));
        Assert.Equal(16, (int)Marshal.OffsetOf<SceAudiodecM4aacInfo>(nameof(SceAudiodecM4aacInfo.Result)));
    }

    [Fact]
    public void CodecAndWordValuesMatchTheDeclaredOnes()
    {
        Assert.Equal(1u, (uint)AudiodecCodecType.At9);
        Assert.Equal(2u, (uint)AudiodecCodecType.Mp3);
        Assert.Equal(3u, (uint)AudiodecCodecType.M4Aac);

        // Two forms, not three. A third was offered under the value that means none at all, and every
        // decoder refuses it - the form is the one field they each check first.
        Assert.Equal(1, (int)AudiodecWordSize.Signed16);
        Assert.Equal(2, (int)AudiodecWordSize.Float);

        Assert.Equal(1152, Audiodec.Mp3MaxFrameSamples);
        Assert.Equal(1441, Audiodec.Mp3MaxFrameSize);
        Assert.Equal(2048, Audiodec.AacMaxFrameSamples);
        Assert.Equal(4608, Audiodec.AacMaxFrameSize);
    }

    [Fact]
    public void EveryStructureIsBlittable()
    {
        // A structure the service is handed by pointer must not need marshalling.
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SceAudiodecParamMp3>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SceAudiodecParamM4aac>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SceAudiodecM4aacInfo>());
    }
}
