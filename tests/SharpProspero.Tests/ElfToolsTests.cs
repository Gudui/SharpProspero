using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public class ElfToolsTests
{
    // A minimal dynamic module: main defined, one imported function. DynamicWriter emits a real dynamic
    // symbol and string table, so the reader utilities have something to read.
    private static byte[] BuildModule()
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x20, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x20] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var import = new ElfSymbol { Name = "sceKernelFoo", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: RelType.Plt32, Addend: -4) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, import],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        var resolution = new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [new ImportSymbol("sceKernelFoo", "libkernel", "libkernel", "libkernel.prx")],
            Unresolved = [],
        };
        return DynamicWriter.Write(resolution, "main");
    }

    [Fact]
    public void SegmentSizes_ReportsCodeAndAConsistentTotal()
    {
        ElfSegmentSizes sizes = ElfTools.SegmentSizes(BuildModule());
        Assert.True(sizes.Code > 0, "a module with a .text segment has non-zero code");
        Assert.Equal(sizes.Code + sizes.ReadOnly + sizes.Data, sizes.File);
        Assert.True(sizes.Memory >= sizes.File, "memory footprint includes the zero-filled tail");
    }

    [Fact]
    public void DynamicSymbols_ListsTheImportedEntries()
    {
        // A module that imports one function and exports nothing has that one import in its dynamic
        // symbol table (as an undefined entry), read back with a resolvable name.
        IReadOnlyList<ElfSymbolEntry> symbols = ElfTools.DynamicSymbols(BuildModule());
        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.IsImport && s.Name.Length > 0 && s.Type == 2 /* func */);
    }

    [Fact]
    public void Strings_FindsRunsAtOrAboveTheMinimumLength()
    {
        byte[] data = Encoding.ASCII.GetBytes("ab\0hello\0\x01\x02world!\0xy");
        var found = ElfTools.Strings(data, minLength: 4);
        Assert.Contains(found, t => t.Text == "hello");
        Assert.Contains(found, t => t.Text == "world!");
        Assert.DoesNotContain(found, t => t.Text == "ab"); // shorter than the minimum
        Assert.DoesNotContain(found, t => t.Text == "xy");
        // The reported offset points at the first character of the run.
        (long offset, string text) = found.First(t => t.Text == "hello");
        Assert.Equal("hello", Encoding.ASCII.GetString(data, (int)offset, 5));
    }

    [Fact]
    public void Strip_RemovesTheSectionHeadersAndStaysReadable()
    {
        byte[] module = BuildModule();
        byte[] stripped = ElfTools.Strip(module);

        Assert.True(stripped.Length <= module.Length);
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(stripped.AsSpan(0x28))); // e_shoff cleared
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(stripped.AsSpan(0x3C)));    // e_shnum cleared

        // The stripped module still parses and still binds its import.
        ElfInfo info = ElfInfo.Parse(stripped);
        Assert.NotEmpty(info.ProgramHeaders);
        Assert.NotEmpty(ElfTools.DynamicSymbols(stripped));
    }

    [Fact]
    public void Strip_RefusesAModuleWithoutADynamicSegment()
    {
        // A payload is an ET_DYN whose section headers the loader reads; it has no dynamic segment, so
        // stripping them would break it. The utility refuses rather than producing a broken file.
        var crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        var options = new LinkOptions();
        options.ExtraObjects.Add(crt);
        LinkResolution res = Linker.Resolve(options);
        byte[] payload = PayloadWriter.Write(res, PayloadCrtEmitter.StartSymbol);
        Assert.Throws<PrxFormatException>(() => ElfTools.Strip(payload));
    }
}
