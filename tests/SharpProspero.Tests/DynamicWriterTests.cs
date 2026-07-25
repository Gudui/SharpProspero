// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
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
        // The code, writable and linking loads + dynamic + procparam + build-id note + reserved note.
        // This object has no read-only content, so the read-only load is left out.
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        Assert.Equal(7, phnum);

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

        Assert.Equal(0x60u, (uint)ppSz);
        Assert.Equal("ORBI", Encoding.ASCII.GetString(file, (int)ppOff + 8, 4));
        // The legacy compatibility word, then the meaningful Prospero SDK version the module targets.
        Assert.Equal(0x08050001u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)ppOff + 0x10)));
        Assert.Equal(0x02000026u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)ppOff + 0x14)));
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
            if (va == 0 && memsz == 0 && filesz == 0x48)
            {
                reservedNote = true;                                         // the non-loaded reserved note
                Assert.True(off + 0x48 <= (ulong)file.Length, "Reserved note must lie within the file.");
            }
        }
        Assert.True(loadedNote, "Expected the loaded 0x24-byte GNU build-id note.");
        Assert.True(reservedNote, "Expected the reserved 0x48-byte note in the file tail.");
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
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
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

        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
        // mov %fs:0,%rax ; lea -8(%rax),%rax
        byte[] expected = [0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x80, 0xF8, 0xFF, 0xFF, 0xFF];
        Assert.Equal(expected, file[(int)textOffset..((int)textOffset + 16)]);
    }

    [Fact]
    public void Write_DtpOffThreadLocal_ResolvesToTheLocalExecOffset()
    {
        // A module-block offset (paired with a relaxed local-dynamic base) resolves to the same value as a
        // local-exec offset: tlsVar's template offset 4 minus the aligned template size 12, i.e. -8.
        byte[] file = DynamicWriter.Write(BuildTlsResolution(RelType.DtpOff32), "main");
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
        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
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

        ulong textOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)textOffset)));

        int ph = 0x40; ulong dynOff = 0, dynSz = 0;
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 2)
            { dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)); }
        // DT_RELACOUNT (0x6ffffff9) is written only when there is at least one base-relative record.
        bool hasRelaCount = false;
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
            if (BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d)) == 0x6ffffff9) hasRelaCount = true;
        Assert.False(hasRelaCount, "A weak-undefined reference must not emit a base-relative relocation.");
    }

    [Fact]
    public void Write_ReferenceToAnIndirectFunction_EmitsAnIrelativeRecord()
    {
        // An absolute reference to a resolver-backed indirect function (STT_GNU_IFUNC) must produce an
        // R_X86_64_IRELATIVE (37) dynamic relocation, so the loader calls the resolver and stores the
        // result, rather than a plain relative record that would leave the resolver address in place.
        var ifunc = new ElfSymbol { Name = "memcpy_impl", Info = (SymBind.Global << 4) | SymType.GnuIfunc, Other = 0, SectionIndex = 1, Value = 0x10, Size = 0 };
        byte[] file = DynamicWriter.Write(BuildSingleReloc(ifunc, RelType.R64, defineTarget: true), "main");

        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong dynOff = 0, dynSz = 0, relaVaddr = 0, relaSz = 0;
        int ph = 0x40;
        var loads = new List<(ulong Vaddr, ulong Off, ulong Filesz)>();
        for (int i = 0; i < phnum; i++, ph += 0x38)
        {
            uint t = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph));
            if (t == 2) { dynOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); dynSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 40)); }
            if (t == 1) loads.Add((BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)), BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 32))));
        }
        for (ulong d = dynOff; d + 16 <= dynOff + dynSz; d += 16)
        {
            long tag = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)d));
            if (tag == 7) relaVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));  // DT_RELA
            if (tag == 8) relaSz = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)d + 8));     // DT_RELASZ
        }
        (ulong Vaddr, ulong Off, ulong Filesz) seg = loads.Find(l => relaVaddr >= l.Vaddr && relaVaddr < l.Vaddr + l.Filesz);
        ulong relaFile = seg.Off + (relaVaddr - seg.Vaddr);
        bool foundIrelative = false;
        for (ulong r = 0; r + 24 <= relaSz; r += 24)
        {
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)(relaFile + r) + 8));
            if ((uint)(info & 0xffffffff) == 37) foundIrelative = true;
        }
        Assert.True(foundIrelative, "Expected an R_X86_64_IRELATIVE record for the indirect function.");
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

    [Fact]
    public void Write_Executable_RunsTheInitArrayFromTheEntryStub()
    {
        // The loader does not run an executable's init array, so the linker must. The entry points at a
        // small constructor-running stub (not straight at main), and the init-array tag is left off.
        byte[] file = DynamicWriter.Write(InitArrayResolution(), "main"); // Executable by default

        Assert.False(HasDynamicTag(file, 25), "an executable must not advertise DT_INIT_ARRAY");
        Assert.False(HasDynamicTag(file, 27), "an executable must not advertise DT_INIT_ARRAYSZ");

        // The entry is the stub, whose bytes begin push rbx; push rbp; lea rbx,[rip+..]. main sits at the
        // module base (address 0), so a stub entry is provably not main.
        ulong entry = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x18));
        Assert.NotEqual(0ul, entry);

        // Map the entry back to a file offset through the code segment, which carries the execute bit
        // and never the read bit.
        int ph = 0x40, phnum = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38));
        ulong textOff = 0, textVaddr = 0;
        for (int i = 0; i < phnum; i++, ph += 0x38)
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph)) == 1 && (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(ph + 4)) & 1) != 0)
            { textOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 8)); textVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(ph + 16)); }

        int stub = (int)(textOff + (entry - textVaddr));
        Assert.Equal(new byte[] { 0x53, 0x55, 0x48, 0x8D, 0x1D }, file[stub..(stub + 5)]);
    }

    [Fact]
    public void Write_Library_KeepsTheInitArrayTagsForTheLoader()
    {
        // A shared library's constructors are run by the loader, so it still advertises the init array.
        byte[] file = DynamicWriter.Write(InitArrayResolution(), "main", ModuleKind.Library);
        Assert.True(HasDynamicTag(file, 25), "a library keeps DT_INIT_ARRAY");
        Assert.True(HasDynamicTag(file, 27), "a library keeps DT_INIT_ARRAYSZ");
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
    public void Write_OmitsRelroRatherThanEmittingOneWithoutItsLoadSegment(string shape)
    {
        // A relro header is only accepted when a writable load segment matches it on offset, address
        // and both sizes; an unmatched one is rejected. The linker does not emit the header at all,
        // which skips the check. If it ever starts emitting one, the match has to hold.
        List<Phdr> phdrs = ReadProgramHeaders(WriteShape(shape));
        foreach (Phdr relro in phdrs.Where(p => p.Type == 0x6474E552))
            Assert.Contains(phdrs, p => p.Type == 1 && p.Flags == 6
                && p.Offset == relro.Offset && p.Addr == relro.Addr
                && p.FileSize == relro.FileSize && p.MemSize == relro.MemSize);
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
}
