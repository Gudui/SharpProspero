// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Audio;
using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Audio;

/// <summary>How much of a decode call's input was used and how much sound came out of it.</summary>
/// <param name="BytesConsumed">Compressed bytes the decoder took from the input.</param>
/// <param name="BytesProduced">Sample bytes written to the output.</param>
public readonly record struct AudioDecodeResult(int BytesConsumed, int BytesProduced);

/// <summary>
/// Decodes compressed audio into the samples an audio port takes. Hand it a run of bytes from a file
/// and it finds the frame, reports how much it used, and writes the sound; repeat from the new
/// position to play a whole track. Pair it with <see cref="AudioOutDevice"/> to hear the result.
/// </summary>
/// <remarks>
/// Load the codec's system module before creating a decoder
/// (<c>SystemModule.Load(SystemModuleId.AudioDec)</c>), and dispose the decoder when finished. The
/// settings and the reported stream details live in unmanaged memory the decoder owns, so a decode
/// call allocates nothing. It is not thread-safe; use one per thread.
/// </remarks>
/// <example>
/// <code>
/// using var decoder = AudioDecoder.CreateMp3();
/// var pcm = new byte[decoder.SuggestedOutputSize];
/// int read = 0;
/// while (read &lt; file.Length)
/// {
///     AudioDecodeResult step = decoder.Decode(file.AsSpan(read), pcm);
///     if (step.BytesConsumed == 0) break;
///     read += step.BytesConsumed;
///     device.Output(MemoryMarshal.Cast&lt;byte, short&gt;(pcm.AsSpan(0, step.BytesProduced)));
/// }
/// </code>
/// </example>
public sealed unsafe class AudioDecoder : IDisposable
{
    private readonly AudiodecCodecType _codec;
    private readonly int _handle;
    private readonly void* _block;
    private readonly SceAudiodecCtrl* _ctrl;
    private readonly SceAudiodecAuInfo* _au;
    private readonly SceAudiodecPcmItem* _pcm;
    private readonly void* _param;
    private readonly void* _info;
    private bool _disposed;

    private AudioDecoder(
        AudiodecCodecType codec, int handle, void* block, SceAudiodecCtrl* ctrl,
        SceAudiodecAuInfo* au, SceAudiodecPcmItem* pcm, void* param, void* info, int suggestedOutputSize)
    {
        _codec = codec;
        _handle = handle;
        _block = block;
        _ctrl = ctrl;
        _au = au;
        _pcm = pcm;
        _param = param;
        _info = info;
        SuggestedOutputSize = suggestedOutputSize;
    }

    /// <summary>A comfortable output size for one decode call, in bytes.</summary>
    public int SuggestedOutputSize { get; }

    /// <summary>Samples per second the decoder last reported, or zero before the first frame.</summary>
    public int SampleRate { get; private set; }

    /// <summary>How many channels the decoder last reported, or zero before the first frame.</summary>
    public int ChannelCount { get; private set; }

    /// <summary>
    /// Creates a decoder for MPEG-1/2 Audio Layer III writing signed 16-bit samples, the form an audio
    /// port takes.
    /// </summary>
    /// <exception cref="ProsperoException">The library or the decoder could not be created.</exception>
    public static AudioDecoder CreateMp3(AudiodecWordSize wordSize = AudiodecWordSize.Signed16)
    {
        int output = Audiodec.Mp3MaxFrameSamples * Audiodec.Mp3MaxChannels * BytesPerSample(wordSize);
        return Create(
            AudiodecCodecType.Mp3, sizeof(SceAudiodecParamMp3), sizeof(SceAudiodecMp3Info), output,
            param =>
            {
                SceAudiodecParamMp3* p = (SceAudiodecParamMp3*)param;
                p->Size = (uint)sizeof(SceAudiodecParamMp3);
                p->WordSize = (int)wordSize;
            },
            info => ((SceAudiodecMp3Info*)info)->Size = (uint)sizeof(SceAudiodecMp3Info));
    }

    /// <summary>
    /// Creates a decoder for MPEG-4 Advanced Audio Coding. <paramref name="selfDescribingFrames"/>
    /// suits a stream whose frames carry their own header, which is the usual case for a file.
    /// </summary>
    /// <exception cref="ProsperoException">The library or the decoder could not be created.</exception>
    public static AudioDecoder CreateAac(
        int maxChannels = 2,
        bool selfDescribingFrames = true,
        bool highEfficiency = false,
        AudiodecWordSize wordSize = AudiodecWordSize.Signed16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChannels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxChannels, Audiodec.AacMaxChannels);

        int output = Audiodec.AacMaxFrameSamples * maxChannels * BytesPerSample(wordSize);
        return Create(
            AudiodecCodecType.M4Aac, sizeof(SceAudiodecParamM4aac), sizeof(SceAudiodecM4aacInfo), output,
            param =>
            {
                SceAudiodecParamM4aac* p = (SceAudiodecParamM4aac*)param;
                p->Size = (uint)sizeof(SceAudiodecParamM4aac);
                p->WordSize = (int)wordSize;
                p->ConfigNumber = selfDescribingFrames ? 1u : 2u;
                p->SamplingFrequencyIndex = 4;   // 44.1 kHz, used only for raw blocks
                p->MaxChannels = (uint)maxChannels;
                p->EnableHeAac = highEfficiency ? 1u : 0u;
            },
            info => ((SceAudiodecM4aacInfo*)info)->Size = (uint)sizeof(SceAudiodecM4aacInfo));
    }

    private static AudioDecoder Create(
        AudiodecCodecType codec, int paramSize, int infoSize, int suggestedOutput,
        Action<IntPtr> fillParam, Action<IntPtr> fillInfo)
    {
        SceResult.ThrowIfFailed(Audiodec.sceAudiodecInitLibrary(codec), nameof(Audiodec.sceAudiodecInitLibrary));

        // One block holds every structure the decoder points at, so its lifetime matches this object
        // and a decode call touches no managed allocation.
        int total = sizeof(SceAudiodecCtrl) + sizeof(SceAudiodecAuInfo) + sizeof(SceAudiodecPcmItem) + paramSize + infoSize;
        void* block = NativeMemory.AllocZeroed((nuint)total);
        try
        {
            byte* at = (byte*)block;
            SceAudiodecCtrl* ctrl = (SceAudiodecCtrl*)at; at += sizeof(SceAudiodecCtrl);
            SceAudiodecAuInfo* au = (SceAudiodecAuInfo*)at; at += sizeof(SceAudiodecAuInfo);
            SceAudiodecPcmItem* pcm = (SceAudiodecPcmItem*)at; at += sizeof(SceAudiodecPcmItem);
            void* param = at; at += paramSize;
            void* info = at;

            au->Size = (uint)sizeof(SceAudiodecAuInfo);
            pcm->Size = (uint)sizeof(SceAudiodecPcmItem);
            fillParam((IntPtr)param);
            fillInfo((IntPtr)info);

            ctrl->Param = param;
            ctrl->StreamInfo = info;
            ctrl->AuInfo = au;
            ctrl->PcmItem = pcm;

            int handle = Audiodec.sceAudiodecCreateDecoder(ctrl, codec);
            SceResult.ThrowIfFailed(handle, nameof(Audiodec.sceAudiodecCreateDecoder));
            return new AudioDecoder(codec, handle, block, ctrl, au, pcm, param, info, suggestedOutput);
        }
        catch
        {
            NativeMemory.Free(block);
            Audiodec.sceAudiodecTermLibrary(codec);
            throw;
        }
    }

    /// <summary>
    /// Decodes from the front of <paramref name="input"/> into <paramref name="output"/>. The decoder
    /// finds the frame itself, so advance the read position by the reported consumed count and call
    /// again. A consumed count of zero means no complete frame was found and more input is needed.
    /// </summary>
    /// <exception cref="ProsperoException">The stream could not be decoded.</exception>
    public AudioDecodeResult Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (input.IsEmpty || output.IsEmpty)
            return new AudioDecodeResult(0, 0);

        fixed (byte* pIn = input)
        fixed (byte* pOut = output)
        {
            _au->Address = pIn;
            _au->Length = (uint)input.Length;
            _pcm->Address = pOut;
            _pcm->Length = (uint)output.Length;

            SceResult.ThrowIfFailed(Audiodec.sceAudiodecDecode(_handle, _ctrl), nameof(Audiodec.sceAudiodecDecode));
        }

        ReadStreamInfo();
        return new AudioDecodeResult((int)_au->Length, (int)_pcm->Length);
    }

    /// <summary>
    /// Drops what the decoder was carrying between frames. Call this after seeking so the first frame
    /// at the new position is not blended with the one before it.
    /// </summary>
    /// <exception cref="ProsperoException">The decoder could not be cleared.</exception>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceResult.ThrowIfFailed(Audiodec.sceAudiodecClearContext(_handle), nameof(Audiodec.sceAudiodecClearContext));
    }

    // The codec reports what it found in its own structure, so the rate and channel count are read
    // from whichever one this decoder was built with.
    private void ReadStreamInfo()
    {
        if (_codec == AudiodecCodecType.M4Aac)
        {
            SceAudiodecM4aacInfo* aac = (SceAudiodecM4aacInfo*)_info;
            SampleRate = (int)aac->SamplingFrequency;
            ChannelCount = (int)aac->ChannelCount;
        }
    }

    private static int BytesPerSample(AudiodecWordSize wordSize) => wordSize switch
    {
        AudiodecWordSize.Signed16 => 2,
        _ => 4,
    };

    /// <summary>Destroys the decoder and stops the library for its form.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Audiodec.sceAudiodecDeleteDecoder(_handle);
        NativeMemory.Free(_block);
        Audiodec.sceAudiodecTermLibrary(_codec);
    }
}
