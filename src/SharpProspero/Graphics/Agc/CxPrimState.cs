// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// Context registers for primitive assembly and NGG shader stages.
/// </summary>
public sealed unsafe class CxPrimState
{
    /// <summary>Number of context registers written by sceAgcCreatePrimState.</summary>
    public const int DriverMaxRegisters = 2;

    /// <summary>Maximum number of primitive and required NGG context registers.</summary>
    public const int MaxRegisters = DriverMaxRegisters + 1;

    /// <summary>PA_SC_NGG_MODE_CNTL context-register offset.</summary>
    public const ushort NggModeControlOffset = 0x0314;

    /// <summary>
    /// GFX10 pixel-wave deallocation limit. MAX_DEALLOCS_IN_WAVE=512 prevents the frontend from
    /// deadlocking while it waits for parameter-cache space; MAX_FPOVS_IN_WAVE remains zero.
    /// </summary>
    public const uint NggModeControlValue = 0x00000200;

    private readonly CxRegister[] _regs = new CxRegister[MaxRegisters];
    private int _count;

    /// <summary>The active primitive state context registers.</summary>
    public ReadOnlySpan<CxRegister> Registers => _regs.AsSpan(0, _count);

    /// <summary>Number of valid registers in the state.</summary>
    public int Count => _count;

    /// <summary>
    /// Builds the primitive state context registers from the vertex/primitive shader and topology type.
    /// </summary>
    public static CxPrimState Create(AgcShader vs, uint primitiveType = 4, AgcShader? gs = null, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(vs);

        var state = new CxPrimState();
        CxRegister* cxBuffer = stackalloc CxRegister[DriverMaxRegisters];
        new Span<byte>(cxBuffer, sizeof(CxRegister) * DriverMaxRegisters).Clear();

        void* gsHandle = gs is not null ? gs.Handle : null;
        int result = SceAgc.sceAgcCreatePrimState(cxBuffer, null, gsHandle, vs.Handle, primitiveType);
        trace?.Invoke("AGC_PRIMSTATE_CX_CREATE result=0x" + result.ToString("X8"));
        if (result != 0)
        {
            trace?.Invoke("AGC_PRIMSTATE_CX_CREATE_FAILED code=0x" + result.ToString("X8"));
            return state;
        }

        int valid = 0;
        for (int i = 0; i < DriverMaxRegisters; i++)
        {
            if (cxBuffer[i].Offset != 0)
            {
                state._regs[valid++] = cxBuffer[i];
                trace?.Invoke("AGC_PRIMSTATE_CX_REG index=" + i + " offset=0x" + cxBuffer[i].Offset.ToString("X4") + " value=0x" + cxBuffer[i].Value.ToString("X8"));
            }
        }

        state._regs[valid++] = new CxRegister(NggModeControlOffset, NggModeControlValue);
        trace?.Invoke(
            "AGC_PRIMSTATE_CX_REG index=" + DriverMaxRegisters +
            " offset=0x" + NggModeControlOffset.ToString("X4") +
            " value=0x" + NggModeControlValue.ToString("X8") +
            " owner=sharp_prospero_ngg_state");
        state._count = valid;
        trace?.Invoke("AGC_PRIMSTATE_CX_TOTAL count=" + state._count);
        return state;
    }
}
