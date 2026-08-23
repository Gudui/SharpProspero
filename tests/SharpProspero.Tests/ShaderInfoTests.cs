using SharpProspero.Graphics.Agc;
using SharpProspero.Prx;
using System.IO;
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
    public void Read_ResolvesSelfRelativeRegisterPointers()
    {
        ShaderInfo info = ShaderInfo.Read(LoadShaderResource("SharpProspero.Shaders.mesh_vs.sb"));

        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x01FF && register.Value == 0x00000040);
        Assert.Contains(
            info.ContextRegisters,
            register => register.Offset == 0x0291 && register.Value == 0x10020040);
        Assert.DoesNotContain(info.ContextRegisters, register => register.Offset == 0x0000);
    }

    [Fact]
    public void Read_RejectsANonShaderFile()
    {
        Assert.Throws<PrxFormatException>(() => ShaderInfo.Read([1, 2, 3, 4]));
    }
}
