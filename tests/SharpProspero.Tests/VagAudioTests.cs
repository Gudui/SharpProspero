using SharpProspero.Audio;
using SharpProspero.Interop;
using System;
using System.Buffers.Binary;
using Xunit;

namespace SharpProspero.Tests;

public sealed class VagAudioTests
{
    // Where a clip's blocks begin.
    private const int HeaderBytes = 48;

    private static PcmAudio Sine(int samples, int rate, int channels, double amplitude, double freq)
    {
        short[] data = new short[samples * channels];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(amplitude * Math.Sin(2 * Math.PI * freq * i / rate));
            for (int c = 0; c < channels; c++)
                data[i * channels + c] = v;
        }
        return new PcmAudio(data, rate, channels);
    }

    [Fact]
    public void Encode_EndsWithATerminatorBlockPerChannel()
    {
        // A file says where its sound stops with a block per channel carrying the end mark and a fixed
        // fill, and marks the last block that does carry sound as the last. Every clip the platform
        // ships is built this way; one without it plays on into whatever follows it in memory.
        foreach (int channels in (int[])[1, 2])
        {
            byte[] vag = VagAudio.Encode(Sine(280, 48000, channels, 8000, 440));
            int blocks = (vag.Length - HeaderBytes) / 16;
            for (int c = 0; c < channels; c++)
            {
                ReadOnlySpan<byte> terminator = vag.AsSpan(vag.Length - (channels - c) * 16, 16);
                Assert.Equal(0, terminator[0]);
                Assert.Equal(7, terminator[1]);
                foreach (byte b in terminator[2..])
                    Assert.Equal(0x77, b);

                // The block before it, for the same channel, is marked as the last carrying sound.
                Assert.Equal(1, vag[HeaderBytes + (blocks - 2 * channels + c) * 16 + 1]);
            }
        }
    }

    [Fact]
    public void Decode_StopsAtTheTerminatorRatherThanPlayingIt()
    {
        // Decoding the terminator turns its fill into twenty-eight samples of a full-scale step, which
        // is a click at the end of every clip. The decoder stops where the file says the sound does.
        PcmAudio original = Sine(280, 48000, 1, 8000, 440);
        byte[] vag = VagAudio.Encode(original);
        PcmAudio decoded = VagAudio.Decode(vag);

        int dataBlocks = (vag.Length - HeaderBytes) / 16 - 2;   // less the leading silence and the terminator
        Assert.Equal(dataBlocks * 28, decoded.Samples.Length);
    }

    [Fact]
    public void Encode_WritesTheHeaderTheDecoderExpects()
    {
        byte[] vag = VagAudio.Encode(Sine(280, 48000, 1, 8000, 440), "clip");
        Assert.Equal((byte)'V', vag[0]);
        Assert.Equal((byte)'p', vag[3]);
        Assert.Equal(48000u, BinaryPrimitives.ReadUInt32BigEndian(vag.AsSpan(16))); // sample rate, big-endian
        Assert.Equal(1, vag[30]);                                                   // channels (ucNumChannels)
        Assert.Equal((byte)'c', vag[32]);                                           // name (ucName)
    }

    [Fact]
    public void EncodeDecode_RoundTripsAMonoSineClosely()
    {
        PcmAudio original = Sine(280, 48000, 1, 8000, 440);
        PcmAudio decoded = VagAudio.Decode(VagAudio.Encode(original));

        Assert.Equal(48000, decoded.SampleRate);
        Assert.Equal(1, decoded.Channels);
        Assert.True(decoded.Samples.Length >= original.Samples.Length);

        double error = 0;
        for (int i = 0; i < original.Samples.Length; i++)
            error += Math.Abs(original.Samples[i] - decoded.Samples[i]);
        error /= original.Samples.Length;
        Assert.True(error < 400, $"average absolute error {error} should be small for a smooth signal");
    }

    [Fact]
    public void EncodeDecode_RoundTripsStereo()
    {
        PcmAudio original = Sine(280, 44100, 2, 6000, 330);
        PcmAudio decoded = VagAudio.Decode(VagAudio.Encode(original));
        Assert.Equal(2, decoded.Channels);
        Assert.Equal(44100, decoded.SampleRate);
        Assert.True(decoded.Samples.Length >= original.Samples.Length);
    }

    [Fact]
    public void Decode_RejectsNonVag()
    {
        Assert.Throws<ProsperoException>(() => VagAudio.Decode([1, 2, 3, 4]));
        Assert.Throws<ProsperoException>(() => VagAudio.Decode(new byte[100]));
    }
}
