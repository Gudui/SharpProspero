// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Audio;

/// <summary>The output port a handle drives.</summary>
public enum AudioOutPortType
{
    /// <summary>The main output.</summary>
    Main = 0,

    /// <summary>Background music.</summary>
    Bgm = 1,

    /// <summary>Voice chat.</summary>
    Voice = 2,

    /// <summary>A personal (per-user) output.</summary>
    Personal = 3,

    /// <summary>The controller speaker.</summary>
    PadSpeaker = 4,

    /// <summary>The controller vibration channel.</summary>
    Vibration = 10,

    /// <summary>An auxiliary output.</summary>
    Aux = 127,
}

/// <summary>The sample format of a port.</summary>
public enum AudioOutFormat : uint
{
    /// <summary>16-bit signed, one channel.</summary>
    S16Mono = 0,

    /// <summary>16-bit signed, two channels, interleaved.</summary>
    S16Stereo = 1,

    /// <summary>16-bit signed, eight channels.</summary>
    S16Eight = 2,

    /// <summary>32-bit float, one channel.</summary>
    FloatMono = 3,

    /// <summary>32-bit float, two channels, interleaved.</summary>
    FloatStereo = 4,

    /// <summary>32-bit float, eight channels.</summary>
    FloatEight = 5,
}

/// <summary>
/// Audio-output bindings. Initialize the subsystem, open a port for a user with a grain (samples per
/// output block), a sample rate and a format, then push one block of samples at a time. Each output
/// call blocks until the block is consumed, which paces the caller to the audio clock.
/// </summary>
public static unsafe partial class AudioOut
{
    private const string Lib = "libSceAudioOut";

    /// <summary>The 0 dB volume level (unattenuated).</summary>
    public const int Volume0Db = 0x8000;

    /// <summary>Smallest grain (samples per block).</summary>
    public const uint MinGrain = 256;

    /// <summary>Largest grain (samples per block).</summary>
    public const uint MaxGrain = 256 * 8;

    /// <summary>Left-channel select bit for the volume call.</summary>
    public const int VolumeFlagLeft = 1 << 0;

    /// <summary>Right-channel select bit for the volume call.</summary>
    public const int VolumeFlagRight = 1 << 1;

    /// <summary>Initializes the audio-output subsystem. Call once before opening a port.</summary>
    /// <returns>Zero on success, or a negative error code (including an already-initialized code).</returns>
    [LibraryImport(Lib)]
    public static partial int sceAudioOutInit();

    /// <summary>Opens an output port. Returns a non-negative handle or a negative error code.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioOutOpen(int userId, int type, int index, uint length, uint freq, uint param);

    /// <summary>Closes a port.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioOutClose(int handle);

    /// <summary>Outputs one block of samples from <paramref name="ptr"/>; blocks until it is consumed.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioOutOutput(int handle, void* ptr);

    /// <summary>Sets the per-channel volume; <paramref name="flag"/> selects the channels in <paramref name="vol"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioOutSetVolume(int handle, int flag, int* vol);
}
