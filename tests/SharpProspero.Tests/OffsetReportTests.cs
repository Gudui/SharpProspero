// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// The offsets contribution tool reads a supplied module and dumps its export surface, and — when asked
// — how it covers the names the SDK needs. These build a real module with a known export and check the
// report reads it back, unwraps a signed container the same way, and reports coverage and misses.
public sealed class OffsetReportTests
{
    [Fact]
    public void Create_ReadsTheExportSurfaceOfAnUnsignedModule()
    {
        byte[] module = BuildModuleExporting("game_frame");

        OffsetReport report = OffsetReport.Create("test.prx", module, firmware: "10.01", includeCoverage: false);

        Assert.Equal("unsigned", report.Container);
        Assert.Equal("10.01", report.Firmware);
        Assert.True(report.ExportsReadable);
        string nid = SceNid.Compute("game_frame");
        Assert.Contains(report.Exports, e => e.Nid == nid && e.IsFunction);
    }

    [Fact]
    public void Create_UnwrapsASignedContainerAndReadsTheSameExports()
    {
        byte[] module = BuildModuleExporting("game_frame");
        byte[] signed = SelfContainer.Sign(module, new SelfSignOptions());

        OffsetReport report = OffsetReport.Create("test.sprx", signed, firmware: null, includeCoverage: false);

        Assert.Equal("signed", report.Container);
        Assert.True(report.ExportsReadable);
        string nid = SceNid.Compute("game_frame");
        Assert.Contains(report.Exports, e => e.Nid == nid);
    }

    [Fact]
    public void Create_Coverage_MatchesTheCatalogLibraryAndReportsMisses()
    {
        // A module that exports one name the SDK needs from libSceVideoOut. Coverage matches that
        // library and reports the one present and the rest as missing — the shape a contributor reads
        // to see what a firmware provides.
        byte[] module = BuildModuleExporting("sceVideoOutOpen");

        OffsetReport report = OffsetReport.Create("libSceVideoOut.sprx", module, firmware: "10.01", includeCoverage: true);

        Assert.NotNull(report.Coverage);
        OffsetCoverage coverage = report.Coverage!;
        Assert.Equal("libSceVideoOut", coverage.MatchedLibrary);
        Assert.Equal(1, coverage.PresentCount);
        Assert.True(coverage.RequiredCount > 1);

        OffsetSymbol present = Assert.Single(coverage.Symbols, s => s.Name == "sceVideoOutOpen");
        Assert.True(present.Present);
        Assert.NotEqual(0ul, present.Address);
        Assert.Equal(SceNid.Compute("sceVideoOutOpen"), present.Nid);

        // A name the SDK needs that this module does not export is reported as a miss, with the
        // identifier it would carry so a contributor can look for it.
        Assert.Contains("sceVideoOutClose", coverage.Missing);
        OffsetSymbol missing = Assert.Single(coverage.Symbols, s => s.Name == "sceVideoOutClose");
        Assert.False(missing.Present);
        Assert.Equal(SceNid.Compute("sceVideoOutClose"), missing.Nid);
    }

    [Fact]
    public void Create_TargetingALibraryReportsEveryNeededNameAsMissing()
    {
        // The module exports nothing libSceAudioOut needs, so targeting that library reports all its
        // names missing rather than matching the library the module actually covers.
        byte[] module = BuildModuleExporting("sceVideoOutOpen");

        OffsetReport report = OffsetReport.Create(
            "mod.prx", module, firmware: null, includeCoverage: true, preferredLibrary: "libSceAudioOut");

        Assert.NotNull(report.Coverage);
        Assert.Equal("libSceAudioOut", report.Coverage!.MatchedLibrary);
        Assert.Equal(0, report.Coverage.PresentCount);
        Assert.Equal(report.Coverage.RequiredCount, report.Coverage.Missing.Count);
    }

    [Fact]
    public void Create_RejectsAFileThatIsNeitherElfNorContainer()
    {
        byte[] garbage = Encoding.ASCII.GetBytes("not a module at all, just some bytes here");
        Assert.Throws<PrxFormatException>(() => OffsetReport.Create("junk.bin", garbage, null, includeCoverage: false));
    }

    // Builds a library module that exports one symbol by name, linked and written the same way the
    // toolchain produces a .prx.
    private static byte[] BuildModuleExporting(string exportName)
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildObjectDefining(exportName), "lib"));
        LinkResolution result = Linker.Resolve(options);
        return DynamicWriter.Write(result, entrySymbol: null, ModuleKind.Library,
            exportSymbols: [exportName], moduleFileName: "test.prx");
    }

    // A minimal ET_REL object defining one global function at a non-zero offset (so it reads back as a
    // defined export) and nothing undefined, so the link needs no stubs.
    private static byte[] BuildObjectDefining(string symbolName)
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0" + symbolName + "\0");
        const int nameOff = 1;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xC3]; // nops then ret

        byte[] symtab = new byte[2 * 24];
        WriteSym(symtab, 1, nameOff, (1 << 4) | 2, 1);                          // global func, defined in .text
        BinaryPrimitives.WriteUInt64LittleEndian(symtab.AsSpan(1 * 24 + 8), 8); // at .text+8 (non-zero)

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
