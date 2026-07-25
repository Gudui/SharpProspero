// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

// Covers the signed-container reader and writer, and the classification the inspector uses to read a
// signed file the same way as an unsigned one.
public sealed class SelfContainerTests
{
    [Fact]
    public void IsElf_And_IsSelf_TellTheFormsApart()
    {
        byte[] module = BuildModule();
        Assert.True(SelfContainer.IsElf(module));
        Assert.False(SelfContainer.IsSelf(module));

        byte[] signed = SelfContainer.Sign(module);
        Assert.True(SelfContainer.IsSelf(signed));
        Assert.False(SelfContainer.IsElf(signed));
        Assert.Equal(SelfContainer.Magic, BinaryPrimitives.ReadUInt32LittleEndian(signed));
    }

    [Fact]
    public void CheckIntegrity_MatchesAFreshlySignedContainer()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());
        SelfIntegrity integrity = SelfContainer.CheckIntegrity(signed);
        Assert.True(integrity.HasDigest);
        Assert.True(integrity.Matches);
        Assert.Equal(32, integrity.Stored.Length);
        Assert.Equal(integrity.Stored, integrity.Computed);
    }

    [Fact]
    public void CheckIntegrity_DetectsATamperedPayload()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());
        // Flip a byte in the trailing payload region, past the header; the recomputed digest no longer
        // matches the one stored in the extended info.
        signed[^1] ^= 0xFF;
        SelfIntegrity integrity = SelfContainer.CheckIntegrity(signed);
        Assert.True(integrity.HasDigest);
        Assert.False(integrity.Matches);
    }

    [Fact]
    public void Sign_DoesNotMutateTheCallersBuffer()
    {
        byte[] module = BuildModule();
        byte[] copy = (byte[])module.Clone();
        _ = SelfContainer.Sign(module);
        Assert.Equal(copy, module);
    }

    [Fact]
    public void Sign_ThenExtract_RecoversTheLoadableContent()
    {
        byte[] module = BuildModule();
        byte[] signed = SelfContainer.Sign(module, new SelfSignOptions { NormalizeHeader = false });
        byte[] recovered = SelfContainer.ExtractElf(signed);

        // The ELF header and program headers come back verbatim.
        Assert.True(SelfContainer.IsElf(recovered));
        int phTableEnd = 0x40 + BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x38)) * 0x38;
        Assert.Equal(module.AsSpan(0, phTableEnd).ToArray(), recovered.AsSpan(0, phTableEnd).ToArray());

        // Every loadable program header's file content is placed back at its offset byte for byte.
        int phoff = 0x40;
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(module.AsSpan(0x38));
        for (int i = 0; i < phnum; i++)
        {
            int ph = phoff + i * 0x38;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(ph));
            ulong off = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 0x08));
            ulong fsz = BinaryPrimitives.ReadUInt64LittleEndian(module.AsSpan(ph + 0x20));
            bool selected = type is 1 or 0x61000000 or 0x61000010 or 0x6FFFFF00;
            if (!selected || fsz == 0)
                continue;
            Assert.Equal(
                module.AsSpan((int)off, (int)fsz).ToArray(),
                recovered.AsSpan((int)off, (int)fsz).ToArray());
        }
    }

    // The real point: a module's exports and type survive a full sign then extract, so the inspector
    // reads a signed module exactly as it reads a plain one.
    [Fact]
    public void SignedModule_ReadsBackItsExportsAndType()
    {
        byte[] module = BuildModule();
        byte[] signed = SelfContainer.Sign(module);

        ModuleFile file = ModuleFile.Parse(signed);
        Assert.True(file.IsSigned);

        PrxImage image = PrxImage.Parse(file.Elf);
        string nid = SceNid.Compute("game_frame");
        PrxExport export = Assert.Single(image.Exports, e => e.Nid == nid);
        Assert.True(export.IsFunction);

        ElfInfo info = ElfInfo.Parse(file.Elf);
        Assert.Equal(0xFE18, info.Type); // ET_SCE_DYNAMIC
    }

    [Fact]
    public void ModuleFile_ClassifiesBothForms()
    {
        byte[] module = BuildModule();
        ModuleFile plain = ModuleFile.Parse(module);
        Assert.Equal(ModuleContainer.Elf, plain.Container);
        Assert.False(plain.IsSigned);
        Assert.Same(module, plain.Elf);

        ModuleFile signed = ModuleFile.Parse(SelfContainer.Sign(module));
        Assert.Equal(ModuleContainer.Signed, signed.Container);
        Assert.True(signed.IsSigned);
    }

    [Fact]
    public void ModuleFile_RejectsSomethingThatIsNeitherForm()
    {
        byte[] junk = Encoding.ASCII.GetBytes("not a module at all, just text padding padding padding");
        Assert.Throws<PrxFormatException>(() => ModuleFile.Parse(junk));
    }

    [Fact]
    public void Classify_TellsTheThreeFormsApart()
    {
        byte[] module = BuildModule();
        Assert.Equal(ModuleForm.UnsignedElf, SelfContainer.Classify(module));
        Assert.Equal(ModuleForm.SignedPlaintext, SelfContainer.Classify(SelfContainer.Sign(module)));

        // Encryption is a per-segment property, not a different header, so a container is read as
        // encrypted only once one of its segments says so.
        byte[] encrypted = MarkFirstDataSegmentEncrypted(SelfContainer.Sign(module));
        Assert.Equal(ModuleForm.SignedEncrypted, SelfContainer.Classify(encrypted));
        Assert.True(SelfContainer.HasEncryptedSegments(encrypted));
        Assert.False(SelfContainer.HasEncryptedSegments(SelfContainer.Sign(module)));

        Assert.Equal(ModuleForm.Unknown, SelfContainer.Classify([1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [Fact]
    public void ModuleFile_RejectsAnEncryptedRetailContainerClearly()
    {
        byte[] encrypted = MarkFirstDataSegmentEncrypted(SelfContainer.Sign(BuildModule()));
        PrxFormatException ex = Assert.Throws<PrxFormatException>(() => ModuleFile.Parse(encrypted));
        Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_ProducesTheContainerTheLoaderAccepts()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());

        // The header values a module must carry to be read by the loader at launch.
        Assert.Equal(0xEEF51454u, BinaryPrimitives.ReadUInt32LittleEndian(signed));
        Assert.Equal(0, signed[0x04]);                                             // version
        Assert.Equal(1, signed[0x05]);                                             // mode
        Assert.Equal(1, signed[0x06]);                                             // endian
        Assert.Equal(0x12, signed[0x07]);                                          // attributes
        Assert.Equal(0x00000101u, BinaryPrimitives.ReadUInt32LittleEndian(signed.AsSpan(0x08)));
        Assert.Equal(0x0022, BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x1A)));
        Assert.Equal((ulong)signed.Length, BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(0x10)));

        // The header region ends where the metadata footer begins, and the footer is sized from the
        // segment count.
        int segCount = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x18));
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x0C));
        int metaSize = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x0E));
        Assert.Equal(0x110 + (segCount + 8) * 0x40, metaSize);

        // Content comes in pairs: a digest segment then the data segment it covers.
        Assert.Equal(0, segCount % 2);
        for (int k = 0; k < segCount / 2; k++)
        {
            ulong digest = BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(0x20 + k * 2 * 0x20));
            ulong data = BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(0x20 + (k * 2 + 1) * 0x20));
            Assert.Equal((ulong)(k * 2 + 1) << 20 | 0x10004, digest);
            Assert.Equal(0x2804u, data & 0xFFFFF);
        }

        // The embedded ELF sits after the segment table, and the extended info carries the authority
        // id a container without a real signature uses.
        int elfStart = 0x20 + segCount * 0x20;
        Assert.True(SelfContainer.IsElf(signed.AsSpan(elfStart)));
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(elfStart + 0x38));
        int extStart = (elfStart + 0x40 + phnum * 0x38 + 15) & ~15;
        Assert.Equal(extStart + 0x40 + 0x30, headerSize);
        Assert.Equal(SelfContainer.DeveloperAuthorityId,
            BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(extStart)));
    }

    // Returns a copy of a container whose first data segment claims encrypted storage.
    private static byte[] MarkFirstDataSegmentEncrypted(byte[] signed)
    {
        byte[] copy = (byte[])signed.Clone();
        int segCount = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(0x18));
        for (int i = 0; i < segCount; i++)
        {
            int entry = 0x20 + i * 0x20;
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(copy.AsSpan(entry));
            if ((flags & 0x800) == 0) continue;   // a digest segment, not payload
            BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(entry), flags | 0x2);
            return copy;
        }
        throw new InvalidOperationException("The container has no data segment to mark.");
    }

    [Fact]
    public void ExtractElf_RejectsEncryptedSegmentData()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());

        // Set the encrypted bit on the first data (blocked) segment. A retail container encrypts its
        // segment data, and the reader must refuse rather than return wrong bytes.
        int segCount = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x18));
        bool flipped = false;
        for (int i = 0; i < segCount; i++)
        {
            int entry = 0x20 + i * 0x20;
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(entry));
            if ((flags & 0x800) != 0) // blocked = a data segment
            {
                BinaryPrimitives.WriteUInt64LittleEndian(signed.AsSpan(entry), flags | 0x2);
                flipped = true;
                break;
            }
        }
        Assert.True(flipped, "no data segment found to mark encrypted");

        PrxFormatException ex = Assert.Throws<PrxFormatException>(() => SelfContainer.ExtractElf(signed));
        Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsATruncatedContainer()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());
        byte[] truncated = signed.AsSpan(0, 0x30).ToArray(); // header plus one entry, table cut off
        Assert.Throws<PrxFormatException>(() => SelfContainer.Parse(truncated));
    }

    // A malformed container whose data-segment offset would wrap the bounds check must be rejected as
    // a format error, not crash the reader with an index exception a caller does not catch.
    [Fact]
    public void ExtractElf_OnAWrappingSegmentOffset_ThrowsPrxFormat()
    {
        byte[] signed = SelfContainer.Sign(BuildModule());

        // Poison the first data (blocked) segment's file offset and size so offset + size overflows.
        int segCount = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x18));
        bool poisoned = false;
        for (int i = 0; i < segCount; i++)
        {
            int entry = 0x20 + i * 0x20;
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(entry));
            if ((flags & 0x800) != 0)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(signed.AsSpan(entry + 0x08), 0xFFFFFFFFFFFFFF00UL);
                BinaryPrimitives.WriteUInt64LittleEndian(signed.AsSpan(entry + 0x10), 0x100UL);
                poisoned = true;
                break;
            }
        }
        Assert.True(poisoned, "no data segment found to poison");

        Assert.Throws<PrxFormatException>(() => SelfContainer.ExtractElf(signed));
    }

    // A program header inside the container that places a segment past a readable range must also be a
    // format error rather than an index exception.
    [Fact]
    public void ExtractElf_OnAWrappingProgramHeaderOffset_ThrowsPrxFormat()
    {
        byte[] signed = SelfContainer.Sign(BuildModule(), new SelfSignOptions { NormalizeHeader = false });

        int segCount = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(0x18));
        int elfStart = 0x20 + segCount * 0x20;

        // Find the first data segment, read the program-header index it names, and poison that header's
        // file offset in the container's embedded ELF region.
        for (int i = 0; i < segCount; i++)
        {
            int entry = 0x20 + i * 0x20;
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(signed.AsSpan(entry));
            if ((flags & 0x800) == 0)
                continue;
            int phIndex = (int)((flags >> 20) & 0xFFFF);
            int ph = elfStart + 0x40 + phIndex * 0x38;
            BinaryPrimitives.WriteUInt64LittleEndian(signed.AsSpan(ph + 0x08), 0xFFFFFFFFFFFFFF00UL);
            break;
        }

        Assert.Throws<PrxFormatException>(() => SelfContainer.ExtractElf(signed));
    }

    // Signing an ELF whose only loadable program header has a wrapping offset must fail as a format
    // error, not crash while copying the segment.
    [Fact]
    public void Sign_OnAWrappingProgramHeaderOffset_ThrowsPrxFormat()
    {
        byte[] elf = new byte[0x100];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1; elf[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x10), 0xFE10);      // e_type
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x12), 0x3E);        // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x20), 0x40);        // e_phoff
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x36), 0x38);        // e_phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x38), 1);           // e_phnum
        BinaryPrimitives.WriteUInt32LittleEndian(elf.AsSpan(0x40), 1);           // p_type PT_LOAD
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x40 + 0x08), 0xFFFFFFFFFFFFFF00UL); // p_offset
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x40 + 0x20), 0x100);                // p_filesz

        Assert.Throws<PrxFormatException>(() => SelfContainer.Sign(elf));
    }

    [Fact]
    public void Sign_DerivesADeveloperAuthorityId()
    {
        SelfImage image = SelfContainer.Parse(SelfContainer.Sign(BuildModule()));
        Assert.NotNull(image.ExtInfo);
        // A developer-accepted container carries the 0x31.. authority prefix.
        Assert.Equal(0x31u, (uint)(image.ExtInfo!.AuthorityId >> 56));
    }

    // Builds a real module through the linker and the module writer: a library with one exported
    // function so the round-trip carries a dynamic table, a symbol table and export records.
    private static byte[] BuildModule()
    {
        var options = new LinkOptions();
        options.ExtraObjects.Add(ElfObjectReader.Read(BuildLibraryObject(), "lib"));
        foreach (StubCatalog.Entry entry in StubCatalog.Core)
            options.ExtraStubs.Add(StubLibrary.Parse(
                PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion),
                entry.Library));

        LinkResolution result = Linker.Resolve(options);
        Assert.Empty(result.Unresolved);
        return DynamicWriter.Write(result, entrySymbol: null, ModuleKind.Library,
            exportSymbols: ["game_frame"], moduleFileName: "test.prx");
    }

    // A library object: a defined global "game_frame" at a non-zero offset plus an undefined reference
    // the stubs resolve.
    private static byte[] BuildLibraryObject()
    {
        byte[] strtab = Encoding.ASCII.GetBytes("\0game_frame\0sceVideoOutOpen\0");
        const int frameOff = 1, videoOff = 12;
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        const int textName = 1, symName = 7, strName = 15, shstrName = 23;
        byte[] text = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xC3]; // nops then ret

        byte[] symtab = new byte[3 * 24];
        WriteSymValue(symtab, 1, frameOff, (1 << 4) | 2, 1, value: 8);
        WriteSym(symtab, 2, videoOff, (1 << 4) | 2, 0);

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

    // A data segment is stored in fixed-size blocks and the digest segment paired with it carries one
    // slot per block. A pair sized for a single block is only correct while the content fits in one
    // block; every larger segment then declares less digest data than the loader reads for it, and the
    // module is turned away before it starts. These lock the size to the block count.

    private const int BlockSize = 0x4000;
    private const int DigestSlot = 0x20;

    // A minimal module with one loadable segment of the requested size, laid out the way the writer
    // requires: the program-header table directly after the ELF header, then the segment content.
    private static byte[] BuildModuleWithLoadSize(int contentSize)
    {
        int contentOffset = 0x40 + 0x38;
        var elf = new byte[contentOffset + contentSize];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1; elf[6] = 1; elf[7] = 9; elf[8] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x10), 0xFE10);
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x12), 0x3E);
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x20), 0x40);
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x34), 0x40);
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x36), 0x38);
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x38), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(elf.AsSpan(0x40), 1);          // PT_LOAD
        BinaryPrimitives.WriteUInt32LittleEndian(elf.AsSpan(0x44), 5);          // read + execute
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x48), (ulong)contentOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x60), (ulong)contentSize);
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x68), (ulong)contentSize);
        for (int i = 0; i < contentSize; i++)
            elf[contentOffset + i] = (byte)(i * 7 + 1);
        return elf;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(BlockSize)]
    [InlineData(BlockSize + 1)]
    [InlineData(BlockSize * 3)]
    [InlineData(BlockSize * 5 + 123)]
    public void Sign_SizesEachDigestSegmentToTheBlockCountOfItsContent(int contentSize)
    {
        SelfImage image = SelfContainer.Parse(SelfContainer.Sign(BuildModuleWithLoadSize(contentSize)));
        int expectedSlots = (contentSize + BlockSize - 1) / BlockSize;

        var pairs = 0;
        for (int i = 0; i + 1 < image.Segments.Count; i += 2)
        {
            SelfSegment digest = image.Segments[i], data = image.Segments[i + 1];
            Assert.False(digest.Blocked);
            Assert.True(data.Blocked);
            Assert.Equal((ulong)contentSize, data.FileSize);
            Assert.Equal((ulong)(expectedSlots * DigestSlot), digest.FileSize);
            Assert.Equal(digest.FileSize, digest.MemSize);
            pairs++;
        }
        Assert.Equal(1, pairs);
    }

    [Fact]
    public void Sign_PlacesEverySegmentWithinTheFileAndRoundTripsAMultiBlockModule()
    {
        byte[] module = BuildModuleWithLoadSize(BlockSize * 4 + 9);
        byte[] signed = SelfContainer.Sign(module);
        SelfImage image = SelfContainer.Parse(signed);

        // Nothing overlaps or runs past the end: each segment starts where the previous one finished,
        // allowing for the 16-byte padding after a content segment.
        ulong cursor = (ulong)(image.HeaderSize + image.MetaSize);
        foreach (SelfSegment seg in image.Segments)
        {
            Assert.True(seg.FileOffset >= cursor, "A segment starts before the end of the one before it.");
            Assert.True(seg.FileOffset + seg.FileSize <= (ulong)signed.Length, "A segment runs past the file.");
            cursor = seg.FileOffset + seg.FileSize;
        }
        Assert.Equal(image.FileSize, (ulong)signed.Length);
        Assert.True(SelfContainer.CheckIntegrity(signed).Matches);
        Assert.Equal(module, SelfContainer.ExtractElf(signed)[..module.Length]);
    }
}
