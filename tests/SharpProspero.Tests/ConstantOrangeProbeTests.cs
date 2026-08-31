using System;
using System.IO;
using System.Security.Cryptography;
using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

// Branch-local diagnostic contract. This does not qualify normal mesh shaders or interpolation.
public sealed class KnownVaryingProbeTests
{
    [Fact]
    public void KnownVaryingRestoresExactCoWithOnlyTwoInstructionReplacements()
    {
        using Stream stream = typeof(BuiltInShaders).Assembly.GetManifestResourceStream("SharpProspero.Shaders.mesh_ps.sb")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] orange = memory.ToArray();
        Assert.Equal(968, orange.Length);
        Assert.Equal(Convert.FromHexString("000708C8"), orange[0x44..0x48]);
        Assert.Equal(Convert.FromHexString("010709C8"), orange[0x54..0x58]);
        Convert.FromHexString("F202047E").CopyTo(orange, 0x44);
        Convert.FromHexString("000080BF").CopyTo(orange, 0x54);
        Assert.Equal("ff49d8756e41d443954a99463b1a050d47b7d25587ee9cb0ccc357c2e5f75b8f",
            Convert.ToHexString(SHA256.HashData(orange)).ToLowerInvariant());
        Assert.Equal(0xF0, orange[0x48]);
        Assert.Equal(0x80, orange[0x4C]);
        byte[] restoredCn = (byte[])orange.Clone();
        restoredCn[0x48] = 0xF2;
        restoredCn[0x4C] = 0xF2;
        Assert.Equal("ab3722ac8e950ead1a53adfa9c2ff0d85be68cae0eb11ea2bc40ffb85461490d",
            Convert.ToHexString(SHA256.HashData(restoredCn)).ToLowerInvariant());
        // Both containers must still load through the SDK's actual loader.
        Assert.Equal(ShaderBinary.Load(restoredCn).Code.Length, BuiltInShaders.MeshPixel().Code.Length);
        Assert.True(ShaderBinary.Load(restoredCn).Header.SequenceEqual(BuiltInShaders.MeshPixel().Header));
    }
}
