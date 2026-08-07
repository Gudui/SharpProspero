// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>
/// Processor masks a thread's affinity is set from. Bit <c>n</c> of a mask admits processor <c>n</c>.
/// </summary>
public static class SceKernelCpumask
{
    /// <summary>Every processor a mask can name. Value 0x1fff.</summary>
    public const ulong All = 0x1fff;

    /// <summary>The mask that admits processor <paramref name="processor"/> alone.</summary>
    public static ulong Only(int processor) => 1UL << processor;
}

/// <summary>
/// Thread bindings. A thread handle is the value the thread itself reads back from
/// <see cref="scePthreadSelf"/>; the affinity calls take that handle, so any thread holding it can
/// read or change another thread's processor set.
/// </summary>
public static unsafe partial class KernelThread
{
    private const string Lib = "libkernel";

    /// <summary>The calling thread's own handle.</summary>
    [LibraryImport(Lib)]
    public static partial nint scePthreadSelf();

    /// <summary>
    /// Confines <paramref name="thread"/> to the processors named by <paramref name="mask"/>.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadSetaffinity(nint thread, ulong mask);

    /// <summary>Reads the processors <paramref name="thread"/> is allowed to run on.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadGetaffinity(nint thread, ulong* mask);
}
