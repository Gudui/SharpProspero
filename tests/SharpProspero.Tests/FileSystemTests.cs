// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Storage;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class FileSystemTests
{
    [Fact]
    public void DecodeEntries_ReadsNameAndTypeAndSkipsDotEntries()
    {
        var buffer = new List<byte>();
        AppendRecord(buffer, ".", FileEntryType.Directory);
        AppendRecord(buffer, "..", FileEntryType.Directory);
        AppendRecord(buffer, "assets", FileEntryType.Directory);
        AppendRecord(buffer, "level.bin", FileEntryType.File);

        var entries = new List<DirectoryEntry>();
        FileSystem.DecodeEntries(buffer.ToArray(), entries);

        Assert.Equal(2, entries.Count);
        Assert.Equal("assets", entries[0].Name);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("level.bin", entries[1].Name);
        Assert.True(entries[1].IsFile);
        Assert.False(entries[1].IsDirectory);
    }

    [Fact]
    public void DecodeEntries_StopsOnZeroLengthRecordInsteadOfLooping()
    {
        // A record length of zero would never advance the offset; the decoder must stop.
        byte[] buffer = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 0);

        var entries = new List<DirectoryEntry>();
        FileSystem.DecodeEntries(buffer, entries);   // must return, not hang

        Assert.Empty(entries);
    }

    [Fact]
    public void DecodeEntries_StopsWhenARecordRunsPastTheBuffer()
    {
        var buffer = new List<byte>();
        AppendRecord(buffer, "good", FileEntryType.File);
        int truncatedStart = buffer.Count;
        AppendRecord(buffer, "truncated", FileEntryType.File);
        byte[] all = [.. buffer];

        // Cut the second record short: its length field still claims the full size.
        var entries = new List<DirectoryEntry>();
        FileSystem.DecodeEntries(all.AsSpan(0, truncatedStart + 10), entries);

        Assert.Single(entries);
        Assert.Equal("good", entries[0].Name);
    }

    [Fact]
    public void DecodeEntries_EmptyBuffer_YieldsNothing()
    {
        var entries = new List<DirectoryEntry>();
        FileSystem.DecodeEntries([], entries);
        Assert.Empty(entries);
    }

    [Fact]
    public void CopyDirectory_RejectsDestinationInsideSource()
    {
        // The containment check runs before any device call, so it is exercised on the host: copying a
        // tree into itself or a sub-directory would recurse without end and must be refused up front.
        Assert.Throws<ProsperoException>(() => FileSystem.CopyDirectory("/data", "/data/backup"));
        Assert.Throws<ProsperoException>(() => FileSystem.CopyDirectory("/data/", "/data"));
        Assert.Throws<ProsperoException>(() => FileSystem.CopyDirectory("/save", "/save/deep/inner"));
    }

    // Builds one directory record: file number, record length, type, name length, then the name
    // padded to a four-byte boundary.
    private static void AppendRecord(List<byte> into, string name, FileEntryType type)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        int length = (8 + nameBytes.Length + 1 + 3) & ~3;
        byte[] record = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 1);                      // d_fileno
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), (ushort)length); // d_reclen
        record[6] = (byte)type;                                                   // d_type
        record[7] = (byte)nameBytes.Length;                                       // d_namlen
        nameBytes.CopyTo(record.AsSpan(8));
        into.AddRange(record);
    }
}
