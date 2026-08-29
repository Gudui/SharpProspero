using SharpProspero.Graphics.Agc;
using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace SharpProspero.Tests;

public class ShaderInfoTests
{
    private static byte[] LoadShaderResource(string name)
    {
        using Stream stream = typeof(BuiltInShaders).Assembly.GetManifestResourceStream(name)
            ?? throw new IOException($"Missing embedded shader {name}.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] LoadShaderSection(byte[] container, string wantedName)
    {
        ulong sectionTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(container.AsSpan(0x28));
        ushort sectionEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x3A));
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x3C));
        ushort stringTableIndex = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x3E));
        int stringRecord = checked((int)sectionTableOffset + stringTableIndex * sectionEntrySize);
        int stringTableOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(container.AsSpan(stringRecord + 24)));

        for (int index = 0; index < sectionCount; index++)
        {
            int record = checked((int)sectionTableOffset + index * sectionEntrySize);
            int nameOffset = checked(stringTableOffset + (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(record)));
            int nameEnd = Array.IndexOf(container, (byte)0, nameOffset);
            string name = System.Text.Encoding.ASCII.GetString(container, nameOffset, nameEnd - nameOffset);
            if (name != wantedName)
                continue;

            int offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(container.AsSpan(record + 24)));
            int size = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(container.AsSpan(record + 32)));
            return container[offset..(offset + size)];
        }

        throw new IOException($"Missing shader section {wantedName}.");
    }

    [Theory]
    [InlineData("SharpProspero.Shaders.mesh_vs.sb")]
    [InlineData("SharpProspero.Shaders.mesh_ps.sb")]
    public void Read_ReportsAValidHeaderMatchingTheDeviceReader(string resource)
    {
        byte[] container = LoadShaderResource(resource);
        ShaderInfo info = ShaderInfo.Read(container);
        ShaderBinary reference = ShaderBinary.Load(container);

        Assert.True(info.IsValid);
        Assert.Equal(ShaderInfo.HeaderMagic, info.Magic);
        Assert.Equal(24u, info.Version);
        // The kind and the register counts agree with the device reader, which reads the same header.
        Assert.Equal(reference.ProgramType, info.Kind);
        Assert.Equal(reference.ContextRegisterCount, info.ContextRegisters.Count);
        Assert.Equal(reference.ShaderRegisterCount, info.ShaderRegisters.Count);
        Assert.True(info.CodeSectionSize > 0, "the microcode section is present");
    }

    [Fact]
    public void Read_TheMeshPixelShaderIsAPixelStage()
    {
        ShaderInfo info = ShaderInfo.Read(LoadShaderResource("SharpProspero.Shaders.mesh_ps.sb"));
        Assert.Equal("pixel", info.KindName);
    }

    [Fact]
    public void Read_MeshPixelShaderAdvertisesOnlyItsUncompressedMrt0Export()
    {
        byte[] container = LoadShaderResource("SharpProspero.Shaders.mesh_ps.sb");
        ShaderInfo info = ShaderInfo.Read(container);

        Assert.Equal(9, info.ContextRegisters.Count);
        Assert.Equal(5, info.ShaderRegisters.Count);
        Assert.Contains(info.ContextRegisters, register => register.Offset == 0x01C4 && register.Value == 0);
        Assert.Contains(info.ContextRegisters, register => register.Offset == 0x01C5 && register.Value == 9);
        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x01B8 && register.Value == 0x01000000);
        Assert.Equal(
            "f62be2093642e923be5ba72cf2c61fa1ae0555584c69e289d3bd7416922659b6",
            Convert.ToHexString(SHA256.HashData(LoadShaderSection(container, ".shader_text"))).ToLowerInvariant());
    }

    [Fact]
    public void Read_MeshVertexShaderMatchesTheValidatedNggExportProgram()
    {
        byte[] container = LoadShaderResource("SharpProspero.Shaders.mesh_vs.sb");

        // The validated gfx1030 program completes its primitive and position exports. Parameter
        // exports do not own the position-stage completion bit, and the merged NGG vertex stage
        // selects vertex_id from v5 after the five geometry-system VGPR inputs.
        Assert.Equal(
            "d732f278f6ef845ea0e1967e8e84f97057a4e61e0455abf4cc9ad0e72642c232",
            Convert.ToHexString(SHA256.HashData(LoadShaderSection(container, ".shader_text"))).ToLowerInvariant());
    }

    [Fact]
    public void Read_ResolvesSelfRelativeRegisterPointers()
    {
        ShaderInfo info = ShaderInfo.Read(LoadShaderResource("SharpProspero.Shaders.mesh_vs.sb"));

        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x01FF && register.Value == 0x00000040);
        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x0291 && register.Value == 0x10020040);
        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x02E4 && register.Value == 0x00000000);
        Assert.DoesNotContain(info.ContextRegisters, register => register.Offset == 0x0000);
    }

    [Fact]
    public void Read_RejectsANonShaderFile()
    {
        Assert.Throws<PrxFormatException>(() => ShaderInfo.Read([1, 2, 3, 4]));
    }
}
