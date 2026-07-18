// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
        Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38)));

        // Find PT_DYNAMIC (type 2) among the program headers.
        bool hasDynamic = false, hasProcParam = false;
        int ph = 0x40;
        for (int i = 0; i < 6; i++, ph += 0x38)
        {
            uint t = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph));
            if (t == 2) hasDynamic = true;
            if (t == 0x61000001) hasProcParam = true;
        }
        Assert.True(hasDynamic, "Expected a PT_DYNAMIC segment.");
        Assert.True(hasProcParam, "Expected a PT_SCE_PROCPARAM segment.");

        // The needed module name appears in the file (in the dynamic string table).
        Assert.Contains("libkernel.prx", Encoding.ASCII.GetString(file));
        // The "ORBI" process-parameter magic is present.
        Assert.Contains("ORBI", Encoding.ASCII.GetString(file));
    }

    // An object whose .text reaches an imported data symbol through the global-offset table.
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
    private static LinkResolution BuildTlsResolution()
    {
        var text = new ElfSection { Name = ".text", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Execute, Address = 0, Size = 0x10, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[0x10] };
        var tdata = new ElfSection { Name = ".tdata", Type = ShType.ProgBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = new byte[8] };
        var tbss = new ElfSection { Name = ".tbss", Type = ShType.NoBits, Flags = ShFlags.Alloc | ShFlags.Write | ShFlags.Tls, Address = 0, Size = 4, Link = 0, Info = 0, AddrAlign = 4, EntSize = 0, Data = [] };
        var nullSec = new ElfSection { Name = "", Type = ShType.Null, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var tlsVar = new ElfSymbol { Name = "tlsVar", Info = (SymBind.Global << 4) | SymType.Tls, Other = 0, SectionIndex = 2, Value = 4, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };
        var relocs = new List<ElfRelocation> { new(Offset: 0, SymbolIndex: 2, Type: RelType.TpOff32, Addend: 0) };
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
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
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
    public void Write_GotDataImport_ProducesARelaTable()
    {
        byte[] file = DynamicWriter.Write(BuildGotResolution(), "main");
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10)));

        // Locate the dynamic segment and confirm a DT_RELA (7) entry is present.
        int ph = 0x40;
        ulong dynOff = 0, dynSz = 0;
        for (int i = 0; i < 6; i++, ph += 0x38)
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
}
