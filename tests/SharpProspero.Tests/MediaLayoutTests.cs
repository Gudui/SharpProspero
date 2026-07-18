// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;
using SharpProspero.Interop.Media;
using Xunit;

namespace SharpProspero.Tests;

// The player reads these blocks directly, so a field in the wrong place is a wrong pointer rather
// than a clean error. These lock the layout the player is built against.
public sealed unsafe class MediaLayoutTests
{
    [Fact]
    public void Blocks_MatchTheExpectedSizes()
    {
        Assert.Equal(40, sizeof(AvPlayerMemAllocator));
        Assert.Equal(40, sizeof(AvPlayerFileReplacement));
        Assert.Equal(16, sizeof(AvPlayerEventReplacement));
        Assert.Equal(120, sizeof(AvPlayerInitData));
        Assert.Equal(40, sizeof(AvPlayerFrameInfo));
        Assert.Equal(16, sizeof(AvPlayerStreamDetails));
    }

    [Theory]
    [InlineData(nameof(AvPlayerMemAllocator.ObjectPointer), 0)]
    [InlineData(nameof(AvPlayerMemAllocator.Allocate), 8)]
    [InlineData(nameof(AvPlayerMemAllocator.Deallocate), 16)]
    [InlineData(nameof(AvPlayerMemAllocator.AllocateTexture), 24)]
    [InlineData(nameof(AvPlayerMemAllocator.DeallocateTexture), 32)]
    public void Allocator_FieldsLandWhereThePlayerExpects(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<AvPlayerMemAllocator>(field));

    [Theory]
    [InlineData(nameof(AvPlayerInitData.MemoryReplacement), 0)]
    [InlineData(nameof(AvPlayerInitData.FileReplacement), 40)]
    [InlineData(nameof(AvPlayerInitData.EventReplacement), 80)]
    [InlineData(nameof(AvPlayerInitData.DebugLevel), 96)]
    [InlineData(nameof(AvPlayerInitData.BasePriority), 100)]
    [InlineData(nameof(AvPlayerInitData.NumOutputVideoFrameBuffers), 104)]
    [InlineData(nameof(AvPlayerInitData.AutoStart), 108)]
    [InlineData(nameof(AvPlayerInitData.DefaultLanguage), 112)]
    public void InitData_FieldsLandWhereThePlayerExpects(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<AvPlayerInitData>(field));

    [Theory]
    [InlineData(nameof(AvPlayerFrameInfo.Data), 0)]
    [InlineData(nameof(AvPlayerFrameInfo.TimeStamp), 16)]
    [InlineData(nameof(AvPlayerFrameInfo.Details), 24)]
    public void FrameInfo_FieldsLandWhereThePlayerExpects(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<AvPlayerFrameInfo>(field));

    [Theory]
    [InlineData(nameof(AvPlayerAudioDetails.ChannelCount), 0)]
    [InlineData(nameof(AvPlayerAudioDetails.SampleRate), 4)]
    [InlineData(nameof(AvPlayerAudioDetails.Size), 8)]
    [InlineData(nameof(AvPlayerAudioDetails.LanguageCode), 12)]
    public void AudioDetails_FieldsLandWhereThePlayerExpects(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<AvPlayerAudioDetails>(field));

    [Theory]
    [InlineData(nameof(AvPlayerVideoDetails.Width), 0)]
    [InlineData(nameof(AvPlayerVideoDetails.Height), 4)]
    [InlineData(nameof(AvPlayerVideoDetails.AspectRatio), 8)]
    public void VideoDetails_FieldsLandWhereThePlayerExpects(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<AvPlayerVideoDetails>(field));

    [Fact]
    public void InitializeData_ZeroesTheBlock()
    {
        AvPlayerInitData data;
        new System.Span<byte>(&data, sizeof(AvPlayerInitData)).Fill(0xCD);
        AvPlayer.InitializeData(&data);

        Assert.True(data.MemoryReplacement.Allocate == null);
        Assert.True(data.FileReplacement.Open == null);      // zero means the player reads the file itself
        Assert.True(data.EventReplacement.EventCallback == null);
        Assert.Equal(0u, data.BasePriority);
        Assert.Equal(0, data.NumOutputVideoFrameBuffers);
        Assert.Equal(0, data.AutoStart);
        Assert.True(data.DefaultLanguage == null);
    }
}
