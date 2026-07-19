// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SharpProspero.Storage;

/// <summary>
/// A small INI-style settings store a module reads and writes with no system module, for keeping its
/// own configuration in a file the user can also read. Values live under named sections as
/// <c>key = value</c> lines; a leading <c>;</c> or <c>#</c> marks a comment. Load a file, read and write
/// typed values, and save it back.
/// </summary>
/// <example>
/// <code>
/// var settings = IniFile.Load("/data/app.ini");
/// int volume = settings.GetInt("audio", "volume", 80);
/// settings.Set("audio", "volume", 90);
/// settings.Save("/data/app.ini");
/// </code>
/// </example>
public sealed class IniFile
{
    // Section name -> (key -> value), all keyed without case sensitivity, insertion order kept.
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private const string RootSection = "";

    /// <summary>Creates an empty settings store.</summary>
    public IniFile() => _sections[RootSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads and parses the INI file at <paramref name="path"/>, or an empty store if it is absent.</summary>
    public static IniFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return FileSystem.Exists(path) ? Parse(Encoding.UTF8.GetString(FileSystem.ReadAllBytes(path))) : new IniFile();
    }

    /// <summary>Parses INI <paramref name="text"/> into a settings store.</summary>
    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        if (string.IsNullOrEmpty(text))
            return ini;

        string section = RootSection;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (!ini._sections.ContainsKey(section))
                    ini._sections[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            ini.Section(section)[key] = value;
        }
        return ini;
    }

    /// <summary>The section names present, including the unnamed root section.</summary>
    public IReadOnlyCollection<string> Sections => _sections.Keys;

    /// <summary>Whether <paramref name="key"/> is present under <paramref name="section"/>.</summary>
    public bool Contains(string section, string key) =>
        _sections.TryGetValue(section ?? RootSection, out Dictionary<string, string>? s) && s.ContainsKey(key);

    /// <summary>The string value, or <paramref name="fallback"/> when it is absent.</summary>
    public string GetString(string section, string key, string fallback = "")
        => _sections.TryGetValue(section ?? RootSection, out Dictionary<string, string>? s)
            && s.TryGetValue(key, out string? value) ? value : fallback;

    /// <summary>The value parsed as an integer, or <paramref name="fallback"/> when absent or unparsable.</summary>
    public int GetInt(string section, string key, int fallback = 0)
        => int.TryParse(GetString(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : fallback;

    /// <summary>The value parsed as a boolean (<c>true</c>/<c>1</c>/<c>yes</c>/<c>on</c>), or <paramref name="fallback"/>.</summary>
    public bool GetBool(string section, string key, bool fallback = false)
    {
        string value = GetString(section, key).Trim();
        if (value.Length == 0)
            return fallback;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sets a string value, adding the section if needed.</summary>
    public void Set(string section, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Section(section ?? RootSection)[key] = value ?? string.Empty;
    }

    /// <summary>Sets an integer value.</summary>
    public void Set(string section, string key, int value)
        => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets a boolean value, written as <c>true</c> or <c>false</c>.</summary>
    public void Set(string section, string key, bool value)
        => Set(section, key, value ? "true" : "false");

    /// <summary>Removes a key; returns whether it was present.</summary>
    public bool Remove(string section, string key)
        => _sections.TryGetValue(section ?? RootSection, out Dictionary<string, string>? s) && s.Remove(key);

    /// <summary>Serializes the store to INI text.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();

        // The unnamed root section's keys come first, without a header.
        if (_sections.TryGetValue(RootSection, out Dictionary<string, string>? root))
            foreach (KeyValuePair<string, string> pair in root)
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');

        foreach (KeyValuePair<string, Dictionary<string, string>> section in _sections)
        {
            if (section.Key.Length == 0 || section.Value.Count == 0)
                continue;
            builder.Append('[').Append(section.Key).Append("]\n");
            foreach (KeyValuePair<string, string> pair in section.Value)
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>Writes the store to the INI file at <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FileSystem.WriteAllText(path, ToString());
    }

    private Dictionary<string, string> Section(string name)
    {
        if (!_sections.TryGetValue(name, out Dictionary<string, string>? section))
        {
            section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[name] = section;
        }
        return section;
    }
}
