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
    // The driver returns a small descriptor: three spaces (context, shader, user-config), each a pointer
    // to a table of pointers - one per register - into the eight-byte {offset, value} records, and the
    // record counts.
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
        if (_descriptor is null)
            throw new InvalidOperationException("sceAgcGetRegisterDefaults returned null.");
    }

    // The table the driver hands back is not uniformly populated. On firmware 5.50 a 481-entry table
    // carries null record pointers at indices 107, 129, 311 and 467, reproducibly across runs and across
    // consoles. Reading through one of those is a crash before the first draw is ever submitted, so every
    // pointer is checked rather than assumed. The count and the table itself are checked for the same
    // reason: a descriptor that does not look like a table is better reported than walked.
    private static void Locate(out CxRegister** table, out uint count)
    {
        Ensure();
        // The context space is a pointer to a table of per-register record pointers.
        table = *(CxRegister***)(_descriptor + 0x00);
        count = *(uint*)(_descriptor + 0x20);
        if (table is null)
            throw new InvalidOperationException("AGC context register-default table is null.");
        if (count == 0 || count > 0x10000)
            throw new InvalidOperationException("AGC context register-default count is invalid: " + count);
    }

    /// <summary>The reset value the driver holds for a context register, or zero if it lists none.</summary>
    public static uint GetContextValue(ushort offset)
    {
        Locate(out CxRegister** table, out uint count);
        for (uint i = 0; i < count; i++)
        {
            CxRegister* record = table[i];
            if (record is not null && record->Offset == offset)
                return record->Value;
        }
        return 0;
    }

    /// <summary>
    /// The sixteen colour render-target registers, each carrying its offset and the driver's reset value,
    /// ready to hand to <see cref="CxRenderTarget.Init"/>.
    /// </summary>
    /// <param name="trace">
    /// Optional diagnostic sink, called once with the table size, the number of null records, and how many
    /// of the sixteen offsets the driver actually listed. Worth wiring up on a new firmware: the block is
    /// silently zero-filled for every offset the table omits, so a renderer that draws nothing looks
    /// identical to one that was handed no defaults at all. On firmware 5.50 only 0x0318 is listed, and it
    /// is listed as zero, which makes the whole block zeroes.
    /// </param>
    public static CxRegister[] RenderTargetBlock(Action<string>? trace = null)
    {
        Locate(out CxRegister** table, out uint count);

        var block = new CxRegister[RenderTargetOffsets.Length];
        int matched = 0;
        for (int i = 0; i < block.Length; i++)
        {
            ushort offset = RenderTargetOffsets[i];
            uint value = 0;
            for (uint tableIndex = 0; tableIndex < count; tableIndex++)
            {
                CxRegister* record = table[tableIndex];
                if (record is null || record->Offset != offset) continue;
                value = record->Value;
                matched++;
                break;
            }
            block[i] = new CxRegister(offset, value);
        }

        if (trace is not null && !_traced)
        {
            _traced = true;
            uint nulls = 0;
            for (uint i = 0; i < count; i++)
                if (table[i] is null) nulls++;
            trace($"AGC_DEFAULTS context_count={count} null_records={nulls} render_target_matches={matched}/{block.Length}");
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
        Locate(out CxRegister** table, out uint count);
        var list = new System.Collections.Generic.List<CxRegister>((int)count);
        uint nulls = 0;
        for (uint i = 0; i < count; i++)
        {
            CxRegister* record = table[i];
            if (record is not null)
            {
                list.Add(new CxRegister(record->Offset, record->Value));
            }
            else
            {
                nulls++;
            }
        }

        if (trace is not null && !_tracedAll)
        {
            _tracedAll = true;
            trace($"AGC_DEFAULTS_ALL total={count} valid={list.Count} null_records={nulls}");
        }

        return list.ToArray();
    }

    private static bool _traced;
    private static bool _tracedAll;
}
