// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SharpProspero.Interop;
using SharpProspero.Interop.Kernel;

namespace SharpProspero.Storage;

/// <summary>What a directory entry refers to.</summary>
public enum FileEntryType
{
    /// <summary>The kind is not reported; stat the path to find out.</summary>
    Unknown = 0,

    /// <summary>A named pipe.</summary>
    Fifo = 1,

    /// <summary>A character device.</summary>
    Character = 2,

    /// <summary>A directory.</summary>
    Directory = 4,

    /// <summary>A block device.</summary>
    Block = 6,

    /// <summary>A regular file.</summary>
    File = 8,

    /// <summary>A symbolic link.</summary>
    SymbolicLink = 10,

    /// <summary>A socket.</summary>
    Socket = 12,
}

/// <summary>One entry in a directory: its name and what it refers to.</summary>
public readonly struct DirectoryEntry
{
    /// <summary>The entry name, without a path.</summary>
    public string Name { get; init; }

    /// <summary>What the entry refers to.</summary>
    public FileEntryType Type { get; init; }

    /// <summary>True when the entry is a directory.</summary>
    public bool IsDirectory => Type == FileEntryType.Directory;

    /// <summary>True when the entry is a regular file.</summary>
    public bool IsFile => Type == FileEntryType.File;

    /// <summary>The entry name.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Browses and changes files and directories by path. Paths are the mounted roots, for example
/// <c>/app0</c> for a module's own read-only files. Use <see cref="PackageFile"/> for the simple case
/// of reading a bundled asset; use this to list a directory or to write to a writable mount.
/// </summary>
public static unsafe class FileSystem
{
    // A directory read returns a packed run of variable-length records. Each record starts with a
    // 4-byte file number, a 2-byte record length, a 1-byte type and a 1-byte name length, then the
    // name; the record length steps to the next entry.
    private const int RecordHeaderSize = 8;
    private const int ReadBufferSize = 8192;

    /// <summary>Lists the entries of the directory at <paramref name="path"/>, excluding . and .. .</summary>
    /// <exception cref="ProsperoException">The directory could not be opened or read.</exception>
    public static IReadOnlyList<DirectoryEntry> EnumerateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int fd = OpenPath(path, KernelFile.ReadOnly | KernelFile.Directory, 0);
        try
        {
            var entries = new List<DirectoryEntry>();
            byte[] buffer = new byte[ReadBufferSize];
            fixed (byte* p = buffer)
            {
                while (true)
                {
                    int read = KernelFile.sceKernelGetdents(fd, p, buffer.Length);
                    if (read < 0)
                        throw new ProsperoException(nameof(KernelFile.sceKernelGetdents), read);
                    if (read == 0)
                        break;
                    DecodeEntries(buffer.AsSpan(0, read), entries);
                }
            }
            return entries;
        }
        finally
        {
            KernelFile.sceKernelClose(fd);
        }
    }

    /// <summary>
    /// Decodes a run of directory records into <paramref name="entries"/>, skipping the . and ..
    /// entries. Stops at the first record that does not fit the buffer.
    /// </summary>
    public static void DecodeEntries(ReadOnlySpan<byte> buffer, List<DirectoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int offset = 0;
        while (offset + RecordHeaderSize <= buffer.Length)
        {
            int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 4));
            // A record shorter than its header, or one that runs past the buffer, ends the run; without
            // this the zero-length case would never advance.
            if (recordLength < RecordHeaderSize || offset + recordLength > buffer.Length)
                return;

            var type = (FileEntryType)buffer[offset + 6];
            int nameLength = Math.Min(buffer[offset + 7], recordLength - RecordHeaderSize);
            string name = Encoding.UTF8.GetString(buffer.Slice(offset + RecordHeaderSize, nameLength));
            if (name.Length > 0 && name != "." && name != "..")
                entries.Add(new DirectoryEntry { Name = name, Type = type });
            offset += recordLength;
        }
    }

    /// <summary>The size in bytes of the file at <paramref name="path"/>.</summary>
    public static long GetFileSize(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int fd = OpenPath(path, KernelFile.ReadOnly, 0);
        try
        {
            long size = KernelFile.sceKernelLseek(fd, 0, KernelFile.SeekEnd);
            if (size < 0)
                throw new ProsperoException(nameof(KernelFile.sceKernelLseek), (int)size);
            return size;
        }
        finally
        {
            KernelFile.sceKernelClose(fd);
        }
    }

    /// <summary>True when <paramref name="path"/> can be reached.</summary>
    public static bool Exists(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] owned = ToNullTerminated(path);
        fixed (byte* p = owned)
            return SceResult.Succeeded(KernelFile.sceKernelCheckReachability(p));
    }

    /// <summary>Creates the directory at <paramref name="path"/>.</summary>
    public static void CreateDirectory(string path, ushort mode = 0x1FF)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] owned = ToNullTerminated(path);
        fixed (byte* p = owned)
            SceResult.ThrowIfFailed(KernelFile.sceKernelMkdir(p, mode), nameof(KernelFile.sceKernelMkdir));
    }

    /// <summary>Removes the empty directory at <paramref name="path"/>.</summary>
    public static void DeleteDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] owned = ToNullTerminated(path);
        fixed (byte* p = owned)
            SceResult.ThrowIfFailed(KernelFile.sceKernelRmdir(p), nameof(KernelFile.sceKernelRmdir));
    }

    /// <summary>Removes the file at <paramref name="path"/>.</summary>
    public static void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] owned = ToNullTerminated(path);
        fixed (byte* p = owned)
            SceResult.ThrowIfFailed(KernelFile.sceKernelUnlink(p), nameof(KernelFile.sceKernelUnlink));
    }

    /// <summary>Renames or moves <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static void Move(string from, string to)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);
        byte[] a = ToNullTerminated(from);
        byte[] b = ToNullTerminated(to);
        fixed (byte* pa = a)
        fixed (byte* pb = b)
            SceResult.ThrowIfFailed(KernelFile.sceKernelRename(pa, pb), nameof(KernelFile.sceKernelRename));
    }

    /// <summary>Reads the whole file at <paramref name="path"/>.</summary>
    public static byte[] ReadAllBytes(string path) => PackageFile.ReadAllBytes(path);

    /// <summary>Writes <paramref name="data"/> to <paramref name="path"/>, replacing any existing file.</summary>
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int fd = OpenPath(path, KernelFile.WriteOnly | KernelFile.Create | KernelFile.Truncate, 0x1B6);
        try
        {
            int written = 0;
            fixed (byte* p = data)
            {
                while (written < data.Length)
                {
                    long n = KernelFile.sceKernelWrite(fd, p + written, (nuint)(data.Length - written));
                    if (n < 0)
                        throw new ProsperoException(nameof(KernelFile.sceKernelWrite), (int)n);
                    if (n == 0)
                        break;
                    written += (int)n;
                }
            }
        }
        finally
        {
            KernelFile.sceKernelClose(fd);
        }
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> as UTF-8.</summary>
    public static void WriteAllText(string path, string text)
        => WriteAllBytes(path, Encoding.UTF8.GetBytes(text ?? string.Empty));

    private static int OpenPath(string path, int flags, ushort mode)
    {
        byte[] owned = ToNullTerminated(path);
        int fd;
        fixed (byte* p = owned)
            fd = KernelFile.sceKernelOpen(p, flags, mode);
        return SceResult.ThrowIfFailed(fd, nameof(KernelFile.sceKernelOpen));
    }

    private static byte[] ToNullTerminated(string path)
    {
        int count = Encoding.UTF8.GetByteCount(path);
        byte[] buffer = new byte[count + 1];
        Encoding.UTF8.GetBytes(path, buffer);
        return buffer;
    }
}
