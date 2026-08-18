// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// Maps vertex/geometry shader exports to pixel shader inputs and produces the required
/// SPI context registers.
/// </summary>
public sealed unsafe class CxInterpolantMapping
{
    /// <summary>Maximum number of interpolant registers.</summary>
    public const int MaxRegisters = 32;

    private readonly CxRegister[] _regs = new CxRegister[MaxRegisters];
    private int _count;

    /// <summary>The active interpolant mapping context registers.</summary>
    public ReadOnlySpan<CxRegister> Registers => _regs.AsSpan(0, _count);

    /// <summary>Number of valid registers in the mapping.</summary>
    public int Count => _count;

    /// <summary>
    /// Builds the interpolant mapping context registers from the vertex shader and pixel shader.
    /// </summary>
    public static CxInterpolantMapping Create(AgcShader vs, AgcShader ps, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(vs);
        ArgumentNullException.ThrowIfNull(ps);

        var mapping = new CxInterpolantMapping();
        // Allocate a 512-byte buffer for the AGC native CxInterpolantMapping struct
        byte* buffer = stackalloc byte[512];
        new Span<byte>(buffer, 512).Clear();

        int result = SceAgc.sceAgcCreateInterpolantMapping(buffer, vs.Handle, ps.Handle);
        trace?.Invoke("AGC_INTERP_CREATE result=0x" + result.ToString("X8"));
        if (result != 0)
        {
            trace?.Invoke("AGC_INTERP_CREATE_FAILED code=0x" + result.ToString("X8"));
            return mapping;
        }

        // Dump first 16 dwords of buffer for diagnostics
        uint* dwords = (uint*)buffer;
        for (int i = 0; i < 16; i++)
        {
            trace?.Invoke("AGC_INTERP_RAW dw[" + i + "]=0x" + dwords[i].ToString("X8"));
        }

        // Direct array of CxRegister
        CxRegister* regs = (CxRegister*)buffer;
        int valid = 0;
        // The built-in mesh pixel shader consumes 2 interpolants (TEXCOORD0 normal, TEXCOORD1 color).
        // Only emit active interpolants so the SPI hardware interpolator does not stall waiting for unused exports.
        int maxInterp = 2;
        for (int i = 0; i < maxInterp; i++)
        {
            if (regs[i].Offset != 0)
            {
                mapping._regs[valid++] = regs[i];
                trace?.Invoke("AGC_INTERP_REG index=" + i + " offset=0x" + regs[i].Offset.ToString("X4") + " value=0x" + regs[i].Value.ToString("X8"));
            }
        }
        mapping._count = valid;

        trace?.Invoke("AGC_INTERP_TOTAL count=" + mapping._count);
        return mapping;
    }
}
