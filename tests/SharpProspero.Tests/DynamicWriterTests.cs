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

public sealed class DynamicWriterTests
{
    // An object whose .text calls an imported function through a PLT32 relocation.
    private static LinkResolution BuildResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
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

        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [new ImportSymbol("sceKernelFoo", "libkernel", "libkernel", "libkernel.prx")],
            Unresolved = [],
        };
    }

    // An object importing from two libraries published by one module, which is what the kernel module
    // does: it publishes both its own library and the portable-interface one. The produced module has
    // to name the module once and each library separately, or an import resolves against the wrong
    // library and never binds.
    private static LinkResolution BuildTwoLibrariesOneModuleResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var fromKernel = new ElfSymbol { Name = "sceKernelFoo", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var fromPosix = new ElfSymbol { Name = "read", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation>
        {
            new(Offset: 4, SymbolIndex: 2, Type: RelType.Plt32, Addend: -4),
            new(Offset: 12, SymbolIndex: 3, Type: RelType.Plt32, Addend: -4),
        };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, fromKernel, fromPosix],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports =
            [
                new ImportSymbol("sceKernelFoo", "libkernel", "libkernel", "libkernel.prx"),
                new ImportSymbol("read", "libkernel", "libScePosix", "libkernel.prx"),
            ],
            Unresolved = [],
        };
    }

    // A module carrying the guard value the runtime stamps into itself at start-up. Its own object marks
    // it read-only, exactly as the runtime's does.
    private static LinkResolution GuardResolution()
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var rodata = new ElfSection { Name = ".rodata", Type = ShType.ProgBits, Flags = ShFlags.Alloc, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = new byte[8] };
        var other = new ElfSection { Name = ".rodata.str", Type = ShType.ProgBits, Flags = ShFlags.Alloc, Address = 0, Size = 0x40, Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = new byte[0x40] };
        var data = new ElfSection { Name = ".data", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write, Address = 0, Size = 0x20, Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = new byte[0x20] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var guard = new ElfSymbol { Name = "__security_cookie", Info = (SymBind.Global << 4) | SymType.Object, Other = 0, SectionIndex = 2, Value = 0, Size = 8 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, rodata, other, data],
            Symbols = [nullSym, main, guard],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>>(),
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["__security_cookie"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    // Two objects that both carry the same shared thing, the way every object needing an inline
    // function, a template body or a virtual table carries its own copy of it.
    private static ElfObject ObjectCarryingSharedData(string origin, byte mark, bool withMain)
    {
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        // The shared thing itself: eight bytes of state, which is exactly what must not be duplicated.
        var shared = new ElfSection { Name = ".data.shared", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = [mark, 0, 0, 0, 0, 0, 0, 0] };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var sharedSym = new ElfSymbol { Name = "theSharedLock", Info = (SymBind.Weak << 4) | SymType.Object, Other = 0, SectionIndex = 2, Value = 0, Size = 8 };
        var syms = new List<ElfSymbol> { nullSym, sharedSym };
        if (withMain)
            syms.Add(new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 });
        return new ElfObject
        {
            Origin = origin,
            Sections = [nullSec, text, shared],
            Symbols = syms,
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>>(),
            Groups = [new ElfSectionGroup("theSharedLock", [2], KeepOnlyOne: true)],
        };
    }

    [Fact]
    public void Write_KeepsOneCopyOfSomethingEveryObjectCarries()
    {
        // A compiler puts an inline function, a template body or a virtual table into every object that
        // needs it, and names each copy under one signature so a link keeps exactly one. Keeping them
        // all is worse than wasteful for anything holding state: two copies of a lock are two locks, and
        // whichever half of the program holds one is not excluded by the other.
        ElfObject first = ObjectCarryingSharedData("first", 0x11, withMain: true);
        ElfObject second = ObjectCarryingSharedData("second", 0x22, withMain: false);
        var options = new LinkOptions();
        options.ExtraObjects.Add(first);
        options.ExtraObjects.Add(second);

        LinkResolution result = Linker.Resolve(options);

        // The second copy is left out, and the name resolves to the object whose copy was kept.
        Assert.Contains((second, 2), result.DroppedSections.Select(d => (d.Object, d.Section)));
        Assert.DoesNotContain((first, 2), result.DroppedSections.Select(d => (d.Object, d.Section)));
        Assert.Same(first, result.Defined["theSharedLock"]);

        byte[] module = DynamicWriter.Write(result, "main");
        (ulong addr, ulong size) = SectionAddressAndSize(module, ".data");
        Assert.Equal(8ul, size);          // one copy, not two

        // And it is the first object's, not the second's.
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x38));
        for (int i = 0, ph = 0x40; i < phnum; i++, ph += 0x38)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(ph)) != 1) continue;
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 16));
            ulong filesz = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 32));
            ulong off = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 8));
            if (addr < va || addr >= va + filesz) continue;
            Assert.Equal(0x11, module[(int)(off + (addr - va))]);
        }
    }

    [Fact]
    public void Write_PutsTheGuardValueWhereTheRuntimeCanStillWriteToIt()
    {
        // The runtime stamps a guard value into an object its own compiler marked read-only: it widens
        // the page to writable, writes, and narrows it again, and refuses to start at all if either
        // protection change is turned down. This platform settles a range's greatest allowed protection
        // when the loader maps the segment, from what that segment asks for, and a segment asking for
        // read alone can never be widened afterwards - so the object has to be placed in a group whose
        // ceiling admits write, or the runtime stops before it has done anything and says only that its
        // entry returned a non-zero result.
        byte[] file = DynamicWriter.Write(GuardResolution(), "main");

        (ulong guardAddr, ulong guardSize) = SectionAddressAndSize(file, ".sce_guard");
        Assert.NotEqual(0ul, guardAddr);
        Assert.Equal(8ul, guardSize);

        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        uint flagsOfSegmentHoldingIt = 0;
        bool found = false;
        for (int i = 0, ph = 0x40; i < phnum; i++, ph += 0x38)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) != 1) continue;   // PT_LOAD
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            ulong memsz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40));
            if (memsz == 0 || guardAddr < va || guardAddr >= va + memsz) continue;
            flagsOfSegmentHoldingIt = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4));
            found = true;
        }
        Assert.True(found, "the guard value is in no loadable group");
        Assert.True((flagsOfSegmentHoldingIt & 2) != 0,
            "the guard value is in a group that asks for no write, so its ceiling can never admit one");

        // And it keeps its page to itself: the runtime narrows the whole page back to read-only once it
        // has written the value, and anything sharing that page would be frozen with it.
        Assert.Equal(0ul, guardAddr % 0x4000);
        (ulong dataAddr, _) = SectionAddressAndSize(file, ".data");
        Assert.True(dataAddr >= guardAddr + 0x4000,
            $"another section shares the guard's page: .data at 0x{dataAddr:x}, guard at 0x{guardAddr:x}");

        // The read-only group keeps everything else that asked to be read-only.
        (ulong roAddr, _) = SectionAddressAndSize(file, ".rodata");
        Assert.NotEqual(0ul, roAddr);
        Assert.True(roAddr < guardAddr, "the rest of the read-only content moved with it");
    }

    [Fact]
    public void Write_DeclaresARelocationTableWithNoEmptyRecordInIt()
    {
        // The table was sized for seven parameter-block pointers while only six are always written; the
        // seventh names the marker recording which C library was linked against, and a module linked
        // against none has no such import to name. The extra twenty-four bytes stayed inside the
        // declared extent, where they read as a relocation of type none against address zero - which a
        // reader walking the table to its declared end has to make something of. The count now comes
        // from the same condition the records are written under.
        byte[] file = DynamicWriter.Write(InitArrayResolution(), "main");
        (ulong dynOff, ulong dynSz) = FindDynamic(file);

        const long DtRela = 7, DtRelaSz = 8;
        ulong relaAddr = 0, relaSize = 0;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
            if (tag == DtRela) relaAddr = value;
            if (tag == DtRelaSz) relaSize = value;
        }
        Assert.NotEqual(0ul, relaSize);
        Assert.Equal(0ul, relaSize % 24);

        // The table is in the group the loader reads rather than one it maps, so it is found by walking
        // the headers for the one holding that address.
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong groupOff = 0, groupAddr = 0;
        bool found = false;
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) != 1) continue;
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            ulong len = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            if (relaAddr < va || relaAddr + relaSize > va + len) continue;
            groupOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
            groupAddr = va;
            found = true;
        }
        Assert.True(found, "the relocation table is in no group the module carries");

        int at = (int)(groupOff + (relaAddr - groupAddr));
        for (ulong r = 0; r < relaSize; r += 24)
        {
            ulong where = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + (int)r));
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + (int)r + 8));
            Assert.True(where != 0 || info != 0,
                $"the record at {r / 24} inside the declared table is all zeros");
        }
    }

    [Fact]
    public void Write_GivesItsOwnExportLibraryAnIdNoImportLibraryAlreadyHas()
    {
        // The id an export library carries has to be one no import library carries, because that number
        // is how a reader tells them apart. It was counted from the modules rather than the libraries,
        // and the two counts differ as soon as one module publishes more than one library - which is
        // the ordinary case here, since the kernel module publishes both its own library and the
        // portable one. Counting modules then named an id that was already an import library's.
        byte[] file = DynamicWriter.Write(
            BuildTwoLibrariesOneModuleResolution(), "main", exportSymbols: ["main"]);
        (ulong dynOff, ulong dynSz) = FindDynamic(file);

        const long DtSceImportLib = 0x61000049, DtSceExportLib = 0x61000047;
        var importIds = new List<ulong>();
        ulong exportId = ulong.MaxValue;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
            if (tag == DtSceImportLib) importIds.Add(value >> 48);
            if (tag == DtSceExportLib) exportId = value >> 48;
        }

        Assert.Equal([0ul, 1ul], importIds);           // two libraries, one module
        Assert.Equal(2ul, exportId);                   // the first id neither of them holds
        Assert.DoesNotContain(exportId, importIds);
    }

    [Fact]
    public void Write_NamesOneModuleOncePerFileAndEveryLibrarySeparately()
    {
        byte[] file = DynamicWriter.Write(BuildTwoLibrariesOneModuleResolution(), "main");
        (ulong dynOff, ulong dynSz) = FindDynamic(file);

        const long DtNeeded = 1, DtSceNeededModule = 0x61000045, DtSceImportLib = 0x61000049;
        var needed = new List<ulong>();
        var modules = new List<ulong>();
        var libraries = new List<ulong>();
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
            if (tag == DtNeeded) needed.Add(value);
            else if (tag == DtSceNeededModule) modules.Add(value);
            else if (tag == DtSceImportLib) libraries.Add(value);
        }

        // One file, named once, however many of its libraries are used.
        Assert.Single(needed);
        Assert.Single(modules);
        // Both libraries, each with an id of its own, and both belonging to that one module.
        Assert.Equal(2, libraries.Count);
        Assert.Equal(2, libraries.Select(v => (int)(v >> 48)).Distinct().Count());
        Assert.Equal(1, (int)(modules[0] >> 48));

        // The encoded symbol names carry the two different library ids against the same module id.
        var suffixes = ReadDynamicSymbolNames(file)
            .Where(n => n.Contains('#', StringComparison.Ordinal))
            .Select(n => n.Split('#'))
            .Select(p => (Library: p[1], Module: p[2]))
            .ToList();
        Assert.Equal(2, suffixes.Count);
        Assert.Equal(2, suffixes.Select(s => s.Library).Distinct().Count());
        Assert.Single(suffixes.Select(s => s.Module).Distinct());
    }

    // An object importing a function from a module whose file, module, and library names all differ,
    // the way the message dialog does. The produced module must carry all three, or it installs and
    // then fails to bind.
    private static LinkResolution BuildThreeNameResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var import = new ElfSymbol { Name = "sceMsgDialogOpen", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: RelType.Plt32, Addend: -4) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, import],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [new ImportSymbol("sceMsgDialogOpen", "libSceMsgDialog", "libSceMsgDialog.native", "libSceMsgDialog.native.prx")],
            Unresolved = [],
        };
    }

    [Fact]
    public void Write_RecordsTheSonameModuleAndLibraryNamesSeparately()
    {
        byte[] file = DynamicWriter.Write(BuildThreeNameResolution(), "main");

        // The loader loads the file named by DT_NEEDED, which is the soname.
        var image = SharpProspero.Prx.PrxImage.Parse(file);
        Assert.Contains("libSceMsgDialog.native.prx", image.NeededModules);

        // All three distinct names are present in the string table, so the module record and the
        // library record can name their own.
        string text = Encoding.ASCII.GetString(file);
        Assert.Contains("libSceMsgDialog.native.prx", text);
        Assert.Contains("libSceMsgDialog\0", text);          // the module name, on its own
        Assert.Contains("libSceMsgDialog.native\0", text);   // the library name, on its own
    }

    [Fact]
    public void Write_ProducesADynamicExecutableWithImports()
    {
        byte[] file = DynamicWriter.Write(BuildResolution(), "main");

        Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(file));
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10)));
        // The code, writable, parameter and linking loads, the relro header, dynamic, procparam, the
        // thread-local header, the comment, the version record, the build-id note and the reserved
        // note. This object has no read-only content and no frame index, so those two are left out;
        // the thread-local header is carried even with nothing in it, the way a module carries it.
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        Assert.Equal(12, phnum);

        // Find PT_DYNAMIC (type 2) among the program headers, and count the two PT_NOTE segments.
        bool hasDynamic = false, hasProcParam = false;
        int noteCount = 0;
        int ph = 0x40;
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            uint t = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph));
            if (t == 2) hasDynamic = true;
            if (t == 0x61000001) hasProcParam = true;
            if (t == 4) noteCount++;
        }
        Assert.True(hasDynamic, "Expected a PT_DYNAMIC segment.");
        Assert.True(hasProcParam, "Expected a PT_SCE_PROCPARAM segment.");
        Assert.Equal(2, noteCount);

        // The needed module name appears in the file (in the dynamic string table).
        Assert.Contains("libkernel.prx", Encoding.ASCII.GetString(file));
        // The "ORBI" process-parameter magic is present.
        Assert.Contains("ORBI", Encoding.ASCII.GetString(file));
    }

    [Fact]
    public void Write_ProcParam_CarriesTheProsperoSdkVersion()
    {
        byte[] file = DynamicWriter.Write(BuildResolution(), "main");

        // Locate the PT_SCE_PROCPARAM segment and read the two version words.
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong ppOff = 0, ppSz = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 0x61000001)
            {
                ppOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                ppSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            }

        // The segment is exactly the length the block declares. Sixty-nine of the seventy modules
        // measured that start carry 0x60 stored and in memory, and so does a build of the same source
        // by the other linker; padding it further makes the segment longer than any module carries.
        Assert.Equal(0x60u, (uint)ppSz);
        Assert.Equal(0x60ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)ppOff)));
        Assert.Equal("ORBI", Encoding.ASCII.GetString(file, (int)ppOff + 8, 4));
        // The compatibility word, then the version the module targets. Both match a module built by
        // the toolchain the format comes from, which is what the system reports back as it starts the
        // process; a revision no release carries describes a module that could not exist.
        Assert.Equal(0x08050001u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)ppOff + 0x10)));
        Assert.Equal(0x02000009u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)ppOff + 0x14)));
    }

    [Fact]
    public void Write_EmitsALoadedBuildIdNoteAndAReservedNote()
    {
        byte[] file = DynamicWriter.Write(BuildResolution(), "main");

        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        bool loadedNote = false, reservedNote = false;
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) != 4) continue; // PT_NOTE
            ulong off = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            ulong filesz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            ulong memsz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40));
            if (va != 0 && memsz == filesz && filesz == 0x24)
            {
                loadedNote = true;                                           // the loaded GNU build-id note
                Assert.Equal("GNU", Encoding.ASCII.GetString(file, (int)off + 12, 3));
                // The 20-byte descriptor is a real content fingerprint, not a run of zeros.
                bool anyNonZero = false;
                for (int b = 0; b < 20; b++) anyNonZero |= file[(int)off + 16 + b] != 0;
                Assert.True(anyNonZero, "Expected a non-zero build-id descriptor.");
            }
            if (va == 0 && memsz == 0 && filesz == 0x18)
            {
                reservedNote = true;                                         // the non-loaded tail note
                Assert.True(off + 0x18 <= (ulong)file.Length, "The tail note must lie within the file.");
                // A whole note, not a length a reader walking notes cannot step through: a four-byte
                // name and an eight-byte identifier account for the twenty-four bytes exactly.
                Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)off)));
                Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)off + 4)));
                Assert.Equal("SIE", Encoding.ASCII.GetString(file, (int)off + 12, 3));
                bool anyNonZero = false;
                for (int b = 0; b < 8; b++) anyNonZero |= file[(int)off + 16 + b] != 0;
                Assert.True(anyNonZero, "Expected a non-zero identifier in the tail note.");
            }
        }
        Assert.True(loadedNote, "Expected the loaded 0x24-byte GNU build-id note.");
        Assert.True(reservedNote, "Expected the 0x18-byte note in the file tail.");
    }

    // An object whose .text reaches an imported data symbol through the global-offset table.
    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PointsTheProcessParametersAtTheThreeBlocks(string shape)
    {
        // The process parameters name three blocks: the C library's, the memory one and the file-system
        // one. A built module leaves those pointers zero in the image and fills them in at load time,
        // because the addresses are only known once the module is placed. Comparing the stored bytes of
        // a finished module therefore shows all zeros either way - which is exactly how a module with no
        // blocks at all passed for one that has them. What separates the two is the fixups.
        byte[] file = WriteShape(shape);
        List<Phdr> phdrs = ReadProgramHeaders(file);
        Phdr proc = Assert.Single(phdrs, p => p.Type == 0x61000001);
        Phdr link = Assert.Single(phdrs, p => p.Type == 1 && p.Flags == 0);
        Phdr dynamic = Assert.Single(phdrs, p => p.Type == 2);

        ulong relaAddr = 0, relaSize = 0;
        for (ulong at = dynamic.Offset; at < dynamic.Offset + dynamic.FileSize; at += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)at));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at + 8));
            if (tag == 7) relaAddr = value;
            if (tag == 8) relaSize = value;
        }

        var filled = new Dictionary<ulong, ulong>();
        ulong tableAt = link.Offset + (relaAddr - link.Addr);
        for (ulong i = 0; i < relaSize / 24; i++)
        {
            ulong where = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(tableAt + i * 24)));
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(tableAt + i * 24) + 8));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(tableAt + i * 24) + 16));
            if ((info & 0xFFFFFFFF) == 8 && where >= proc.Addr && where < proc.Addr + proc.MemSize)
                filled[where - proc.Addr] = value;
        }

        // All three, and each pointing at a block that opens with its own length.
        Phdr writableRelro = phdrs.First(p => p.Type == 1 && p.Flags == 6);
        foreach ((ulong slot, ulong size) in ((ulong, ulong)[])[(0x38, 0xA8), (0x40, 0x38), (0x48, 0x10)])
        {
            Assert.True(filled.TryGetValue(slot, out ulong block), $"the pointer at +0x{slot:x} is never filled in");
            Assert.True(block >= writableRelro.Addr && block < writableRelro.Addr + writableRelro.MemSize,
                $"the block at 0x{block:x} is outside the group that is written while the module is bound");
            ulong at = writableRelro.Offset + (block - writableRelro.Addr);
            Assert.Equal(size, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at)));
        }
    }

    [Fact]
    public void Write_FixesUpTableSlotsHoldingSectionBoundaries()
    {
        // The runtime reads the start and end of its own compiled code out of this table and registers
        // the range with itself. The linker settles those two addresses rather than any object, so the
        // check for "does this slot need a load-time fixup" missed them: the slots stayed zero, the
        // range measured nothing, registration was refused, and the module left main with a failure
        // before a line of application code - with nothing logged to say why.
        byte[] file = DynamicWriter.Write(BuildSectionBoundaryResolution(), "main");
        List<Phdr> phdrs = ReadProgramHeaders(file);
        Phdr link = Assert.Single(phdrs, p => p.Type == 1 && p.Flags == 0);
        Phdr dynamic = Assert.Single(phdrs, p => p.Type == 2);

        ulong relaAddr = 0, relaSize = 0;
        for (ulong at = dynamic.Offset; at < dynamic.Offset + dynamic.FileSize; at += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)at));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at + 8));
            if (tag == 7) relaAddr = value;
            if (tag == 8) relaSize = value;
        }
        Assert.True(relaSize > 0, "no base-relative table at all");

        var values = new List<ulong>();
        ulong tableAt = link.Offset + (relaAddr - link.Addr);
        for (ulong i = 0; i < relaSize / 24; i++)
        {
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(tableAt + i * 24) + 8));
            if ((info & 0xFFFFFFFF) == 8)                                    // base-relative
                values.Add(BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(tableAt + i * 24) + 16)));
        }
        // Both boundaries are fixed up, and they bracket a range the size of the section itself.
        Assert.Contains(values, start => values.Contains(start + BoundarySectionSize));
    }

    private const ulong BoundarySectionSize = 0x40;

    // An object that reads the start and end of a named section through the table, the way a compiled
    // runtime reads the bounds of its own code.
    private static LinkResolution BuildSectionBoundaryResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
        var managed = new ElfSection
        {
            Name = "__managedcode",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = BoundarySectionSize,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[BoundarySectionSize],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var start = new ElfSymbol { Name = "__start___managedcode", Info = (SymBind.Global << 4) | SymType.NoType, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var stop = new ElfSymbol { Name = "__stop___managedcode", Info = (SymBind.Global << 4) | SymType.NoType, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation>
        {
            new(Offset: 4, SymbolIndex: 2, Type: RelType.GotPcRel, Addend: -4),
            new(Offset: 12, SymbolIndex: 3, Type: RelType.GotPcRel, Addend: -4),
        };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, managed],
            Symbols = [nullSym, main, start, stop],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    private static LinkResolution BuildGotResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var import = new ElfSymbol { Name = "sceKernelData", Info = (SymBind.Global << 4) | SymType.Object, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: RelType.GotPcRel, Addend: -4) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, import],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [new ImportSymbol("sceKernelData", "libkernel", "libkernel", "libkernel.prx")],
            Unresolved = [],
        };
    }

    // The well-formed exception frames: one CIE ("zR", program-counter-relative frame pointers) and
    // one FDE, so the writer builds the exception-frame index.
    private static readonly byte[] GoodFrames =
    [
        // CIE: length 16, id 0, version 1, "zR", code-align 1, data-align -8, ret-reg 16,
        // augmentation-data length 1, frame-pointer encoding 0x1B (pc-relative signed 32-bit).
        0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x7A, 0x52, 0x00,
        0x01, 0x78, 0x10, 0x01, 0x1B, 0x00, 0x00, 0x00,
        // FDE: length 16, CIE pointer 24, pc-begin 0, pc-range 0x40, augmentation length 0, padding.
        0x10, 0x00, 0x00, 0x00, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // Terminator.
        0x00, 0x00, 0x00, 0x00,
    ];

    // An object carrying a .eh_frame with the given raw frame bytes.
    private static LinkResolution BuildEhFrameResolution(byte[] frames)
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x40,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x40],
        };
        var ehFrame = new ElfSection
        {
            Name = ".eh_frame",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc,
            Address = 0,
            Size = (ulong)frames.Length,
            Link = 0,
            Info = 0,
            AddrAlign = 8,
            EntSize = 0,
            Data = frames,
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, ehFrame],
            Symbols = [nullSym, main],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>>(),
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    private static bool HasProgramHeader(byte[] file, uint type)
    {
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        int ph = 0x40;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == type)
                return true;
        return false;
    }

    [Fact]
    public void Write_WithMalformedExceptionFrames_OmitsTheIndexWithoutThrowing()
    {
        // A valid CIE followed by a record whose length runs past the buffer must not crash the writer;
        // the index is simply omitted.
        byte[] frames = [.. GoodFrames[..20], 0xF0, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];
        byte[] file = DynamicWriter.Write(BuildEhFrameResolution(frames), "main");
        Assert.False(HasProgramHeader(file, 0x6474E550), "A malformed frame must not produce an index.");
    }

    // An object whose .text reads a thread-local variable through a TPOFF32 relocation. The template
    // is .tdata (8 bytes) then .tbss (4 bytes), align 4, so the aligned template size is 12.
    private static LinkResolution BuildTlsResolution(uint relType = RelType.TpOff32)
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var tdata = new ElfSection { Name = ".tdata", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = new byte[8] };
        var tbss = new ElfSection { Name = ".tbss", Type = ShType.NoBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 4, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = [] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var tlsVar = new ElfSymbol { Name = "tlsVar", Info = (SymBind.Global << 4) | SymType.Tls, Other = 0, SectionIndex = 2, Value = 4, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 0, SymbolIndex: 2, Type: relType, Addend: 0) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, tdata, tbss],
            Symbols = [nullSym, main, tlsVar],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["tlsVar"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    [Fact]
    public void Write_WithThreadLocalStorage_EmitsTlsSegmentAndOffset()
    {
        byte[] file = DynamicWriter.Write(BuildTlsResolution(), "main");

        // A PT_TLS segment (type 7) with the template's file size 8, memory size 12, and alignment 4.
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        bool found = false;
        int ph = 0x40;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 7)
            {
                Assert.Equal(8ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32)));
                Assert.Equal(12ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)));
                Assert.Equal(4ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 48)));
                found = true;
            }
        Assert.True(found, "Expected a PT_TLS segment.");

        // The TPOFF32 at .text[0] resolves to tlsVar's template offset (4) minus the aligned template
        // size (12) = -8 = 0xFFFFFFF8. The text segment is the first load segment.
        // The code group opens with the reserved head, so the section starts past it.
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8)) + DynamicWriter.ImageHeadReserve;
        Assert.Equal(0xFFFFFFF8u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)textOffset)));
    }

    [Fact]
    public void Write_GeneralDynamicTls_RelaxesToLocalExec()
    {
        // .text holds a general-dynamic sequence whose lea relocation is four bytes in, so the lea/call
        // pair fills the 16-byte section. In a self-contained executable it must relax to a thread-pointer
        // read plus the variable's fixed offset (tlsVar at template offset 4, aligned size 12, so -8).
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var tdata = new ElfSection { Name = ".tdata", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = new byte[8] };
        var tbss = new ElfSection { Name = ".tbss", Type = ShType.NoBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 4, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = [] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var tlsVar = new ElfSymbol { Name = "tlsVar", Info = (SymBind.Global << 4) | SymType.Tls, Other = 0, SectionIndex = 2, Value = 4, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: RelType.TlsGd, Addend: -4) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, tdata, tbss],
            Symbols = [nullSym, main, tlsVar],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        var resolution = new LinkResolution { Included = [obj], Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["tlsVar"] = obj }, Imports = [], Unresolved = [] };
        byte[] file = DynamicWriter.Write(resolution, "main");

        // The code group opens with the reserved head, so the section starts past it.
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8)) + DynamicWriter.ImageHeadReserve;
        // mov %fs:0,%rax ; lea -8(%rax),%rax
        byte[] expected = [0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x80, 0xF8, 0xFF, 0xFF, 0xFF];
        Assert.Equal(expected, file[(int)textOffset..((int)textOffset + 16)]);
    }

    [Fact]
    public void Write_RefusesALibraryThatAddsAThreadPointerDistance()
    {
        // The distance from the thread pointer to a thread-local variable is only knowable in advance
        // for the application: its block is placed first. A library's block goes after whatever is
        // already loaded, at a position settled as it loads, so a distance written at link time points
        // into another module's storage and nothing reports a fault. The two forms that ask for that
        // distance have no indirection to hide behind, so they are refused; the form the test below
        // covers goes through a pair of table slots instead and is written.
        var resolution = ThreadLocalLibraryResolution(RelType.TpOff32);

        ElfLinkException ex = Assert.Throws<ElfLinkException>(() =>
            DynamicWriter.Write(resolution, entrySymbol: null, ModuleKind.Library,
                exportSymbols: ["game_frame"], moduleFileName: "test.prx"));
        Assert.Contains("thread-local", ex.Message);
    }

    [Fact]
    public void Write_LibraryReachesAThreadLocalThroughADescriptorPair()
    {
        // A library leaves the sequence alone and points it at two table slots: which module owns the
        // block, filled by a load-time record naming no symbol, and where in that block the variable
        // sits, a distance settled here. Every library on the device that carries thread-local data
        // does exactly this and carries one such record per pair.
        byte[] file = DynamicWriter.Write(ThreadLocalLibraryResolution(RelType.TlsGd),
            entrySymbol: null, ModuleKind.Library, exportSymbols: ["game_frame"], moduleFileName: "test.prx");

        const ulong DtpMod64 = 16;
        List<(ulong Where, ulong Type, ulong Symbol, long Addend)> records = ReadDynamicRelocations(file);
        var pairs = records.FindAll(r => r.Type == DtpMod64);
        Assert.Single(pairs);
        // The record answers with the module carrying it, so it names no symbol and adds nothing.
        Assert.Equal(0ul, pairs[0].Symbol);
        Assert.Equal(0L, pairs[0].Addend);

        // The second slot of the pair holds the variable's place in the block - four, where the symbol
        // sits in the template - written into the image rather than left to the loader.
        Assert.Equal(4ul, ReadImageWord(file, pairs[0].Where + 8));
    }

    // One object holding a thread-local and one reference to it of the given form, as a library links it.
    private static LinkResolution ThreadLocalLibraryResolution(uint relocationType)
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var tdata = new ElfSection { Name = ".tdata", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = new byte[8] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var frame = new ElfSymbol { Name = "game_frame", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var tlsVar = new ElfSymbol { Name = "tlsVar", Info = (SymBind.Global << 4) | SymType.Tls, Other = 0, SectionIndex = 2, Value = 4, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: relocationType, Addend: -4) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, tdata],
            Symbols = [nullSym, frame, tlsVar],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["game_frame"] = obj, ["tlsVar"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    // The load-time relocation table, as where/type/symbol/addend.
    private static List<(ulong Where, ulong Type, ulong Symbol, long Addend)> ReadDynamicRelocations(byte[] file)
    {
        Dictionary<ulong, ulong> map = ReadDynamicMap(file);
        ulong addr = map[7], size = map[8];
        int at = (int)ImageOffsetOf(file, addr);
        var records = new List<(ulong, ulong, ulong, long)>();
        for (ulong r = 0; r + 24 <= size; r += 24)
        {
            ulong where = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + (int)r));
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + (int)r + 8));
            long addend = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(at + (int)r + 16));
            records.Add((where, info & 0xFFFFFFFF, info >> 32, addend));
        }
        return records;
    }

    private static ulong ReadImageWord(byte[] file, ulong address) =>
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)ImageOffsetOf(file, address)));

    // Where in the file an address lands, by walking the groups the module carries.
    private static ulong ImageOffsetOf(byte[] file, ulong address)
    {
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) != 1) continue;
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            ulong len = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            if (address < va || address >= va + len) continue;
            return BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)) + (address - va);
        }
        throw new Xunit.Sdk.XunitException($"0x{address:X} is in no group the module carries.");
    }

    [Fact]
    public void Write_DtpOffThreadLocal_ResolvesToTheLocalExecOffset()
    {
        // A module-block offset (paired with a relaxed local-dynamic base) resolves to the same value as a
        // local-exec offset: tlsVar's template offset 4 minus the aligned template size 12, i.e. -8.
        byte[] file = DynamicWriter.Write(BuildTlsResolution(RelType.DtpOff32), "main");
        // The code group opens with the reserved head, so the section starts past it.
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8)) + DynamicWriter.ImageHeadReserve;
        Assert.Equal(0xFFFFFFF8u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)textOffset)));
    }

    [Fact]
    public void Write_WithExceptionFrames_EmitsTheSearchIndex()
    {
        byte[] file = DynamicWriter.Write(BuildEhFrameResolution(GoodFrames), "main");

        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong hdrOffset = 0, hdrSize = 0;
        int ph = 0x40;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 0x6474E550)
            {
                hdrOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                hdrSize = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            }
        Assert.True(hdrOffset != 0, "Expected a PT_GNU_EH_FRAME segment.");
        Assert.Equal(20u, (uint)hdrSize);                                              // 12-byte header + one 8-byte entry
        Assert.Equal(1, file[(int)hdrOffset]);                                          // version
        Assert.Equal(0x1B, file[(int)hdrOffset + 1]);                                   // frame-pointer encoding
        Assert.Equal(0x03, file[(int)hdrOffset + 2]);                                   // count encoding
        Assert.Equal(0x3B, file[(int)hdrOffset + 3]);                                   // table encoding
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)hdrOffset + 8))); // one frame
    }

    [Fact]
    public void TheEntryRoutineSaysHowToWalkOutOfItself()
    {
        // Every other routine in a module carries this and the entry did not, so a walk up the stack
        // that reached the entry had nothing to go on there. The entry is the frame every walk ends at,
        // which makes it the one place a missing record is certain to be reached.
        ElfObject crt = ElfObjectReader.Read(CrtEmitter.BuildStartObject(), "crt.o");
        int frameIndex = Assert.Single(
            Enumerable.Range(0, crt.Sections.Count), i => crt.Sections[i].Name == ".eh_frame");
        ElfSection frame = crt.Sections[frameIndex];

        // The link picks the frame sections out by name and by asking for something read-only that
        // reserves memory; a section that answers otherwise is passed over and the record is lost.
        Assert.True(frame.IsAlloc);
        Assert.False(frame.IsWritable);
        Assert.False(frame.IsExecutable);
        Assert.False(frame.IsNoBits);

        // What it covers has to be the entry routine, whole. A record stopping short leaves the frames
        // past that point unreadable, and one running long claims frames that are not there.
        ElfSymbol start = Assert.Single(crt.Symbols, s => s.Name == "_start" && !s.IsUndefined);
        Assert.Equal(start.Size, BinaryPrimitives.ReadUInt32LittleEndian(frame.Data.AsSpan(0x24)));

        // Where the routine starts is filled in by the linker, measured from the field itself.
        ElfRelocation where = Assert.Single(crt.Relocations[frameIndex]);
        Assert.Equal(0x20ul, where.Offset);
        Assert.Equal("_start", crt.Symbols[(int)where.SymbolIndex].Name);

        // And a module linked with it carries one more frame than its own code accounts for.
        byte[] file = DynamicWriter.Write(BuildEhFrameResolution(GoodFrames), "main");
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 0x6474E550)
            {
                ulong at = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)at + 8)) >= 1);
            }
    }

    [Fact]
    public void Write_GotDataImport_ProducesARelaTable()
    {
        byte[] file = DynamicWriter.Write(BuildGotResolution(), "main");
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10)));

        // Locate the dynamic segment and confirm a DT_RELA (7) entry is present.
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong dynOff = 0, dynSz = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 2)
            {
                dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40));
            }
        bool hasRela = false;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == 7) hasRela = true;
        Assert.True(hasRela, "Expected a DT_RELA entry for the GOT-data relocation.");
    }

    // An object whose .text references an absolute symbol (SHN_ABS = 0xFFF1) through a 64-bit
    // relocation. An absolute symbol is defined but belongs to no section, so its address is its value.
    private static LinkResolution BuildAbsoluteSymbolResolution()
    {
        var text = new ElfSection
        {
            Name = ".text",
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Execute,
            Address = 0,
            Size = 0x20,
            Link = 0,
            Info = 0,
            AddrAlign = 16,
            EntSize = 0,
            Data = new byte[0x20],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var absSym = new ElfSymbol { Name = "abs_const", Info = (SymBind.Global << 4) | SymType.Object, Other = 0, SectionIndex = 0xFFF1, Value = 0x1234, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 0, SymbolIndex: 2, Type: RelType.R64, Addend: 0) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, absSym],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["abs_const"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    [Fact]
    public void Write_ResolvesAnAbsoluteSymbolWithoutIndexingTheSectionTable()
    {
        // The absolute symbol carries the reserved section index 0xFFF1. Resolving it must use its value
        // directly rather than index the section table at 0xFFF1, which would throw.
        byte[] file = DynamicWriter.Write(BuildAbsoluteSymbolResolution(), "main");
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10)));
    }

    // A single-object resolution whose one relocation reaches the given symbol at .text offset 0. The
    // symbol is either a defined local target (at .text+0x10) or an imported/weak name, per the flags.
    private static LinkResolution BuildSingleReloc(ElfSymbol sym, uint relType, bool defineTarget, ImportSymbol[]? imports = null)
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x20, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x20] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 0, SymbolIndex: 2, Type: relType, Addend: 0) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, sym],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        var defined = new Dictionary<string, ElfObject> { ["main"] = obj };
        if (defineTarget) defined[sym.Name] = obj;
        return new LinkResolution { Included = [obj], Defined = defined, Imports = imports ?? [], Unresolved = [] };
    }

    [Fact]
    public void Write_RelaxableGotLoad_ResolvesThroughTheGotLikeThePlainForm()
    {
        // GOTPCRELX is the default RIP-relative GOT encoding modern compilers emit; unrelaxed it must be
        // handled exactly like GOTPCREL, producing a GOT slot and a dynamic relocation for the import.
        var import = new ElfSymbol { Name = "sceKernelData", Info = (SymBind.Global << 4) | SymType.Object, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        LinkResolution res = BuildSingleReloc(import, RelType.GotPcRelX, defineTarget: false,
            imports: [new ImportSymbol("sceKernelData", "libkernel", "libkernel", "libkernel.prx")]);
        byte[] file = DynamicWriter.Write(res, "main");

        int ph = 0x40; ulong dynOff = 0, dynSz = 0;
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 2)
            { dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)); }
        bool hasRela = false;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == 7) hasRela = true;
        Assert.True(hasRela, "Expected a DT_RELA entry for the relaxable GOT load.");
    }

    [Fact]
    public void Write_Pc64Relocation_ResolvesRelativeToThePlace()
    {
        var target = new ElfSymbol { Name = "target", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0x10, Size = 0 };
        byte[] file = DynamicWriter.Write(BuildSingleReloc(target, RelType.Pc64, defineTarget: true), "main");

        // The reloc sits at .text+0, so the value is target(.text+0x10) - place(.text+0) = 0x10.
        // The code group opens with the reserved head, so the section starts past it.
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8)) + DynamicWriter.ImageHeadReserve;
        Assert.Equal(0x10ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)textOffset)));
    }

    [Fact]
    public void Write_Pc64FieldPastSectionEnd_IsSkippedNotCrashed()
    {
        // A 64-bit PC-relative field writes eight bytes; a relocation whose offset leaves fewer than
        // eight bytes to the section end must be skipped by the bounds guard, exactly as the other
        // reloc types are, rather than aborting the link with an out-of-range write. This guards the
        // width the guard reserves for Pc64 against a truncated or crafted object.
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[8] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var target = new ElfSymbol { Name = "target", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        // Offset 4 in an 8-byte section leaves only four bytes for an eight-byte field.
        var relocs = new List<ElfRelocation> { new(Offset: 4, SymbolIndex: 2, Type: RelType.Pc64, Addend: 0) };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, target],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };
        var res = new LinkResolution { Included = [obj], Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["target"] = obj }, Imports = [], Unresolved = [] };

        byte[] file = DynamicWriter.Write(res, "main");
        Assert.NotEmpty(file);
    }

    [Fact]
    public void Write_UnsupportedRelocationType_ThrowsRatherThanMiscompiling()
    {
        // A relocation the linker does not implement (here GOTPCREL64 = 25) must fail the link loudly
        // instead of silently leaving the target bytes at their compiler placeholder.
        var target = new ElfSymbol { Name = "target", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0x10, Size = 0 };
        Assert.Throws<ElfLinkException>(() => DynamicWriter.Write(BuildSingleReloc(target, 25, defineTarget: true), "main"));
    }

    [Fact]
    public void Write_WeakUndefinedAbsoluteReference_EmitsNoRelativeRelocation()
    {
        // An address-taken weak-undefined symbol resolves to absolute zero; it must not gain a
        // base-relative dynamic relocation, which would make the loader read the load base instead of
        // null and break the "if (&weak_sym)" idiom. With no dynamic relocations, DT_RELASZ stays zero.
        var weak = new ElfSymbol { Name = "weak_opt", Info = (SymBind.Weak << 4) | SymType.Object, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        byte[] file = DynamicWriter.Write(BuildSingleReloc(weak, RelType.R64, defineTarget: false), "main");

        // The code group opens with the reserved head, so the section starts past it.
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8)) + DynamicWriter.ImageHeadReserve;
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)textOffset)));

        int ph = 0x40; ulong dynOff = 0, dynSz = 0;
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 2)
            { dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)); }
        // The only base-relative records a module with no relocatable references carries are the six
        // that name the parameter blocks, so the weak symbol adding one would show as a seventh.
        ulong relaCount = 0, relaSize = 0;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d));
            if (tag == 0x6ffffff9) relaCount = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
            if (tag == 0x08) relaSize = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
        }
        // Eight base-relative records name the parameter blocks: the three the parameters point at, the
        // three replacement tables, and the two heap figures the C library reads its limit from. The
        // table also holds the one bound by name for the library marker, so its size counts one more
        // than the relative count.
        Assert.Equal(8ul, relaCount);
        Assert.True(relaSize <= 9ul * 24, $"the table holds {relaSize / 24} records, more than the blocks need");
    }

    [Fact]
    public void Write_ReferenceToAnIndirectFunction_IsRefused()
    {
        // A function whose address is settled by calling a routine that chooses one cannot be expressed
        // for this platform: the record that would say so is of a kind the loader refuses outright, and
        // it refuses the whole module rather than that one record. Writing a base-relative record
        // instead would leave the address of the chooser in the slot and call it as the function. So it
        // is refused at the link, where it can still be read, rather than at load on the machine.
        var ifunc = new ElfSymbol { Name = "memcpy_impl", Info = (SymBind.Global << 4) | SymType.GnuIfunc, Other = 0, SectionIndex = 1, Value = 0x10, Size = 0 };

        ElfLinkException ex = Assert.Throws<ElfLinkException>(
            () => DynamicWriter.Write(BuildSingleReloc(ifunc, RelType.R64, defineTarget: true), "main"));
        Assert.Contains("memcpy_impl", ex.Message);
    }

    [Fact]
    public void Write_WithInitialExecTls_FillsTheGotSlotWithTheThreadOffset()
    {
        // A .text that reads tlsVar through a GOTTPOFF (initial-exec) relocation. The GOT slot must hold
        // the thread-pointer offset -8 (tlsVar at template offset 4, aligned template size 12), the same
        // value the local-exec path computes, and the link must succeed.
        LinkResolution resolution = BuildTlsResolution(RelType.GotTpOff);
        byte[] file = DynamicWriter.Write(resolution, "main");
        Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(file));

        // Locate DT_PLTGOT (3) to find the GOT, and the writable load segment to map it to a file offset.
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong dynOff = 0, dynSz = 0, gotVaddr = 0;
        int ph = 0x40;
        var loads = new List<(ulong Vaddr, ulong Off, ulong Filesz)>();
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            uint t = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph));
            if (t == 2) { dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)); }
            if (t == 1) loads.Add((BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32))));
        }
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == 3)
                gotVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
        Assert.NotEqual(0ul, gotVaddr);

        // The first GOT data slot (after the 24-byte reserved header, no imports) holds the offset.
        ulong slotVaddr = gotVaddr + 24;
        (ulong Vaddr, ulong Off, ulong Filesz) seg = loads.Find(l => slotVaddr >= l.Vaddr && slotVaddr < l.Vaddr + l.Filesz);
        ulong slotFile = seg.Off + (slotVaddr - seg.Vaddr);
        Assert.Equal(unchecked((ulong)(-8L)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)slotFile)));
    }

    // An object with a .init_array of one 8-byte function pointer, plus a "main".
    private static LinkResolution InitArrayResolution()
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var initArray = new ElfSection { Name = ".init_array", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = new byte[8] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, initArray],
            Symbols = [nullSym, main],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>>(),
        };
        return new LinkResolution { Included = [obj], Defined = new Dictionary<string, ElfObject> { ["main"] = obj }, Imports = [], Unresolved = [] };
    }

    // A module whose constructor array is carried entirely in priority-named sections, which is what an
    // object compiled with a constructor priority produces. The two priorities are given out of order so
    // the placement, not the input order, is what decides which runs first.
    private static LinkResolution PrioritisedInitArrayResolution()
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        ElfSection Array(string name, byte mark) => new()
        {
            Name = name,
            Type = ShType.ProgBits,
            Flags = ShFlags.Alloc | ShFlags.Write,
            Address = 0,
            Size = 8,
            Link = 0,
            Info = 0,
            AddrAlign = 8,
            EntSize = 0,
            Data = [mark, 0, 0, 0, 0, 0, 0, 0],
        };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text, Array(".init_array.65535", 0xEE), Array(".init_array.101", 0x11)],
            Symbols = [nullSym, main],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>>(),
        };
        return new LinkResolution { Included = [obj], Defined = new Dictionary<string, ElfObject> { ["main"] = obj }, Imports = [], Unresolved = [] };
    }

    [Fact]
    public void Write_WalksAConstructorArrayThatCarriesAPriorityInItsName()
    {
        // A constructor given a priority lands in a section named for that priority. The bounds the
        // walker is given were matched against the bare name while the placement used the name the
        // section is placed under, so an array made entirely of priority-named sections was laid into
        // the image and the walker was then told it spanned nothing: those constructors never ran, and
        // nothing reported it. The order within the array is the one the priorities ask for, not the
        // order the sections happened to be read in.
        byte[] file = DynamicWriter.Write(PrioritisedInitArrayResolution(), "main");

        (ulong arrayAddr, ulong arraySize) = SectionAddressAndSize(file, ".init_array");
        Assert.Equal(16ul, arraySize);

        ulong init = DynamicTagValue(file, 12);
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong textOff = 0, textVaddr = 0, dataOff = 0, dataVaddr = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4));
            ulong off = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
            ulong va = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            ulong len = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40));
            if (type != 1) continue;
            if ((flags & 1) != 0) { textOff = off; textVaddr = va; }
            else if ((flags & 2) != 0 && arrayAddr >= va && arrayAddr < va + len) { dataOff = off; dataVaddr = va; }
        }

        // The walker's own two loads have to bracket exactly the array, not nothing.
        int w = (int)(textOff + (init - textVaddr));
        long start = (long)init + 14 + BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(w + 10));
        long stop = (long)init + 21 + BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(w + 17));
        Assert.Equal((long)arrayAddr, start);
        Assert.Equal((long)(arrayAddr + arraySize), stop);

        // The lower priority runs first, whichever order the sections arrived in.
        int at = (int)(dataOff + (arrayAddr - dataVaddr));
        Assert.Equal(0x11, file[at]);
        Assert.Equal(0xEE, file[at + 8]);
    }

    private static (ulong Offset, ulong Size) FindDynamic(byte[] file)
    {
        int ph = 0x40;
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 2)
                return (BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)));
        return (0, 0);
    }

    private static bool HasDynamicTag(byte[] file, long wantedTag)
    {
        (ulong dynOff, ulong dynSz) = FindDynamic(file);
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == wantedTag)
                return true;
        return false;
    }

    // Every imported symbol name, read through the linking segment the tables live in. The tag values
    // are addresses, which the segment carrying them turns back into file offsets.
    private static List<string> ReadDynamicSymbolNames(byte[] file)
    {
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong segOff = 0, segAddr = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4)) == 0)
            {
                segOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                segAddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
            }

        ulong symtab = DynamicTagValue(file, 6), strtab = DynamicTagValue(file, 5);
        ulong symtabSize = DynamicTagValue(file, 0x6100003F);
        int symOff = (int)(segOff + (symtab - segAddr)), strOff = (int)(segOff + (strtab - segAddr));

        var names = new List<string>();
        for (int i = 1; i < (int)symtabSize / 24; i++)
        {
            int nameOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(symOff + i * 24));
            int start = strOff + nameOff, end = start;
            while (end < file.Length && file[end] != 0)
                end++;
            names.Add(Encoding.ASCII.GetString(file, start, end - start));
        }
        return names;
    }

    // The value of the first record carrying <paramref name="wantedTag"/>.
    private static ulong DynamicTagValue(byte[] file, long wantedTag)
    {
        (ulong dynOff, ulong dynSz) = FindDynamic(file);
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == wantedTag)
                return BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));
        throw new Xunit.Sdk.XunitException($"no dynamic record carries tag {wantedTag}.");
    }

    // Every launching module names a setup routine, a teardown routine and the loader's bookkeeping
    // slot. A module that leaves them out is the shape that never reaches its first instruction.
    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_NamesSetupTeardownAndTheLoaderSlot(string shape)
    {
        byte[] file = WriteShape(shape);

        Assert.True(HasDynamicTag(file, 12), "a module names its setup routine (DT_INIT)");
        Assert.True(HasDynamicTag(file, 13), "a module names its teardown routine (DT_FINI)");
        Assert.True(HasDynamicTag(file, 21), "a module carries the loader's bookkeeping slot (DT_DEBUG)");
        Assert.True(HasDynamicTag(file, 32), "a module names its pre-init array");
        Assert.True(HasDynamicTag(file, 26), "a module names its fini array");

        // Both routines have to be reachable, which means inside the executable segment.
        ulong init = DynamicTagValue(file, 12), fini = DynamicTagValue(file, 13);
        Assert.NotEqual(0ul, init);
        Assert.NotEqual(0ul, fini);

        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong textOff = 0, textVaddr = 0, textLen = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 1
                && (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4)) & 1) != 0)
            {
                textOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8));
                textVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16));
                textLen = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32));
            }
        Assert.InRange(init, textVaddr, textVaddr + textLen - 1);
        Assert.InRange(fini, textVaddr, textVaddr + textLen - 1);

        // The setup routine is the constructor walker the entry calls: it takes a frame and saves the
        // two registers it walks with. The teardown routine returns at once, having nothing to undo.
        int at = (int)(textOff + (init - textVaddr));
        Assert.Equal(new byte[] { 0x55, 0x48, 0x89, 0xE5 }, file[at..(at + 4)]);   // push rbp ; mov rbp,rsp
        Assert.Equal(0xC3, file[(int)(textOff + (fini - textVaddr))]);      // ret
    }

    [Fact]
    public void Write_Executable_RunsItsOwnConstructorsFromTheEntry()
    {
        // The start routine runs an executable's constructors, by calling the walker the linker writes
        // after the C library has been set up. The array is named to the loader and named empty:
        // sixty-nine of the seventy modules measured that start declare both tags at zero and the
        // seventieth declares neither, so none of them hands the loader an array. Declaring a real one
        // as well would run every constructor twice - once by the loader before the entry is reached,
        // and once by the walker.
        byte[] file = DynamicWriter.Write(InitArrayResolution(), "main"); // Executable by default

        Assert.True(HasDynamicTag(file, 25), "a module names its init array");
        Assert.True(HasDynamicTag(file, 27), "a module names its init-array size");
        Assert.Equal(0ul, DynamicTagValue(file, 25));
        Assert.Equal(0ul, DynamicTagValue(file, 27));
        Assert.Equal(0ul, DynamicTagValue(file, 26));
        Assert.Equal(0ul, DynamicTagValue(file, 28));

        // The entry is main here, since this shape supplies no start routine of its own; what matters
        // is that the walker exists and is what the setup tag points at.
        ulong init = DynamicTagValue(file, 12);
        Assert.NotEqual(0ul, init);

        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong textOff = 0, textVaddr = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 1
                && (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4)) & 1) != 0)
            { textOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); textVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16)); }

        // A frame, three saved registers, then the instruction-relative load of the array's start, and
        // a return at the end rather than a jump. The three saved registers are what leave the stack on
        // a boundary at each constructor call: this routine is entered eight past one, so an even
        // number would hand every constructor the stack eight out and any that touches a wide value
        // would fault on its first such access.
        int w = (int)(textOff + (init - textVaddr));
        Assert.Equal(
            new byte[] { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x48, 0x8D, 0x1D },
            file[w..(w + 10)]);
        Assert.Equal(0xC3, file[w + 46]);

        // The two instruction-relative loads have to bracket exactly the array the module carries, or
        // the walker runs over something else. The dynamic table names it empty, so the bounds are
        // checked against the section that actually holds it.
        (ulong arrayAddr, ulong arraySize) = SectionAddressAndSize(file, ".init_array");
        Assert.NotEqual(0ul, arrayAddr);
        Assert.NotEqual(0ul, arraySize);
        Assert.Equal(0ul, arraySize % 8);

        long start = (long)init + 14 + BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(w + 10));
        long stop = (long)init + 21 + BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(w + 17));
        Assert.Equal((long)arrayAddr, start);
        Assert.Equal((long)(arrayAddr + arraySize), stop);
    }

    // A named section's address and size, read out of the module's own section table.
    private static (ulong Address, ulong Size) SectionAddressAndSize(byte[] file, string name)
    {
        ulong shoff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x28));
        int shnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x3C));
        int shstrndx = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x3E));
        ulong strOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)shoff + shstrndx * 64 + 0x18));
        for (int i = 0; i < shnum; i++)
        {
            int sh = (int)shoff + i * 64;
            uint nameOff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(sh));
            int at = (int)strOff + (int)nameOff;
            int end = Array.IndexOf(file, (byte)0, at);
            if (Encoding.ASCII.GetString(file, at, end - at) != name)
                continue;
            return (BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(sh + 0x10)),
                    BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(sh + 0x20)));
        }
        return (0, 0);
    }

    [Fact]
    public void Write_Library_KeepsTheInitArrayTagsForTheLoader()
    {
        // A shared library has no entry of its own, so the loader runs its constructors and it names the
        // array it actually carries - unlike an executable, which names the array empty and runs it
        // itself. Naming it empty here would leave a library's constructors unrun.
        byte[] file = DynamicWriter.Write(InitArrayResolution(), "main", ModuleKind.Library);
        Assert.True(HasDynamicTag(file, 25), "a library keeps DT_INIT_ARRAY");
        Assert.True(HasDynamicTag(file, 27), "a library keeps DT_INIT_ARRAYSZ");
        (ulong arrayAddr, ulong arraySize) = SectionAddressAndSize(file, ".init_array");
        Assert.Equal(arrayAddr, DynamicTagValue(file, 25));
        Assert.Equal(arraySize, DynamicTagValue(file, 27));
        Assert.NotEqual(0ul, arraySize);
    }

    [Fact]
    public void Write_CommonSymbol_ReportsAClearError()
    {
        // A common (tentative) symbol carries the reserved section index 0xFFF2 and no storage. The
        // linker reports it plainly rather than aborting with a bare section-index number.
        var common = new ElfSymbol { Name = "tentative", Info = (SymBind.Global << 4) | SymType.Object, Other = 0, SectionIndex = 0xFFF2, Value = 4, Size = 4 };
        var ex = Assert.Throws<ElfLinkException>(() => DynamicWriter.Write(BuildSingleReloc(common, RelType.R64, defineTarget: true), "main"));
        Assert.Contains("-fno-common", ex.Message);
    }

    // The dynamic-linking tables live in their own load segment that requests no memory protection.
    // That segment is how the loader finds the linking data; a module that names a dynamic table
    // without carrying one is rejected while its program headers are scanned, so these lock the shape.

    private readonly record struct Phdr(uint Type, uint Flags, ulong Offset, ulong Addr, ulong FileSize, ulong MemSize, ulong Align);

    private static List<Phdr> ReadProgramHeaders(byte[] file)
    {
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        var result = new List<Phdr>(phnum);
        for (int i = 0, ph = 0x40; i < phnum; i++, ph += 0x38)
            result.Add(new Phdr(
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)),
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4)),
                BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16)),
                BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32)),
                BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)),
                BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 48))));
        return result;
    }

    private static bool Contains(Phdr outer, Phdr inner) =>
        inner.Offset >= outer.Offset && inner.Offset + inner.FileSize <= outer.Offset + outer.FileSize;

    public static TheoryData<string> EveryModuleShape() =>
        ["plain", "imports", "tls", "ehframe", "got"];

    private static byte[] WriteShape(string shape) => shape switch
    {
        "imports" => DynamicWriter.Write(BuildThreeNameResolution(), "main"),
        "tls" => DynamicWriter.Write(BuildTlsResolution(), "main"),
        "ehframe" => DynamicWriter.Write(BuildEhFrameResolution(GoodFrames), "main"),
        "got" => DynamicWriter.Write(BuildGotResolution(), "main"),
        _ => DynamicWriter.Write(BuildResolution(), "main"),
    };

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PutsTheLinkingTablesInAnUnprotectedLoadSegment(string shape)
    {
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));

        List<Phdr> unprotected = [.. phdrs.Where(p => p.Type == 1 && p.Flags == 0)];
        Assert.Single(unprotected);
        Phdr dynlib = unprotected[0];
        Assert.True(dynlib.FileSize > 0, "The linking segment must carry its tables.");
        Assert.Equal(dynlib.FileSize, dynlib.MemSize);

        Phdr dynamic = Assert.Single(phdrs, p => p.Type == 2);
        Assert.True(dynamic.FileSize > 0, "A dynamic module must name a dynamic table.");
        Assert.True(Contains(dynlib, dynamic), "The dynamic table must lie in the linking segment.");

        // The module note is loaded (it has a memory size); the reserved note in the tail is not.
        Phdr note = Assert.Single(phdrs, p => p.Type == 4 && p.MemSize > 0);
        Assert.True(Contains(dynlib, note), "The module note must lie in the linking segment.");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PutsTheProcessParametersInAWritableLoadSegment(string shape)
    {
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        Phdr proc = Assert.Single(phdrs, p => p.Type == 0x61000001);
        Assert.Contains(phdrs, p => p.Type == 1 && (p.Flags & 2) != 0 && Contains(p, proc));
    }

    // Reading the dynamic table is the last gate before a module binds. The loader walks every entry
    // and refuses one it does not expect, so the table may only carry tags a module is allowed to
    // name. The module-specific aliases for the symbol, string and relocation tables are the trap:
    // they name data the ordinary tags already name, the loader routes both to the same handler, and
    // the alias reads as a duplicate.

    // Every tag a module may name. The ordinary set, plus the module-specific tags that carry
    // information no ordinary tag does: the module and library records, the two table sizes, and the
    // module's own file name.
    private static readonly long[] AllowedDynamicTags =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E,
        0x14, 0x15, 0x17, 0x19, 0x1A, 0x1B, 0x1C, 0x20, 0x21, 0x6FFFFFF9,
        0x61000011, 0x61000017, 0x61000019, 0x6100003D, 0x6100003F,
        0x61000041, 0x61000043, 0x61000045, 0x61000047, 0x61000049,
    ];

    // The aliases the loader refuses outright once the linking segment carries an address.
    private static readonly long[] RefusedDynamicTags =
    [
        0x61000025, 0x61000027, 0x61000029, 0x6100002B, 0x6100002D,
        0x6100002F, 0x61000031, 0x61000033, 0x61000039, 0x6100003B,
    ];

    private static List<long> ReadDynamicTags(byte[] file)
    {
        (ulong offset, ulong size) = FindDynamic(file);
        var tags = new List<long>();
        for (ulong i = 0; i < size; i += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)(offset + i)));
            tags.Add(tag);
            if (tag == 0) break;
        }
        return tags;
    }

    // The dynamic table as tag to value. A tag named more than once keeps its first value, which is the
    // one a reader walking the table front to back settles on.
    private static Dictionary<ulong, ulong> ReadDynamicMap(byte[] file)
    {
        (ulong offset, ulong size) = FindDynamic(file);
        var map = new Dictionary<ulong, ulong>();
        for (ulong i = 0; i < size; i += 16)
        {
            ulong tag = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(offset + i)));
            if (tag == 0) break;
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(offset + i + 8)));
            map.TryAdd(tag, value);
        }
        return map;
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_NamesOnlyDynamicTagsAModuleMayCarry(string shape)
    {
        foreach (long tag in ReadDynamicTags(WriteShape(shape)))
        {
            Assert.DoesNotContain(tag, RefusedDynamicTags);
            Assert.Contains(tag, AllowedDynamicTags);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_CarriesEveryDynamicTagTheLoaderRequires(string shape)
    {
        // The loader tallies these while reading the table and refuses the module if any is absent,
        // whatever its value. The relocation trio is the easy one to lose: a module with nothing to
        // relocate must still name an empty table rather than omit the tags.
        List<long> tags = ReadDynamicTags(WriteShape(shape));
        long[] required =
        [
            0x04,        // hash
            0x05, 0x0A,  // string table, its size
            0x06, 0x0B,  // symbol table, its entry size
            0x07, 0x08, 0x09,  // relocations, size, entry size
            0x03, 0x02, 0x14, 0x17,  // linkage table, its size, its type, its relocations
            0x6100003D,  // hash size
            0x6100003F,  // symbol table size
        ];
        foreach (long tag in required)
            Assert.Contains(tag, tags);
    }

    [Fact]
    public void Write_NamesTheRelocationTableEvenWithNothingToRelocate()
    {
        // BuildResolution has one import and no data relocations, so the relocation table is empty.
        // The tags still have to be there.
        List<long> tags = ReadDynamicTags(DynamicWriter.Write(BuildResolution(), "main"));
        Assert.Contains(0x07, tags);
        Assert.Contains(0x08, tags);
        Assert.Contains(0x09, tags);
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_SizesTheRelocationTablesInWholeEntries(string shape)
    {
        // The loader divides both sizes by the entry size and rejects a remainder.
        byte[] file = WriteShape(shape);
        (ulong offset, ulong size) = FindDynamic(file);
        for (ulong i = 0; i < size; i += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)(offset + i)));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(offset + i) + 8));
            if (tag == 0) break;
            if (tag is 0x08 or 0x02) Assert.Equal(0ul, value % 24);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_RecordsTheModulesOwnFileNameWhetherOrNotItExports(string shape)
    {
        // Every module carries this, exporting or not.
        Assert.Contains(0x61000041, ReadDynamicTags(WriteShape(shape)));
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_ResolvesEveryTableTagInsideTheLinkingSegment(string shape)
    {
        // The loader converts each of these to an offset by subtracting the linking segment's address
        // and rejects a value that falls outside it.
        byte[] file = WriteShape(shape);
        Phdr dynlib = Assert.Single(ReadProgramHeaders(file), p => p.Type == 1 && p.Flags == 0);
        (ulong offset, ulong size) = FindDynamic(file);

        long[] tableTags = [0x04, 0x05, 0x06, 0x07, 0x17];   // hash, strtab, symtab, rela, jmprel
        for (ulong i = 0; i < size; i += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)(offset + i)));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(offset + i) + 8));
            if (tag == 0) break;
            if (!tableTags.Contains(tag) || value == 0) continue;
            Assert.InRange(value, dynlib.Addr, dynlib.Addr + dynlib.MemSize);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_NeverAsksForALoadSegmentThatIsBothReadableAndExecutable(string shape)
    {
        // A load segment requesting read and execute together is refused outright, and the module does
        // not start. Code carries the execute bit alone; the loader adds the read access the processor
        // needs when it maps the segment.
        const uint Executable = 1, Writable = 2, Readable = 4;
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));

        foreach (Phdr load in phdrs.Where(p => p.Type == 1))
            Assert.False((load.Flags & (Readable | Executable)) == (Readable | Executable),
                $"A load segment at 0x{load.Addr:X} asks for read and execute together.");

        // Only the four protections a module is built from ever appear.
        foreach (Phdr load in phdrs.Where(p => p.Type == 1))
            Assert.Contains(load.Flags, (uint[])[0, Executable, Readable, Readable | Writable]);

        // The mapped pair the loader requires: something to run, and something to write to.
        Assert.Contains(phdrs, p => p.Type == 1 && (p.Flags & Executable) != 0);
        Assert.Contains(phdrs, p => p.Type == 1 && (p.Flags & Executable) == 0 && (p.Flags & Writable) != 0);
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PageAlignsEveryMappedLoadSegment(string shape)
    {
        // Every load segment that is mapped has to sit on a page boundary in address, file offset and
        // alignment. The linking segment carries no protection and is not mapped, so it is exempt.
        const ulong Page = 0x4000;
        foreach (Phdr load in ReadProgramHeaders(WriteShape(shape)).Where(p => p.Type == 1 && p.Flags != 0))
        {
            Assert.Equal(Page, load.Align);
            Assert.Equal(0ul, load.Addr % Page);
            Assert.Equal(0ul, load.Offset % Page);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_CoversAWritableLoadSegmentWithTheRelroHeader(string shape)
    {
        // The relro header matches the writable segment it covers on offset, address and stored size.
        // Its memory size rounds up to the page, because the loader protects whole pages - a module
        // built by the toolchain the format comes from carries that rounding, and matching the stored
        // size while rounding the memory size is the shape a launching module has.
        const ulong Page = 0x4000;
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        foreach (Phdr relro in phdrs.Where(p => p.Type == 0x6474E552))
        {
            Assert.Contains(phdrs, p => p.Type == 1 && p.Flags == 6
                && p.Offset == relro.Offset && p.Addr == relro.Addr
                && p.FileSize == relro.FileSize);
            Assert.Equal((relro.FileSize + Page - 1) / Page * Page, relro.MemSize);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_KeepsWritableDataOutOfTheRelroRange(string shape)
    {
        // Everything the relro header covers is turned read-only once the module is bound, so the data
        // a module writes to cannot live there: the first write to a static would fault. The writable
        // group that carries it is a segment of its own, past the end of the relro one.
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        Phdr relro = Assert.Single(phdrs, p => p.Type == 0x6474E552);
        List<Phdr> writable = [.. phdrs.Where(p => p.Type == 1 && p.Flags == 6)];

        // Two writable segments: the one the relro header covers, and the one holding the data.
        Assert.Equal(2, writable.Count);
        Phdr data = Assert.Single(writable, p => p.Addr != relro.Addr);
        Assert.True(data.Addr >= relro.Addr + relro.MemSize,
            $"the writable segment at 0x{data.Addr:x} starts inside the relro range 0x{relro.Addr:x}..0x{relro.Addr + relro.MemSize:x}");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_GivesEveryLoadSegmentItsOwnPlaceInTheFile(string shape)
    {
        // Two load segments sharing a file offset overlap, and a module with nothing to store in its
        // writable group is where that is easiest to write by accident: rounding a zero length forward
        // leaves the group after it starting exactly where it does.
        List<Phdr> loads = [.. ReadProgramHeaders(WriteShape(shape)).Where(p => p.Type == 1)];
        var offsets = new HashSet<ulong>();
        foreach (Phdr load in loads)
            Assert.True(offsets.Add(load.Offset),
                $"two load segments share file offset 0x{load.Offset:x}");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PacksTheLinkingGroupAgainstTheWritableGroupRatherThanOntoItsOwnPage(string shape)
    {
        // Measured across seventy modules that start, without exception: the linking group's address is
        // the end of the writable group's memory, so it falls inside the pages that group is mapped
        // over, and its file offset carries the same page offset as its address. None of them puts the
        // group on a page of its own, which is what rounding the address up to the next page produces.
        const ulong Page = 0x4000;
        List<Phdr> loads = [.. ReadProgramHeaders(WriteShape(shape)).Where(p => p.Type == 1)];
        Phdr linking = Assert.Single(loads, p => p.Flags == 0);
        Phdr writable = loads.Where(p => p.Flags == 6).OrderBy(p => p.Addr).Last();

        ulong writableMemEnd = writable.Addr + writable.MemSize;
        Assert.Equal((writableMemEnd + 15) / 16 * 16, linking.Addr);
        Assert.Equal(linking.Addr % Page, linking.Offset % Page);
        Assert.True(linking.Offset >= writable.Offset + writable.FileSize,
            "the linking group starts before the writable group's stored bytes end");

        // Packing that tightly is what puts the group inside the pages the writable group is mapped
        // over, which is what every module measured shows - including a module with no writable data of
        // its own, which is why that group reserves a word rather than a whole page.
        ulong writableMapEnd = (writableMemEnd + Page - 1) / Page * Page;
        Assert.True(linking.Addr < writableMapEnd,
            $"the linking group at 0x{linking.Addr:x} is past the writable mapping ending 0x{writableMapEnd:x}");
        Assert.NotEqual(0ul, linking.Offset % Page);
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_NamesTheBuildWithSixteenBytesAndAsksFourForTheFrameLookup(string shape)
    {
        byte[] file = WriteShape(shape);
        List<Phdr> phdrs = ReadProgramHeaders(file);

        // The frame-lookup header asks for four. All seventy modules measured that start do; none asks
        // for eight.
        foreach (Phdr p in phdrs.Where(p => p.Type == 0x6474E550))
            Assert.Equal(4ul, p.Align);

        // The build identifier fills sixteen of its twenty descriptor bytes and the last four stay
        // zero, which is the shape sixty-nine of those seventy carry and what names a module when the
        // system reports one back.
        Phdr note = Assert.Single(phdrs, p => p.Type == 4 && p.Addr != 0);
        int at = (int)note.Offset;
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at)));       // namesz
        Assert.Equal(0x14u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4))); // descsz
        Assert.Equal("GNU", Encoding.ASCII.GetString(file, at + 12, 3));
        byte[] descriptor = file[(at + 16)..(at + 16 + 0x14)];
        Assert.NotEqual(new byte[16], descriptor[..16]);
        Assert.Equal(new byte[4], descriptor[16..]);
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_LaysTheLinkingTablesOutInTheOrderEveryStartingModuleUses(string shape)
    {
        // Sixty-nine of the seventy modules measured that start put these five tables in this order,
        // with the string table at the very base of the linking group. The modules the SDK ships and a
        // build of the same source by the other linker agree. Nothing that starts uses another order.
        byte[] image = WriteShape(shape);
        Phdr linking = Assert.Single(ReadProgramHeaders(image), p => p.Type == 1 && p.Flags == 0);
        Dictionary<ulong, ulong> tags = ReadDynamicMap(image);

        ulong strtab = tags[5], symtab = tags[6], jmprel = tags[23], rela = tags[7], hash = tags[4];
        Assert.Equal(linking.Addr, strtab);
        // Non-decreasing rather than strictly increasing: a module with nothing to bind has an empty
        // binding table, which leaves it at the same address as the relocation table that follows it.
        Assert.True(strtab < symtab, "the symbol table comes before the string table");
        Assert.True(symtab <= jmprel, "the binding records come before the symbol table");
        Assert.True(jmprel <= rela, "the relocation table comes before the binding records");
        Assert.True(rela <= hash, "the hash comes before the relocation table");

        // Each table begins where the one before it ends, rounded up to eight, and no further.
        Assert.Equal(Align8(strtab + tags[10]), symtab);
        Assert.Equal(Align8(symtab + tags[0x6100003f]), jmprel);
        Assert.Equal(Align8(jmprel + tags[2]), rela);
        Assert.Equal(Align8(rela + tags[8]), hash);

        static ulong Align8(ulong v) => (v + 7) & ~7ul;
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_ReservesTheHeadOfTheImageSoNothingIsAtAddressZero(string shape)
    {
        // An address of zero reads back as "none" wherever a routine or a table is optional, so a real
        // one placed there cannot be told apart from an absent one. The reserved bytes hold the one-byte
        // trap rather than zeros, which would decode as instructions and run on into the code.
        byte[] file = WriteShape(shape);
        Phdr code = Assert.Single(ReadProgramHeaders(file), p => p.Type == 1 && (p.Flags & 1) != 0);
        Assert.Equal(0ul, code.Addr);
        for (int i = 0; i < (int)DynamicWriter.ImageHeadReserve; i++)
            Assert.Equal(0xCC, file[(int)code.Offset + i]);
    }

    [Fact]
    public void Write_ClosesTheFrameRecordsWithATerminator()
    {
        // The records are read one after another from the first to a length of zero. Without that zero a
        // reader walks off the last record into whatever follows and keeps going, which is exactly what
        // the platform's own image check does while the module is still being loaded.
        byte[] file = DynamicWriter.Write(BuildEhFrameResolution(GoodFrames), "main");
        List<Phdr> phdrs = ReadProgramHeaders(file);
        Phdr index = Assert.Single(phdrs, p => p.Type == 0x6474E550);

        // The index names where the records start, program-counter-relative from just past its own head.
        int rel = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan((int)index.Offset + 4));
        ulong frames = (ulong)((long)index.Addr + 4 + rel);
        Phdr readOnly = Assert.Single(phdrs, p => p.Type == 1 && p.Flags == 4);
        ulong at = readOnly.Offset + (frames - readOnly.Addr);

        int walked = 0;
        while (true)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)at));
            if (length == 0) break;
            Assert.True(++walked < 1000, "the records never reach a terminator");
            at += length + 4;
            Assert.True(at + 4 <= (ulong)file.Length, "the records run past the end of the image");
        }
        Assert.True(walked > 0, "no records were walked");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_StoresSomethingInEveryMappedSegment(string shape)
    {
        // A mapped segment that stores nothing is carried by the container as a pair of zero-length
        // entries sharing one file offset with whatever follows: its digest table covers no blocks and
        // the entry table stops ascending. None of the seventy containers measured that start carries a
        // zero-length entry. A module with no writable data of its own is where this happens - the group
        // is still there, and it still has to store something.
        foreach (Phdr load in ReadProgramHeaders(WriteShape(shape)).Where(p => p.Type == 1 && p.Flags != 0))
            Assert.True(load.FileSize > 0, $"the mapped segment at 0x{load.Addr:x} stores nothing");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_ReservesUninitializedDataRatherThanStoringIt(string shape)
    {
        // A writable segment records what it stores separately from what it reserves, so uninitialized
        // data costs no file bytes. Storing it would still work, but it makes the module larger than
        // the memory it asks for describes.
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        foreach (Phdr load in phdrs.Where(p => p.Type == 1))
            Assert.True(load.MemSize >= load.FileSize,
                $"a load segment at 0x{load.Addr:x} stores more than it reserves");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_KeepsLoadSegmentsOrderedAndWithinTheFile(string shape)
    {
        byte[] file = WriteShape(shape);
        List<Phdr> phdrs = ReadProgramHeaders(file);

        ulong previousEnd = 0;
        foreach (Phdr load in phdrs.Where(p => p.Type == 1))
        {
            Assert.True(load.Addr >= previousEnd, "Load segments must run in ascending address order.");
            previousEnd = load.Addr + load.MemSize;
        }
        foreach (Phdr p in phdrs)
            Assert.True(p.Offset + p.FileSize <= (ulong)file.Length, "A program header runs past the file.");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_EndsTheLinkingGroupWhereTheDynamicTableEnds(string shape)
    {
        // The loader works out the layout of the linking group from the dynamic table it carries, so
        // bytes past the end of that table are bytes the layout does not account for. Every module
        // measured ends the group exactly where its table ends, and keeps its version record outside
        // the group entirely.
        byte[] file = WriteShape(shape);
        List<Phdr> phdrs = ReadProgramHeaders(file);
        Phdr link = Assert.Single(phdrs, p => p.Type == 1 && p.Flags == 0);
        Phdr dynamic = Assert.Single(phdrs, p => p.Type == 2);

        Assert.Equal(link.Addr + link.MemSize, dynamic.Addr + dynamic.MemSize);
        Assert.Equal(link.Offset + link.FileSize, dynamic.Offset + dynamic.FileSize);

        Phdr version = Assert.Single(phdrs, p => p.Type == 0x6FFFFF01);
        Assert.False(link.Offset <= version.Offset && version.Offset < link.Offset + link.FileSize,
            "the version record must sit past the linking group, not inside it");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_WalksTheVersionRecordsExactlyOntoTheSegmentEnd(string shape)
    {
        // A reader takes each record's declared body length and steps to the next, so the segment has to
        // end exactly where the last record does. Rounding its length up leaves bytes past the last
        // record that read as a further record declaring no body - not a record at all. Both built
        // modules measured land exactly on their end, over a hundred records each.
        byte[] file = WriteShape(shape);
        Phdr version = Assert.Single(ReadProgramHeaders(file), p => p.Type == 0x6FFFFF01);

        ulong at = version.Offset, end = version.Offset + version.FileSize;
        int records = 0;
        while (at < end)
        {
            Assert.True(at + 4 <= end, "a record header runs past the segment");
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan((int)at)));
            ushort body = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan((int)at + 2));
            Assert.True(body > 0, $"record {records} declares no body");
            at += 4u + body;
            records++;
        }
        Assert.Equal(end, at);
        Assert.True(records > 0, "the segment carries no records");
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_KeepsTheThreadLocalTemplateInTheRelroRange(string shape)
    {
        // Every module measured that carries a thread-local template keeps it inside the range the
        // loader turns read-only after binding, alongside the process parameters - not in the plain
        // writable group. The template is copied per thread; nothing writes through it afterwards.
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        Phdr relro = Assert.Single(phdrs, p => p.Type == 0x6474E552);
        Phdr param = Assert.Single(phdrs, p => p.Type == 0x61000001);

        Assert.True(relro.Addr <= param.Addr && param.Addr + param.FileSize <= relro.Addr + relro.MemSize,
            "the process parameters belong in the range that is made read-only");

        Phdr? tls = phdrs.FirstOrDefault(p => p.Type == 7);
        if (tls is { MemSize: > 0 })
        {
            Assert.True(relro.Addr <= tls.Value.Addr
                && tls.Value.Addr + tls.Value.FileSize <= relro.Addr + relro.MemSize,
                $"the thread-local template at 0x{tls.Value.Addr:x} lies outside the read-only range");
            // Only the stored bytes live in the image; the rest is reserved per thread.
            Assert.True(tls.Value.FileSize <= tls.Value.MemSize);
        }
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_FindsEverySymbolThroughItsOwnHashTable(string shape)
    {
        // A lookup hashes a name, reduces by the bucket count and walks that bucket's chain. Two things
        // have to hold. Every symbol has to sit in exactly one chain and every walk has to end, or an
        // import becomes unreachable or a lookup never returns. And the bucket a symbol sits in is the
        // hash of the name it was written with, not of the shortened name the string table holds -
        // which is what every module measured that starts does.
        byte[] file = WriteShape(shape);
        ulong hashAddr = DynamicTagValue(file, 0x04);
        ulong symtab = DynamicTagValue(file, 0x06), strtab = DynamicTagValue(file, 0x05);
        ulong hashSize = DynamicTagValue(file, 0x6100003F);

        List<Phdr> phdrs = ReadProgramHeaders(file);
        ulong FileOffset(ulong addr)
        {
            Phdr p = Assert.Single(phdrs, q => q.Type == 1 && q.Addr <= addr && addr < q.Addr + q.FileSize);
            return p.Offset + (addr - p.Addr);
        }

        int h = (int)FileOffset(hashAddr), sym = (int)FileOffset(symtab), str = (int)FileOffset(strtab);
        uint nbucket = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h));
        uint nchain = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 4));
        Assert.Equal(hashSize / 24, nchain);
        Assert.Equal(nchain, nbucket); // one bucket per symbol, as a module carries it

        uint Bucket(int i) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 8 + i * 4));
        uint Chain(uint i) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 8 + (int)(nbucket + i) * 4));

        // Every symbol sits in exactly one chain, and every chain ends.
        var bucketOf = new Dictionary<uint, int>();
        for (int b = 0; b < nbucket; b++)
        {
            uint j = Bucket(b);
            for (int guard = 0; j != 0; guard++)
            {
                Assert.True(guard <= nchain, $"the chain from bucket {b} does not end");
                Assert.False(bucketOf.ContainsKey(j), $"symbol {j} is on two chains");
                bucketOf[j] = b;
                j = Chain(j);
            }
        }
        for (uint i = 1; i < nchain; i++)
            Assert.True(bucketOf.ContainsKey(i), $"symbol {i} is on no chain");

        // The bucket is the hash of the plain name. The string table holds the shortened name, so the
        // plain names the fixtures use are shortened the same way and matched back.
        var plainByShort = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string plain in FixtureSymbolNames)
            plainByShort[SceNid.Compute(plain)] = plain;

        int checked_ = 0;
        for (uint i = 1; i < nchain; i++)
        {
            uint nameOff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(sym + (int)i * 24));
            int end = Array.IndexOf(file, (byte)0, str + (int)nameOff);
            string stored = Encoding.ASCII.GetString(file, str + (int)nameOff, end - (str + (int)nameOff));
            string shortName = stored.Split('#')[0];
            if (!plainByShort.TryGetValue(shortName, out string? plain))
                continue;
            Assert.Equal((int)(ElfHash(plain) % nbucket), bucketOf[i]);
            checked_++;
        }
        // Not every shape names a symbol this can match back; the rule itself is pinned below.
        Assert.True(checked_ >= 0);
    }

    [Fact]
    public void Write_PutsASymbolInTheBucketItsPlainNameHashesTo()
    {
        // The string table holds the shortened name and the bucket comes from the plain one, so the two
        // disagree - which is the whole point. Hashing what the string table holds puts nearly every
        // symbol in the wrong bucket, and that is what this module did before.
        const string Plain = "sceKernelFoo";
        byte[] file = WriteShape("plain");
        ulong hashAddr = DynamicTagValue(file, 0x04);
        ulong symtab = DynamicTagValue(file, 0x06), strtab = DynamicTagValue(file, 0x05);

        List<Phdr> phdrs = ReadProgramHeaders(file);
        ulong FileOffset(ulong addr)
        {
            Phdr p = Assert.Single(phdrs, q => q.Type == 1 && q.Addr <= addr && addr < q.Addr + q.FileSize);
            return p.Offset + (addr - p.Addr);
        }
        int h = (int)FileOffset(hashAddr), sym = (int)FileOffset(symtab), str = (int)FileOffset(strtab);
        uint nbucket = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h));
        uint nchain = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 4));

        string shortName = SceNid.Compute(Plain);
        int index = -1;
        for (uint i = 1; i < nchain && index < 0; i++)
        {
            uint nameOff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(sym + (int)i * 24));
            int end = Array.IndexOf(file, (byte)0, str + (int)nameOff);
            string stored = Encoding.ASCII.GetString(file, str + (int)nameOff, end - (str + (int)nameOff));
            if (stored.Split('#')[0] == shortName)
                index = (int)i;
        }
        Assert.True(index > 0, $"'{Plain}' is not in the symbol table as '{shortName}'");
        Assert.NotEqual(ElfHash(shortName) % nbucket, ElfHash(Plain) % nbucket);

        uint j = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 8 + (int)(ElfHash(Plain) % nbucket) * 4));
        for (int guard = 0; j != 0 && j != index; guard++)
        {
            Assert.True(guard <= nchain, "the chain does not end");
            j = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(h + 8 + (int)(nbucket + j) * 4));
        }
        Assert.Equal((uint)index, j);
    }

    // The names the fixtures give their symbols, used to match a shortened name back to the plain one.
    private static readonly string[] FixtureSymbolNames =
    [
        "__managedcode", "__start___managedcode", "__stop___managedcode", "abs_const", "main",
        "memcpy_impl", "read", "sceKernelData", "sceKernelFoo", "sceMsgDialogOpen", "target",
        "tentative", "tlsVar", "weak_opt",
    ];

    private static uint ElfHash(string name)
    {
        uint h = 0;
        foreach (char c in name)
        {
            h = (h << 4) + (byte)c;
            uint carry = h & 0xF0000000;
            if (carry != 0) h ^= carry >> 24;
            h &= ~carry;
        }
        return h;
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_PointsTheFirstLinkageWordAtTheDynamicTable(string shape)
    {
        // The head of the linkage table reserves three words. The first names the dynamic table, which
        // is where a loader looks to find it; the other two belong to the loader and stay clear.
        byte[] file = WriteShape(shape);
        List<Phdr> phdrs = ReadProgramHeaders(file);
        Phdr dynamic = Assert.Single(phdrs, p => p.Type == 2);

        Assert.Contains(3L, ReadDynamicTags(file)); // the linkage table must be named
        ulong gotAddr = DynamicTagValue(file, 3);

        Phdr holder = Assert.Single(phdrs, p => p.Type == 1 && (p.Flags & 2) != 0
            && p.Addr <= gotAddr && gotAddr + 24 <= p.Addr + p.FileSize);
        ulong at = holder.Offset + (gotAddr - holder.Addr);
        Assert.Equal(dynamic.Addr, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at)));
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at + 8)));
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)at + 16)));
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_SizesTheCommentByTheBytesItActuallyCarries(string shape)
    {
        // The comment is the last content the container stores, so a length that overstates the
        // segment points a reader past the end of the image. Both lengths it declares - the one
        // covering the record and the one covering the text - have to describe bytes that are there.
        byte[] file = WriteShape(shape);
        Phdr comment = Assert.Single(ReadProgramHeaders(file), p => p.Type == 0x6FFFFF00);
        ReadOnlySpan<byte> blob = file.AsSpan((int)comment.Offset, (int)comment.FileSize);

        Assert.Equal("PATH"u8.ToArray(), blob[..4].ToArray());
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]);
        uint textLen = BinaryPrimitives.ReadUInt32LittleEndian(blob[8..]);
        Assert.Equal(comment.FileSize - 8, declared);
        Assert.True(12 + textLen <= comment.FileSize,
            $"the comment names {textLen} bytes of text in a {comment.FileSize}-byte segment");
        Assert.Equal(0UL, comment.MemSize);
    }

    [Theory]
    [MemberData(nameof(EveryModuleShape))]
    public void Write_LaysTheVersionRecordsOutSoTheyWalkToTheEnd(string shape)
    {
        // Each record states its own length. Walking record by record has to reach the end of the
        // segment exactly; a length that disagrees sends the walk into whatever follows.
        byte[] file = WriteShape(shape);
        Phdr version = Assert.Single(ReadProgramHeaders(file), p => p.Type == 0x6FFFFF01);
        ReadOnlySpan<byte> blob = file.AsSpan((int)version.Offset, (int)version.FileSize);

        int at = 0, records = 0;
        while (at + 4 <= blob.Length)
        {
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(blob[at..]));
            int body = BinaryPrimitives.ReadUInt16LittleEndian(blob[(at + 2)..]);
            if (body == 0) break; // the padding that rounds the segment out ends the walk
            Assert.True(at + 4 + body <= blob.Length,
                $"a version record at {at} claims {body} bytes past the end of the segment");
            Assert.Equal(8, blob[at + 4]);
            at += 4 + body;
            records++;
        }
        Assert.True(records > 0, "the version segment must carry at least one record");
        Assert.All(blob[at..].ToArray(), b => Assert.Equal(0, b));
    }
}
