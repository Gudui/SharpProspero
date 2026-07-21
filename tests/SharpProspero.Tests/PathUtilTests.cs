// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PathUtilTests
{
    [Theory]
    [InlineData("/app0", "data", "/app0/data")]
    [InlineData("/app0/", "data", "/app0/data")]
    [InlineData("/app0", "/data", "/data")] // right side is absolute
    [InlineData("", "b", "b")]
    [InlineData("a", "", "a")]
    public void Combine_JoinsWithOneSeparator(string left, string right, string expected) =>
        Assert.Equal(expected, PathUtil.Combine(left, right));

    [Fact]
    public void Combine_ManyParts() =>
        Assert.Equal("/app0/data/level.csv", PathUtil.Combine("/app0", "data", "level.csv"));

    [Theory]
    [InlineData("/a/b/c.txt", "c.txt")]
    [InlineData("c.txt", "c.txt")]
    [InlineData("/a/", "")]
    public void GetFileName(string path, string expected) =>
        Assert.Equal(expected, PathUtil.GetFileName(path));

    [Theory]
    [InlineData("/a/b.txt", ".txt")]
    [InlineData("/a/b", "")]
    [InlineData("/a/.hidden", "")] // a leading dot is a hidden name, not an extension
    [InlineData("archive.tar.gz", ".gz")]
    public void GetExtension(string path, string expected) =>
        Assert.Equal(expected, PathUtil.GetExtension(path));

    [Theory]
    [InlineData("/a/b.txt", "b")]
    [InlineData("/a/.hidden", ".hidden")]
    [InlineData("c", "c")]
    public void GetFileNameWithoutExtension(string path, string expected) =>
        Assert.Equal(expected, PathUtil.GetFileNameWithoutExtension(path));

    [Theory]
    [InlineData("/a/b/c", "/a/b")]
    [InlineData("/a", "/")] // the root
    [InlineData("a/b", "a")]
    [InlineData("a", "")]
    public void GetDirectoryName(string path, string expected) =>
        Assert.Equal(expected, PathUtil.GetDirectoryName(path));

    [Fact]
    public void IsAbsoluteAndHasExtension()
    {
        Assert.True(PathUtil.IsAbsolute("/x"));
        Assert.False(PathUtil.IsAbsolute("x"));
        Assert.False(PathUtil.IsAbsolute(""));
        Assert.True(PathUtil.HasExtension("a.txt"));
        Assert.False(PathUtil.HasExtension("a"));
    }

    [Theory]
    [InlineData("/a/b.txt", "png", "/a/b.png")]
    [InlineData("/a/b.txt", ".png", "/a/b.png")]
    [InlineData("/a/b", "png", "/a/b.png")]
    [InlineData("/a/b.txt", null, "/a/b")]
    public void ChangeExtension(string path, string? extension, string expected) =>
        Assert.Equal(expected, PathUtil.ChangeExtension(path, extension));
}
