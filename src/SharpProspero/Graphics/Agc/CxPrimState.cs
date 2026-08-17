// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// Primitive state configuration for NGG/primitive assembly.
/// </summary>
public sealed unsafe class CxPrimState
{
    /// <summary>Maximum number of primitive state registers.</summary>
    public const int MaxRegisters = 32;

    private readonly CxRegister[] _regs = new CxRegister[MaxRegisters];
    private int _count;

    /// <summary>The active primitive state context registers.</summary>
    public ReadOnlySpan<CxRegister> Registers => _regs.AsSpan(0, _count);

    /// <summary>Number of valid registers in the state.</summary>
    public int Count => _count;

    /// <summary>
    /// Builds the primitive state context registers from the vertex/geometry shader.
    /// </summary>
    public static CxPrimState Create(AgcShader vs, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(vs);

        var state = new CxPrimState();
        byte* buffer = stackalloc byte[512];
        new Span<byte>(buffer, 512).Clear();

        int result = SceAgc.sceAgcCreatePrimState(buffer, vs.Handle, null, null, 0);
        trace?.Invoke("AGC_PRIMSTATE_CREATE result=0x" + result.ToString("X8"));
        if (result != 0)
        {
            trace?.Invoke("AGC_PRIMSTATE_CREATE_FAILED code=0x" + result.ToString("X8"));
            return state;
        }

        uint* dwords = (uint*)buffer;
        for (int i = 0; i < 16; i++)
        {
            trace?.Invoke("AGC_PRIMSTATE_RAW dw[" + i + "]=0x" + dwords[i].ToString("X8"));
        }

        uint headerCount = dwords[0];
        if (headerCount > 0 && headerCount <= MaxRegisters)
        {
            CxRegister* regs = (CxRegister*)(buffer + 8);
            for (int i = 0; i < (int)headerCount; i++)
            {
                state._regs[i] = regs[i];
                trace?.Invoke("AGC_PRIMSTATE_REG index=" + i + " offset=0x" + regs[i].Offset.ToString("X4") + " value=0x" + regs[i].Value.ToString("X8"));
            }
            state._count = (int)headerCount;
        }
        else
        {
            CxRegister* regs = (CxRegister*)buffer;
            int valid = 0;
            for (int i = 0; i < MaxRegisters; i++)
            {
                if (regs[i].Offset != 0)
                {
                    state._regs[valid++] = regs[i];
                    trace?.Invoke("AGC_PRIMSTATE_REG index=" + i + " offset=0x" + regs[i].Offset.ToString("X4") + " value=0x" + regs[i].Value.ToString("X8"));
                }
            }
            state._count = valid;
        }

        trace?.Invoke("AGC_PRIMSTATE_TOTAL count=" + state._count);
        return state;
    }
}
