// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Audio;

/// <summary>The purpose of an input port, which selects how the system routes and processes the audio.</summary>
public enum AudioInType
{
    /// <summary>Voice chat, with the system's voice processing applied.</summary>
    VoiceChat = 0,

    /// <summary>General capture, for recording or analysis.</summary>
    General = 1,
}

/// <summary>The sample format of an input port.</summary>
public enum AudioInFormat : uint
{
    /// <summary>16-bit signed, one channel.</summary>
    S16Mono = 0x01,

    /// <summary>16-bit signed, two channels, interleaved.</summary>
    S16Stereo = 0x02,

    /// <summary>32-bit float, one channel.</summary>
    FloatMono = 0x11,

    /// <summary>32-bit float, two channels, interleaved.</summary>
    FloatStereo = 0x12,
}

/// <summary>
/// Audio-input bindings. Open a capture port for a signed-in user with a grain (samples per block), a
/// sample rate and a format, then pull one block of samples at a time. Each input call blocks until a
/// block is captured, which paces the caller to the audio clock, the same shape as audio output.
/// </summary>
public static unsafe partial class AudioIn
{
    private const string Lib = "libSceAudioIn";

    /// <summary>The 16 kHz sample rate, the default for voice.</summary>
    public const uint Freq16k = 16000;

    /// <summary>The 48 kHz sample rate, for higher-quality capture.</summary>
    public const uint Freq48k = 48000;

    /// <summary>The 128-sample grain.</summary>
    public const uint Grain128 = 128;

    /// <summary>The 256-sample grain.</summary>
    public const uint Grain256 = 256;

    /// <summary>Opens an input port. Returns a non-negative handle or a negative error code.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioInOpen(int userId, int type, int index, uint length, uint freq, uint param);

    /// <summary>Captures one block of samples into <paramref name="dest"/>; blocks until it is filled.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioInInput(int handle, void* dest);

    /// <summary>Reports whether the input is silent (muted at the hardware or by the system).</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioInGetSilentState(int handle);

    /// <summary>Closes a port.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAudioInClose(int handle);
}
