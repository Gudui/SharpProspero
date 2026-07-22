// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;

namespace SharpProspero.Graphics.Agc;

/// <summary>The pipeline stage a shader runs at.</summary>
public enum ShaderStage : byte
{
    /// <summary>Compute shader.</summary>
    Compute = 0,
    /// <summary>Pixel (fragment) shader.</summary>
    Pixel = 1,
    /// <summary>Geometry shader (or a fused vertex-plus-geometry shader).</summary>
    Geometry = 2,
    /// <summary>Hull shader (a fused pair of hull halves).</summary>
    Hull = 3,
    /// <summary>The front half of a geometry shader, to be fused with a back half.</summary>
    GeometryFront = 4,
    /// <summary>The front half of a hull shader, to be fused with a back half.</summary>
    HullFront = 5,
    /// <summary>The back half of a geometry shader, to be fused with a front half.</summary>
    GeometryBack = 6,
    /// <summary>The back half of a hull shader, to be fused with a front half.</summary>
    HullBack = 7,
    /// <summary>Function shader.</summary>
    Function = 8,
}

/// <summary>
/// A shader created from a compiled shader binary. A shader binary is produced ahead of time by the
/// shader compiler and is a header section plus a code section; <see cref="Create"/> reads the header,
/// prepares the shader in place, and returns a handle the command buffer binds. The header and code
/// memory must stay alive - and the code must live in GPU-readable memory - for as long as the shader
/// is in use.
/// </summary>
public sealed unsafe class AgcShader
{
    private AgcShader(void* handle) => Handle = handle;

    /// <summary>The shader handle, as the register-binding and context calls expect it.</summary>
    public void* Handle { get; }

    /// <summary>
    /// Prepares a shader from its compiled binary: <paramref name="header"/> is the binary's header
    /// section (writable, and where the shader is prepared), and <paramref name="gpuCode"/> is the code
    /// section in GPU-readable memory. Both must outlive the shader.
    /// </summary>
    /// <exception cref="ProsperoException">The shader could not be created.</exception>
    public static AgcShader Create(void* header, void* gpuCode)
    {
        void* handle = null;
        SceResult.ThrowIfFailed(
            SceAgc.sceAgcCreateShader(&handle, header, gpuCode),
            nameof(SceAgc.sceAgcCreateShader));
        return new AgcShader(handle);
    }

    /// <summary>
    /// Fuses a front shader half and a back shader half (the two-part geometry or hull stages) into one
    /// shader written to <paramref name="fusedStorage"/>, using <paramref name="scratch"/> as working
    /// memory. Storage must be at least the size the fused-size query reports.
    /// </summary>
    /// <exception cref="ProsperoException">The halves could not be fused.</exception>
    public static AgcShader Fuse(AgcShader front, AgcShader back, void* fusedStorage, void* scratch)
    {
        SceResult.ThrowIfFailed(
            SceAgc.sceAgcFuseShaderHalves(fusedStorage, front.Handle, back.Handle, scratch),
            nameof(SceAgc.sceAgcFuseShaderHalves));
        return new AgcShader(fusedStorage);
    }
}
