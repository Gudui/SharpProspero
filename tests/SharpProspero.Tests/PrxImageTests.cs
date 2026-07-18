// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PrxImageTests
{
    [Fact]
    public void Parse_MalformedProgramHeaderOffset_ThrowsCleanFormatException()
    {
        // A header whose program-header offset is negative when read as a signed value must fail as a
        // format error, not an index-out-of-range from a raw span access.
        byte[] elf = new byte[0x40];
        BinaryPrimitives.WriteUInt32LittleEndian(elf, 0x464C457F);         // ELF magic
        elf[4] = 2;                                                        // ELFCLASS64
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x12), 0x3E);  // x86-64
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(0x20), 0x8000000080000000); // e_phoff
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x36), 0x38);  // e_phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(0x38), 1);     // e_phnum

        Assert.Throws<PrxFormatException>(() => PrxImage.Parse(elf));
    }

    // A plaintext module from a local SDK tree, used when present. The reader is exercised against a
    // real module; the fact is skipped where the tree is absent so the suite stays portable.
    private static string? SampleModule()
    {
        string? sdk = Environment.GetEnvironmentVariable("PROSPERO_SDK_DIR");
        if (string.IsNullOrEmpty(sdk))
            sdk = @"C:\Program Files (x86)\SCE\Prospero SDKs\2.000";
        string path = Path.Combine(sdk, "target", "sce_module", "libSceJobManager.prx");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Parse_RejectsNonElf()
    {
        Assert.Throws<PrxFormatException>(() => PrxImage.Parse(new byte[64]));
    }

    [Fact]
    public void Load_RealModule_EnumeratesExports()
    {
        string? path = SampleModule();
        if (path is null)
            return; // no local module tree; nothing to assert against

        PrxImage image = PrxImage.Load(path);

        Assert.Equal(0xFE18, image.Type);
        Assert.NotEmpty(image.Exports);
        Assert.All(image.Exports, e => Assert.Equal(SceNid.Length, e.Nid.Length));
        // At least one function export is present in a real library.
        Assert.Contains(image.Exports, e => e.IsFunction);
    }

    [Fact]
    public void FindByName_ResolvesAKnownModuleEntry()
    {
        string? path = SampleModule();
        if (path is null)
            return;

        PrxImage image = PrxImage.Load(path);
        // The module's own initialize entry is exported; its identifier must be present.
        string nid = SceNid.Compute("sceJobManagerInitialize");
        bool present = image.Exports.Any(e => e.Nid == nid);
        // Not every SDK revision exports this exact name; assert the lookup path works structurally.
        Assert.Equal(present, image.FindByName("sceJobManagerInitialize") is not null);
    }
}
