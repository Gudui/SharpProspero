// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Compression;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;
using Zip = SharpProspero.Compression.ZipArchive;

namespace SharpProspero.Tests;

public sealed class ZipArchiveTests
{
    // Builds a ZIP in memory with a mix of compressed, stored, and folder entries.
    private static byte[] BuildZip()
    {
        using var memory = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry hello = archive.CreateEntry("readme.txt", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(hello.Open())) writer.Write("hello from a zip");

            ZipArchiveEntry big = archive.CreateEntry("data/big.bin", CompressionLevel.Optimal);
            using (Stream s = big.Open()) s.Write(Encoding.ASCII.GetBytes(new string('Q', 10000)));

            ZipArchiveEntry stored = archive.CreateEntry("data/stored.bin", CompressionLevel.NoCompression);
            using (Stream s = stored.Open()) s.Write([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

            archive.CreateEntry("data/"); // an explicit folder entry
        }
        return memory.ToArray();
    }

    [Fact]
    public void Open_ListsEveryEntry()
    {
        Zip zip = Zip.Open(BuildZip());
        string[] names = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "data/", "data/big.bin", "data/stored.bin", "readme.txt" }, names);
        Assert.True(zip.Entries.Single(e => e.Name == "data/").IsDirectory);
    }

    [Fact]
    public void Extract_DecompressesADeflatedEntry()
    {
        Zip zip = Zip.Open(BuildZip());
        Assert.Equal("hello from a zip", Encoding.UTF8.GetString(zip.Extract("readme.txt")));
        Assert.Equal(new string('Q', 10000), Encoding.ASCII.GetString(zip.Extract("data/big.bin")));
    }

    [Fact]
    public void Extract_ReadsAStoredEntry()
    {
        Zip zip = Zip.Open(BuildZip());
        Assert.True(zip.TryGetEntry("data/stored.bin", out ZipEntry stored));
        Assert.Equal((ushort)0, stored.Method); // stored
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, zip.Extract(stored));
    }

    [Fact]
    public void Extract_OfADirectoryIsEmpty()
    {
        Zip zip = Zip.Open(BuildZip());
        Assert.Empty(zip.Extract("data/"));
    }

    [Fact]
    public void EntryMetadata_ReportsSizesAndCrc()
    {
        Zip zip = Zip.Open(BuildZip());
        ZipEntry big = zip.Entries.Single(e => e.Name == "data/big.bin");
        Assert.Equal(10000, big.UncompressedSize);
        Assert.True(big.CompressedSize < big.UncompressedSize, "the deflated size is smaller");
        Assert.Equal((ushort)8, big.Method); // deflate
    }

    [Fact]
    public void Open_RejectsDataThatIsNotAZip()
        => Assert.Throws<CompressionException>(() => Zip.Open(Encoding.ASCII.GetBytes("this is definitely not a zip archive at all")));

    [Fact]
    public void TryGetEntry_ReturnsFalseForAMissingName()
    {
        Zip zip = Zip.Open(BuildZip());
        Assert.False(zip.TryGetEntry("nope.txt", out _));
    }
}
