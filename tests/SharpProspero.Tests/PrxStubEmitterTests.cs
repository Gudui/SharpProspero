// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PrxStubEmitterTests
{
    [Fact]
    public void BuildObject_IsAStubLibElf()
    {
        byte[] obj = PrxStubEmitter.BuildObject("libFoo", ["sceFooOpen", "sceFooClose"]);

        Assert.True(obj.Length > 64);
        Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(obj)); // ELF magic
        Assert.Equal(2, obj[4]);  // ELFCLASS64
        Assert.Equal(0xFE0C, BinaryPrimitives.ReadUInt16LittleEndian(obj.AsSpan(0x10))); // ET_SCE_STUBLIB
        Assert.Equal(0x3E, BinaryPrimitives.ReadUInt16LittleEndian(obj.AsSpan(0x12)));   // x86-64
    }

    [Fact]
    public void BuildObject_EmbedsTheComputedIdentifiers()
    {
        byte[] obj = PrxStubEmitter.BuildObject("libFoo", ["sceFooOpen"]);

        // The identifier bytes for the export must appear in the object (in the .scenid section).
        byte[] nid = SceNid.ComputeBytes("sceFooOpen");
        Assert.True(Contains(obj, nid), "Expected the computed identifier bytes in the stub.");
    }

    [Fact]
    public void WriteStub_ProducesARawStubLibObject()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            PrxStubEmitter.WriteStub("libFoo", ["sceFooOpen"], path);
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Assert.True(bytes.Length > 64);
            Assert.Equal(0x464C457Fu, BinaryPrimitives.ReadUInt32LittleEndian(bytes)); // raw ELF, no archive wrapper
            Assert.Equal(0xFE0C, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x10)));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ElfInfo_ReadsTheEmittedStubHeader()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            PrxStubEmitter.WriteStub("libFoo", ["sceFooOpen"], path);
            ElfInfo info = ElfInfo.Read(path);
            Assert.True(info.Is64Bit);
            Assert.Equal(0xFE0C, info.Type);
            Assert.Equal("SCE stub library", info.TypeName);
            Assert.Equal(0x3E, info.Machine);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match)
                return true;
        }
        return false;
    }
}
