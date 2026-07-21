// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpProspero.Storage;

/// <summary>One member of a tar archive: its path, whether it is a directory, and its bytes.</summary>
/// <param name="Name">The stored path, with a directory prefix already joined on.</param>
/// <param name="IsDirectory">True for a directory entry, which carries no data.</param>
/// <param name="Data">The file's bytes, or an empty array for a directory.</param>
public readonly record struct TarEntry(string Name, bool IsDirectory, byte[] Data)
{
    /// <summary>The entry's bytes decoded as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Data);
}

/// <summary>
/// Reads a tar archive — the common way to bundle many asset files into one — into its members, with no
/// system module. It understands the widely used forms (the original layout, the ustar extension with its
/// long-path prefix, and GNU long names) and returns regular files and directories; other record kinds,
/// such as links and device nodes, are skipped. The archive is not compressed, so a member's bytes are
/// returned as they are stored.
/// </summary>
/// <example>
/// <code>
/// foreach (TarEntry entry in TarArchive.Read(FileSystem.ReadAllBytes("/data/assets.tar")))
/// {
///     if (!entry.IsDirectory)
///         Install(entry.Name, entry.Data);
/// }
/// </code>
/// </example>
public static class TarArchive
{
    private const int BlockSize = 512;

    /// <summary>Reads <paramref name="data"/> and returns its regular-file and directory members in order.</summary>
    /// <exception cref="ProsperoException">The archive is malformed — a bad header checksum, a bad size, or an entry running past the end.</exception>
    public static List<TarEntry> Read(ReadOnlySpan<byte> data)
    {
        var entries = new List<TarEntry>();
        string? longName = null;
        int offset = 0;

        while (offset + BlockSize <= data.Length)
        {
            ReadOnlySpan<byte> header = data.Slice(offset, BlockSize);
            if (IsZeroBlock(header))
                break; // two zero blocks end the archive; the first is enough to stop.

            if (!ChecksumMatches(header))
                throw new ProsperoException("The tar header checksum does not match.", -1);

            long size = ParseOctal(header.Slice(124, 12));
            if (size < 0)
                throw new ProsperoException("The tar entry has an invalid size.", -1);

            byte typeFlag = header[156];
            offset += BlockSize;
            if (offset + size > data.Length)
                throw new ProsperoException("The tar entry runs past the end of the archive.", -1);

            int dataLength = (int)size;               // safe: size <= data.Length - offset, an int range.
            int paddedLength = RoundUpToBlock(dataLength);

            switch (typeFlag)
            {
                case (byte)'L': // A GNU long-name record: its data is the next entry's full path.
                    string parsed = ReadString(data.Slice(offset, dataLength));
                    longName = parsed.Length > 0 ? parsed : null; // an empty payload falls back to the header name
                    break;

                case 0:
                case (byte)'0':
                case (byte)'7': // A regular (or contiguous) file.
                    {
                        string name = longName ?? ReadName(header);
                        longName = null;
                        bool trailingSlash = name.Length > 0 && name[^1] == '/';
                        entries.Add(new TarEntry(name, trailingSlash, trailingSlash ? [] : data.Slice(offset, dataLength).ToArray()));
                        break;
                    }

                case (byte)'5': // A directory.
                    {
                        string name = longName ?? ReadName(header);
                        longName = null;
                        entries.Add(new TarEntry(name, true, []));
                        break;
                    }

                default: // Links, device nodes, and extended headers are skipped, but any long name is spent.
                    longName = null;
                    break;
            }

            // The trailing padding of the last entry may be absent from a truncated archive; when it runs
            // off the end there are no further blocks, so stop rather than advance past the buffer.
            if (paddedLength > data.Length - offset)
                break;
            offset += paddedLength;
        }

        return entries;
    }

    private static string ReadName(ReadOnlySpan<byte> header)
    {
        string name = ReadString(header[..100]);

        // ustar splits a long path into a 155-byte prefix and the 100-byte name.
        if (header.Slice(257, 5).SequenceEqual("ustar"u8))
        {
            string prefix = ReadString(header.Slice(345, 155));
            if (prefix.Length > 0)
                return prefix + "/" + name;
        }

        return name;
    }

    private static string ReadString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end < 0)
            end = field.Length;
        return Encoding.UTF8.GetString(field[..end]);
    }

    private static long ParseOctal(ReadOnlySpan<byte> field)
    {
        int start = 0;
        int end = field.Length;
        while (start < end && (field[start] == ' ' || field[start] == 0))
            start++;
        while (end > start && (field[end - 1] == ' ' || field[end - 1] == 0))
            end--;

        long value = 0;
        for (int i = start; i < end; i++)
        {
            byte b = field[i];
            if (b < '0' || b > '7')
                return -1;
            value = (value << 3) + (b - '0');
        }

        return value;
    }

    private static bool ChecksumMatches(ReadOnlySpan<byte> header)
    {
        long stored = ParseOctal(header.Slice(148, 8));
        if (stored < 0)
            return false;

        // The checksum is computed with its own eight-byte field read as spaces. Accept the unsigned sum
        // and the signed sum, since old archives used one or the other.
        long unsignedSum = 0;
        long signedSum = 0;
        for (int i = 0; i < BlockSize; i++)
        {
            int b = (i >= 148 && i < 156) ? ' ' : header[i];
            unsignedSum += b;
            signedSum += (sbyte)b;
        }

        return stored == unsignedSum || stored == signedSum;
    }

    private static bool IsZeroBlock(ReadOnlySpan<byte> block)
    {
        foreach (byte b in block)
        {
            if (b != 0)
                return false;
        }

        return true;
    }

    private static int RoundUpToBlock(int length) => (length + BlockSize - 1) / BlockSize * BlockSize;
}
