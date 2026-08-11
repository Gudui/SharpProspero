// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace SharpProspero.Link;

/// <summary>
/// Writes the start object for a payload: the entry the loader jumps to with the resolver arguments in
/// the first register. It saves those arguments, resolves each recorded outside reference through the
/// resolver (trying the process image, then the kernel library), marks the C runtime threaded, sets up
/// the thread-local block the managed runtime needs (allocate, copy the template, install the thread
/// pointer), runs the global constructors, calls <c>main</c>, and returns the result through the output
/// pointer the loader supplied.
/// </summary>
public static class PayloadCrtEmitter
{
    private const int ShtProgBits = 1, ShtSymTab = 2, ShtStrTab = 3, ShtRela = 4;
    private const ulong ShfAlloc = 0x2, ShfWrite = 0x1, ShfExec = 0x4;
    private const byte GlobalFunc = (1 << 4) | 2;      // STB_GLOBAL, STT_FUNC
    private const byte GlobalObject = (1 << 4) | 1;    // STB_GLOBAL, STT_OBJECT
    private const byte GlobalNoType = 1 << 4;          // STB_GLOBAL, STT_NOTYPE (undefined reference)
    private const uint RPc32 = 2, RPlt32 = 4;

    /// <summary>The entry symbol the loader jumps to.</summary>
    public const string StartSymbol = "_start";

    /// <summary>The symbol the managed accessor imports to retrieve the payload arguments pointer.</summary>
    public const string GetArgsSymbol = "__prospero_get_payload_args";

    /// <summary>
    /// The names this start object defines. A payload link resolves these from the start object rather
    /// than through the stub catalog or the compat object.
    /// </summary>
    public static IReadOnlyList<string> DefinedNames { get; } =
    [
        StartSymbol,
        ImportTableSymbol,
        "__prospero_payload_args",
        "__prospero_payload_isthreaded",
        "__prospero_payload_isthreaded_name",
        "__prospero_payload_calloc",
        "__prospero_payload_calloc_name",
        "__prospero_payload_setfsbase",
        "__prospero_payload_setfsbase_name",
        GetArgsSymbol,
    ];

    /// <summary>
    /// The symbol whose header the writer fills: the resolver-table bounds, the global-constructor array
    /// bounds, then the thread-local template address and its file and aligned-memory sizes.
    /// </summary>
    public const string ImportTableSymbol = "__prospero_payload_imports";

    // _start(args): args in rdi. Header (56 bytes): [0] resolver-table start, [8] end, [16] constructor
    // array start, [24] end, [32] thread-local template address, [40] template file size, [48] aligned
    // template memory size. Data after it: g_args (8); an __isthreaded slot, a calloc slot and a
    // set-fs-base slot (8 each); then the three name strings.
    //   push rbx/r12/r13/r14/r15 (five, keeps the call boundary aligned)
    //   mov  r14, rdi                     ; args
    //   lea  r13, [rip+header] ; lea rax,[rip+g_args] ; mov [rax], r14
    //   ; resolve each outside reference through args->dlsym (handle 1, then 0x2001)
    //   mov  r15,[r13]; mov r12,[r13+8]
    // il:  cmp r15,r12; jae ildone; mov rsi,[r15]; mov rdx,[r15+8]; mov edi,1; mov rax,[r14]; call rax
    //      test eax,eax; jz iln; (retry with edi=0x2001); iln: add r15,16; jmp il
    //   ; set the C runtime's __isthreaded to 1 (handle 2, then 0x2001)
    // ildone: lea rsi,[rip+name]; lea rdx,[rip+slot]; mov edi,2; call args->dlsym; test eax,eax; jz iset
    //      (retry 0x2001); iset: mov rax,[rip+slot]; test rax,rax; jz tls; mov dword[rax],1
    //   ; give the managed runtime its thread-local storage. Skip unless a template is present and the
    //   ; allocator and thread-pointer setter both resolve; degrade to no set-up rather than fault.
    // tls: mov r12,[r13+48]; test r12,r12; jz iastart               ; no template - nothing to do
    //      resolve calloc (handle 2) and amd64_set_fsbase (handle 0x2001) into their slots
    //      mov rax,[calloc]; test; jz iastart; lea rdi,[r12+64]; mov esi,1; call rax  ; block = tls+tcb
    //      test rax,rax; jz iastart; mov rbx,rax
    //      mov rdi,rbx; mov rsi,[r13+32]; mov rcx,[r13+40]; rep movsb                 ; copy template
    //      lea rax,[rbx+r12]; mov [rax],rax; mov rdi,rax                              ; tcb self-pointer
    //      mov rax,[fsbase]; test rax,rax; jz iastart; call rax                       ; install the pointer
    //   ; run the global constructors [header+16, header+24)
    // iastart: mov r15,[r13+16]; mov r12,[r13+24]; ia: cmp r15,r12; jae iadone; mov rax,[r15]; call rax;
    //      add r15,8; jmp ia
    // iadone: mov rdi,r14; call main; mov rcx,[r14+0x28]; test rcx,rcx; jz ret; mov [rcx],eax
    // ret: pop r15/r14/r13/r12/rbx; ret
    private static byte[] BuildCode()
    {
        byte[] c =
        [
            0x53,                               // 0x00 push rbx
            0x41, 0x54,                         // 0x01 push r12
            0x41, 0x55,                         // 0x03 push r13
            0x41, 0x56,                         // 0x05 push r14
            0x41, 0x57,                         // 0x07 push r15
            0x49, 0x89, 0xFE,                   // 0x09 mov r14, rdi
            0x4C, 0x8D, 0x2D, 0,0,0,0,          // 0x0C lea r13,[rip+header]     (disp @0x0F)
            0x48, 0x8D, 0x05, 0,0,0,0,          // 0x13 lea rax,[rip+g_args]     (disp @0x16)
            0x4C, 0x89, 0x30,                   // 0x1A mov [rax], r14
            0x4D, 0x8B, 0x7D, 0x00,             // 0x1D mov r15,[r13]
            0x4D, 0x8B, 0x65, 0x08,             // 0x21 mov r12,[r13+8]
            0x4D, 0x39, 0xE7,                   // 0x25 cmp r15,r12         (il)
            0x0F, 0x83, 0x2C, 0x00, 0x00, 0x00, // 0x28 jae ildone
            0x49, 0x8B, 0x37,                   // 0x2E mov rsi,[r15]
            0x49, 0x8B, 0x57, 0x08,             // 0x31 mov rdx,[r15+8]
            0xBF, 0x01, 0x00, 0x00, 0x00,       // 0x35 mov edi,1
            0x49, 0x8B, 0x06,                   // 0x3A mov rax,[r14]
            0xFF, 0xD0,                         // 0x3D call rax
            0x85, 0xC0,                         // 0x3F test eax,eax
            0x74, 0x11,                         // 0x41 jz iln
            0x49, 0x8B, 0x37,                   // 0x43 mov rsi,[r15]
            0x49, 0x8B, 0x57, 0x08,             // 0x46 mov rdx,[r15+8]
            0xBF, 0x01, 0x20, 0x00, 0x00,       // 0x4A mov edi,0x2001
            0x49, 0x8B, 0x06,                   // 0x4F mov rax,[r14]
            0xFF, 0xD0,                         // 0x52 call rax
            0x49, 0x83, 0xC7, 0x10,             // 0x54 add r15,16          (iln)
            0xEB, 0xCB,                         // 0x58 jmp il
            0x48, 0x8D, 0x35, 0,0,0,0,          // 0x5A lea rsi,[rip+name]  (ildone) (disp @0x5D)
            0x48, 0x8D, 0x15, 0,0,0,0,          // 0x61 lea rdx,[rip+slot]  (disp @0x64)
            0xBF, 0x02, 0x00, 0x00, 0x00,       // 0x68 mov edi,2          (the C runtime library handle)
            0x49, 0x8B, 0x06,                   // 0x6D mov rax,[r14]
            0xFF, 0xD0,                         // 0x70 call rax
            0x85, 0xC0,                         // 0x72 test eax,eax
            0x74, 0x18,                         // 0x74 jz iset
            0x48, 0x8D, 0x35, 0,0,0,0,          // 0x76 lea rsi,[rip+name]  (disp @0x79)
            0x48, 0x8D, 0x15, 0,0,0,0,          // 0x7D lea rdx,[rip+slot]  (disp @0x80)
            0xBF, 0x01, 0x20, 0x00, 0x00,       // 0x84 mov edi,0x2001
            0x49, 0x8B, 0x06,                   // 0x89 mov rax,[r14]
            0xFF, 0xD0,                         // 0x8C call rax
            0x48, 0x8B, 0x05, 0,0,0,0,          // 0x8E mov rax,[rip+slot]  (iset) (disp @0x91)
            0x48, 0x85, 0xC0,                   // 0x95 test rax,rax
            0x74, 0x06,                         // 0x98 jz tls
            0xC7, 0x00, 0x01, 0x00, 0x00, 0x00, // 0x9A mov dword [rax],1
            // ---- thread-local storage set-up (tls) ----
            0x4D, 0x8B, 0x65, 0x30,             // 0xA0 mov r12,[r13+48]   (tls) aligned template size
            0x4D, 0x85, 0xE4,                   // 0xA4 test r12,r12
            0x0F, 0x84, 0x81, 0x00, 0x00, 0x00, // 0xA7 jz iastart        (no template)
            0x48, 0x8D, 0x35, 0,0,0,0,          // 0xAD lea rsi,[rip+calloc_name]     (disp @0xB0)
            0x48, 0x8D, 0x15, 0,0,0,0,          // 0xB4 lea rdx,[rip+calloc_slot]     (disp @0xB7)
            0xBF, 0x02, 0x00, 0x00, 0x00,       // 0xBB mov edi,2
            0x49, 0x8B, 0x06,                   // 0xC0 mov rax,[r14]
            0xFF, 0xD0,                         // 0xC3 call rax
            0x48, 0x8D, 0x35, 0,0,0,0,          // 0xC5 lea rsi,[rip+fsbase_name]     (disp @0xC8)
            0x48, 0x8D, 0x15, 0,0,0,0,          // 0xCC lea rdx,[rip+fsbase_slot]     (disp @0xCF)
            0xBF, 0x01, 0x20, 0x00, 0x00,       // 0xD3 mov edi,0x2001
            0x49, 0x8B, 0x06,                   // 0xD8 mov rax,[r14]
            0xFF, 0xD0,                         // 0xDB call rax
            0x48, 0x8B, 0x05, 0,0,0,0,          // 0xDD mov rax,[rip+calloc_slot]     (disp @0xE0)
            0x48, 0x85, 0xC0,                   // 0xE4 test rax,rax
            0x0F, 0x84, 0x41, 0x00, 0x00, 0x00, // 0xE7 jz iastart        (allocator not resolved)
            0x49, 0x8D, 0x7C, 0x24, 0x40,       // 0xED lea rdi,[r12+64]  nmemb = aligned size + tcb
            0xBE, 0x01, 0x00, 0x00, 0x00,       // 0xF2 mov esi,1
            0xFF, 0xD0,                         // 0xF7 call rax          calloc(nmemb,1)
            0x48, 0x85, 0xC0,                   // 0xF9 test rax,rax
            0x0F, 0x84, 0x2C, 0x00, 0x00, 0x00, // 0xFC jz iastart        (allocation failed)
            0x48, 0x89, 0xC3,                   // 0x102 mov rbx,rax       block base
            0x48, 0x89, 0xDF,                   // 0x105 mov rdi,rbx
            0x49, 0x8B, 0x75, 0x20,             // 0x108 mov rsi,[r13+32]  template address
            0x49, 0x8B, 0x4D, 0x28,             // 0x10C mov rcx,[r13+40]  template file size
            0xF3, 0xA4,                         // 0x110 rep movsb         copy the template
            0x4A, 0x8D, 0x04, 0x23,             // 0x112 lea rax,[rbx+r12]  thread pointer (block + size)
            0x48, 0x89, 0x00,                   // 0x116 mov [rax],rax     tcb self-pointer
            0x48, 0x89, 0xC7,                   // 0x119 mov rdi,rax
            0x48, 0x8B, 0x05, 0,0,0,0,          // 0x11C mov rax,[rip+fsbase_slot]    (disp @0x11F)
            0x48, 0x85, 0xC0,                   // 0x123 test rax,rax
            0x0F, 0x84, 0x02, 0x00, 0x00, 0x00, // 0x126 jz iastart        (setter not resolved)
            0xFF, 0xD0,                         // 0x12C call rax          amd64_set_fsbase(tcb)
            // ---- global constructors ----
            0x4D, 0x8B, 0x7D, 0x10,             // 0x12E mov r15,[r13+16]  (iastart)
            0x4D, 0x8B, 0x65, 0x18,             // 0x132 mov r12,[r13+24]
            0x4D, 0x39, 0xE7,                   // 0x136 cmp r15,r12       (ia)
            0x0F, 0x83, 0x0B, 0x00, 0x00, 0x00, // 0x139 jae iadone
            0x49, 0x8B, 0x07,                   // 0x13F mov rax,[r15]
            0xFF, 0xD0,                         // 0x142 call rax
            0x49, 0x83, 0xC7, 0x08,             // 0x144 add r15,8
            0xEB, 0xEC,                         // 0x148 jmp ia
            0x4C, 0x89, 0xF7,                   // 0x14A mov rdi,r14        (iadone) pass args to main
            0xE8, 0,0,0,0,                      // 0x14D call main          (disp @0x14E)
            0x49, 0x8B, 0x4E, 0x28,             // 0x152 mov rcx,[r14+0x28]
            0x48, 0x85, 0xC9,                   // 0x156 test rcx,rcx
            0x74, 0x02,                         // 0x159 jz ret
            0x89, 0x01,                         // 0x15B mov [rcx],eax
            0x41, 0x5F,                         // 0x15D pop r15            (ret)
            0x41, 0x5E,                         // 0x15F pop r14
            0x41, 0x5D,                         // 0x161 pop r13
            0x41, 0x5C,                         // 0x163 pop r12
            0x5B,                               // 0x165 pop rbx
            0xC3,                               // 0x166 ret
            // ---- getter for the saved payload_args pointer ----
            0x48, 0x8B, 0x05, 0,0,0,0,          // 0x167 mov rax,[rip+g_args] (disp @0x16A)
            0xC3,                               // 0x16E ret
        ];
        return c;
    }

    // Displacement fields that reference a data symbol, each a PC-relative fix-up with addend -4.
    private const int RelHeaderA = 0x0F, RelGArgs = 0x16;
    private const int RelNameA = 0x5D, RelSlotA = 0x64, RelNameB = 0x79, RelSlotB = 0x80, RelSlotC = 0x91;
    private const int RelCallocName = 0xB0, RelCallocSlotA = 0xB7, RelFsbaseName = 0xC8, RelFsbaseSlotA = 0xCF;
    private const int RelCallocSlotB = 0xE0, RelFsbaseSlotB = 0x11F;
    private const int RelMain = 0x14E;
    private const int RelGetArgsRef = 0x16A;

    /// <summary>Builds the payload start object bytes.</summary>
    public static byte[] BuildStartObject()
    {
        byte[] text = BuildCode();
        // Data layout the start code and the writer share:
        //   [0..56)   the header the writer fills: resolver-table bounds, constructor-array bounds, then
        //             the thread-local template address and its file and aligned-memory sizes
        //   [56..64)  the saved arguments
        //   [64..72)  the resolved __isthreaded slot
        //   [72..80)  the resolved calloc slot         (the allocator for the thread-local block)
        //   [80..88)  the resolved amd64_set_fsbase slot (installs the thread pointer)
        //   [88..101) "__isthreaded"   [101..108) "calloc"   [108..125) "amd64_set_fsbase"
        byte[] data = new byte[125];
        Encoding.ASCII.GetBytes("__isthreaded").CopyTo(data, 88);
        Encoding.ASCII.GetBytes("calloc").CopyTo(data, 101);
        Encoding.ASCII.GetBytes("amd64_set_fsbase").CopyTo(data, 108);

        var strtab = new StringTable();
        int nStart = strtab.Add(StartSymbol);
        int nMain = strtab.Add("main");
        int nImports = strtab.Add(ImportTableSymbol);
        int nArgs = strtab.Add("__prospero_payload_args");
        int nSlotIt = strtab.Add("__prospero_payload_isthreaded");
        int nNameIt = strtab.Add("__prospero_payload_isthreaded_name");
        int nSlotCa = strtab.Add("__prospero_payload_calloc");
        int nNameCa = strtab.Add("__prospero_payload_calloc_name");
        int nSlotFb = strtab.Add("__prospero_payload_setfsbase");
        int nNameFb = strtab.Add("__prospero_payload_setfsbase_name");
        int nGetArgs = strtab.Add(GetArgsSymbol);
        byte[] strtabBytes = strtab.ToBytes();

        const int shText = 1, shRela = 2, shData = 3, shSym = 4, shStr = 5, shShStr = 6;
        const int symStart = 1, symMain = 2, symImports = 3, symArgs = 4, symSlotIt = 5, symNameIt = 6,
            symSlotCa = 7, symNameCa = 8, symSlotFb = 9, symNameFb = 10, symGetArgs = 11;
        byte[] symtab = new byte[24 * 12];
        WriteSym(symtab, symStart, nStart, GlobalFunc, shText, 0, (ulong)text.Length);
        WriteSym(symtab, symMain, nMain, GlobalNoType, 0, 0, 0);
        WriteSym(symtab, symImports, nImports, GlobalObject, shData, 0, 56);
        WriteSym(symtab, symArgs, nArgs, GlobalObject, shData, 56, 8);
        WriteSym(symtab, symSlotIt, nSlotIt, GlobalObject, shData, 64, 8);
        WriteSym(symtab, symNameIt, nNameIt, GlobalObject, shData, 88, 13);
        WriteSym(symtab, symSlotCa, nSlotCa, GlobalObject, shData, 72, 8);
        WriteSym(symtab, symNameCa, nNameCa, GlobalObject, shData, 101, 7);
        WriteSym(symtab, symSlotFb, nSlotFb, GlobalObject, shData, 80, 8);
        WriteSym(symtab, symNameFb, nNameFb, GlobalObject, shData, 108, 17);
        WriteSym(symtab, symGetArgs, nGetArgs, GlobalFunc, shText, 0x167, 8);

        byte[] rela = new byte[24 * 15];
        WriteRela(rela, 0, RelHeaderA, symImports, RPc32, -4);
        WriteRela(rela, 1, RelGArgs, symArgs, RPc32, -4);
        WriteRela(rela, 2, RelNameA, symNameIt, RPc32, -4);
        WriteRela(rela, 3, RelSlotA, symSlotIt, RPc32, -4);
        WriteRela(rela, 4, RelNameB, symNameIt, RPc32, -4);
        WriteRela(rela, 5, RelSlotB, symSlotIt, RPc32, -4);
        WriteRela(rela, 6, RelSlotC, symSlotIt, RPc32, -4);
        WriteRela(rela, 7, RelCallocName, symNameCa, RPc32, -4);
        WriteRela(rela, 8, RelCallocSlotA, symSlotCa, RPc32, -4);
        WriteRela(rela, 9, RelFsbaseName, symNameFb, RPc32, -4);
        WriteRela(rela, 10, RelFsbaseSlotA, symSlotFb, RPc32, -4);
        WriteRela(rela, 11, RelCallocSlotB, symSlotCa, RPc32, -4);
        WriteRela(rela, 12, RelFsbaseSlotB, symSlotFb, RPc32, -4);
        WriteRela(rela, 13, RelMain, symMain, RPlt32, -4);
        WriteRela(rela, 14, RelGetArgsRef, symArgs, RPc32, -4);

        var shstr = new StringTable();
        int nTextS = shstr.Add(".text");
        int nRelaS = shstr.Add(".rela.text");
        int nDataS = shstr.Add(".data");
        int nSymS = shstr.Add(".symtab");
        int nStrS = shstr.Add(".strtab");
        int nShStrS = shstr.Add(".shstrtab");
        byte[] shstrBytes = shstr.ToBytes();

        var body = new List<byte>();
        long textOff = Place(body, text);
        long relaOff = Place(body, rela);
        long dataOff = Place(body, data);
        long symOff = Place(body, symtab);
        long strOff = Place(body, strtabBytes);
        long shstrOff = Place(body, shstrBytes);
        Align(body, 8);
        long shdrOff = 64 + body.Count;

        byte[] shdr = new byte[64 * 7];
        WriteShdr(shdr, shText, nTextS, ShtProgBits, ShfAlloc | ShfExec, textOff, text.Length, 0, 0, 16, 0);
        WriteShdr(shdr, shRela, nRelaS, ShtRela, 0, relaOff, rela.Length, shSym, shText, 8, 24);
        WriteShdr(shdr, shData, nDataS, ShtProgBits, ShfAlloc | ShfWrite, dataOff, data.Length, 0, 0, 8, 0);
        WriteShdr(shdr, shSym, nSymS, ShtSymTab, 0, symOff, symtab.Length, shStr, 1, 8, 24);
        WriteShdr(shdr, shStr, nStrS, ShtStrTab, 0, strOff, strtabBytes.Length, 0, 0, 1, 0);
        WriteShdr(shdr, shShStr, nShStrS, ShtStrTab, 0, shstrOff, shstrBytes.Length, 0, 0, 1, 0);

        var output = new List<byte>(64 + body.Count + shdr.Length);
        output.AddRange(BuildHeader(shdrOff, shShStr, sectionCount: 7));
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
        while ((64 + body.Count) % alignment != 0) body.Add(0);
    }

    private static byte[] BuildHeader(long shoff, int shstrndx, int sectionCount)
    {
        byte[] e = new byte[64];
        e[0] = 0x7F; e[1] = (byte)'E'; e[2] = (byte)'L'; e[3] = (byte)'F';
        e[4] = 2; e[5] = 1; e[6] = 1; e[7] = 9;
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x10), 1);     // ET_REL
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x12), 0x3E);  // x86-64
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(0x28), (ulong)shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x34), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3A), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3C), (ushort)sectionCount);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x3E), (ushort)shstrndx);
        return e;
    }

    private static void WriteSym(byte[] table, int index, int nameOff, byte info, int sectionIndex, ulong value, ulong size)
    {
        int b = index * 24;
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(b), (uint)nameOff);
        table[b + 4] = info;
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
            if (_off.TryGetValue(value, out int existing)) return existing;
            int offset = _bytes.Count;
            _bytes.AddRange(Encoding.ASCII.GetBytes(value));
            _bytes.Add(0);
            _off[value] = offset;
            return offset;
        }
        public byte[] ToBytes() => [.. _bytes];
    }
}
