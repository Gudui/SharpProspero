// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Agc;

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

    private static void Ensure()
    {
        if (_descriptor is null) _descriptor = (byte*)SceAgc.sceAgcGetRegisterDefaults();
    }

    /// <summary>The reset value the driver holds for a context register, or zero if it lists none.</summary>
    public static uint GetContextValue(ushort offset)
    {
        Ensure();
        // The context space is a pointer to a table of per-register record pointers.
        CxRegister** table = *(CxRegister***)(_descriptor + 0x00);
        uint count = *(uint*)(_descriptor + 0x20);
        for (uint i = 0; i < count; i++)
            if (table[i]->Offset == offset) return table[i]->Value;
        return 0;
    }

    /// <summary>
    /// The sixteen colour render-target registers, each carrying its offset and the driver's reset value,
    /// ready to hand to <see cref="CxRenderTarget.Init"/>.
    /// </summary>
    public static CxRegister[] RenderTargetBlock()
    {
        var block = new CxRegister[RenderTargetOffsets.Length];
        for (int i = 0; i < block.Length; i++)
            block[i] = new CxRegister(RenderTargetOffsets[i], GetContextValue(RenderTargetOffsets[i]));
        return block;
    }
}
