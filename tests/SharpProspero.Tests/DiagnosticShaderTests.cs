using SharpProspero.Graphics.Agc;
using SharpProspero.Graphics.Agc.Diagnostics;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace SharpProspero.Tests;

public class DiagnosticShaderTests
{
    private static byte[] Resource(string name)
    {
        using Stream stream = typeof(BuiltInShaders).Assembly.GetManifestResourceStream(name)
            ?? throw new IOException("Missing resource: " + name);
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    [Theory]
    [InlineData("mesh_vs.sb", "2eec16311d00559c85bfb0a540e0c3df70fd17348a842ece30fbe68ca77fe114")]
    [InlineData("mesh_ps.sb", "fa760509a971810edf704a2e38e7522d8b241c938f48e0a196f964f0d95eaa0c")]
    public void NormalMeshResourcesMatchUpstreamInsteadOfDiagnostics(string name, string hash)
    {
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(Resource("SharpProspero.Shaders." + name))).ToLowerInvariant());
    }

#if DIAGNOSTIC_SHADERS
    [Theory]
    [InlineData("hardcoded_triangle_vs.sb", 992, "cc740ea480864fdd96925e89e4a623b1f42c1fea8f8e58a38f64f2063300a28c")]
    [InlineData("constant_white_ps.sb", 968, "ab3722ac8e950ead1a53adfa9c2ff0d85be68cae0eb11ea2bc40ffb85461490d")]
    public void ExplicitDiagnosticApiPreservesTheCompleteClResource(string name, int length, string hash)
    {
        byte[] bytes = Resource("SharpProspero.Diagnostics.Shaders." + name);
        Assert.Equal(length, bytes.Length);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        ShaderBinary expected = ShaderBinary.Load(bytes);
        ShaderBinary actual = name == "hardcoded_triangle_vs.sb"
            ? DiagnosticShaders.HardcodedTriangleVertex()
            : DiagnosticShaders.ConstantWhitePixel();
        Assert.Equal(expected.Code.ToArray(), actual.Code.ToArray());
        Assert.Equal(expected.Header.ToArray(), actual.Header.ToArray());
    }
#else
    [Fact]
    public void DiagnosticsAreAbsentAndFailClearlyWithoutOptIn()
    {
        Assert.DoesNotContain(typeof(BuiltInShaders).Assembly.GetManifestResourceNames(),
            name => name.StartsWith("SharpProspero.Diagnostics.", StringComparison.Ordinal));
        Assert.Contains("EmbedDiagnosticShaders=true",
            Assert.Throws<InvalidOperationException>(() => DiagnosticShaders.HardcodedTriangleVertex()).Message);
        Assert.Contains("EmbedDiagnosticShaders=true",
            Assert.Throws<InvalidOperationException>(() => DiagnosticShaders.ConstantWhitePixel()).Message);
    }
#endif
}
