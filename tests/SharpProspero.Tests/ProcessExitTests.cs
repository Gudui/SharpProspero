// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpProspero.Tests;

// A module must not return from its entry point. The start object the toolchain links in - which is
// the same sequence the platform's own start object uses - reports the return before the status
// reaches the C library, and the platform treats that report as a fault: the process is killed, a
// crash report is written, and the user is shown the box that says the application closed
// unexpectedly. The recorded reason is "Returned from main with zero", so even a clean exit is
// reported as a crash. Leaving through the C library instead never makes that report.
//
// The rule cannot be enforced by the type system, so it is enforced here: every entry point the SDK
// ships has to end the process rather than fall off the end of its own Main.
public sealed class ProcessExitTests
{
    [Fact]
    public void EveryEntryPointEndsTheProcessRatherThanReturning()
    {
        var offenders = new List<string>();
        int checkedFiles = 0;

        foreach (string file in EntryPointFiles())
        {
            string source = File.ReadAllText(file);
            if (!Regex.IsMatch(source, @"\bstatic\s+void\s+Main\s*\("))
                continue;
            checkedFiles++;
            if (!source.Contains("ProcessExit.Exit(", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(Path.GetDirectoryName(file)) + "/" + Path.GetFileName(file));
        }

        Assert.True(checkedFiles > 0, "No entry point was found to check.");
        Assert.True(
            offenders.Count == 0,
            "These entry points return from Main instead of ending the process, which the platform "
                + "reports as a crash: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheStartObjectStillReportsAReturnFromMain()
    {
        // The report is what makes the rule above necessary. The start object matches the platform's
        // own, so the call stays; if it were ever dropped, the rule could be relaxed - and this says
        // so rather than leaving the next reader to work it out.
        string crt = File.ReadAllText(RepositoryPath("tools/SharpProspero.Link/CrtEmitter.cs"));
        Assert.Contains("catchReturnFromMain", crt, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EntryPointFiles()
    {
        string root = RepositoryRoot();
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "templates"), "*.cs", SearchOption.AllDirectories))
        {
            // Only the sources, never a copy left in a build output.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            // A payload is not an application. It carries a start object of its own that calls main and
            // hands the result back to whatever loaded it, with no report made and no C library to
            // leave through, so returning is how a payload is meant to end.
            if (file.Contains($"{Path.DirectorySeparatorChar}prospero-payload", StringComparison.Ordinal))
                continue;

            yield return file;
        }

        string sample = Path.Combine(root, "src", "SharpProspero.Sample", "Program.cs");
        if (File.Exists(sample))
            yield return sample;
    }

    private static string RepositoryPath(string relative)
        => Path.Combine(RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    // The tests run from the build output, so the tree is found by walking up to the folder holding it.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "templates"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the SDK folder above the test output.");
    }
}
