using System;
using System.IO;
using System.Security.Cryptography;
using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

// Branch-local diagnostic contract. Target evidence is required before this becomes a capability claim.
public sealed class StructuredVertexProbeTests
{
    [Fact]
    public void VertexProgramRetainsCrPrologueAndUsesOneWaitedStructuredFetch()
    {
        using Stream stream = typeof(BuiltInShaders).Assembly.GetManifestResourceStream("SharpProspero.Shaders.mesh_vs.sb")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] container = memory.ToArray();

        Assert.Equal(992, container.Length);
        Assert.Equal(
            "87f723dc7c805b104989bce1fc7aaca6c3f2975cf7cf19dfc559f560c815f6b7",
            Convert.ToHexString(SHA256.HashData(container)).ToLowerInvariant());
        Assert.Equal(
            "4f762099cbc8a080cc7fab1fd58a03c04c61f3fc24245407223d179757474c1a",
            Convert.ToHexString(SHA256.HashData(container[64..132])).ToLowerInvariant());
        Assert.Equal(
            Convert.FromHexString(
                "002038E005080280703F8CBFF2021A7E8002027E80020C7E0B03047EF202087E" +
                "8002187E80020E7EF2021C7E8002067ECF0800F808090A0D1F0200F801060204" +
                "0F0200F80C070E03000081BF"),
            container[132..208]);
        for (int offset = 208; offset < 320; offset += 4)
            Assert.Equal(Convert.FromHexString("00009FBF"), container[offset..(offset + 4)]);
    }
}
