// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// The retarget tool rewrites what a module records it was built against (the load-time firmware gate)
// and a needed library's recorded version. These check the edits on a module with a version block and
// on a linked module with a needed-module tag.
public sealed class ModuleEditorTests
{
    [Fact]
    public void Read_ReportsTheRecordedVersion()
    {
        byte[] module = BuildModuleTargeting(0x12000009);   // 12.00, patch 9
        Assert.Equal(0x12000009u, ModuleEditor.Read(module).SdkVersion);
    }

    [Fact]
    public void SetSdkVersion_DowngradesTheMajorMinorAndKeepsThePatch()
    {
        byte[] module = BuildModuleTargeting(0x12000009);   // 12.00, patch 9

        Assert.True(ModuleEditor.SetSdkVersion(module, 0x0900)); // to 9.00
        Assert.Equal(0x09000009u, ModuleEditor.Read(module).SdkVersion);
        Assert.Equal("09.00", PrxImage.FormatSystemVersion(ModuleEditor.Read(module).SdkVersion));
    }

    [Fact]
    public void SetSdkVersion_ReturnsFalseWhenTheModuleRecordsNoVersion()
    {
        // A module produced by the linker records no version block, so there is nothing to gate the
        // load and nothing to rewrite.
        byte[] module = BuildModuleImportingVideoOut();
        Assert.Equal(0u, ModuleEditor.Read(module).SdkVersion);
        Assert.False(ModuleEditor.SetSdkVersion(module, 0x0900));
    }

    [Fact]
    public void SetLibraryVersion_RewritesTheNeededModuleVersion()
    {
        byte[] module = BuildModuleImportingVideoOut();

        // The module imports from libSceVideoOut, recorded at the default module version 1.1.
        LibraryTag before = Assert.Single(
            ModuleEditor.Read(module).Libraries, t => t.Kind == "needed module" && t.Name == "libSceVideoOut");
        Assert.Equal((ushort)0x0101, before.Version);

        int rewritten = ModuleEditor.SetLibraryVersion(module, "libSceVideoOut", 0x0205);
        Assert.Equal(1, rewritten);

        LibraryTag after = Assert.Single(
            ModuleEditor.Read(module).Libraries, t => t.Kind == "needed module" && t.Name == "libSceVideoOut");
        Assert.Equal((ushort)0x0205, after.Version);
    }

    [Fact]
    public void SetLibraryVersion_LeavesAnUnknownLibraryAlone()
    {
        byte[] module = BuildModuleImportingVideoOut();
        Assert.Equal(0, ModuleEditor.SetLibraryVersion(module, "libSceNotHere", 0x0102));
    }

    [Fact]
    public void SetSdkVersion_LeavesAnOlderParamBlockAlone()
    {
        // A param block older than version 2 records no version field; the reader reports no version,
        // so the writer must not overwrite the field and claim a change.
        byte[] module = BuildModuleTargeting(0x12000009);
        BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(0x80 + 12), 1); // block struct version -> 1

        Assert.Equal(0u, ModuleEditor.Read(module).SdkVersion);
        Assert.False(ModuleEditor.SetSdkVersion(module, 0x0900));
    }

    [Fact]
    public void Read_RejectsAnOutOfRangeDynamicSegmentWithoutThrowing()
    {
        // A crafted PT_DYNAMIC offset near the maximum must be rejected as out of range, not indexed
        // (which would wrap to a negative span index and throw).
        byte[] module = BuildModuleWithDynamicOffset(0x7FFFFFFFFFFFFFF8);
        Assert.Empty(ModuleEditor.Read(module).Libraries);
    }

    // A minimal module ELF carrying only a PT_SCE_MODULE_PARAM segment with a valid version block, so
    // the recorded version can be read and rewritten.
    private static byte[] BuildModuleTargeting(uint sdkVersion)
    {
        const int paramOff = 0x80;
        byte[] file = new byte[paramOff + 0x18];

        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 0xFE18); // ET_SCE_DYNAMIC
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E);   // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x20), 0x40);   // phoff
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x36), 0x38);   // phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x38), 1);      // phnum

        int ph = 0x40;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph), 0x61000002);        // PT_SCE_MODULE_PARAM
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 8), paramOff);      // p_offset
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 0x20), 0x18);       // p_filesz

        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(paramOff), 0x18);            // block size
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(paramOff + 8), 0x3C13F4BF);  // magic
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(paramOff + 12), 2);          // struct version
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(paramOff + 0x14), sdkVersion);
        return file;
    }

    // A minimal module ELF with a single PT_DYNAMIC program header whose file offset is set by the
    // caller, so an out-of-range offset can be exercised.
    private static byte[] BuildModuleWithDynamicOffset(ulong dynamicOffset)
    {
        byte[] file = new byte[0x40 + 0x38];

        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 0xFE18); // ET_SCE_DYNAMIC
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E);   // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x20), 0x40);   // phoff
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x36), 0x38);   // phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x38), 1);      // phnum

        int ph = 0x40;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph), 2);              // PT_DYNAMIC
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 8), dynamicOffset); // p_offset
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 0x20), 0);       // p_filesz
        return file;
    }

    // A linked library module that imports from libSceVideoOut, so it carries a needed-module tag.
    private static byte[] BuildModuleImportingVideoOut()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildImportingObject(), "app"));
        StubCatalog.Entry vo = StubCatalog.Core.Single(e => e.Library == "libSceVideoOut");
        options.ExtraStubs.Add(StubLibrary.Parse(
            PrxStubEmitter.BuildObject(vo.Library, vo.Exports, vo.ModuleVersion, vo.LibraryVersion),
            vo.Library));
        LinkResolution result = Linker.Resolve(options);
        Assert.Empty(result.Unresolved);
        return DynamicWriter.Write(result, entrySymbol: null, ModuleKind.Library, exportSymbols: null, moduleFileName: "test.prx");
    }

    // A library object: a defined global "game_frame" plus an undefined reference to sceVideoOutOpen the
    // stub resolves, so the module ends up with a needed-module record for libSceVideoOut.
    private static byte[] BuildImportingObject()
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0game_frame\0sceVideoOutOpen\0");
        const int frameOff = 1, videoOff = 12;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xC3]; // nops then ret

        byte[] symtab = new byte[3 * 24];
        WriteSym(symtab, 1, frameOff, (1 << 4) | 2, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(symtab.AsSpan(1 * 24 + 8), 8); // game_frame at .text+8
        WriteSym(symtab, 2, videoOff, (1 << 4) | 2, 0);                         // sceVideoOutOpen undefined

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
