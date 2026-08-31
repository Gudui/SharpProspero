using System;
using System.IO;
using System.Security.Cryptography;
using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

// Branch-local diagnostic contract. This does not qualify normal mesh shaders or interpolation.
public sealed class ConstantOrangeProbeTests
{
    [Fact]
    public void OrangeResourceDiffersFromCnOnlyInTwoConstantOperands()
    {
        using Stream stream = typeof(BuiltInShaders).Assembly.GetManifestResourceStream("SharpProspero.Shaders.mesh_ps.sb")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] orange = memory.ToArray();
        Assert.Equal(968, orange.Length);
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
