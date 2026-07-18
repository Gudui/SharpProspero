// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public sealed class LinkTests
{
    private static string LibDir()
    {
        string? sdk = Environment.GetEnvironmentVariable("PROSPERO_SDK_DIR");
        if (string.IsNullOrEmpty(sdk))
            sdk = @"C:\Program Files (x86)\SCE\Prospero SDKs\2.000";
        return Path.Combine(sdk, "target", "lib");
    }

    private static string? Object(string name)
    {
        string path = Path.Combine(LibDir(), name);
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void ElfObjectReader_RejectsNonElf()
    {
        Assert.Throws<ElfLinkException>(() => ElfObjectReader.Read(new byte[64], "junk"));
    }

    [Fact]
    public void ElfObjectReader_ReadsARealObject()
    {
        string? path = Object("crti.o") ?? Object("crtbegin.o") ?? Object("crt1.o");
        if (path is null)
            return;

        ElfObject obj = ElfObjectReader.Read(File.ReadAllBytes(path), path);
        Assert.NotEmpty(obj.Sections);
        // A named section table and a symbol table are present in a normal object.
        Assert.Contains(obj.Sections, s => s.Name.Length > 0);
    }

    [Fact]
    public void ArReader_ReadsARealArchive()
    {
        string? path = Object("libEdgeAnim.a") ?? Object("libc_lto.a");
        if (path is null)
            return;

        IReadOnlyList<ArMember> members = ArReader.Read(File.ReadAllBytes(path), Path.GetFileName(path));
        Assert.NotEmpty(members);
        Assert.All(members, m => Assert.True(m.Name.Length > 0));
    }

    [Fact]
    public void ArReader_TreatsABareObjectAsOneMember()
    {
        // A raw ELF (such as a stub library) is one member.
        byte[] elf = new byte[64];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(elf, 0x464C457F);
        IReadOnlyList<ArMember> members = ArReader.Read(elf, "stub");
        Assert.Single(members);
    }

    [Fact]
    public void Resolve_LinksTheStartObjectsAndReportsUnresolved()
    {
        var options = new LinkOptions();
        foreach (string name in new[] { "crt1.o", "crti.o", "crtbegin.o", "crtn.o" })
        {
            string? path = Object(name);
            if (path is not null)
                options.Objects.Add(path);
        }
        if (options.Objects.Count == 0)
            return;

        LinkResolution result = Linker.Resolve(options);
        Assert.NotEmpty(result.Included);
        // The start objects define symbols and reference libc functions that no included object
        // defines and (no stubs supplied) nothing provides, so both sets carry real entries.
        Assert.NotEmpty(result.Defined);
        Assert.NotEmpty(result.Unresolved);
    }

    [Fact]
    public void Resolve_ClassifiesDefinedStrongUndefinedAndWeak()
    {
        string path = Path.Combine(Path.GetTempPath(), "sharpprospero_resolve_test.o");
        File.WriteAllBytes(path, BuildRelocatableObject());
        try
        {
            var options = new LinkOptions();
            options.Objects.Add(path);
            LinkResolution result = Linker.Resolve(options);

            Assert.Contains("main", result.Defined.Keys);              // defined in .text
            Assert.Contains("sceKernelFoo", result.Unresolved);        // strong undefined, unprovided
            Assert.DoesNotContain("__optional__", result.Unresolved);  // weak undefined -> bound to zero
            Assert.DoesNotContain("main", result.Unresolved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Builds a minimal ET_REL object with a defined global "main", a strong undefined "sceKernelFoo",
    // and a weak undefined "__optional__", so the resolver's classification can be checked in memory.
    private static byte[] BuildRelocatableObject()
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0main\0sceKernelFoo\0__optional__\0");
        const int mainOff = 1, fooOff = 6, optOff = 19;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = new byte[0x10];

        byte[] symtab = new byte[4 * 24];
        WriteSym(symtab, 1, mainOff, (1 << 4) | 2, 1);  // main: global func, defined in .text
        WriteSym(symtab, 2, fooOff, (1 << 4) | 2, 0);   // sceKernelFoo: global func, undefined
        WriteSym(symtab, 3, optOff, 2 << 4, 0);         // __optional__: weak, undefined

        int off = 0x40;
        int textOff = off; off += text.Length;
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
        WriteShdr(file, shoff + 64, textName, 1, 6, textOff, text.Length, 0, 16, 0);   // .text PROGBITS ALLOC|EXEC
        WriteShdr(file, shoff + 128, symName, 2, 0, symOff, symtab.Length, 3, 8, 24);  // .symtab, link -> .strtab
        WriteShdr(file, shoff + 192, strName, 3, 0, strOff, strtab.Length, 0, 1, 0);   // .strtab STRTAB
        WriteShdr(file, shoff + 256, shstrName, 3, 0, shstrOff, shstr.Length, 0, 1, 0);// .shstrtab STRTAB
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
