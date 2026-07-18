// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SharpProspero.Link;
using Xunit;

namespace SharpProspero.Tests;

public sealed class LinkWriterTests
{
    // Builds a self-contained object: a .text section, two symbols (main at 0, target at 0x10), and
    // two relocations at offsets 0 (R_X86_64_64 -> target) and 8 (R_X86_64_PC32 -> target).
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
        var nullSec = new ElfSection
        {
            Name = "",
            Type = ShType.Null,
            Flags = 0,
            Address = 0,
            Size = 0,
            Link = 0,
            Info = 0,
            AddrAlign = 0,
            EntSize = 0,
            Data = [],
        };

        var main = new ElfSymbol { Name = "main", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0, Size = 0 };
        var target = new ElfSymbol { Name = "target", Info = (SymBind.Global << 4) | SymType.Func, Other = 0, SectionIndex = 1, Value = 0x10, Size = 0 };
        var nullSym = new ElfSymbol { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 };

        var relocs = new List<ElfRelocation>
        {
            new(Offset: 0, SymbolIndex: 2, Type: RelType.R64, Addend: 0),   // target is symbol index 2
            new(Offset: 8, SymbolIndex: 2, Type: RelType.Pc32, Addend: 0),
        };

        var obj = new ElfObject
        {
            Origin = "synthetic",
            Sections = [nullSec, text],
            Symbols = [nullSym, main, target],
            Relocations = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [1] = relocs },
        };

        return new LinkResolution
        {
            Included = [obj],
            Defined = new Dictionary<string, ElfObject> { ["main"] = obj, ["target"] = obj },
            Imports = [],
            Unresolved = [],
        };
    }

    [Fact]
    public void WriteExecutable_ProducesAValidDynamicExecutable()
    {
        byte[] file = LinkWriter.WriteExecutable(BuildResolution(), "main");

        Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(file));       // ELF magic
        Assert.Equal(9, file[7]);                                                        // OS/ABI
        Assert.Equal(0xFE10, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10))); // ET_SCE_DYNEXEC
        Assert.Equal(0x3E, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x12)));   // x86-64
        Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x38)) >= 1);     // a load segment
    }

    [Fact]
    public void WriteExecutable_AppliesRelocations()
    {
        byte[] file = LinkWriter.WriteExecutable(BuildResolution(), "main");

        // The text segment is the first load segment; read its file offset from the phdr.
        ulong segOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 8));
        ulong segVaddr = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(0x40 + 16));

        // Reloc at 0: R_X86_64_64 -> target address (segVaddr + 0x10).
        ulong r64 = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan((int)segOffset));
        Assert.Equal(segVaddr + 0x10, r64);

        // Reloc at 8: R_X86_64_PC32 -> target - place = (segVaddr + 0x10) - (segVaddr + 8) = 8.
        uint pc32 = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)segOffset + 8));
        Assert.Equal(8u, pc32);
    }

    [Fact]
    public void WriteExecutable_RejectsUnresolvedImports()
    {
        LinkResolution resolution = BuildResolution();
        var withImport = new LinkResolution
        {
            Included = resolution.Included,
            Defined = resolution.Defined,
            Imports = [],
            Unresolved = ["sceKernelSomething"],
        };
        Assert.Throws<ElfLinkException>(() => LinkWriter.WriteExecutable(withImport, "main"));
    }
}
