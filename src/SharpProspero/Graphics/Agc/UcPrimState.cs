// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// User config registers for Geometry Engine (GE) and primitive assembly.
/// </summary>
public sealed unsafe class UcPrimState
{
    /// <summary>Maximum number of primitive state user-config registers.</summary>
    public const int MaxRegisters = 3;

    private readonly CxRegister[] _regs = new CxRegister[MaxRegisters];
    private int _count;

    /// <summary>The active primitive state user-config registers.</summary>
    public ReadOnlySpan<CxRegister> Registers => _regs.AsSpan(0, _count);

    /// <summary>Number of valid registers in the state.</summary>
    public int Count => _count;

    /// <summary>
    /// Builds the primitive state user-config registers from the vertex/primitive shader and topology type.
    /// </summary>
    public static UcPrimState Create(AgcShader vs, uint primitiveType = 4, AgcShader? gs = null, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(vs);

        var state = new UcPrimState();
        CxRegister* ucBuffer = stackalloc CxRegister[MaxRegisters];
        new Span<byte>(ucBuffer, sizeof(CxRegister) * MaxRegisters).Clear();

        void* gsHandle = gs is not null ? gs.Handle : null;
        int result = SceAgc.sceAgcCreatePrimState(null, ucBuffer, gsHandle, vs.Handle, primitiveType);
        trace?.Invoke("AGC_PRIMSTATE_UC_CREATE result=0x" + result.ToString("X8"));
        if (result != 0)
        {
            trace?.Invoke("AGC_PRIMSTATE_UC_CREATE_FAILED code=0x" + result.ToString("X8"));
            return state;
        }

        int valid = 0;
        for (int i = 0; i < MaxRegisters; i++)
        {
            if (ucBuffer[i].Offset != 0)
            {
                state._regs[valid++] = ucBuffer[i];
                trace?.Invoke("AGC_PRIMSTATE_UC_REG index=" + i + " offset=0x" + ucBuffer[i].Offset.ToString("X4") + " value=0x" + ucBuffer[i].Value.ToString("X8"));
            }
        }
        state._count = valid;
        trace?.Invoke("AGC_PRIMSTATE_UC_TOTAL count=" + state._count);
        return state;
    }
}
