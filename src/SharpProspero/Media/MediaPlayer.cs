// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SharpProspero.Interop;
using SharpProspero.Interop.Media;

namespace SharpProspero.Media;

/// <summary>One decoded audio frame: the samples, when they play, and how they are laid out.</summary>
public readonly ref struct AudioFrame
{
    /// <summary>The interleaved 16-bit samples.</summary>
    public ReadOnlySpan<short> Samples { get; init; }

    /// <summary>When the frame plays, in milliseconds.</summary>
    public ulong TimeStamp { get; init; }

    /// <summary>How many channels the samples interleave.</summary>
    public int ChannelCount { get; init; }

    /// <summary>Samples per second.</summary>
    public int SampleRate { get; init; }
}

/// <summary>
/// Plays a media file. Open it for a path, start it, then pull decoded audio frames while it stays
/// active and push them at an audio port. The player decodes on its own threads and calls back for
/// memory, which this type supplies.
/// </summary>
/// <example>
/// <code>
/// using var player = MediaPlayer.Open("/app0/movie.mp4");
/// using var audio = AudioOutDevice.OpenStereo();
/// player.Start();
/// while (player.IsActive)
/// {
///     if (player.TryGetAudioFrame(out AudioFrame frame))
///         audio.Output(frame.Samples);
/// }
/// </code>
/// </example>
public sealed unsafe class MediaPlayer : IDisposable
{
    private void* _handle;
    private bool _disposed;

    private MediaPlayer(void* handle) => _handle = handle;

    /// <summary>
    /// Starts a player for the file at <paramref name="path"/>. The player reads the file itself, so
    /// the path must be one it can reach.
    /// </summary>
    /// <param name="path">Absolute path of the media file.</param>
    /// <param name="basePriority">Player thread priority; zero takes the default.</param>
    /// <exception cref="ProsperoException">The player would not start or would not take the source.</exception>
    public static MediaPlayer Open(string path, uint basePriority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        AvPlayerInitData data;
        AvPlayer.InitializeData(&data);
        data.MemoryReplacement.Allocate = &AllocateGeneral;
        data.MemoryReplacement.Deallocate = &DeallocateGeneral;
        data.MemoryReplacement.AllocateTexture = &AllocateGeneral;
        data.MemoryReplacement.DeallocateTexture = &DeallocateGeneral;
        data.BasePriority = basePriority;
        data.AutoStart = 0;

        void* handle = AvPlayer.sceAvPlayerInit(&data);
        if (handle == null)
            throw new ProsperoException(nameof(AvPlayer.sceAvPlayerInit), -1);

        var player = new MediaPlayer(handle);
        try
        {
            int byteCount = Encoding.UTF8.GetByteCount(path);
            Span<byte> buffer = byteCount < 512 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
            int written = Encoding.UTF8.GetBytes(path, buffer);
            buffer[written] = 0;
            int rc;
            fixed (byte* p = buffer)
                rc = AvPlayer.sceAvPlayerAddSource(handle, p);
            SceResult.ThrowIfFailed(rc, nameof(AvPlayer.sceAvPlayerAddSource));
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    /// <summary>Whether the player still has something to play.</summary>
    public bool IsActive => !_disposed && AvPlayer.sceAvPlayerIsActive(_handle);

    /// <summary>The playback position in milliseconds.</summary>
    public ulong Position => AvPlayer.sceAvPlayerCurrentTime(Live());

    /// <summary>Begins playback.</summary>
    public void Start() => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerStart(Live()), nameof(AvPlayer.sceAvPlayerStart));

    /// <summary>Stops playback.</summary>
    public void Stop() => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerStop(Live()), nameof(AvPlayer.sceAvPlayerStop));

    /// <summary>Pauses playback.</summary>
    public void Pause() => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerPause(Live()), nameof(AvPlayer.sceAvPlayerPause));

    /// <summary>Resumes paused playback.</summary>
    public void Resume() => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerResume(Live()), nameof(AvPlayer.sceAvPlayerResume));

    /// <summary>Sets whether the source repeats.</summary>
    public void SetLooping(bool loop)
        => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerSetLooping(Live(), loop), nameof(AvPlayer.sceAvPlayerSetLooping));

    /// <summary>Moves playback to <paramref name="milliseconds"/>.</summary>
    public void JumpTo(ulong milliseconds)
        => SceResult.ThrowIfFailed(AvPlayer.sceAvPlayerJumpToTime(Live(), milliseconds), nameof(AvPlayer.sceAvPlayerJumpToTime));

    /// <summary>
    /// Takes the next decoded audio frame. Returns false when none is ready, which is normal while the
    /// player is still decoding. The samples stay valid until the next call.
    /// </summary>
    public bool TryGetAudioFrame(out AudioFrame frame)
    {
        AvPlayerFrameInfo info;
        if (!AvPlayer.sceAvPlayerGetAudioData(Live(), &info) || info.Data == null)
        {
            frame = default;
            return false;
        }

        // The payload size is in bytes; the samples are 16-bit.
        int sampleCount = (int)(info.Details.Audio.Size / sizeof(short));
        frame = new AudioFrame
        {
            Samples = new ReadOnlySpan<short>(info.Data, sampleCount),
            TimeStamp = info.TimeStamp,
            ChannelCount = info.Details.Audio.ChannelCount,
            SampleRate = (int)info.Details.Audio.SampleRate,
        };
        return true;
    }

    private void* Live()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _handle;
    }

    /// <summary>Shuts the player down and releases it.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_handle != null)
        {
            AvPlayer.sceAvPlayerClose(_handle);
            _handle = null;
        }
    }

    // The player has no allocator of its own and calls these from its own threads, so they must not
    // touch managed state. Aligned blocks come straight from the unmanaged heap. The player expects a
    // null return when a block cannot be given, and it runs on a thread with no managed frame above
    // it, so an exception must never leave this method: it would abort the process instead of failing
    // the allocation. Every path that could throw is turned into a null return.
    [UnmanagedCallersOnly]
    private static void* AllocateGeneral(void* context, uint alignment, uint size)
    {
        try
        {
            return NativeMemory.AlignedAlloc(size, RoundUpToPowerOfTwo(alignment));
        }
        catch
        {
            return null;
        }
    }

    // The aligned allocator requires a power-of-two alignment; the player passes a plain byte count.
    private static nuint RoundUpToPowerOfTwo(uint alignment)
    {
        if (alignment <= 1)
            return 1;
        return (nuint)BitOperations.RoundUpToPowerOf2(alignment);
    }

    [UnmanagedCallersOnly]
    private static void DeallocateGeneral(void* context, void* memory)
    {
        if (memory != null)
            NativeMemory.AlignedFree(memory);
    }
}
