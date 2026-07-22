// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Prx;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SharpProspero.Tests;

// The self-contained link resolves every imported function name through the stub catalog. If the SDK
// binds a function the catalog does not list, that name is left unresolved and the module fails to
// link the moment an application reaches it, far from where the binding was added. This pins the
// invariant: every symbol the SDK imports is named by some catalog entry.
public sealed class StubCatalogCoverageTests
{
    private static IEnumerable<string> ImportedSymbols()
    {
        Assembly sdk = typeof(Color).Assembly;
        foreach (System.Type type in sdk.GetTypes())
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                CustomAttributeData? import = method.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.Name == "LibraryImportAttribute");
                if (import is null)
                    continue;
                // The imported name is the explicit entry point when given, otherwise the method name.
                string? entryPoint = import.NamedArguments
                    .FirstOrDefault(n => n.MemberName == "EntryPoint").TypedValue.Value as string;
                yield return entryPoint ?? method.Name;
            }
    }

    [Fact]
    public void EveryImportedSymbolIsProvidedByAStubCatalogEntry()
    {
        var provided = new HashSet<string>();
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            foreach (string name in entry.Exports)
                provided.Add(name);

        string[] missing = ImportedSymbols().Where(s => !provided.Contains(s)).Distinct().Order().ToArray();

        Assert.True(missing.Length == 0,
            "These imported symbols are not named by any stub catalog entry, so a module that reaches them " +
            "would fail to link:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void TheCatalogNamesTheExpandedUserServiceEntries()
    {
        // A direct pin for the login-user query set, the omission the audit found: these are reached
        // through the public Users API and must resolve.
        var provided = StubCatalog.Core.SelectMany(e => e.Exports).ToHashSet();
        Assert.Contains("sceUserServiceGetLoginUserIdList", provided);
        Assert.Contains("sceUserServiceGetUserName", provided);
        Assert.Contains("sceUserServiceGetUserNumber", provided);
    }
}
