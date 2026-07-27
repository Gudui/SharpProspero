// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Audio;
using System;

namespace SharpProspero.Audio;

/// <summary>
/// A stereo 16-bit audio-output port. Open it once, then push one block of interleaved samples per
/// call; each push blocks until the block plays, which paces the caller to the audio clock. Fill a
/// buffer of <see cref="SamplesPerBlock"/> shorts (left, right, left, right, …) and pass it to
/// <see cref="Output"/>.
/// </summary>
public sealed unsafe class AudioOutDevice : IDisposable
{
    private const int Channels = 2;

    private readonly int _handle;
    private readonly uint _grain;
    private bool _disposed;

    private AudioOutDevice(int handle, uint grain)
    {
        _handle = handle;
        _grain = grain;
    }

    /// <summary>Samples in one output block: the grain times the two channels.</summary>
    public int SamplesPerBlock => (int)_grain * Channels;

    /// <summary>Frames (per-channel samples) in one output block.</summary>
    public uint Grain => _grain;

    /// <summary>
    /// Opens the main output as a stereo 16-bit port. <paramref name="grain"/> is the samples per
    /// block, in whole multiples of 256 from 256 to 2048; <paramref name="sampleRate"/> is the rate in
    /// hertz, which the main output takes at 48000 or 192000 and at nothing else.
    /// </summary>
    /// <exception cref="ProsperoException">Opening the port failed.</exception>
    public static AudioOutDevice OpenStereo(uint grain = AudioOut.MinGrain, uint sampleRate = 48000, int userId = SceUser.System)
    {
        // Both of these are refused by the output rather than adjusted, and the refusal arrives as a
        // number from a call several layers down. Checking them here says which argument was wrong.
        if (grain < AudioOut.MinGrain || grain > AudioOut.MaxGrain || grain % AudioOut.MinGrain != 0)
            throw new ArgumentOutOfRangeException(nameof(grain),
                "Grain must be a whole multiple of 256 samples, from 256 to 2048.");
        if (sampleRate is not (48000 or 192000))
            throw new ArgumentOutOfRangeException(nameof(sampleRate),
                "The main output takes 48000 or 192000 hertz. Resample before playing anything else.");

        // Initialization is tolerant: a module may find the subsystem already started, which the
        // return code reports without preventing a port from opening.
        AudioOut.sceAudioOutInit();

        int handle = AudioOut.sceAudioOutOpen(
            userId, (int)AudioOutPortType.Main, 0, grain, sampleRate, (uint)AudioOutFormat.S16Stereo);
        SceResult.ThrowIfFailed(handle, nameof(AudioOut.sceAudioOutOpen));

        var device = new AudioOutDevice(handle, grain);
        device.SetVolume(AudioOut.Volume0Db);
        return device;
    }

    /// <summary>
    /// Outputs one block of interleaved stereo samples and blocks until it plays. The span must hold
    /// at least <see cref="SamplesPerBlock"/> shorts.
    /// </summary>
    /// <exception cref="ProsperoException">The output call failed.</exception>
    public void Output(ReadOnlySpan<short> samples)
    {
        if (samples.Length < SamplesPerBlock)
            throw new ArgumentException($"A block needs at least {SamplesPerBlock} samples.", nameof(samples));
        fixed (short* p = samples)
            SceResult.ThrowIfFailed(AudioOut.sceAudioOutOutput(_handle, p), nameof(AudioOut.sceAudioOutOutput));
    }

    /// <summary>Sets both channels to <paramref name="volume"/>, from 0 (silent) to <see cref="AudioOut.Volume0Db"/>.</summary>
    /// <exception cref="ProsperoException">The volume call failed.</exception>
    public void SetVolume(int volume)
    {
        int level = Math.Clamp(volume, 0, AudioOut.Volume0Db);
        // Size the buffer to the maximum channel count so the service cannot read past it regardless of
        // how it indexes the array; only the selected (left, right) entries are applied.
        int* channels = stackalloc int[8];
        for (int i = 0; i < 8; i++)
            channels[i] = level;
        SceResult.ThrowIfFailed(
            AudioOut.sceAudioOutSetVolume(_handle, AudioOut.VolumeFlagLeft | AudioOut.VolumeFlagRight, channels),
            nameof(AudioOut.sceAudioOutSetVolume));
    }

    /// <summary>Closes the port.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        AudioOut.sceAudioOutClose(_handle);
    }
}
