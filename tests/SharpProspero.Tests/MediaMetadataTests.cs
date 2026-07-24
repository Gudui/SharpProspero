using SharpProspero.Media;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class MediaMetadataTests
{
    // A version-2.3 tag block with Latin-1 text frames.
    private static byte[] FrontTag(params (string Id, string Text)[] frames)
    {
        var body = new List<byte>();
        foreach ((string id, string text) in frames)
        {
            byte[] payload = new byte[1 + text.Length];
            for (int i = 0; i < text.Length; i++) payload[1 + i] = (byte)text[i]; // encoding 0 (Latin-1)
            body.AddRange(Encoding.ASCII.GetBytes(id));
            uint size = (uint)payload.Length;
            body.AddRange([(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size]); // v2.3 plain size
            body.AddRange([0, 0]);
            body.AddRange(payload);
        }
        byte[] header = new byte[10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(header, 0);
        header[3] = 3;
        int total = body.Count;
        header[6] = (byte)((total >> 21) & 0x7F);
        header[7] = (byte)((total >> 14) & 0x7F);
        header[8] = (byte)((total >> 7) & 0x7F);
        header[9] = (byte)(total & 0x7F);
        return header.Concat(body).ToArray();
    }

    private static byte[] TrailingTag(string title, string artist)
    {
        byte[] file = new byte[400 + 128];
        int at = 400;
        Encoding.ASCII.GetBytes("TAG").CopyTo(file, at);
        for (int i = 0; i < title.Length && i < 30; i++) file[at + 3 + i] = (byte)title[i];
        for (int i = 0; i < artist.Length && i < 30; i++) file[at + 33 + i] = (byte)artist[i];
        return file;
    }

    [Fact]
    public void Read_TheFrontTagFrames()
    {
        MediaTags tags = MediaMetadata.Read(FrontTag(("TIT2", "My Song"), ("TPE1", "The Band"), ("TALB", "First Album"), ("TRCK", "3/12")));
        Assert.Equal("My Song", tags.Title);
        Assert.Equal("The Band", tags.Artist);
        Assert.Equal("First Album", tags.Album);
        Assert.Equal("3/12", tags.TrackNumber);
    }

    [Fact]
    public void Read_DecodesUtf16Text()
    {
        // TIT2 with a UTF-16 little-endian body and a byte-order mark.
        byte[] text = new byte[] { 1, 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Café")).ToArray();
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("TIT2"));
        uint size = (uint)text.Length;
        body.AddRange([(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, 0, 0]);
        body.AddRange(text);
        byte[] header = new byte[10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(header, 0);
        header[3] = 3;
        header[9] = (byte)body.Count;
        MediaTags tags = MediaMetadata.Read(header.Concat(body).ToArray());
        Assert.Equal("Café", tags.Title);
    }

    [Fact]
    public void Read_FallsBackToTheTrailingTag()
    {
        MediaTags tags = MediaMetadata.Read(TrailingTag("Old Track", "Old Artist"));
        Assert.Equal("Old Track", tags.Title);
        Assert.Equal("Old Artist", tags.Artist);
    }

    [Fact]
    public void Read_ReturnsEmptyWhenThereAreNoTags()
    {
        Assert.True(MediaMetadata.Read(new byte[200]).IsEmpty);
    }

    [Fact]
    public void Read_DoesNotThrowOnACraftedFrameSize()
    {
        // A frame whose plain size is the largest positive value once tried to overflow the bounds check.
        byte[] header = new byte[10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(header, 0);
        header[3] = 3;
        header[9] = 40;
        byte[] frame = Encoding.ASCII.GetBytes("TIT2").Concat(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0, 0 }).ToArray();
        byte[] file = header.Concat(frame).Concat(new byte[30]).ToArray();
        Assert.True(MediaMetadata.Read(file).IsEmpty); // returns empty instead of throwing
    }

    [Fact]
    public void Read_DoesNotThrowOnACraftedExtendedHeader()
    {
        byte[] header = new byte[10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(header, 0);
        header[3] = 3;
        header[5] = 0x40;           // extended header present
        header[9] = 40;
        byte[] file = header.Concat(new byte[] { 0x80, 0, 0, 0 }).Concat(new byte[40]).ToArray();
        Assert.True(MediaMetadata.Read(file).IsEmpty);
    }
}
