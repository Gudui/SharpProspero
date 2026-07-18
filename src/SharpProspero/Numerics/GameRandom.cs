// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Numerics;

/// <summary>
/// A fast pseudo-random generator for gameplay. Seed it for a reproducible sequence, or create one
/// from the entropy source for an unpredictable start. The same seed always yields the same sequence.
/// It is not for cryptographic use; take those bytes from <see cref="HardwareEntropy"/>.
/// </summary>
public sealed class GameRandom
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Creates a generator with a fixed seed, so the sequence is reproducible.</summary>
    public GameRandom(ulong seed)
    {
        // Spread the seed across the state so even a small seed starts well-mixed.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    /// <summary>Creates a generator seeded from the entropy source.</summary>
    public static GameRandom FromEntropy() => new(HardwareEntropy.NextUInt64());

    /// <summary>The next 64-bit value.</summary>
    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>The next 32-bit value.</summary>
    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    /// <summary>A value in the range 0 (inclusive) to 1 (exclusive).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>An integer from <paramref name="min"/> (inclusive) to <paramref name="max"/> (exclusive).</summary>
    public int Next(int min, int max)
    {
        if (max <= min)
            return min;
        uint range = (uint)((long)max - min);
        return min + (int)(NextUInt32() % range);
    }

    /// <summary>An integer from 0 (inclusive) to <paramref name="max"/> (exclusive).</summary>
    public int Next(int max) => Next(0, max);

    // SplitMix64 mixes a counter into a well-distributed 64-bit value.
    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
