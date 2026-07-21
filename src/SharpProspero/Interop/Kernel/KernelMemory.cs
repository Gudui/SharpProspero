// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>
/// Direct-memory bindings. A module reserves a region of physical memory, then maps it into its
/// address space with a chosen CPU/GPU protection. Framebuffers and GPU-visible buffers come from
/// this path. All lengths and alignments are byte counts; addresses returned by the reserve step
/// are physical offsets, not pointers.
/// </summary>
public static unsafe partial class KernelMemory
{
    private const string Lib = "libkernel";

    /// <summary>General-purpose cached memory.</summary>
    public const int MemoryTypeCached = 11;

    /// <summary>Cached memory shared between the CPU and the GPU. The common choice for framebuffers.</summary>
    public const int MemoryTypeCachedShared = 12;

    /// <summary>CPU may read the mapping.</summary>
    public const int ProtCpuRead = 0x01;

    /// <summary>CPU write bit.</summary>
    public const int ProtCpuWrite = 0x02;

    /// <summary>CPU may read and write the mapping: the read and write bits combined (0x03).</summary>
    public const int ProtCpuReadWrite = ProtCpuRead | ProtCpuWrite;

    /// <summary>CPU may execute from the mapping.</summary>
    public const int ProtCpuExecute = 0x04;

    /// <summary>CPU may read, write and execute.</summary>
    public const int ProtCpuAll = 0x07;

    /// <summary>GPU may read the mapping.</summary>
    public const int ProtGpuRead = 0x10;

    /// <summary>GPU may write the mapping.</summary>
    public const int ProtGpuWrite = 0x20;

    /// <summary>GPU may read and write the mapping.</summary>
    public const int ProtGpuReadWrite = 0x30;

    /// <summary>Alias of <see cref="ProtGpuReadWrite"/>.</summary>
    public const int ProtGpuAll = 0x30;

    /// <summary>
    /// Mapping flag: keep this mapping to itself rather than joining it to a neighbouring one. A
    /// service that is handed a region and checks what backs it needs the mapping left as it was made.
    /// </summary>
    public const int MapNoCoalesce = 0x400000;

    /// <summary>The size of a memory page, and the alignment a direct mapping is made on.</summary>
    public const nuint PageSize = 16384;

    /// <summary>Size, in bytes, of the direct-memory pool available to the module.</summary>
    [LibraryImport(Lib)]
    public static partial nuint sceKernelGetDirectMemorySize();

    /// <summary>
    /// Reserves <paramref name="length"/> bytes of direct memory of <paramref name="memoryType"/>
    /// within [<paramref name="searchStart"/>, <paramref name="searchEnd"/>), aligned to
    /// <paramref name="alignment"/>. On success writes the physical offset to
    /// <paramref name="physicalAddressOut"/>.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelAllocateDirectMemory(
        long searchStart, long searchEnd, nuint length, nuint alignment, int memoryType, long* physicalAddressOut);

    /// <summary>
    /// Maps a reserved region starting at <paramref name="directMemoryStart"/> into the address
    /// space with <paramref name="protection"/>, writing the mapped pointer to
    /// <paramref name="address"/>.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelMapDirectMemory(
        void** address, nuint length, int protection, int flags, long directMemoryStart, nuint alignment);

    /// <summary>Releases a reserved region previously obtained from the allocate call.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelReleaseDirectMemory(long start, nuint length);
}
