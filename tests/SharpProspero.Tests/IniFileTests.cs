// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using Xunit;

namespace SharpProspero.Tests;

public sealed class IniFileTests
{
    [Fact]
    public void Parse_ReadsSectionsKeysAndComments()
    {
        const string text = """
            ; a comment
            root_key = root_value

            [audio]
            volume = 90
            muted = yes
            # trailing comment
            [display]
            fullscreen = false
            """;

        IniFile ini = IniFile.Parse(text);
        Assert.Equal("root_value", ini.GetString("", "root_key"));
        Assert.Equal(90, ini.GetInt("audio", "volume"));
        Assert.True(ini.GetBool("audio", "muted"));
        Assert.False(ini.GetBool("display", "fullscreen"));
    }

    [Fact]
    public void Get_ReturnsFallbackWhenAbsent()
    {
        IniFile ini = IniFile.Parse("[a]\nx=1\n");
        Assert.Equal("d", ini.GetString("a", "missing", "d"));
        Assert.Equal(7, ini.GetInt("a", "missing", 7));
        Assert.True(ini.GetBool("nope", "missing", true));
        Assert.Equal(1, ini.GetInt("a", "x"));
    }

    [Fact]
    public void SetAndRoundTrip_PreservesValues()
    {
        var ini = new IniFile();
        ini.Set("audio", "volume", 80);
        ini.Set("audio", "muted", true);
        ini.Set("name", "title", "My App");

        IniFile reparsed = IniFile.Parse(ini.ToString());
        Assert.Equal(80, reparsed.GetInt("audio", "volume"));
        Assert.True(reparsed.GetBool("audio", "muted"));
        Assert.Equal("My App", reparsed.GetString("name", "title"));
    }

    [Fact]
    public void Remove_DropsKey()
    {
        var ini = new IniFile();
        ini.Set("a", "x", 1);
        Assert.True(ini.Contains("a", "x"));
        Assert.True(ini.Remove("a", "x"));
        Assert.False(ini.Contains("a", "x"));
        Assert.False(ini.Remove("a", "x"));
    }
}
