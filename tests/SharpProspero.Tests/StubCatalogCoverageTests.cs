// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Prx;
using System;
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
        {
            // Payload bindings are resolved by the payload CRT inside an already-running host
            // process. They intentionally do not participate in an application's PRX stub catalog.
            if (type.Namespace?.StartsWith("SharpProspero.Payload", StringComparison.Ordinal) == true)
                continue;
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
    }

    [Fact]
    public void EveryImportedSymbolIsProvidedByAStubCatalogEntry()
    {
        // A name resolves either because a module publishes it, in which case the catalog names it, or
        // because the compat object defines it. The catalog may only name what a module really
        // publishes: a name listed there that nothing publishes produces an import the loader cannot
        // bind, and a module whose imports do not bind never reaches its first instruction.
        var provided = new HashSet<string>(SharpProspero.Link.CompatEmitter.DefinedNames);
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            foreach (string name in entry.Exports)
                provided.Add(name);

        string[] missing = ImportedSymbols().Where(s => !provided.Contains(s)).Distinct().Order().ToArray();

        Assert.True(missing.Length == 0,
            "These imported symbols are named by no stub catalog entry and defined by no compat entry, " +
            "so a module that reaches them would fail to link:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void TheCatalogNamesEachLibraryTheWayAModuleThatStartsNamesIt()
    {
        // An import records three names: the library it binds to, the module publishing that library,
        // and the file the loader loads. They are not always the same word, and getting the file wrong
        // means naming a module that is not there. Each pairing below was read out of the import records
        // of launching titles - the count is how many of them agree - so none of it is inferred from the
        // library's name.
        (string Library, string Module, string File, int Titles)[] measured =
        [
            ("libSceAjm", "libSceAjm", "libSceAjm.native.prx", 55),
            ("libSceAppContent", "libSceAppContentUtil", "libSceAppContent.prx", 66),
            ("libSceAudiodec", "libSceAudiodec", "libSceAudiodec.native.prx", 10),
            ("libSceAvPlayer", "libSceAvPlayer", "libSceAvPlayer.native.prx", 33),
            // These four are read out of the publishing modules themselves rather than out of the
            // titles that import them, because each module records which libraries it publishes and
            // under which file. The bus module publishes three libraries, and the device enquiries are
            // in the second of them rather than the one named after the module; the character-set
            // converter and the font engines each live in a file named nothing like their library.
            ("libSceCes", "libSceCes", "libSceCesCs-module.prx", 0),
            ("libSceDeviceService", "libSceMbus", "libSceMbus.prx", 0),
            ("libSceFont", "libSceFont", "libSceFont-module.prx", 0),
            ("libSceFontFt", "libSceFontFt", "libSceFontFt-module.prx", 0),
            // Already right, kept here so the whole set is checked together.
            ("libSceMsgDialog.native", "libSceMsgDialog", "libSceMsgDialog.native.prx", 0),
            ("libSceSaveData_native", "libSceSaveData_native", "libSceSaveData.native.prx", 0),
            ("libScePosix", "libkernel", "libkernel.prx", 0),
        ];

        foreach ((string library, string module, string file, int _) in measured)
        {
            StubCatalog.Entry entry = Assert.Single(StubCatalog.Core, e => e.Library == library);
            Assert.Equal(module, entry.ModuleName ?? entry.Library);
            Assert.Equal(file, entry.Soname ?? entry.Library + ".prx");
        }
    }

    [Fact]
    public void EveryNameTheCompatObjectReachesForIsOneTheCatalogResolves()
    {
        // The compat object defines what nothing publishes by reaching for what something does, so its
        // own undefined names are imports like any other. A name it reaches that the catalog does not
        // list is left unresolved, and a module whose imports do not all bind never reaches its first
        // instruction - so adding a call here and forgetting the catalog breaks the module rather than
        // the build. This is the check that turns that into a build failure.
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            foreach (string name in entry.Exports)
                named.Add(name);

        SharpProspero.Link.ElfObject obj =
            SharpProspero.Link.ElfObjectReader.Read(SharpProspero.Link.CompatEmitter.BuildObject(), "compat.o");
        // A name under the alias prefix reaches the published name after it: that is how a routine
        // standing in front of something the platform publishes still reaches the platform's.
        static string Published(string n) =>
            n.StartsWith(SharpProspero.Link.Linker.DeviceAliasPrefix, StringComparison.Ordinal)
                ? n[SharpProspero.Link.Linker.DeviceAliasPrefix.Length..]
                : n;

        string[] reached = [.. obj.Symbols
            .Where(s => s.IsUndefined && s.Name.Length > 0)
            .Select(s => Published(s.Name))
            .Where(n => !named.Contains(n))
            // The linker places these itself; they name no module.
            .Where(n => n != SharpProspero.Link.CompatEmitter.ModuleBaseSymbol
                     && n != SharpProspero.Link.CompatEmitter.TextEndSymbol
                     && n != SharpProspero.Link.CompatEmitter.FrameIndexSymbol
                     && n != SharpProspero.Link.CompatEmitter.FrameIndexEndSymbol)
            .Distinct().Order()];

        Assert.True(reached.Length == 0,
            "The compat object reaches for these, and no catalog entry names them, so a module using " +
            "them would carry imports that cannot bind:\n  " + string.Join("\n  ", reached));
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

    // Where the toolchain is installed, or null when it is not.
    // The archive that is the authority for an entry. A library's archive is normally named after the
    // library, but not always: some are named after the module instead, and one is only shipped in the
    // form a development machine uses. Guessing the first spelling and moving on when it is absent is
    // how a wrong entry survived - the check skipped exactly the entry that was wrong.
    private static string? StubArchive(string root, StubCatalog.Entry entry)
    {
        string dir = System.IO.Path.Combine(root, "target", "lib");
        string library = entry.Library.Replace(".native", "").Replace("_native", "");
        string module = (entry.ModuleName ?? entry.Library).Replace(".native", "").Replace("_native", "");
        foreach (string stem in new[] { library, module })
            foreach (string suffix in new[] { "_stub_weak.a", "_nosubmission_stub_weak.a" })
            {
                string path = System.IO.Path.Combine(dir, stem + suffix);
                if (System.IO.File.Exists(path))
                    return path;
            }
        return null;
    }

    private static string? ToolchainRoot()
    {
        string? root = System.Environment.GetEnvironmentVariable("PROSPERO_SDK_DIR");
        if (!string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root))
            return root;
        const string Installed = @"C:\Program Files (x86)\SCE\Prospero SDKs\2.000";
        return System.IO.Directory.Exists(Installed) ? Installed : null;
    }

    // The names a stub library publishes, read from its dynamic symbol table.
    private static HashSet<string> PublishedNames(string path)
    {
        byte[] f = System.IO.File.ReadAllBytes(path);
        var names = new HashSet<string>(System.StringComparer.Ordinal);
        ulong shoff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(0x28));
        int shnum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(0x3C));
        for (int i = 0; i < shnum; i++)
        {
            int sh = (int)shoff + i * 64;
            uint type = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 4));
            if (type != 11) continue;                       // SHT_DYNSYM
            ulong off = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x18));
            ulong size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x20));
            uint link = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 0x28));
            ulong strOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                f.AsSpan((int)shoff + (int)link * 64 + 0x18));
            for (ulong e = 24; e < size; e += 24)
            {
                uint nameOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan((int)(off + e)));
                int at = (int)strOff + (int)nameOff;
                int end = System.Array.IndexOf(f, (byte)0, at);
                if (end > at) names.Add(System.Text.Encoding.ASCII.GetString(f, at, end - at));
            }
        }
        return names;
    }

    // The file name a library's own stub declares for itself, or null when it declares none.
    private static string? DeclaredFileName(string path)
    {
        byte[] f = System.IO.File.ReadAllBytes(path);
        ulong shoff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(0x28));
        int shnum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(0x3C));
        for (int i = 0; i < shnum; i++)
        {
            int sh = (int)shoff + i * 64;
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 4)) != 6)
                continue;                                   // SHT_DYNAMIC
            ulong off = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x18));
            ulong size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x20));
            uint link = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 0x28));
            ulong strOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                f.AsSpan((int)shoff + (int)link * 64 + 0x18));
            for (ulong e = 0; e + 16 <= size; e += 16)
            {
                long tag = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(f.AsSpan((int)(off + e)));
                ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan((int)(off + e) + 8));
                if (tag == 0) break;
                if (tag != 14) continue;                    // DT_SONAME
                int at = (int)strOff + (int)value;
                int end = System.Array.IndexOf(f, (byte)0, at);
                return System.Text.Encoding.ASCII.GetString(f, at, end - at);
            }
        }
        return null;
    }

    [Fact]
    public void EveryCatalogEntryNamesTheFileItsOwnLibraryDeclares()
    {
        // An import records three names, and the one the loader acts on is the file. Getting it wrong
        // names a module that is not there, and every import bound to that library then fails - which
        // stops the module before its first instruction rather than at the call. The file is not
        // guessable from the library name: several libraries live in a file named nothing like them,
        // and there is no rule to it. Each library declares its own file, and that is what is checked
        // here, so the whole class is settled by the toolchain rather than one entry at a time.
        string? root = ToolchainRoot();
        if (root is null)
            return;                                        // toolchain not installed on this machine

        var wrong = new List<string>();
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
        {
            string? stub = StubArchive(root, entry);
            if (stub is null)
                continue;
            // An archive shipped only in the form a development machine uses describes that machine's
            // layout, not a retail one: this one names a file that exists on no retail system, where
            // the library lives in a file named after its module instead. Which file to load is settled
            // against the machine rather than against that archive, so it is not compared here. What
            // the archive does settle - which names the library publishes - is checked either way.
            if (stub.Contains("_nosubmission_", StringComparison.Ordinal))
                continue;
            string? declared = DeclaredFileName(stub);
            if (declared is null)
                continue;
            string named = entry.Soname ?? entry.Library + ".prx";
            if (named != declared)
                wrong.Add($"{entry.Library}: the catalog loads {named}, the library declares {declared}");
        }

        Assert.True(wrong.Count == 0,
            "These entries name a file the library itself does not:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void EveryCatalogNameIsOneTheToolchainPublishes()
    {
        // The inverse of the check above. A name listed under a library that does not publish it
        // produces an import nothing can bind, and the module never reaches its first instruction. The
        // toolchain ships one stub library per module, and that is what each entry is measured against.
        // Entries whose library ships no stub library are reached on the device only and are checked
        // elsewhere; they are skipped rather than failed.
        string? root = ToolchainRoot();
        if (root is null)
            return;                                        // toolchain not installed on this machine

        // Names the device carries that the toolchain does not offer an application. Each one is
        // confirmed present in the module that publishes it, so an import of it binds; the toolchain
        // simply does not hand it out. Add to this only with that confirmation.
        // Names the device publishes that the link-time archives do not carry. The archives are the
        // authority on what an application may link, so a name here is one the console was measured to
        // export and the archive simply omits.
        var deviceOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "sceSystemServiceLaunchApp",
            "sceKernelGetProsperoSystemSwVersion",
            "sceKernelGetAllowedSdkVersionOnSystem",
            "sysctlbyname",
            "sceKernelGetOpenPsId",
        };

        var wrong = new List<string>();
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
        {
            string? stub = StubArchive(root, entry);
            if (stub is null)
                continue;
            HashSet<string> published = PublishedNames(stub);
            foreach (string name in entry.Exports)
                if (!published.Contains(name) && !deviceOnly.Contains(name))
                    wrong.Add($"{entry.Library}: {name}");
        }

        Assert.True(wrong.Count == 0,
            "These catalog names are not published by the library they are listed under, so an import " +
            "of them could never bind:\n  " + string.Join("\n  ", wrong));
    }
}
