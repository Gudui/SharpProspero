// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Compression;
using SharpProspero.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpProspero.Tests;

// Handing a format string to a date reaches the general formatter, which carries the time-zone
// specifiers, which asks for the local time zone, which reads a time-zone database off the file
// system. That database is not on the device, and the cost is not a failure at run time: the reader
// pulls in the process layer of the run-time support library, and thirteen of the names that layer
// needs - _exit, execve, getcwd, getegid, geteuid, getgroups, getpriority, getsid, seteuid,
// setgroups, setpriority, setuid, syslog - are published by nothing. Any module reaching one of these
// two places therefore fails to link, and the failure names those thirteen and says nothing about a
// date. Both are pinned here: the shape they produce, and the absence of the call that would restore
// the chain.
public sealed class DateFormattingReachTests
{
    private static readonly DateTime Sample = new(2026, 3, 7, 4, 5, 6, 78, DateTimeKind.Utc);

    [Fact]
    public void ZipEntry_WritesItsTimeFromTheParts()
    {
        var entry = new ZipEntry("a/b.txt", 0x1234u, 10, 20, 8, Sample, IsDirectory: false);
        string text = entry.ToString();

        // Every member is still there, and the time reads as the fixed shape rather than whatever a
        // format string would give.
        Assert.Contains("Name = a/b.txt", text, StringComparison.Ordinal);
        Assert.Contains("LastModified = 2026-03-07 04:05:06", text, StringComparison.Ordinal);
        Assert.Contains("IsDirectory = False", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogLine_WritesItsTimeFromTheParts()
    {
        string line = LogFormat.Line(LogLevel.Warning, "something");

        // Either the clock answered and the time is eight digits and three more, or it did not and the
        // line says so. Neither goes through a format string.
        Assert.Matches(new Regex(@"^(\d{2}:\d{2}:\d{2}\.\d{3}|--:--:--\.---) WRN something$"), line);
    }

    [Theory]
    [InlineData("Diagnostics/Log.cs")]
    [InlineData("Compression/ZipArchive.cs")]
    public void NeitherPlaceHandsAFormatStringToADate(string relativePath)
    {
        string source = File.ReadAllText(SourcePath(relativePath));

        // A format string on a date is what starts the chain. Catching it here names the reason, which
        // a link failure listing thirteen unrelated symbols never would.
        MatchCollection matches = Regex.Matches(source, @"\.ToString\(""[^""]*[HhmsfyMd][^""]*""\)");
        Assert.True(
            matches.Count == 0,
            $"{relativePath} formats a value with a format string that could be a date: "
                + string.Join(", ", matches.Select(m => m.Value)));
    }

    // The tests run from the build output, so the source tree is found by walking up to the folder
    // that holds it.
    private static string SourcePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SharpProspero", relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find src/SharpProspero/{relativePath} above the test output.");
    }
}
