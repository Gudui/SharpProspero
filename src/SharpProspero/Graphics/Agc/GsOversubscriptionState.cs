// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// The coupled user-config and shader-register state that controls GS/NGG oversubscription.
/// </summary>
/// <remarks>
/// AGC derives both records from the prepared shader and returns them as one contract. The records
/// belong to different register spaces and must be emitted through the matching command-buffer calls.
/// </remarks>
public sealed unsafe class GsOversubscriptionState
{
    /// <summary>The user-config register returned first by AGC.</summary>
    public const ushort GePcAllocOffset = 0x0260;

    /// <summary>The shader register returned second by AGC.</summary>
    public const ushort SpiShaderPgmRsrc4GsOffset = 0x0081;

    /// <summary>The full-oversubscription mask in GE_PC_ALLOC.</summary>
    public const uint FullGePcAllocMask = 0x000007FF;

    /// <summary>The full late-allocation mask in SPI_SHADER_PGM_RSRC4_GS.</summary>
    public const uint FullRsrc4GsMask = 0x007F0000;

    private GsOversubscriptionState(CxRegister userConfigRegister, CxRegister shaderRegister)
    {
        UserConfigRegister = userConfigRegister;
        ShaderRegister = shaderRegister;
    }

    /// <summary>The GE_PC_ALLOC record to emit through user-config register space.</summary>
    public CxRegister UserConfigRegister { get; }

    /// <summary>The SPI_SHADER_PGM_RSRC4_GS record to emit through shader register space.</summary>
    public CxRegister ShaderRegister { get; }

    /// <summary>
    /// Computes the paired GS/NGG oversubscription state for a prepared geometry shader.
    /// </summary>
    /// <param name="geometryShader">Prepared geometry or fused NGG shader.</param>
    /// <param name="budget">Oversubscription budget. <see cref="uint.MaxValue"/> requests AGC's explicit full mode.</param>
    /// <param name="factor">Interpolation factor used by AGC for finite budgets.</param>
    /// <param name="trace">Optional diagnostic sink.</param>
    /// <exception cref="ProsperoException">AGC rejected the request.</exception>
    /// <exception cref="InvalidOperationException">AGC returned records for unexpected register offsets.</exception>
    public static GsOversubscriptionState Create(
        AgcShader geometryShader,
        uint budget,
        float factor = 1.0f,
        Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(geometryShader);

        CxRegister* registers = stackalloc CxRegister[2];
        new Span<byte>(registers, 2 * sizeof(CxRegister)).Clear();

        int result = SceAgc.sceAgcGetGsOversubscription(
            registers, geometryShader.Handle, budget, factor);
        trace?.Invoke(
            "AGC_GS_OVERSUB_CREATE result=0x" + result.ToString("X8") +
            " budget=" + budget);
        if (result != 0)
        {
            trace?.Invoke("AGC_GS_OVERSUB_CREATE_FAILED code=0x" + result.ToString("X8"));
            SceResult.ThrowIfFailed(result, nameof(SceAgc.sceAgcGetGsOversubscription));
        }

        CxRegister userConfig = registers[0];
        CxRegister shader = registers[1];
        if (userConfig.Offset != GePcAllocOffset || shader.Offset != SpiShaderPgmRsrc4GsOffset)
        {
            trace?.Invoke(
                "AGC_GS_OVERSUB_LAYOUT_INVALID uc_offset=0x" + userConfig.Offset.ToString("X4") +
                " sh_offset=0x" + shader.Offset.ToString("X4"));
            throw new InvalidOperationException("AGC returned an unexpected GS oversubscription register layout.");
        }

        trace?.Invoke(
            "AGC_GS_OVERSUB_UC offset=0x" + userConfig.Offset.ToString("X4") +
            " value=0x" + userConfig.Value.ToString("X8"));
        trace?.Invoke(
            "AGC_GS_OVERSUB_SH offset=0x" + shader.Offset.ToString("X4") +
            " value=0x" + shader.Value.ToString("X8"));

        return new GsOversubscriptionState(userConfig, shader);
    }
}
