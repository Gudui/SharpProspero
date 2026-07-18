// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// Covers the pieces that make a link need no start file or stub library from elsewhere: the start
// object the SDK emits, the stubs it generates from its catalog, and a resolve that pulls both in.
public sealed class SelfContainedLinkTests
{
    [Fact]
    public void CrtEmitter_DefinesStartAndReferencesMainAndExit()
    {
        ElfObject crt = ElfObjectReader.Read(CrtEmitter.BuildStartObject(), "crt");

        ElfSymbol start = crt.Symbols.Single(s => s.Name == "_start");
        Assert.False(start.IsUndefined);
        Assert.Equal(SymType.Func, start.Type);

        Assert.Contains(crt.Symbols, s => s.Name == "main" && s.IsUndefined);
        Assert.Contains(crt.Symbols, s => s.Name == "exit" && s.IsUndefined);
    }

    [Fact]
    public void CrtEmitter_EmitsTwoCallRelocations()
    {
        ElfObject crt = ElfObjectReader.Read(CrtEmitter.BuildStartObject(), "crt");

        // The two relocations sit on the executable section and patch the call displacements.
        int textIndex = -1;
        for (int i = 0; i < crt.Sections.Count; i++)
            if (crt.Sections[i] is { IsExecutable: true, Name: ".text" })
                textIndex = i;
        Assert.True(textIndex >= 0);

        IReadOnlyList<ElfRelocation> relocs = crt.Relocations[textIndex];
        Assert.Equal(2, relocs.Count);
        Assert.All(relocs, r => Assert.Equal(RelType.Plt32, r.Type));
        Assert.All(relocs, r => Assert.Equal(-4, r.Addend));
        Assert.Contains(relocs, r => r.Offset == 16);
        Assert.Contains(relocs, r => r.Offset == 23);

        // Each relocation names either main or exit.
        foreach (ElfRelocation r in relocs)
        {
            string name = crt.Symbols[(int)r.SymbolIndex].Name;
            Assert.True(name is "main" or "exit");
        }
    }

    [Fact]
    public void StubCatalog_StubsParseAndMapNamesToTheirModule()
    {
        var provided = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
        {
            StubLibrary stub = StubLibrary.Parse(
                PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
                entry.Library);
            Assert.Equal(entry.Library + ".prx", stub.Soname);
            foreach (string name in stub.Provided)
                provided[name] = stub.Soname;
        }

        Assert.Equal("libSceVideoOut.prx", provided["sceVideoOutOpen"]);
        Assert.Equal("libkernel.prx", provided["sceKernelOpen"]);
        Assert.Equal("libkernel.prx", provided["scePthreadCreate"]);
        Assert.Equal("libc.prx", provided["memcpy"]);
        Assert.Equal("libc.prx", provided["exit"]);
        Assert.Equal("libSceAudioOut.prx", provided["sceAudioOutOutput"]);
        Assert.Equal("libSceRtc.prx", provided["sceRtcGetCurrentClock"]);
        Assert.Equal("libSceRandom.prx", provided["sceRandomGetRandomNumber"]);
    }

    // The filesystem surface reaches these libkernel functions, so the catalog must provide a stub for
    // each. Without them, a self-contained link of any application that lists a directory, makes or
    // removes one, deletes, renames, or truncates a file, or checks a path leaves them unresolved and
    // writes nothing.
    [Theory]
    [InlineData("sceKernelGetdents")]
    [InlineData("sceKernelMkdir")]
    [InlineData("sceKernelRmdir")]
    [InlineData("sceKernelUnlink")]
    [InlineData("sceKernelRename")]
    [InlineData("sceKernelTruncate")]
    [InlineData("sceKernelCheckReachability")]
    public void StubCatalog_ProvidesTheFilesystemKernelFunctions(string name)
    {
        StubCatalog.Entry kernel = StubCatalog.Core.Single(e => e.Library == "libkernel");
        StubLibrary stub = StubLibrary.Parse(
            PrxStubEmitter.BuildObject(kernel.Library, kernel.Exports, kernel.ModuleVersion, kernel.LibraryVersion),
            kernel.Library);
        Assert.Contains(name, stub.Provided);
    }

    // A stub records the version the module publishes, and the loader binds an import only when the
    // version matches. Every module in the catalog publishes 1.1 except the media player, which
    // publishes 1.0; a stub built with the wrong version would install and then fail to bind.
    [Theory]
    [InlineData("libSceAvPlayer", 0x0100)]
    [InlineData("libkernel", 0x0101)]
    [InlineData("libSceVideoOut", 0x0101)]
    public void StubCatalog_RecordsTheModuleVersionEachLibraryPublishes(string library, int expectedModuleVersion)
    {
        StubCatalog.Entry entry = StubCatalog.Core.Single(e => e.Library == library);
        Assert.Equal((ushort)expectedModuleVersion, entry.ModuleVersion);
        Assert.Equal((ushort)1, entry.LibraryVersion);

        StubLibrary stub = StubLibrary.Parse(
            PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
            entry.Library);
        Assert.Equal((ushort)expectedModuleVersion, stub.ModuleVersion);
        Assert.Equal((ushort)1, stub.LibraryVersion);
    }

    [Fact]
    public void Resolve_SelfContained_LeavesNothingUnresolvedAndProducesAModule()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildApplicationObject(), "app"));
        options.ExtraObjects.Add(ElfObjectReader.Read(CrtEmitter.BuildStartObject(), "crt"));
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            options.ExtraStubs.Add(StubLibrary.Parse(
                PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
                entry.Library));

        LinkResolution result = Linker.Resolve(options);

        Assert.Empty(result.Unresolved);
        Assert.Contains("_start", result.Defined.Keys);
        Assert.Contains("main", result.Defined.Keys);
        Assert.Contains(result.Imports, i => i.Name == "sceVideoOutOpen" && i.Soname == "libSceVideoOut.prx");
        Assert.Contains(result.Imports, i => i.Name == "exit" && i.Soname == "libc.prx");

        byte[] module = DynamicWriter.Write(result, CrtEmitter.StartSymbol, ModuleKind.Executable);
        Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(module));
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x10))); // ET_SCE_DYNEXEC

        // The module starts at _start (the injected start object), not at zero or at main.
        ulong entryAddress = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(0x18));
        Assert.NotEqual(0ul, entryAddress);

        // The inspector reads the header and program headers back from the produced module.
        ElfInfo info = ElfInfo.Parse(module);
        Assert.Equal(0xFE10, info.Type);
        Assert.Contains(info.ProgramHeaders, p => p.TypeName == "DYNAMIC");
        Assert.Contains(info.ProgramHeaders, p => p.TypeName == "LOAD");
    }

    [Fact]
    public void StubLibrary_ReadsBackTheVersionsItWasBuiltWith()
    {
        // A library that publishes something other than the usual versions must be carried through
        // exactly; an import that records the wrong version does not bind.
        byte[] bytes = PrxStubEmitter.BuildObject("libMyLib", ["myLibDoThing"],
            moduleVersion: 0x0304, libraryVersion: 0x0007);
        StubLibrary stub = StubLibrary.Parse(bytes, "libMyLib");

        Assert.Equal("libMyLib.prx", stub.Soname);
        Assert.Equal("libMyLib", stub.ModuleName);
        Assert.Equal("libMyLib", stub.LibraryName);
        Assert.Equal(0x0304, stub.ModuleVersion);
        Assert.Equal(0x0007, stub.LibraryVersion);
        Assert.Contains("myLibDoThing", stub.Provided);
    }

    // A module that names its file, its module, and its library differently must round-trip all three
    // (the message dialog is the real case: file libSceMsgDialog.native.prx, module libSceMsgDialog,
    // library libSceMsgDialog.native). A stub that collapsed them would install and then not bind.
    [Fact]
    public void StubLibrary_CarriesAllThreeNamesWhenTheyDiffer()
    {
        byte[] bytes = PrxStubEmitter.BuildObject(
            "libSceMsgDialog.native", ["sceMsgDialogOpen"],
            moduleName: "libSceMsgDialog", soname: "libSceMsgDialog.native.prx");
        StubLibrary stub = StubLibrary.Parse(bytes, "libSceMsgDialog");

        Assert.Equal("libSceMsgDialog.native.prx", stub.Soname);
        Assert.Equal("libSceMsgDialog", stub.ModuleName);
        Assert.Equal("libSceMsgDialog.native", stub.LibraryName);
        Assert.Contains("sceMsgDialogOpen", stub.Provided);
    }

    [Fact]
    public void StubLibrary_DefaultsToTheUsualVersions()
    {
        StubLibrary stub = StubLibrary.Parse(PrxStubEmitter.BuildObject("libMyLib", ["myLibDoThing"]), "libMyLib");
        Assert.Equal(StubLibrary.DefaultModuleVersion, stub.ModuleVersion);
        Assert.Equal(StubLibrary.DefaultLibraryVersion, stub.LibraryVersion);
    }

    [Fact]
    public void StubLibrary_RejectsAMalformedSectionHeaderTable()
    {
        // A stub whose section-header offset points past the file must fail as a link error, not throw
        // an index exception the link command does not catch.
        byte[] bad = new byte[0x40];
        bad[0] = 0x7F; bad[1] = (byte)'E'; bad[2] = (byte)'L'; bad[3] = (byte)'F';
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(0x28), 0x100000); // shoff far past the file
        BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(0x3A), 0x40);     // shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(0x3C), 4);        // shnum

        Assert.Throws<ElfLinkException>(() => StubLibrary.Parse(bad, "bad.stub"));
    }

    [Fact]
    public void Resolve_CarriesAUserLibrarysVersionsIntoTheImport()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildLibraryObject(), "app"));
        // A user's own library that publishes unusual versions.
        options.ExtraStubs.Add(StubLibrary.Parse(
            PrxStubEmitter.BuildObject("libMyLib", ["sceVideoOutOpen"], moduleVersion: 0x0205, libraryVersion: 0x0003),
            "libMyLib"));

        LinkResolution result = Linker.Resolve(options);

        ImportSymbol import = Assert.Single(result.Imports, i => i.Name == "sceVideoOutOpen");
        Assert.Equal("libMyLib.prx", import.Soname);
        Assert.Equal("libMyLib", import.ModuleName);
        Assert.Equal(0x0205, import.ModuleVersion);
        Assert.Equal(0x0003, import.LibraryVersion);
    }

    [Fact]
    public void Write_RecordsTheModulesOwnVersionsNotAFixedPair()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildLibraryObject(), "app"));
        options.ExtraStubs.Add(StubLibrary.Parse(
            PrxStubEmitter.BuildObject("libMyLib", ["sceVideoOutOpen"], moduleVersion: 0x0205, libraryVersion: 0x0003),
            "libMyLib"));
        LinkResolution result = Linker.Resolve(options);

        byte[] module = DynamicWriter.Write(result, entrySymbol: null, ModuleKind.Library);

        // The needed record packs nameOffset | (version << 32) | (id << 48); find it and read the
        // version back out of the produced module rather than trusting the writer's inputs.
        Assert.True(TryFindDynamicValue(module, 0x61000045, out ulong needed), "no needed-module record");
        Assert.Equal(0x0205, (int)((needed >> 32) & 0xFFFF));

        Assert.True(TryFindDynamicValue(module, 0x61000049, out ulong importLib), "no import-library record");
        Assert.Equal(0x0003, (int)((importLib >> 32) & 0xFFFF));
    }

    // Walks the produced module's program headers to its dynamic segment and returns the first value
    // recorded for a tag.
    private static bool TryFindDynamicValue(byte[] module, long tag, out ulong value)
    {
        value = 0;
        ulong phoff = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(0x20));
        int phentsize = BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x36));
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x38));
        for (int i = 0; i < phnum; i++)
        {
            int ph = (int)phoff + i * phentsize;
            if (BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(ph)) != 2) // PT_DYNAMIC
                continue;
            int offset = (int)BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 8));
            int size = (int)BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 32));
            for (int d = offset; d + 16 <= offset + size; d += 16)
            {
                if (BinaryPrimitives.ReadInt64LittleEndian(module.AsSpan(d)) != tag)
                    continue;
                value = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(d + 8));
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void Write_PrxWithExports_RoundTripsThroughTheModuleReader()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildLibraryObject(), "lib"));
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            options.ExtraStubs.Add(StubLibrary.Parse(
                PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
                entry.Library));

        LinkResolution result = Linker.Resolve(options);
        Assert.Empty(result.Unresolved);

        byte[] module = DynamicWriter.Write(result, entrySymbol: null, ModuleKind.Library,
            exportSymbols: ["game_frame"], moduleFileName: "test.prx");

        // A library module type, and the export reads back with the identifier the reader computes.
        Assert.Equal(0xFE18, BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x10))); // ET_SCE_DYNAMIC
        PrxImage image = PrxImage.Parse(module);
        string nid = SceNid.Compute("game_frame");
        PrxExport export = Assert.Single(image.Exports, e => e.Nid == nid);
        Assert.True(export.IsFunction);
    }

    [Fact]
    public void Write_ExportOfUndefinedSymbol_Throws()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildLibraryObject(), "lib"));
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            options.ExtraStubs.Add(StubLibrary.Parse(
                PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
                entry.Library));
        LinkResolution result = Linker.Resolve(options);

        Assert.Throws<ElfLinkException>(() =>
            DynamicWriter.Write(result, null, ModuleKind.Library, exportSymbols: ["not_defined"]));
    }

    // A library object: a defined global "game_frame" at a non-zero offset (so it reads back as a
    // defined export) plus an undefined reference the stubs resolve.
    private static byte[] BuildLibraryObject()
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0game_frame\0sceVideoOutOpen\0");
        const int frameOff = 1, videoOff = 12;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xC3]; // nops then ret

        byte[] symtab = new byte[3 * 24];
        WriteSymValue(symtab, 1, frameOff, (1 << 4) | 2, 1, value: 8);   // game_frame: global func at .text+8
        WriteSym(symtab, 2, videoOff, (1 << 4) | 2, 0);                  // sceVideoOutOpen: undefined

        int off = 0x40;
        int textOff = off; off += text.Length;
        off = (off + 7) & ~7;
        int symOff = off; off += symtab.Length;
        int strOff = off; off += strtab.Length;
        int shstrOff = off; off += shstr.Length;
        off = (off + 7) & ~7;
        int shoff = off;
        byte[] file = new byte[shoff + 5 * 64];

        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 1);    // ET_REL
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E); // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x28), (ulong)shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3A), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3C), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3E), 4);

        text.CopyTo(file.AsSpan(textOff));
        symtab.CopyTo(file.AsSpan(symOff));
        strtab.CopyTo(file.AsSpan(strOff));
        shstr.CopyTo(file.AsSpan(shstrOff));

        WriteShdr(file, shoff, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteShdr(file, shoff + 64, textName, 1, 6, textOff, text.Length, 0, 16, 0);
        WriteShdr(file, shoff + 128, symName, 2, 0, symOff, symtab.Length, 3, 8, 24);
        WriteShdr(file, shoff + 192, strName, 3, 0, strOff, strtab.Length, 0, 1, 0);
        WriteShdr(file, shoff + 256, shstrName, 3, 0, shstrOff, shstr.Length, 0, 1, 0);
        return file;
    }

    private static void WriteSymValue(byte[] table, int index, int nameOff, int info, int shndx, ulong value)
    {
        WriteSym(table, index, nameOff, info, shndx);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(index * 24 + 8), value);
    }

    // A minimal application object: a defined global "main" plus undefined references a self-contained
    // link resolves through the SDK's start object (exit) and its stubs (sceVideoOutOpen).
    private static byte[] BuildApplicationObject()
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0main\0sceVideoOutOpen\0");
        const int mainOff = 1, videoOff = 6;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = [0xC3]; // ret

        byte[] symtab = new byte[3 * 24];
        WriteSym(symtab, 1, mainOff, (1 << 4) | 2, 1);   // main: global func, defined in .text
        WriteSym(symtab, 2, videoOff, (1 << 4) | 2, 0);  // sceVideoOutOpen: global func, undefined

        int off = 0x40;
        int textOff = off; off += text.Length;
        off = (off + 7) & ~7;
        int symOff = off; off += symtab.Length;
        int strOff = off; off += strtab.Length;
        int shstrOff = off; off += shstr.Length;
        off = (off + 7) & ~7;
        int shoff = off;
        byte[] file = new byte[shoff + 5 * 64];

        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 1);    // ET_REL
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E); // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x28), (ulong)shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3A), 64);   // shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3C), 5);    // shnum
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3E), 4);    // shstrndx

        text.CopyTo(file.AsSpan(textOff));
        symtab.CopyTo(file.AsSpan(symOff));
        strtab.CopyTo(file.AsSpan(strOff));
        shstr.CopyTo(file.AsSpan(shstrOff));

        WriteShdr(file, shoff, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteShdr(file, shoff + 64, textName, 1, 6, textOff, text.Length, 0, 16, 0);    // .text ALLOC|EXEC
        WriteShdr(file, shoff + 128, symName, 2, 0, symOff, symtab.Length, 3, 8, 24);   // .symtab -> .strtab
        WriteShdr(file, shoff + 192, strName, 3, 0, strOff, strtab.Length, 0, 1, 0);    // .strtab
        WriteShdr(file, shoff + 256, shstrName, 3, 0, shstrOff, shstr.Length, 0, 1, 0); // .shstrtab
        return file;
    }

    private static void WriteSym(byte[] table, int index, int nameOff, int info, int shndx)
    {
        int b = index * 24;
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(b), (uint)nameOff);
        table[b + 4] = (byte)info;
        BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(b + 6), (ushort)shndx);
    }

    private static void WriteShdr(
        byte[] file, int at, int name, uint type, ulong flags, int offset, int size, uint link, ulong align, ulong entsize)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), (uint)name);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), type);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 8), flags);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 24), (ulong)offset);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 32), (ulong)size);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 40), link);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 48), align);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 56), entsize);
    }
}
