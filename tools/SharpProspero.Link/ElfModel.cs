// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK

using System.Collections.Generic;

namespace SharpProspero.Link;

/// <summary>Section types the reader recognizes.</summary>
public static class ShType
{
    public const uint Null = 0;
    public const uint ProgBits = 1;
    public const uint SymTab = 2;
    public const uint StrTab = 3;
    public const uint Rela = 4;
    public const uint NoBits = 8; // .bss
}

/// <summary>Section flags.</summary>
public static class ShFlags
{
    public const ulong Write = 0x1;
    public const ulong Alloc = 0x2;
    public const ulong Execute = 0x4;
    public const ulong Tls = 0x400;
}

/// <summary>Symbol binding and type nibbles.</summary>
public static class SymBind { public const int Local = 0; public const int Global = 1; public const int Weak = 2; }
public static class SymType { public const int NoType = 0; public const int Object = 1; public const int Func = 2; public const int Section = 3; public const int Tls = 6; public const int GnuIfunc = 10; }

/// <summary>x86-64 relocation types.</summary>
public static class RelType
{
    public const uint None = 0;
    public const uint R64 = 1;
    public const uint Pc32 = 2;
    public const uint Plt32 = 4;
    public const uint Relative = 8;
    public const uint GotPcRel = 9;
    public const uint R32 = 10;
    public const uint R32S = 11;
    public const uint JumpSlot = 7;
    public const uint Pc64 = 24;
    public const uint TpOff64 = 18;
    public const uint TpOff32 = 23;

    /// <summary>Initial-exec thread-local load through the GOT: the slot holds the symbol's thread-pointer
    /// offset. In a self-contained executable that offset is known at link time.</summary>
    public const uint GotTpOff = 22;

    /// <summary>Relaxable global-offset-table load; the default RIP-relative GOT encoding modern
    /// compilers emit. It resolves exactly like <see cref="GotPcRel"/> when left unrelaxed.</summary>
    public const uint GotPcRelX = 41;

    /// <summary>Relaxable global-offset-table load with a REX prefix; resolved like <see cref="GotPcRel"/>.</summary>
    public const uint RexGotPcRelX = 42;

    /// <summary>True for the plain and both relaxable GOT-relative load relocations.</summary>
    public static bool IsGotPcRel(uint type) => type is GotPcRel or GotPcRelX or RexGotPcRelX;
}

/// <summary>One section of an input object.</summary>
public sealed class ElfSection
{
    public required string Name { get; init; }
    public required uint Type { get; init; }
    public required ulong Flags { get; init; }
    public required ulong Address { get; init; }
    public required ulong Size { get; init; }
    public required uint Link { get; init; }
    public required uint Info { get; init; }
    public required ulong AddrAlign { get; init; }
    public required ulong EntSize { get; init; }

    /// <summary>Section bytes, or empty for a no-bits section.</summary>
    public required byte[] Data { get; init; }

    public bool IsAlloc => (Flags & ShFlags.Alloc) != 0;
    public bool IsExecutable => (Flags & ShFlags.Execute) != 0;
    public bool IsWritable => (Flags & ShFlags.Write) != 0;
    public bool IsTls => (Flags & ShFlags.Tls) != 0;
    public bool IsNoBits => Type == ShType.NoBits;
}

/// <summary>One symbol of an input object.</summary>
public sealed class ElfSymbol
{
    public required string Name { get; init; }
    public required byte Info { get; init; }
    public required byte Other { get; init; }
    public required ushort SectionIndex { get; init; }
    public required ulong Value { get; init; }
    public required ulong Size { get; init; }

    public int Bind => Info >> 4;
    public int Type => Info & 0xF;
    public bool IsUndefined => SectionIndex == 0;
    public bool IsGlobalOrWeak => Bind is SymBind.Global or SymBind.Weak;
    public bool IsWeak => Bind == SymBind.Weak;
}

/// <summary>One relocation applied to a target section.</summary>
public readonly record struct ElfRelocation(ulong Offset, uint SymbolIndex, uint Type, long Addend);

/// <summary>A parsed relocatable object.</summary>
public sealed class ElfObject
{
    public required string Origin { get; init; }
    public required IReadOnlyList<ElfSection> Sections { get; init; }
    public required IReadOnlyList<ElfSymbol> Symbols { get; init; }

    /// <summary>Relocations keyed by the index of the section they apply to.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<ElfRelocation>> Relocations { get; init; }
}
