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

public sealed class ZipBuilderTests
{
    private static byte[] Build()
        => new ZipBuilder()
            .AddText("readme.txt", new string('x', 4000))          // compresses
            .Add("data/raw.bin", [1, 2, 3, 4, 5], compress: false) // stored
            .AddDirectory("data")
            .ToArray();

    [Fact]
    public void OurReaderReadsWhatWeWrote()
    {
        Zip zip = Zip.Open(Build());
        Assert.Equal(new string('x', 4000), Encoding.UTF8.GetString(zip.Extract("readme.txt")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, zip.Extract("data/raw.bin"));
        Assert.True(zip.Entries.Single(e => e.Name == "data/").IsDirectory);
    }

    [Fact]
    public void TheFrameworkReaderReadsWhatWeWrote()
    {
        using var memory = new MemoryStream(Build());
        using var archive = new System.IO.Compression.ZipArchive(memory, ZipArchiveMode.Read);

        ZipArchiveEntry readme = archive.GetEntry("readme.txt")!;
        using (var reader = new StreamReader(readme.Open()))
            Assert.Equal(new string('x', 4000), reader.ReadToEnd());

        ZipArchiveEntry raw = archive.GetEntry("data/raw.bin")!;
        using (var stream = raw.Open())
        {
            byte[] buffer = new byte[5];
            stream.ReadExactly(buffer);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer);
        }
    }

    [Fact]
    public void CompressedEntryUsesDeflateAndShrinks()
    {
        Zip zip = Zip.Open(Build());
        ZipEntry readme = zip.Entries.Single(e => e.Name == "readme.txt");
        Assert.Equal((ushort)8, readme.Method);
        Assert.True(readme.CompressedSize < readme.UncompressedSize);
    }

    [Fact]
    public void StoredEntryKeepsItsBytes()
    {
        Zip zip = Zip.Open(Build());
        ZipEntry raw = zip.Entries.Single(e => e.Name == "data/raw.bin");
        Assert.Equal((ushort)0, raw.Method);
        Assert.Equal(5, raw.CompressedSize);
    }
}
