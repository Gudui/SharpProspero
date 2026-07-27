// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Storage;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class TarArchiveTests
{
    [Fact]
    public void Read_ReturnsFilesWithNamesAndData()
    {
        byte[] archive = Archive(
            Entry("hello.txt", "Hi there"u8.ToArray()),
            Entry("data/level.bin", [1, 2, 3, 4, 5]));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("hello.txt", entries[0].Name);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal("Hi there", entries[0].Text);
        Assert.Equal("data/level.bin", entries[1].Name);
        Assert.Equal([1, 2, 3, 4, 5], entries[1].Data);
    }

    [Fact]
    public void Read_MarksDirectoryEntries()
    {
        byte[] archive = Archive(Entry("assets/", [], typeFlag: '5'));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.True(entries[0].IsDirectory);
        Assert.Empty(entries[0].Data);
    }

    [Fact]
    public void Read_JoinsTheUstarPrefixOntoTheName()
    {
        byte[] archive = Archive(Entry("file.txt", "x"u8.ToArray(), prefix: "very/long/path"));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Equal("very/long/path/file.txt", entries[0].Name);
    }

    [Fact]
    public void Read_HonoursAGnuLongName()
    {
        string longName = new string('a', 180) + ".txt"; // longer than the 100-byte name field
        byte[] longNameData = Encoding.UTF8.GetBytes(longName + "\0");
        byte[] archive = Archive(
            Entry("././@LongLink", longNameData, typeFlag: 'L'),
            Entry("truncated-name", "payload"u8.ToArray()));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.Equal(longName, entries[0].Name);
        Assert.Equal("payload", entries[0].Text);
    }

    [Fact]
    public void Read_HonoursAnExtendedHeaderPath()
    {
        // What the standard way of writing a tar produces: an extended header carrying the whole path,
        // and a shortened name in the entry's own header for a reader that does not read the extended
        // one. Two entries differing only past the hundredth character must stay two entries.
        string first = new string('a', 120) + "/one.txt";
        string second = new string('a', 120) + "/two.txt";
        byte[] archive = Archive(
            Entry("PaxHeaders/one", Pax("path", first), typeFlag: 'x'),
            Entry(first[..99], "1"u8.ToArray()),
            Entry("PaxHeaders/two", Pax("path", second), typeFlag: 'x'),
            Entry(second[..99], "2"u8.ToArray()));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal(first, entries[0].Name);
        Assert.Equal("1", entries[0].Text);
        Assert.Equal(second, entries[1].Name);
        Assert.Equal("2", entries[1].Text);
    }

    [Fact]
    public void Read_ExtendedHeaderPathTakesPrecedenceOverALongNameRecord()
    {
        byte[] archive = Archive(
            Entry("././@LongLink", Encoding.UTF8.GetBytes("from-the-old-record"), typeFlag: 'L'),
            Entry("PaxHeaders/x", Pax("path", "from-the-extended-header"), typeFlag: 'x'),
            Entry("shortened", "x"u8.ToArray()));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.Equal("from-the-extended-header", entries[0].Name);
    }

    [Fact]
    public void Read_GlobalExtendedHeaderDoesNotNameTheNextEntry()
    {
        byte[] archive = Archive(
            Entry("PaxHeaders/g", Pax("path", "not-a-name"), typeFlag: 'g'),
            Entry("real.txt", "x"u8.ToArray()));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.Equal("real.txt", entries[0].Name);
    }

    // One extended-header record: the length of the whole record, a space, the pair, a newline.
    private static byte[] Pax(string key, string value)
    {
        string body = $" {key}={value}\n";
        int length = body.Length + 1;
        if (length.ToString().Length != 1)
            length = body.Length + length.ToString().Length;
        return Encoding.UTF8.GetBytes(length.ToString() + body);
    }

    [Fact]
    public void Read_EmptyGnuLongNameFallsBackToTheHeaderName()
    {
        // A degenerate long-name record with no payload must not erase the following entry's real name.
        byte[] archive = Archive(
            Entry("././@LongLink", [], typeFlag: 'L'),
            Entry("real-name.txt", "data"u8.ToArray()));

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.Equal("real-name.txt", entries[0].Name);
    }

    [Fact]
    public void Read_ToleratesAFinalEntryWithoutTrailingPadding()
    {
        // A last entry whose data is not padded to a 512-byte block (a truncated archive) is read cleanly.
        byte[] data = "0123456789"u8.ToArray();
        byte[] header = BuildHeader("trunc.bin", data.Length, '0', prefix: "");
        byte[] archive = new byte[512 + data.Length];
        header.CopyTo(archive, 0);
        data.CopyTo(archive, 512);

        List<TarEntry> entries = TarArchive.Read(archive);

        Assert.Single(entries);
        Assert.Equal("trunc.bin", entries[0].Name);
        Assert.Equal(data, entries[0].Data);
    }

    [Fact]
    public void Read_StopsAtTheZeroBlocksAndIgnoresTrailingBytes()
    {
        var builder = new List<byte>();
        builder.AddRange(Entry("only.txt", "data"u8.ToArray()));
        builder.AddRange(new byte[1024]); // the two zero blocks that end the archive
        builder.AddRange(Encoding.ASCII.GetBytes("garbage after the end marker that must be ignored"));

        List<TarEntry> entries = TarArchive.Read(builder.ToArray());

        Assert.Single(entries);
        Assert.Equal("only.txt", entries[0].Name);
    }

    [Fact]
    public void Read_RejectsABadHeaderChecksum()
    {
        byte[] archive = Archive(Entry("f.txt", "data"u8.ToArray()));
        archive[0] ^= 0xFF; // corrupt the name so the stored checksum no longer matches

        Assert.Throws<ProsperoException>(() => TarArchive.Read(archive));
    }

    [Fact]
    public void Read_RejectsAnEntryThatRunsPastTheEnd()
    {
        // A header that claims 4096 bytes of data, with none following it.
        byte[] header = BuildHeader("big.bin", 4096, '0', prefix: "");
        Assert.Throws<ProsperoException>(() => TarArchive.Read(header));
    }

    // --- helpers: build a minimal but valid ustar archive ---

    private static byte[] Archive(params byte[][] members)
    {
        var all = new List<byte>();
        foreach (byte[] member in members)
            all.AddRange(member);
        all.AddRange(new byte[1024]); // end-of-archive zero blocks
        return all.ToArray();
    }

    private static byte[] Entry(string name, byte[] data, char typeFlag = '0', string prefix = "")
    {
        byte[] header = BuildHeader(name, data.Length, typeFlag, prefix);
        int padded = (data.Length + 511) / 512 * 512;
        byte[] block = new byte[512 + padded];
        header.CopyTo(block, 0);
        data.CopyTo(block, 512);
        return block;
    }

    private static byte[] BuildHeader(string name, long size, char typeFlag, string prefix)
    {
        byte[] header = new byte[512];
        Encoding.UTF8.GetBytes(name).CopyTo(header, 0);
        WriteOctal(header, 124, 11, size);   // size, 11 octal digits then a NUL at 135
        header[156] = (byte)typeFlag;
        Encoding.ASCII.GetBytes("ustar").CopyTo(header, 257);
        header[263] = (byte)'0';
        header[264] = (byte)'0';
        if (prefix.Length > 0)
            Encoding.UTF8.GetBytes(prefix).CopyTo(header, 345);

        // Checksum: sum every byte with the checksum field read as spaces, then store it.
        for (int i = 148; i < 156; i++)
            header[i] = (byte)' ';
        int sum = 0;
        foreach (byte b in header)
            sum += b;
        WriteOctal(header, 148, 6, sum);
        header[154] = 0;
        header[155] = (byte)' ';
        return header;
    }

    private static void WriteOctal(byte[] buffer, int offset, int width, long value)
    {
        for (int i = width - 1; i >= 0; i--)
        {
            buffer[offset + i] = (byte)('0' + (int)(value & 7));
            value >>= 3;
        }
    }
}
