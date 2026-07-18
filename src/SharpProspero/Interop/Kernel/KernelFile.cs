// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>
/// File bindings. A module opens a path with a set of flags, reads or writes at the current offset,
/// seeks, and closes the descriptor. Paths are the mounted package roots, for example
/// <c>/app0/assets/level.bin</c> for a packaged asset. Byte counts and offsets are signed 64-bit.
/// </summary>
public static unsafe partial class KernelFile
{
    private const string Lib = "libkernel";

    /// <summary>Open for reading only.</summary>
    public const int ReadOnly = 0x0000;

    /// <summary>Open for writing only.</summary>
    public const int WriteOnly = 0x0001;

    /// <summary>Open for reading and writing.</summary>
    public const int ReadWrite = 0x0002;

    /// <summary>Append writes to the end of the file.</summary>
    public const int Append = 0x0008;

    /// <summary>Create the file if it does not exist.</summary>
    public const int Create = 0x0200;

    /// <summary>Truncate the file to empty on open.</summary>
    public const int Truncate = 0x0400;

    /// <summary>Fail if the path already exists (with <see cref="Create"/>).</summary>
    public const int Exclusive = 0x0800;

    /// <summary>Fail if the path is not a directory. Required to list a directory.</summary>
    public const int Directory = 0x00020000;

    /// <summary>Seek relative to the start of the file.</summary>
    public const int SeekSet = 0;

    /// <summary>Seek relative to the current offset.</summary>
    public const int SeekCurrent = 1;

    /// <summary>Seek relative to the end of the file.</summary>
    public const int SeekEnd = 2;

    /// <summary>Opens <paramref name="path"/> (a null-terminated UTF-8 string) with <paramref name="flags"/>.</summary>
    /// <returns>A non-negative descriptor on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceKernelOpen(byte* path, int flags, ushort mode);

    /// <summary>Closes a descriptor.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelClose(int descriptor);

    /// <summary>Reads up to <paramref name="length"/> bytes into <paramref name="buffer"/>.</summary>
    /// <returns>The number of bytes read (zero at end of file), or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial long sceKernelRead(int descriptor, void* buffer, nuint length);

    /// <summary>Writes up to <paramref name="length"/> bytes from <paramref name="buffer"/>.</summary>
    /// <returns>The number of bytes written, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial long sceKernelWrite(int descriptor, void* buffer, nuint length);

    /// <summary>Moves the file offset and returns the new offset, or a negative error code.</summary>
    [LibraryImport(Lib)]
    public static partial long sceKernelLseek(int descriptor, long offset, int whence);

    /// <summary>
    /// Reads directory entries from a descriptor opened with <see cref="Directory"/> into
    /// <paramref name="buffer"/>, as a packed run of variable-length records.
    /// </summary>
    /// <returns>Bytes written (zero at the end of the directory), or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceKernelGetdents(int descriptor, byte* buffer, int length);

    /// <summary>Creates the directory at <paramref name="path"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelMkdir(byte* path, ushort mode);

    /// <summary>Removes the empty directory at <paramref name="path"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelRmdir(byte* path);

    /// <summary>Removes the file at <paramref name="path"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelUnlink(byte* path);

    /// <summary>Renames or moves <paramref name="from"/> to <paramref name="to"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelRename(byte* from, byte* to);

    /// <summary>Sets the length of the file at <paramref name="path"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelTruncate(byte* path, long length);

    /// <summary>Reports whether <paramref name="path"/> can be reached.</summary>
    /// <returns>Zero when it is reachable, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceKernelCheckReachability(byte* path);
}
