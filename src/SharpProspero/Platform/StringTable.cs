// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SharpProspero.Platform;

/// <summary>
/// The user-facing text of an application, keyed by a stable identifier and looked up per language, so the
/// strings live in data rather than in code. A table has a locale, a set of key-to-text entries, and an
/// optional fallback table consulted when a key is missing — usually the default language. Load the entries
/// from the INI or JSON readers, or add them directly. The current user language comes from
/// <c>SystemParameters</c>.
/// </summary>
/// <example>
/// <code>
/// var en = new StringTable("en").Set("greeting", "Hello, {0}");
/// var fr = new StringTable("fr", fallback: en).Set("greeting", "Bonjour, {0}");
/// string text = fr.Format("greeting", playerName); // "Bonjour, Sven"
/// </code>
/// </example>
public sealed class StringTable
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    /// <summary>Creates a table for <paramref name="locale"/>, optionally chained to a <paramref name="fallback"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="locale"/> is null or empty.</exception>
    public StringTable(string locale, StringTable? fallback = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        Locale = locale;
        Fallback = fallback;
    }

    /// <summary>The language tag this table holds, such as "en" or "fr".</summary>
    public string Locale { get; }

    /// <summary>The table consulted when a key is missing here, or null.</summary>
    public StringTable? Fallback { get; }

    /// <summary>How many entries this table holds directly, not counting the fallback.</summary>
    public int Count => _entries.Count;

    /// <summary>Adds or replaces one entry and returns this table so calls chain.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public StringTable Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _entries[key] = value;
        return this;
    }

    /// <summary>Adds or replaces many entries and returns this table so calls chain.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public StringTable Add(IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (KeyValuePair<string, string> entry in entries)
            Set(entry.Key, entry.Value);
        return this;
    }

    /// <summary>Whether <paramref name="key"/> resolves here or in the fallback chain.</summary>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.ContainsKey(key) || (Fallback?.Contains(key) ?? false);
    }

    /// <summary>Looks up <paramref name="key"/>, following the fallback chain; returns whether it was found.</summary>
    public bool TryGet(string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_entries.TryGetValue(key, out string? own))
        {
            value = own;
            return true;
        }

        if (Fallback is not null)
            return Fallback.TryGet(key, out value);

        value = key;
        return false;
    }

    /// <summary>
    /// Returns the text for <paramref name="key"/>, following the fallback chain. When no table has the
    /// key, the key itself is returned so a missing string is visible rather than blank.
    /// </summary>
    public string Get(string key)
    {
        TryGet(key, out string value);
        return value;
    }

    /// <summary>
    /// Looks up <paramref name="key"/> and fills in the positional arguments with
    /// <see cref="string.Format(IFormatProvider, string, object?[])"/>. With no arguments the text is
    /// returned unchanged, so a template that itself contains braces is left alone.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public string Format(string key, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string template = Get(key);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
