using SharpProspero.Graphics.Agc;
using System;
using System.Buffers.Binary;
using Xunit;

namespace SharpProspero.Tests;

// Builds a minimal container in the same shape the shader compiler emits - a section table with a
// .shader_text (code) and a .shader_header (the program header, magic then version then the counts) -
// and checks the loader reads both sections and the header fields. The section layout mirrors a real
// compiled binary, where .shader_header carries the magic 0x34333231 and version 24.
public sealed class ShaderBinaryTests
{
    private static byte[] BuildContainer(byte type, byte numCx, byte numSh, byte[] code)
    {
        byte[] strtab = System.Text.Encoding.ASCII.GetBytes("\0.shader_text\0.shader_header\0.shstrtab\0");
        int nameText = 1, nameHeader = 14, nameStr = 29;

        byte[] header = new byte[96];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x34333231);       // magic
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 24u);    // version
        header[90] = type; header[91] = numCx; header[92] = numSh;

        int textOff = 64;
        int headerOff = textOff + code.Length;
        int strOff = headerOff + header.Length;
        int shoff = (strOff + strtab.Length + 7) & ~7;
        int total = shoff + 4 * 64;
        byte[] elf = new byte[total];
        elf[0] = 0x7f; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(40), (ulong)shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(58), 64);   // shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(60), 4);    // shnum
        BinaryPrimitives.WriteUInt16LittleEndian(elf.AsSpan(62), 3);    // shstrndx

        code.CopyTo(elf.AsSpan(textOff));
        header.CopyTo(elf.AsSpan(headerOff));
        strtab.CopyTo(elf.AsSpan(strOff));

        void Section(int i, int name, int off, int size)
        {
            int rec = shoff + i * 64;
            BinaryPrimitives.WriteUInt32LittleEndian(elf.AsSpan(rec), (uint)name);
            BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(rec + 24), (ulong)off);
            BinaryPrimitives.WriteUInt64LittleEndian(elf.AsSpan(rec + 32), (ulong)size);
        }
        Section(0, 0, 0, 0);
        Section(1, nameText, textOff, code.Length);
        Section(2, nameHeader, headerOff, header.Length);
        Section(3, nameStr, strOff, strtab.Length);
        return elf;
    }

    [Fact]
    public void Load_ReadsHeaderReflectionAndCode()
    {
        byte[] code = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        ShaderBinary bin = ShaderBinary.Load(BuildContainer(type: 1, numCx: 7, numSh: 5, code));
        Assert.Equal(ShaderBinary.HeaderMagic, bin.Magic);
        Assert.Equal(24u, bin.Version);
        Assert.Equal(1, bin.ProgramType);
        Assert.Equal(7, bin.ContextRegisterCount);
        Assert.Equal(5, bin.ShaderRegisterCount);
        Assert.Equal(8, bin.Code.Length);
        Assert.Equal(0x44, bin.Code[3]);
        Assert.Equal(96, bin.Header.Length);
    }

    [Fact]
    public void Load_RejectsNonContainer()
    {
        Assert.Throws<ArgumentException>(() => ShaderBinary.Load(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Fact]
    public void Load_RejectsMissingSections()
    {
        // A valid ELF header with no section table is not a shader binary.
        byte[] bytes = new byte[64];
        bytes[0] = 0x7f; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        Assert.Throws<ArgumentException>(() => ShaderBinary.Load(bytes));
    }

    [Fact]
    public void BuiltInMeshShaders_AreEmbeddedAndParse()
    {
        // The whole offline path: the mesh shaders were compiled and embedded, and load and parse here.
        ShaderBinary vs = BuiltInShaders.MeshVertex();
        Assert.Equal(ShaderBinary.HeaderMagic, vs.Magic);
        Assert.Equal(24u, vs.Version);
        Assert.True(vs.Code.Length > 0);

        ShaderBinary ps = BuiltInShaders.MeshPixel();
        Assert.Equal(ShaderBinary.HeaderMagic, ps.Magic);
        Assert.Equal(24u, ps.Version);
        Assert.True(ps.Code.Length > 0);
    }
}
