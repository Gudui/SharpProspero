// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK
//
// Reads relocatable objects and archives, and resolves the symbol graph: which object defines each
// symbol, which archive members are pulled in to satisfy a reference, and which symbols are left for
// an imported module to provide. This is the input side of producing a module. The layout,
// relocation, and file-writing phases build on the resolved graph.

using System;
using System.Collections.Generic;
using System.IO;

namespace SharpProspero.Link;

/// <summary>Whether the output is an application or a library module.</summary>
public enum ModuleKind { Executable, Library }

/// <summary>Inputs to a link.</summary>
public sealed class LinkOptions
{
    /// <summary>Objects always included in the output.</summary>
    public List<string> Objects { get; } = [];

    /// <summary>Archives whose members are pulled in only to satisfy a reference.</summary>
    public List<string> Archives { get; } = [];

    /// <summary>Stub libraries that name the modules a reference can be imported from.</summary>
    public List<string> Stubs { get; } = [];

    /// <summary>Objects the SDK supplies directly, always included (e.g. the start object).</summary>
    public List<ElfObject> ExtraObjects { get; } = [];

    /// <summary>Stub libraries the SDK supplies directly, without a file on disk.</summary>
    public List<StubLibrary> ExtraStubs { get; } = [];

    /// <summary>The output module kind.</summary>
    public ModuleKind Kind { get; set; } = ModuleKind.Executable;
}

/// <summary>A reference satisfied by an imported module rather than an included object.</summary>
/// <param name="Name">The plain symbol name.</param>
/// <param name="ModuleName">The module name the providing module publishes, e.g. <c>libkernel</c>.</param>
/// <param name="LibraryName">The library name the providing module publishes, usually the same as the module name.</param>
/// <param name="Soname">The module file name the loader loads, e.g. <c>libkernel.prx</c>.</param>
/// <param name="ModuleVersion">The module version to record, taken from the stub that provides the name.</param>
/// <param name="LibraryVersion">The library version to record, taken from the same stub.</param>
public readonly record struct ImportSymbol(
    string Name,
    string ModuleName,
    string LibraryName,
    string Soname,
    ushort ModuleVersion = StubLibrary.DefaultModuleVersion,
    ushort LibraryVersion = StubLibrary.DefaultLibraryVersion);

/// <summary>The resolved symbol graph.</summary>
public sealed class LinkResolution
{
    /// <summary>Objects included in the output, in order.</summary>
    public required IReadOnlyList<ElfObject> Included { get; init; }

    /// <summary>Global symbol name to the object that defines it.</summary>
    public required IReadOnlyDictionary<string, ElfObject> Defined { get; init; }

    /// <summary>References a stub library provides, resolved to their module.</summary>
    public required IReadOnlyList<ImportSymbol> Imports { get; init; }

    /// <summary>References neither an included object nor a stub provides.</summary>
    public required IReadOnlyList<string> Unresolved { get; init; }

    /// <summary>Archive members the reader could not parse, with the reason. An unexpected entry here
    /// hides the symbols that member would have defined, so it is surfaced rather than passed over.</summary>
    public IReadOnlyList<string> SkippedMembers { get; init; } = [];
}

/// <summary>The input side of the linker: read objects and archives and resolve the symbol graph.</summary>
public static class Linker
{
    /// <summary>
    /// Resolves the symbol graph for <paramref name="options"/>: reads the objects, pulls the archive
    /// members that satisfy an otherwise-undefined reference, and reports what is defined and what is
    /// left for an imported module.
    /// </summary>
    public static LinkResolution Resolve(LinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var included = new List<ElfObject>();
        var defined = new Dictionary<string, ElfObject>(StringComparer.Ordinal);
        var undefined = new HashSet<string>(StringComparer.Ordinal);
        // Names referenced by at least one strong (non-weak) undefined symbol. A reference that is only
        // ever weak and stays unsatisfied is a legal binding to address zero, not a link error.
        var strongUndefined = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in options.Objects)
            Include(ElfObjectReader.Read(File.ReadAllBytes(path), path), included, defined, undefined, strongUndefined);
        foreach (ElfObject extra in options.ExtraObjects)
            Include(extra, included, defined, undefined, strongUndefined);

        // Undefined names the objects the link is built from ask for directly. These always have to
        // resolve. Names an archive member only declares are held to the stricter test below (a
        // relocation has to reference them), so an incidental declaration in a pulled member is not an
        // error the way a genuine reference is.
        var primaryUndefined = new HashSet<string>(strongUndefined, StringComparer.Ordinal);

        // Load the archive members and index which global symbols each one defines.
        var pending = new List<(ElfObject Object, HashSet<string> Defines)>();
        var skipped = new List<string>();
        foreach (string archivePath in options.Archives)
        {
            byte[] bytes = File.ReadAllBytes(archivePath);
            foreach (ArMember member in ArReader.Read(bytes, Path.GetFileName(archivePath)))
            {
                ElfObject obj;
                try { obj = ElfObjectReader.Read(member.Data, member.Name); }
                catch (ElfLinkException ex) { skipped.Add($"{Path.GetFileName(archivePath)}({member.Name}): {ex.Message}"); continue; }
                var defines = new HashSet<string>(StringComparer.Ordinal);
                foreach (ElfSymbol s in obj.Symbols)
                    if (s.IsGlobalOrWeak && !s.IsUndefined && s.Name.Length > 0)
                        defines.Add(s.Name);
                pending.Add((obj, defines));
            }
        }

        // Pull in members that satisfy an undefined reference, until nothing new is added.
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < pending.Count; i++)
            {
                (ElfObject obj, HashSet<string> defines) = pending[i];
                bool needed = false;
                foreach (string name in defines)
                {
                    if (undefined.Contains(name)) { needed = true; break; }
                }
                if (!needed)
                    continue;
                Include(obj, included, defined, undefined, strongUndefined);
                pending.RemoveAt(i);
                i--;
                changed = true;
            }
        }

        // Map every name a stub provides to the stub that provides it, so the module name and the
        // versions it records travel together. Stubs the SDK supplies come first, so a project's own
        // stub cannot shadow a core module's name.
        var providedBy = new Dictionary<string, StubLibrary>(StringComparer.Ordinal);
        foreach (StubLibrary stub in options.ExtraStubs)
            foreach (string name in stub.Provided)
                providedBy.TryAdd(name, stub);
        foreach (string stubPath in options.Stubs)
        {
            StubLibrary stub = StubLibrary.Load(stubPath);
            foreach (string name in stub.Provided)
                providedBy.TryAdd(name, stub);
        }

        // A symbol only has to resolve when a relocation in an included object actually references it.
        // An object may also carry a global declaration (`.globl`) that no relocation uses — an
        // alternate entry an assembler emitted, for instance — and that is not a reference to satisfy.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (ElfObject obj in included)
            foreach (IReadOnlyList<ElfRelocation> relocs in obj.Relocations.Values)
                foreach (ElfRelocation r in relocs)
                    if (r.SymbolIndex < obj.Symbols.Count)
                    {
                        ElfSymbol s = obj.Symbols[(int)r.SymbolIndex];
                        if (s.IsUndefined && s.Name.Length > 0)
                            referenced.Add(s.Name);
                    }

        // Names the linker itself defines: the start and end of an allocated section named like a C
        // identifier, which code reads to walk that section. The writer fills in their addresses.
        var sectionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ElfObject obj in included)
            foreach (ElfSection s in obj.Sections)
                if (s.IsAlloc && s.Name.Length > 0)
                    sectionNames.Add(s.Name);

        var imports = new List<ImportSymbol>();
        var unresolved = new List<string>();
        foreach (string name in undefined)
        {
            if (defined.ContainsKey(name) || !(referenced.Contains(name) || primaryUndefined.Contains(name)))
                continue;
            if (providedBy.TryGetValue(name, out StubLibrary? stub))
                imports.Add(new ImportSymbol(
                    name, stub.ModuleName, stub.LibraryName, stub.Soname, stub.ModuleVersion, stub.LibraryVersion));
            else if (IsEncapsulationSymbol(name, sectionNames, out _, out _))
                continue; // the linker synthesizes this at the section boundary
            else if (LinkerProvided.Contains(name))
                continue; // the linker writes this routine itself and places it with the linkage table
            else if (strongUndefined.Contains(name))
                unresolved.Add(name);
            // A weak-only reference that nothing satisfies is left out; the writer binds it to zero.
        }
        imports.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        unresolved.Sort(StringComparer.Ordinal);

        return new LinkResolution
        {
            Included = included,
            Defined = defined,
            Imports = imports,
            Unresolved = unresolved,
            SkippedMembers = skipped,
        };
    }

    /// <summary>
    /// Recognizes a section-boundary symbol: <c>__start_&lt;section&gt;</c> or <c>__stop_&lt;section&gt;</c>
    /// naming a section that is present. These are defined by the linker at the section's start and end,
    /// the way the system linker does, so code can walk a named section without a table of its own.
    /// </summary>
    /// <summary>
    /// The routines the linker writes itself and places with the linkage table, rather than taking from
    /// an object: the constructor walker the entry calls, and the teardown routine beside it. A
    /// reference to either resolves at layout time, so neither counts as unresolved.
    /// </summary>
    internal static readonly IReadOnlySet<string> LinkerProvided =
        new HashSet<string>(StringComparer.Ordinal) { "_init", "_fini", CompatEmitter.ModuleBaseSymbol };

    internal static bool IsEncapsulationSymbol(string name, ICollection<string> sectionNames, out string section, out bool isStop)
    {
        section = "";
        isStop = false;
        if (name.StartsWith("__start_", StringComparison.Ordinal)) { section = name["__start_".Length..]; isStop = false; }
        else if (name.StartsWith("__stop_", StringComparison.Ordinal)) { section = name["__stop_".Length..]; isStop = true; }
        else return false;
        return section.Length > 0 && sectionNames.Contains(section);
    }

    private static void Include(
        ElfObject obj, List<ElfObject> included,
        Dictionary<string, ElfObject> defined, HashSet<string> undefined, HashSet<string> strongUndefined)
    {
        included.Add(obj);
        foreach (ElfSymbol s in obj.Symbols)
        {
            if (s.Name.Length == 0 || !s.IsGlobalOrWeak)
                continue;
            if (s.IsUndefined)
            {
                if (!defined.ContainsKey(s.Name))
                {
                    undefined.Add(s.Name);
                    if (!s.IsWeak)
                        strongUndefined.Add(s.Name);
                }
            }
            else
            {
                defined.TryAdd(s.Name, obj);
                undefined.Remove(s.Name);
                strongUndefined.Remove(s.Name);
            }
        }
    }
}
