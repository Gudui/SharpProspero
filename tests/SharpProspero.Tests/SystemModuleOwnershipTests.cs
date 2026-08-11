// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpProspero.Tests;

// Loading a system module raises a count the platform keeps, and only an unload lowers it. A wrapper
// that loads one and then throws - because the service it wanted refused to start - leaves that count
// raised with nothing left holding it, so the module stays mapped for the life of the process and a
// caller that tries again raises the count once more. A wrapper whose Dispose forgets the unload does
// the same on the ordinary path.
//
// Neither shape can be caught by the type system and neither shows up off the console, so it is pinned
// here: a module reference is always held by a SystemModule, which gives it back on dispose, and every
// file that takes one gives it back.
public sealed class SystemModuleOwnershipTests
{
    // The one type that may speak to the loader directly: it is what owns a reference and gives it back.
    private const string Owner = "Modules/SystemModule.cs";

    // The raw binding declarations themselves, which name the calls because they are the calls.
    private const string Bindings = "Interop/Sysmodule/Sysmodule.cs";

    [Fact]
    public void OnlyTheOwningTypeReachesTheModuleLoaderDirectly()
    {
        var offenders = new List<string>();
        int checkedFiles = 0;

        foreach (string file in SdkSources())
        {
            string relative = Relative(file);
            if (relative == Owner || relative == Bindings)
                continue;
            checkedFiles++;
            string source = File.ReadAllText(file);
            if (source.Contains("sceSysmoduleLoadModule", StringComparison.Ordinal)
                || source.Contains("sceSysmoduleUnloadModule", StringComparison.Ordinal))
                offenders.Add(relative);
        }

        Assert.True(checkedFiles > 0, "No SDK source was found to check.");
        Assert.True(
            offenders.Count == 0,
            "These reach the module loader without a SystemModule holding the reference, so a failure "
                + "between the load and the unload leaves the module loaded for the life of the process: "
                + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryFileThatLoadsAModuleAlsoGivesItBack()
    {
        var offenders = new List<string>();
        int loaders = 0;

        foreach (string file in SdkSources())
        {
            string source = File.ReadAllText(file);
            // Only a real call, never a mention of one in a documentation comment.
            if (!Regex.IsMatch(StripComments(source), @"\bSystemModule\.Load\s*\("))
                continue;
            loaders++;
            if (!Regex.IsMatch(StripComments(source), @"\b\w*[Mm]odule\w*\.Dispose\s*\(\s*\)"))
                offenders.Add(Relative(file));
        }

        Assert.True(loaders > 0, "No module load was found to check.");
        Assert.True(
            offenders.Count == 0,
            "These load a system module and never unload it, so the module stays loaded for the life of "
                + "the process: " + string.Join(", ", offenders));
    }

    // Comments describe the calls as often as the code makes them, so they are removed before matching.
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", " ");
    }

    private static IEnumerable<string> SdkSources()
    {
        string root = Path.Combine(RepositoryRoot(), "src", "SharpProspero");
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Only the sources, never a copy left in a build output.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            yield return file;
        }
    }

    private static string Relative(string file)
    {
        string root = Path.Combine(RepositoryRoot(), "src", "SharpProspero") + Path.DirectorySeparatorChar;
        return file[root.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

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
