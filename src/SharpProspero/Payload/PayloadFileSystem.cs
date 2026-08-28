// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload;

/// <summary>
/// FreeBSD directory entry as returned by <see cref="PayloadFileSystem.readdir"/>.
/// Layout matches the FreeBSD 12.x <c>struct dirent</c> used on this platform.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FreeBsdDirent
{
    /// <summary>Inode number of the entry.</summary>
    public ulong d_fileno;

    /// <summary>Offset to the next entry (opaque).</summary>
    public long d_off;

    /// <summary>Length of this record.</summary>
    public ushort d_reclen;

    /// <summary>File type (DT_DIR=4, DT_REG=8, etc.).</summary>
    public byte d_type;

    /// <summary>Padding.</summary>
    public byte d_pad0;

    /// <summary>Length of <see cref="d_name"/>.</summary>
    public ushort d_namlen;

    /// <summary>Padding.</summary>
    public ushort d_pad1;

    /// <summary>Entry name (NUL-terminated, up to 255 characters).</summary>
    public fixed byte d_name[256];
}

/// <summary>
/// FreeBSD <c>struct stat</c> for the <c>stat(2)</c> system call. Layout matches the FreeBSD
/// 12.x kernel's <c>struct stat</c> (not the Linux layout). The SDK <c>list_files</c> sample
/// uses <c>stat</c> to distinguish directories from regular files.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FreeBsdStat
{
    /// <summary>Device containing file.</summary>
    public ulong st_dev;

    /// <summary>Inode number.</summary>
    public ulong st_ino;

    /// <summary>Number of hard links.</summary>
    public ulong st_nlink;

    /// <summary>File mode and permissions.</summary>
    public ushort st_mode;

    /// <summary>Padding.</summary>
    private short _pad0;

    /// <summary>User ID of owner.</summary>
    public uint st_uid;

    /// <summary>Group ID of owner.</summary>
    public uint st_gid;

    /// <summary>Padding.</summary>
    private uint _pad1;

    /// <summary>Device ID (if special file).</summary>
    public ulong st_rdev;

    /// <summary>Access time (seconds).</summary>
    public long st_atim_sec;

    /// <summary>Access time (nanoseconds).</summary>
    public long st_atim_nsec;

    /// <summary>Modification time (seconds).</summary>
    public long st_mtim_sec;

    /// <summary>Modification time (nanoseconds).</summary>
    public long st_mtim_nsec;

    /// <summary>Status change time (seconds).</summary>
    public long st_ctim_sec;

    /// <summary>Status change time (nanoseconds).</summary>
    public long st_ctim_nsec;

    /// <summary>Birth time (seconds).</summary>
    public long st_birthtim_sec;

    /// <summary>Birth time (nanoseconds).</summary>
    public long st_birthtim_nsec;

    /// <summary>File size in bytes.</summary>
    public long st_size;

    /// <summary>Blocks allocated for file.</summary>
    public long st_blocks;

    /// <summary>Optimal I/O block size.</summary>
    public int st_blksize;

    /// <summary>User-defined flags.</summary>
    public uint st_flags;

    /// <summary>Generation number.</summary>
    public ulong st_gen;

    private fixed byte _spare[80];
}

/// <summary>
/// POSIX directory and file operations for a payload context. Wraps <c>opendir</c>,
/// <c>readdir</c>, <c>closedir</c>, and <c>stat</c> from <c>libc</c>, which are the
/// functions the SDK <c>list_files</c> sample uses to recursively enumerate the filesystem
/// after escaping the jail.
/// </summary>
public static unsafe partial class PayloadFileSystem
{
    private const string Lib = "libc";

    /// <summary>File type constant: directory.</summary>
    public const ushort S_IFDIR = 0x4000;

    /// <summary>File type mask.</summary>
    public const ushort S_IFMT = 0xF000;

    /// <summary>Directory entry type: directory.</summary>
    public const byte DT_DIR = 4;

    /// <summary>Directory entry type: regular file.</summary>
    public const byte DT_REG = 8;

    /// <summary>Opens a directory for reading.</summary>
    /// <param name="name">A NUL-terminated UTF-8 path.</param>
    /// <returns>A directory handle, or null on error.</returns>
    [LibraryImport(Lib)]
    public static partial void* opendir(byte* name);

    /// <summary>Reads the next entry from a directory.</summary>
    /// <param name="dirp">A directory handle from <see cref="opendir"/>.</param>
    /// <returns>A pointer to the next <see cref="FreeBsdDirent"/>, or null at the end.</returns>
    [LibraryImport(Lib)]
    public static partial FreeBsdDirent* readdir(void* dirp);

    /// <summary>Closes a directory handle.</summary>
    /// <returns>Zero on success, or -1 on error.</returns>
    [LibraryImport(Lib)]
    public static partial int closedir(void* dirp);

    /// <summary>Reads file status.</summary>
    /// <param name="path">A NUL-terminated UTF-8 path.</param>
    /// <param name="buf">A buffer to receive the status.</param>
    /// <returns>Zero on success, or -1 on error.</returns>
    [LibraryImport(Lib)]
    public static partial int stat(byte* path, FreeBsdStat* buf);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="mode"/> indicates a directory.
    /// Matches the <c>S_ISDIR</c> macro from <c>&lt;sys/stat.h&gt;</c>.
    /// </summary>
    public static bool IsDirectory(ushort mode) => (mode & S_IFMT) == S_IFDIR;
}
