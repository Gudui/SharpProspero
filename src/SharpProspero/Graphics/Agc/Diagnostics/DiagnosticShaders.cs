using System;
using System.IO;

namespace SharpProspero.Graphics.Agc.Diagnostics;

/// <summary>Explicit shader resources for isolated GPU diagnostics, not general mesh rendering.</summary>
/// <remarks>
/// Set the MSBuild property <c>EmbedDiagnosticShaders=true</c> to include these resources.
/// The programs are preserved from the firmware-5.50 GPU-CL control. Their successful use in that
/// probe does not qualify <see cref="SharpProspero.Graphics.Renderer3D"/> or arbitrary pipeline state.
/// </remarks>
public static class DiagnosticShaders
{
    /// <summary>Loads the GPU-CL shader-generated clip-space triangle program; it reads no mesh buffers.</summary>
    public static ShaderBinary HardcodedTriangleVertex() => Load("hardcoded_triangle_vs.sb");

    /// <summary>Loads the GPU-CL constant-white pixel program; it does not consume interpolated colour.</summary>
    public static ShaderBinary ConstantWhitePixel() => Load("constant_white_ps.sb");

    private static ShaderBinary Load(string name)
    {
        using Stream stream = typeof(DiagnosticShaders).Assembly.GetManifestResourceStream(
            "SharpProspero.Diagnostics.Shaders." + name)
            ?? throw new InvalidOperationException(
                "Diagnostic shaders are not embedded. Build SharpProspero with EmbedDiagnosticShaders=true.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return ShaderBinary.Load(bytes);
    }
}
