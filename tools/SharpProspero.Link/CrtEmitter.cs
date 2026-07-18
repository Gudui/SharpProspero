// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace SharpProspero.Link;

/// <summary>
/// Writes the C-runtime start object: a relocatable object that defines the program entry point
/// <c>_start</c>. The entry reads the argument count and vector the loader places on the stack, calls
/// <c>main</c>, and hands the return value to <c>exit</c>. It is included ahead of the compiled object
/// so a module has an entry point without linking any outside start file.
/// </summary>
public static class CrtEmitter
{
    private const int ShtProgBits = 1;
    private const int ShtSymTab = 2;
    private const int ShtStrTab = 3;
    private const int ShtRela = 4;

    private const ulong ShfAlloc = 0x2;
    private const ulong ShfExec = 0x4;

    private const byte GlobalFunc = (1 << 4) | 2;   // STB_GLOBAL, STT_FUNC
    private const byte GlobalNoType = 1 << 4;       // STB_GLOBAL, STT_NOTYPE (undefined reference)

    private const uint RPlt32 = 4;                  // R_X86_64_PLT32

    /// <summary>The symbol the loader jumps to; the default entry for an application module.</summary>
    public const string StartSymbol = "_start";

    // _start: reads argc/argv from the stack, aligns it, calls main, then calls exit with the result.
    //   xor  ebp, ebp
    //   mov  rdi, [rsp]        ; argc
    //   lea  rsi, [rsp+8]      ; argv
    //   and  rsp, -16          ; keep the call boundary 16-byte aligned
    //   call main
    //   mov  edi, eax          ; exit status
    //   call exit
    //   hlt
    private static ReadOnlySpan<byte> StartCode =>
    [
        0x31, 0xED,                         // xor ebp, ebp
        0x48, 0x8B, 0x3C, 0x24,             // mov rdi, [rsp]
        0x48, 0x8D, 0x74, 0x24, 0x08,       // lea rsi, [rsp+8]
        0x48, 0x83, 0xE4, 0xF0,             // and rsp, -16
        0xE8, 0x00, 0x00, 0x00, 0x00,       // call main       (rel32 at offset 16)
        0x89, 0xC7,                         // mov edi, eax
        0xE8, 0x00, 0x00, 0x00, 0x00,       // call exit        (rel32 at offset 23)
        0xF4,                               // hlt
    ];

    private const int CallMainRel = 16;
    private const int CallExitRel = 23;

    /// <summary>Builds the start object bytes.</summary>
    public static byte[] BuildStartObject()
    {
        byte[] text = StartCode.ToArray();

        // .strtab: symbol names.
        var strtab = new StringTable();
        int nStart = strtab.Add(StartSymbol);
        int nMain = strtab.Add("main");
        int nExit = strtab.Add("exit");
        byte[] strtabBytes = strtab.ToBytes();

        // .symtab: null, _start (defined in .text), main and exit (undefined references).
        // Locals come first; only the null entry is local, so sh_info is 1.
        const int symStart = 1, symMain = 2, symExit = 3;
        byte[] symtab = new byte[24 * 4];
        WriteSym(symtab, symStart, nStart, GlobalFunc, sectionIndex: 1, value: 0, size: (ulong)text.Length);
        WriteSym(symtab, symMain, nMain, GlobalNoType, sectionIndex: 0, value: 0, size: 0);
        WriteSym(symtab, symExit, nExit, GlobalNoType, sectionIndex: 0, value: 0, size: 0);

        // .rela.text: the two calls, each a PLT32 with addend -4 (the displacement is measured from the
        // end of the four-byte field).
        byte[] rela = new byte[24 * 2];
        WriteRela(rela, 0, CallMainRel, symMain, RPlt32, -4);
        WriteRela(rela, 1, CallExitRel, symExit, RPlt32, -4);

        // .shstrtab: section names.
        var shstr = new StringTable();
        int nText = shstr.Add(".text");
        int nRela = shstr.Add(".rela.text");
        int nSym = shstr.Add(".symtab");
        int nStr = shstr.Add(".strtab");
        int nShStr = shstr.Add(".shstrtab");
        byte[] shstrBytes = shstr.ToBytes();

        // Section header indices: [0]null [1].text [2].rela.text [3].symtab [4].strtab [5].shstrtab.
        const int shText = 1, shRela = 2, shSym = 3, shStr = 4, shShStr = 5;

        var body = new List<byte>();
        long textOff = Place(body, text);
        long relaOff = Place(body, rela);
        long symOff = Place(body, symtab);
        long strOff = Place(body, strtabBytes);
        long shstrOff = Place(body, shstrBytes);
        Align(body, 8);
        long shdrOff = 64 + body.Count;

        byte[] shdr = new byte[64 * 6];
        WriteShdr(shdr, shText, nText, ShtProgBits, ShfAlloc | ShfExec, textOff, text.Length, 0, 0, 16, 0);
        WriteShdr(shdr, shRela, nRela, ShtRela, 0, relaOff, rela.Length, shSym, shText, 8, 24);
        WriteShdr(shdr, shSym, nSym, ShtSymTab, 0, symOff, symtab.Length, shStr, 1, 8, 24);
        WriteShdr(shdr, shStr, nStr, ShtStrTab, 0, strOff, strtabBytes.Length, 0, 0, 1, 0);
        WriteShdr(shdr, shShStr, nShStr, ShtStrTab, 0, shstrOff, shstrBytes.Length, 0, 0, 1, 0);

        var output = new List<byte>(64 + body.Count + shdr.Length);
        output.AddRange(BuildHeader(shdrOff, shShStr, sectionCount: 6));
        output.AddRange(body);
        output.AddRange(shdr);
        return [.. output];
    }

    private static long Place(List<byte> body, byte[] data)
    {
        Align(body, 8);
        long offset = 64 + body.Count;
        body.AddRange(data);
        return offset;
    }

    private static void Align(List<byte> body, int alignment)
    {
        while ((64 + body.Count) % alignment != 0)
            body.Add(0);
    }

    private static byte[] BuildHeader(long shoff, int shstrndx, int sectionCount)
    {
        byte[] e = new byte[64];
        e[0] = 0x7F; e[1] = (byte)'E'; e[2] = (byte)'L'; e[3] = (byte)'F';
        e[4] = 2;    // ELFCLASS64
        e[5] = 1;    // ELFDATA2LSB
        e[6] = 1;    // EV_CURRENT
        e[7] = 9;    // OS/ABI
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x10), 1);     // ET_REL
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x12), 0x3E);  // x86-64
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0x14), 1);     // e_version
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(0x28), (ulong)shoff); // e_shoff
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x34), 64);    // e_ehsize
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3A), 64);    // e_shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3C), (ushort)sectionCount);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3E), (ushort)shstrndx);
        return e;
    }

    private static void WriteSym(byte[] table, int index, int nameOff, byte info, int sectionIndex, ulong value, ulong size)
    {
        int b = index * 24;
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(b), (uint)nameOff);
        table[b + 4] = info;
        table[b + 5] = 0; // st_other
        BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(b + 6), (ushort)sectionIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b + 8), value);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b + 16), size);
    }

    private static void WriteRela(byte[] table, int index, int offset, int symbol, uint type, long addend)
    {
        int b = index * 24;
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b), (ulong)offset);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b + 8), ((ulong)(uint)symbol << 32) | type);
        BinaryPrimitives.WriteInt64LittleEndian(table.AsSpan(b + 16), addend);
    }

    private static void WriteShdr(byte[] shdr, int index, int nameOff, uint type, ulong flags, long offset, long size,
        int link, int info, int align, int entsize)
    {
        int b = index * 64;
        BinaryPrimitives.WriteUInt32LittleEndian(shdr.AsSpan(b), (uint)nameOff);
        BinaryPrimitives.WriteUInt32LittleEndian(shdr.AsSpan(b + 4), type);
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 8), flags);
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 16), 0); // addr
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 24), (ulong)offset);
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 32), (ulong)size);
        BinaryPrimitives.WriteUInt32LittleEndian(shdr.AsSpan(b + 40), (uint)link);
        BinaryPrimitives.WriteUInt32LittleEndian(shdr.AsSpan(b + 44), (uint)info);
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 48), (ulong)align);
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 56), (ulong)entsize);
    }

    private sealed class StringTable
    {
        private readonly List<byte> _bytes = [0];
        private readonly Dictionary<string, int> _off = new(StringComparer.Ordinal);

        public int Add(string value)
        {
            if (_off.TryGetValue(value, out int existing))
                return existing;
            int offset = _bytes.Count;
            _bytes.AddRange(Encoding.ASCII.GetBytes(value));
            _bytes.Add(0);
            _off[value] = offset;
            return offset;
        }

        public byte[] ToBytes() => [.. _bytes];
    }
}
