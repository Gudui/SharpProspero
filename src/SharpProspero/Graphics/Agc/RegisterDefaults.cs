// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Agc;
using System;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// The graphics driver's register reset values. Some register blocks (the colour render target among
/// them) take their reset offsets and values from the driver, because they depend on the graphics
/// processor configuration rather than being fixed. This reads that table once and looks a value up by
/// its register offset, and assembles the render-target block a colour target needs.
/// </summary>
public static unsafe class RegisterDefaults
{
    // Each space points to block pointers into a flat array of eight-byte {offset, value} records.
    // The descriptor count is the RECORD count, not the length of the block-pointer table.
    private static byte* _descriptor;

    // The sixteen context registers of a colour render-target block, in the order the block expects.
    private static readonly ushort[] RenderTargetOffsets =
    [
        0x0318, 0x031B, 0x031C, 0x031D, 0x031E, 0x031F, 0x0321, 0x0323,
        0x0324, 0x0325, 0x0390, 0x0398, 0x03A0, 0x03A8, 0x03B0, 0x03B8,
    ];

    private static readonly ushort[] BlendColorOffsets = [0x0105, 0x0106, 0x0107, 0x0108];
    private static readonly ushort[] BlendControlOffsets = [0x01E0];
    private static readonly ushort[] DepthStencilControlOffsets = [0x0200];
    private static readonly ushort[] StencilOpControlOffsets = [0x010B];
    private static readonly ushort[] StencilControlOffsets = [0x010C];
    private static readonly ushort[] StencilControlBackFaceOffsets = [0x010D];

    private static void Ensure()
    {
        if (_descriptor is null) _descriptor = (byte*)SceAgc.sceAgcGetRegisterDefaults();
    }

    // Null descriptor / zero count means no defaults, matching upstream. A nonempty descriptor
    // must provide both the block table and its first record. Never walk blocks by record count.
    private static void Locate(out CxRegister* records, out uint count)
    {
        Ensure();
        records = null;
        count = 0;
        if (_descriptor is null) return;
        count = *(uint*)(_descriptor + 0x20);
        if (count == 0) return;
        if (count > 0x10000)
            throw new InvalidOperationException("AGC context register-default count is invalid: " + count);
        CxRegister** blocks = *(CxRegister***)(_descriptor + 0x00);
        if (blocks is null)
            throw new InvalidOperationException("AGC context register-default table is null.");
        records = blocks[0];
        if (records is null)
            throw new InvalidOperationException("AGC context register-default first block is null.");
    }

    /// <summary>The reset value the driver holds for a context register, or zero if it lists none.</summary>
    public static uint GetContextValue(ushort offset)
    {
        Locate(out CxRegister* records, out uint count);
        for (uint i = 0; i < count; i++)
        {
            if (records[i].Offset == offset)
                return records[i].Value;
        }
        return 0;
    }

    /// <summary>
    /// The sixteen colour render-target registers, each carrying its offset and the driver's reset value,
    /// ready to hand to <see cref="CxRenderTarget.Init"/>.
    /// </summary>
    /// <param name="trace">
    /// Optional diagnostic sink, called once with the flat-record count, match count and block values.
    /// Missing offsets are zero-filled. Nulls observed by the historical per-pointer walk were not
    /// evidence of holes in the native flat array.
    /// </param>
    public static CxRegister[] RenderTargetBlock(Action<string>? trace = null)
    {
        Locate(out CxRegister* records, out uint count);

        var block = new CxRegister[RenderTargetOffsets.Length];
        int matched = 0;
        for (int i = 0; i < block.Length; i++)
        {
            ushort offset = RenderTargetOffsets[i];
            uint value = 0;
            for (uint tableIndex = 0; tableIndex < count; tableIndex++)
            {
                if (records[tableIndex].Offset != offset) continue;
                value = records[tableIndex].Value;
                matched++;
                break;
            }
            block[i] = new CxRegister(offset, value);
        }

        if (trace is not null && !_traced)
        {
            _traced = true;
            trace($"AGC_DEFAULTS context_count={count} null_records=0 render_target_matches={matched}/{block.Length} layout=flat_records");
            foreach (CxRegister record in block)
                trace($"AGC_DEFAULT_RT offset=0x{record.Offset:X4} value=0x{record.Value:X8}");
        }
        return block;
    }

    /// <summary>The blend-equation register of colour target zero.</summary>
    public static CxRegister[] BlendControlBlock() => Block(BlendControlOffsets);

    /// <summary>The four constant blend-colour registers.</summary>
    public static CxRegister[] BlendColorBlock() => Block(BlendColorOffsets);

    /// <summary>The depth and stencil test control register.</summary>
    public static CxRegister[] DepthStencilControlBlock() => Block(DepthStencilControlOffsets);

    /// <summary>The front-face stencil mask register.</summary>
    public static CxRegister[] StencilControlBlock() => Block(StencilControlOffsets);

    /// <summary>The back-face stencil mask register.</summary>
    public static CxRegister[] StencilControlBackFaceBlock() => Block(StencilControlBackFaceOffsets);

    /// <summary>The stencil operation register.</summary>
    public static CxRegister[] StencilOpControlBlock() => Block(StencilOpControlOffsets);

    private static CxRegister[] Block(ReadOnlySpan<ushort> offsets)
    {
        var block = new CxRegister[offsets.Length];
        for (int i = 0; i < block.Length; i++)
            block[i] = new CxRegister(offsets[i], GetContextValue(offsets[i]));
        return block;
    }

    /// <summary>
    /// Returns all valid default context registers defined by the graphics driver.
    /// </summary>
    public static CxRegister[] AllContextDefaults(Action<string>? trace = null)
    {
        Locate(out CxRegister* records, out uint count);
        var result = new ReadOnlySpan<CxRegister>(records, (int)count).ToArray();

        if (trace is not null && !_tracedAll)
        {
            _tracedAll = true;
            var unique = new System.Collections.Generic.HashSet<ushort>();
            foreach (CxRegister record in result) unique.Add(record.Offset);
            trace($"AGC_DEFAULTS_ALL total={count} valid={result.Length} null_records=0 unique_offsets={unique.Count} layout=flat_records");
        }

        return result;
    }

    private static bool _traced;
    private static bool _tracedAll;
}
