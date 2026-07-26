// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpProspero.Link;

/// <summary>
/// Writes the runtime-support compat object: a relocatable object that defines the small set of C-library
/// names the ahead-of-time runtime imports which the device modules do not publish directly. Each is a
/// thin definition — a tail call to the name the device does publish, a fixed result, or a clean refusal —
/// so the linked module needs no outside object for them. It is included alongside the compiled object and
/// the runtime archives.
/// </summary>
/// <remarks>
/// The set is the difference between what a compiled module imports and what the C and kernel modules
/// export (see <c>runtime/imports/compat.txt</c>). The large-file variants forward to the base name; a few
/// names map to a kernel entry; the rest return a fixed value an application module can accept (it does not
/// fork, is not a terminal, and does not walk its own program headers).
/// </remarks>
public static class CompatEmitter
{
    private const int ShtProgBits = 1;
    private const int ShtSymTab = 2;
    private const int ShtStrTab = 3;
    private const int ShtRela = 4;
    private const int ShtNoBits = 8;

    private const ulong ShfWrite = 0x1;
    private const ulong ShfAlloc = 0x2;
    private const ulong ShfExec = 0x4;
    private const ulong ShfTls = 0x400;

    private const uint RPlt32 = 4;     // R_X86_64_PLT32
    private const uint RTpOff32 = 23;  // R_X86_64_TPOFF32 (local-exec thread-local offset)
    private const uint RAbs64 = 1;     // R_X86_64_64 (a variable holding an address)

    private const byte TypeFunc = 2;
    private const byte TypeObject = 1;
    private const byte TypeTls = 6;
    private const byte BindLocal = 0;
    private const byte BindGlobal = 1;
    private const byte BindWeak = 2;

    // The per-thread scratch that readdir64 translates into. It lives in the object's own thread-local
    // block so concurrent directory reads on different threads never share it. Its name is object-local.
    private const string ReaddirBufSymbol = "__sp_readdir64_buf";
    private const int ReaddirBufSize = 288;   // a directory entry the runtime reads (280), rounded up to 8

    /// <summary>
    /// One defined function: its name, binding, code bytes, tail-call relocations, and an optional
    /// thread-local reference (the offset of a local-exec address load and the thread-local it names).
    /// </summary>
    private readonly record struct CompatFunc(
        string Name, bool Weak, byte[] Code, (int Offset, string Target)[] Relocs,
        (int Offset, string Symbol)? Tls = null);

    /// <summary>
    /// One defined pointer-sized variable: its name and the name whose address it holds, or null for a
    /// variable that starts out empty.
    /// </summary>
    private readonly record struct CompatData(string Name, string? PointsAt);

    // Code fragments.
    private static byte[] RetZero() => [0x31, 0xC0, 0xC3];                       // xor eax,eax ; ret
    private static byte[] RetImm(int value)                                     // mov eax, imm32 ; ret
    {
        byte[] c = [0xB8, 0, 0, 0, 0, 0xC3];
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(1), value);
        return c;
    }
    private static byte[] JmpTail() => [0xE9, 0, 0, 0, 0];                       // jmp rel32 (target patched by reloc)

    // Forward the current call unchanged to another name (same arguments, its result becomes ours).
    private static CompatFunc Forward(string name, string target) =>
        new(name, false, JmpTail(), [(1, target)]);

    // Forward to a name using the 3rd and 4th argument registers as the 1st and 2nd.
    // Used by clock_nanosleep(clockid, flags, req, rem) -> nanosleep(req, rem).
    private static CompatFunc ForwardShiftTwo(string name, string target)
    {
        // mov rdi, rdx ; mov rsi, rcx ; jmp target
        byte[] code = [0x48, 0x89, 0xD7, 0x48, 0x89, 0xCE, 0xE9, 0, 0, 0, 0];
        return new(name, false, code, [(7, target)]);
    }

    private static CompatFunc Value(string name, int value) => new(name, false, RetImm(value), []);
    private static CompatFunc Zero(string name) => new(name, false, RetZero(), []);
    private static CompatFunc WeakZero(string name) => new(name, true, RetZero(), []);
    private static CompatFunc Refuse(string name) => new(name, false, RetImm(-1), []);

    // A refusal whose result is a full 64-bit -1 rather than a sign-extended word: a pointer-returning
    // entry that reports failure has to set the whole register, since the caller compares all of it.
    /// <summary>
    /// The start of the image, at link-time address zero. Reached instruction-relative it reads back
    /// as the address the module was loaded at. Every linker defines this name, so an object using it
    /// links the same way whichever one builds the module.
    /// </summary>
    public const string ModuleBaseSymbol = "__executable_start";

    // The name dladdr reports for the module. The one caller that reads it hands the pointer straight to
    // a string length, so it cannot be null - a name that is merely approximate costs nothing, a null one
    // faults. It lives among the variables rather than in the code, which is mapped execute-only and
    // cannot be read.
    private const string ModuleNameSymbol = "__sp_module_name";
    private static readonly byte[] ModuleNameText = "/app0/eboot.bin\0"u8.ToArray();

    // dladdr(address, info): fills in what the runtime reads out of it, which is the address the
    // module holding that address was loaded at. Nothing published here reports that for an arbitrary
    // address, but the one caller asks about a pointer inside this module, and this module's own base
    // is reachable: the linker puts a symbol at link-time address zero, so reaching it
    // instruction-relative gives the address the module was placed at.
    //
    // A stub reporting failure here is not a small thing. The runtime treats the answer as the handle
    // that identifies the module, and a module registered under a null handle is one the runtime
    // cannot match an address back to.
    private static CompatFunc DlAddr()
    {
        byte[] code =
        [
            0x48, 0x8D, 0x05, 0, 0, 0, 0,       // 0x00 lea rax, [rip + the image start]  (rel32 at 3)
            0x48, 0x89, 0x46, 0x08,             // 0x07 mov [rsi + 8], rax   (where it was loaded)
            0x48, 0x8D, 0x05, 0, 0, 0, 0,       // 0x0B lea rax, [rip + the name]  (rel32 at 0x0E)
            0x48, 0x89, 0x06,                   // 0x12 mov [rsi], rax      (the file it came from)
            0x48, 0xC7, 0x46, 0x10, 0, 0, 0, 0, // 0x15 mov qword [rsi+16], 0 (no symbol name)
            0x48, 0xC7, 0x46, 0x18, 0, 0, 0, 0, // 0x1D mov qword [rsi+24], 0 (no symbol address)
            0xB8, 0x01, 0x00, 0x00, 0x00,       // 0x25 mov eax, 1           (found)
            0xC3,                               // 0x2A ret
        ];
        return new("dladdr", false, code, [(3, ModuleBaseSymbol), (0x0E, ModuleNameSymbol)]);
    }

    // mmap(addr, len, prot, flags, fd, offset): the anonymous private mapping the runtime reserves its
    // heap with. This platform has no mmap of its own - memory comes from the flexible pool, which is
    // reached by handing an address slot in and reading back where the range was placed. The slot
    // starts as the address the caller named, or zero to let the system choose; naming one asks for it
    // to be held there.
    //
    // The protection bits agree with the ordinary ones except for write: here read-and-write is a
    // single bit rather than read plus write, so a request for both is folded onto it. Everything else
    // - none, read, read-execute, all - already lines up.
    // Holding a range at a named address, and the protection bits. Read and write is a single bit
    // here rather than read plus write, so a request for both folds onto it; none, read, read-execute
    // and all already line up.
    private const byte MapFixed = 0x10, ProtReadWrite = 0x02, PosixReadWrite = 0x03;

    private static CompatFunc Mmap()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x83, 0xEC, 0x10);                     // sub rsp, 16   (the address slot)
        // A length is rounded up to a whole page. Asking for memory hands back whole pages either way,
        // and the platform's call refuses a length that is not a multiple of one - so a request for a
        // few hundred bytes, which the ordinary call answers with a page, would be refused outright.
        a.Emit(0x48, 0x81, 0xC6, 0xFF, 0x3F, 0x00, 0x00);   // add rsi, page - 1
        a.Emit(0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF);   // and rsi, -page
        a.Emit(0x48, 0x89, 0x3C, 0x24);                     // mov [rsp], rdi
        a.Emit(0x31, 0xC9);                                 // xor ecx, ecx  (place it anywhere)
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("placed");   // test rdi, rdi
        a.Emit(0xB9, MapFixed, 0x00, 0x00, 0x00);           // mov ecx, fixed
        a.Mark("placed");
        a.Emit(0x48, 0x89, 0xE7);                           // mov rdi, rsp  (the address slot)

        // Asking for no access is asking for room, not for memory. Backing it would spend the pool on
        // a range nothing has written to yet - and what is asked for here is a multiple of everything
        // the module could ever hold, so backing it fails outright. Reserve the room; the first write
        // to a page of it arrives as a protection change, which is where the memory is taken.
        // Reserving takes the flags third and an alignment fourth, where mapping takes the protection
        // third and the flags fourth, so the flags move across and the alignment is left open.
        a.Emit(0x85, 0xD2); a.JumpIfNotEqual("access");      // test edx, edx
        a.Emit(0x89, 0xCA);                                 // mov edx, ecx  (the flags)
        a.Emit(0x31, 0xC9);                                 // xor ecx, ecx  (any alignment)
        a.Call("sceKernelReserveVirtualRange");
        a.JumpIfAlways("settle");

        a.Mark("access");
        a.Emit(0x83, 0xFA, PosixReadWrite);                 // cmp edx, read|write
        a.JumpIfNotEqual("mapit");
        a.Emit(0xBA, ProtReadWrite, 0x00, 0x00, 0x00);      // mov edx, read-write
        a.Mark("mapit");
        a.Call("sceKernelMapFlexibleMemory");

        a.Mark("settle");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.Emit(0x48, 0x8B, 0x04, 0x24);                     // mov rax, [rsp]  (where it landed)
        a.Emit(0xC9, 0xC3);                                 // leave ; ret
        a.Mark("refuse");
        a.Emit(0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC9, 0xC3); // mov rax,-1 ; leave ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("mmap", false, code, relocs);
    }

    // mprotect(addr, len, prot): a protection change on memory that is already there, or the moment a
    // reserved range is first written to. The platform's own call handles the first; the second it
    // refuses, because there is nothing mapped yet - so a refusal is answered by taking memory from the
    // pool and placing it exactly there.
    private static CompatFunc MProtect()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x83, 0xEC, 0x20);                     // sub rsp, 32
        // A protection change covers whole pages: the address moves back to the start of its page and
        // the length grows by as much, then rounds up. The platform's call refuses anything else, and
        // the runtime changes protection on ranges it sized in objects rather than in pages.
        a.Emit(0x48, 0x89, 0xF8);                           // mov rax, rdi
        a.Emit(0x25, 0xFF, 0x3F, 0x00, 0x00);               // and eax, page - 1
        a.Emit(0x48, 0x01, 0xC6);                           // add rsi, rax
        a.Emit(0x48, 0x81, 0xE7, 0x00, 0xC0, 0xFF, 0xFF);   // and rdi, -page
        a.Emit(0x48, 0x81, 0xC6, 0xFF, 0x3F, 0x00, 0x00);   // add rsi, page - 1
        a.Emit(0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF);   // and rsi, -page
        a.Emit(0x48, 0x89, 0x7D, 0xF8);                     // mov [rbp-8], rdi   (addr)
        a.Emit(0x48, 0x89, 0x75, 0xF0);                     // mov [rbp-16], rsi  (len)
        a.Emit(0x83, 0xFA, PosixReadWrite);                 // cmp edx, read|write
        a.JumpIfNotEqual("kept");
        a.Emit(0xBA, ProtReadWrite, 0x00, 0x00, 0x00);      // mov edx, read-write
        a.Mark("kept");
        a.Emit(0x89, 0x55, 0xE8);                           // mov [rbp-24], edx  (prot)
        a.Call("sceKernelMprotect");
        a.Emit(0x85, 0xC0); a.JumpIfEqual("done");          // test eax, eax

        // Nothing mapped there yet: take it from the pool, held at that address.
        a.Emit(0x48, 0x8B, 0x45, 0xF8);                     // mov rax, [rbp-8]
        a.Emit(0x48, 0x89, 0x04, 0x24);                     // mov [rsp], rax
        a.Emit(0x48, 0x89, 0xE7);                           // mov rdi, rsp
        a.Emit(0x48, 0x8B, 0x75, 0xF0);                     // mov rsi, [rbp-16]
        a.Emit(0x8B, 0x55, 0xE8);                           // mov edx, [rbp-24]
        a.Emit(0xB9, MapFixed, 0x00, 0x00, 0x00);           // mov ecx, fixed
        a.Call("sceKernelMapFlexibleMemory");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.Mark("done");
        a.Emit(0x31, 0xC0, 0xC9, 0xC3);                     // xor eax, eax ; leave ; ret
        a.Mark("refuse");
        a.Emit(0xB8, 0xFF, 0xFF, 0xFF, 0xFF, 0xC9, 0xC3);   // mov eax, -1 ; leave ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("mprotect", false, code, relocs);
    }

    // sysconf(name): the two answers the runtime acts on. The page size decides how the collector
    // reserves memory and the processor count how many threads it starts, so both are answered
    // properly; anything else reports that the value is not available, which every caller handles.
    // Branch displacements are worked out from where the labels land rather than written by hand. A
    // displacement counted by hand once sent the memory-size question to the processor-count answer,
    // which reported 128 KB of memory and was invisible until the module was on a console.
    private sealed class Asm
    {
        private readonly List<byte> _code = [];
        private readonly List<(int At, string Label)> _fixups = [];
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<(int Offset, string Target)> _calls = [];

        public int Length => _code.Count;
        public void Emit(params byte[] bytes) => _code.AddRange(bytes);
        public void Mark(string label) => _labels[label] = _code.Count;

        /// <summary>A short conditional jump to a label placed later.</summary>
        public void JumpIfEqual(string label)
        {
            _code.Add(0x74);
            _fixups.Add((_code.Count, label));
            _code.Add(0);
        }

        public void JumpIfAlways(string label)
        {
            _code.Add(0xEB);
            _fixups.Add((_code.Count, label));
            _code.Add(0);
        }

        public void JumpIfNotEqual(string label)
        {
            _code.Add(0x75);
            _fixups.Add((_code.Count, label));
            _code.Add(0);
        }

        /// <summary>A call to a name the linker binds, recording where its displacement sits.</summary>
        public void Call(string target)
        {
            _code.Add(0xE8);
            _calls.Add((_code.Count, target));
            _code.AddRange([0, 0, 0, 0]);
        }

        public (byte[] Code, (int Offset, string Target)[] Relocs) Build()
        {
            byte[] code = [.. _code];
            foreach ((int at, string label) in _fixups)
            {
                int delta = _labels[label] - (at + 1);
                if (delta is < sbyte.MinValue or > sbyte.MaxValue)
                    throw new ElfLinkException($"A branch to '{label}' does not reach.");
                code[at] = unchecked((byte)(sbyte)delta);
            }
            return (code, [.. _calls]);
        }
    }

    private static CompatFunc SysConf()
    {
        const byte ScPageSize = 30, ScProcessorsConf = 83, ScProcessorsOnline = 84, ScPhysPages = 85;
        var a = new Asm();
        a.Emit(0x83, 0xFF, ScPageSize); a.JumpIfEqual("page");              // cmp edi, _SC_PAGESIZE
        a.Emit(0x83, 0xFF, ScProcessorsConf); a.JumpIfEqual("cpus");
        a.Emit(0x83, 0xFF, ScProcessorsOnline); a.JumpIfEqual("cpus");
        a.Emit(0x83, 0xFF, ScPhysPages); a.JumpIfEqual("pages");
        a.Emit(0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3);             // mov rax, -1 ; ret

        a.Mark("page");
        a.Emit(0xB8, 0x00, 0x40, 0x00, 0x00, 0xC3);                         // mov eax, 16384 ; ret

        a.Mark("cpus");
        a.Emit(0xB8, 0x08, 0x00, 0x00, 0x00, 0xC3);                         // mov eax, 8 ; ret

        // How much memory this module can have, in pages. What it can get is what it has: the pool it
        // maps from. Reporting more has the collector size itself against memory that is not there;
        // reporting less starves it. A refusal is not an option - the caller's own result is this
        // answer not refusing.
        a.Mark("pages");
        a.Emit(0x55, 0x48, 0x89, 0xE5);                                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x83, 0xEC, 0x10);                                     // sub rsp, 16
        a.Emit(0x48, 0xC7, 0x04, 0x24, 0, 0, 0, 0);                         // mov qword [rsp], 0
        a.Emit(0x48, 0x89, 0xE7);                                           // mov rdi, rsp
        a.Call("sceKernelConfiguredFlexibleMemorySize");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");                     // test eax, eax
        a.Emit(0x48, 0x8B, 0x04, 0x24);                                     // mov rax, [rsp]
        a.Emit(0x48, 0xC1, 0xE8, 0x0E);                                     // shr rax, 14 (16 KB pages)
        a.Emit(0xC9, 0xC3);                                                 // leave ; ret

        a.Mark("refuse");
        a.Emit(0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC9, 0xC3);       // mov rax, -1 ; leave ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("sysconf", false, code, relocs);
    }

    // ---------------------------------------------------------------------------
    // struct stat translation.
    //
    // The runtime's operating-system layer reads a file's status through a struct whose fields sit at
    // one set of offsets; the device's own status call writes a struct with different field types and
    // offsets. A large-file status entry therefore calls the device's status call into a scratch buffer
    // and copies each field across, so the runtime reads the right value at the right place. The offsets
    // are taken from both headers for the x86-64 target and locked by a test.
    // ---------------------------------------------------------------------------

    private const int Rax = 0, Rbx = 3, Rsp = 4, Rsi = 6, Rdi = 7;

    // Device struct size (rounded up to a 16-byte scratch); the runtime struct's field offsets follow.
    private const int DeviceStatSize = 128;

    // (runtime offset, device offset, load-size in bytes). A load-size < 8 zero-extends into a 64-bit
    // field; the mode field is the one 4-byte destination.
    private static readonly (int To, int From, int Size)[] StatFields =
    [
        (0, 0, 4),     // st_dev
        (8, 4, 4),     // st_ino
        (16, 10, 2),   // st_nlink
        (24, 8, 2),    // st_mode  (4-byte destination)
        (28, 12, 4),   // st_uid
        (32, 16, 4),   // st_gid
        (40, 20, 4),   // st_rdev
        (48, 72, 8),   // st_size
        (56, 88, 4),   // st_blksize
        (64, 80, 8),   // st_blocks
        (72, 24, 8),   // st_atim.tv_sec
        (80, 32, 8),   // st_atim.tv_nsec
        (88, 40, 8),   // st_mtim.tv_sec
        (96, 48, 8),   // st_mtim.tv_nsec
        (104, 56, 8),  // st_ctim.tv_sec
        (112, 64, 8),  // st_ctim.tv_nsec
    ];

    // Runtime-struct bytes not written by a field copy, zeroed so no stale value is read.
    private static readonly int[] StatZeroGaps = [36, 120, 128, 136];

    // Builds `name(version, target-arg, out-buf)`: call the device status function into a scratch, then
    // translate into the runtime struct at the caller's buffer. `byFd` passes the second argument as a
    // 32-bit file descriptor rather than a pointer.
    private static CompatFunc StatThunk(string name, string target, bool byFd)
    {
        var c = new List<byte>
        {
            0x53                                        // push rbx
        };
        c.AddRange([0x48, 0x89, 0xD3]);                     // mov rbx, rdx    (save out-buf)
        c.AddRange([0x48, 0x81, 0xEC, DeviceStatSize, 0x00, 0x00, 0x00]); // sub rsp, DeviceStatSize
        c.AddRange(byFd ? [0x89, 0xF7] : new byte[] { 0x48, 0x89, 0xF7 }); // mov edi,esi / mov rdi,rsi
        c.AddRange([0x48, 0x89, 0xE6]);                     // mov rsi, rsp    (&scratch)
        int callRel = c.Count + 1;
        c.AddRange([0xE8, 0, 0, 0, 0]);                     // call target
        c.AddRange([0x85, 0xC0]);                           // test eax, eax
        int jnz = c.Count;
        c.AddRange([0x0F, 0x85, 0, 0, 0, 0]);               // jnz done (patched)
        int bodyStart = c.Count;

        c.AddRange([0x31, 0xC0]);                           // xor eax, eax
        foreach (int gap in StatZeroGaps)
            Store64(c, Rbx, gap);                           // zero the runtime-struct gaps
        foreach ((int to, int from, int size) in StatFields)
        {
            LoadScratch(c, from, size);
            if (to == 24) Store32(c, Rbx, to);              // st_mode is a 4-byte field
            else Store64(c, Rbx, to);
        }
        c.AddRange([0x31, 0xC0]);                           // xor eax, eax   (return 0)

        int done = c.Count;
        int disp = done - bodyStart;
        c[jnz + 2] = (byte)disp; c[jnz + 3] = (byte)(disp >> 8);
        c[jnz + 4] = (byte)(disp >> 16); c[jnz + 5] = (byte)(disp >> 24);
        c.AddRange([0x48, 0x81, 0xC4, DeviceStatSize, 0x00, 0x00, 0x00]); // add rsp, DeviceStatSize
        c.Add(0x5B);                                        // pop rbx
        c.Add(0xC3);                                        // ret
        return new CompatFunc(name, false, [.. c], [(callRel, target)]);
    }

    // Loads a value of the given size from [rsp+off] into eax/rax, zero-extending a short field.
    private static void LoadScratch(List<byte> c, int off, int size)
    {
        if (size == 8) { c.AddRange([0x48, 0x8B]); ModRm(c, Rax, Rsp, off); }        // mov rax, [rsp+off]
        else if (size == 4) { c.Add(0x8B); ModRm(c, Rax, Rsp, off); }                // mov eax, [rsp+off]
        else { c.AddRange([0x0F, 0xB7]); ModRm(c, Rax, Rsp, off); }                  // movzx eax, word [rsp+off]
    }

    private static void Store64(List<byte> c, int baseReg, int off) { c.AddRange([0x48, 0x89]); ModRm(c, Rax, baseReg, off); }
    private static void Store32(List<byte> c, int baseReg, int off) { c.Add(0x89); ModRm(c, Rax, baseReg, off); }

    // Appends the ModRM (and SIB/displacement) for `reg`-with-[base+disp]. Handles rsp (needs a SIB) and
    // a disp of zero, one byte, or four bytes.
    private static void ModRm(List<byte> c, int reg, int baseReg, int disp)
    {
        int mod = disp == 0 ? 0 : (disp >= -128 && disp <= 127 ? 1 : 2);
        bool sib = baseReg == Rsp;
        c.Add((byte)((mod << 6) | ((reg & 7) << 3) | (sib ? 4 : baseReg & 7)));
        if (sib) c.Add(0x24);
        if (mod == 1) c.Add((byte)disp);
        else if (mod == 2) { c.Add((byte)disp); c.Add((byte)(disp >> 8)); c.Add((byte)(disp >> 16)); c.Add((byte)(disp >> 24)); }
    }

    // ---------------------------------------------------------------------------
    // struct dirent translation.
    //
    // The device's directory read returns a pointer to an entry whose fields sit at one set of offsets;
    // the runtime reads the returned pointer as an entry with a wider inode and different offsets. readdir64
    // therefore calls the device's readdir, copies the entry into a per-thread block laid out the way the
    // runtime expects, and returns a pointer to that. A null result (end of directory) passes straight
    // through. Offsets are from both headers for the x86-64 target and locked by a test.
    //
    //   device entry:  d_fileno @0 (4)  d_reclen @4 (2)  d_type @6 (1)  d_namlen @7 (1)  d_name @8
    //   runtime entry: d_ino    @0 (8)  d_off    @8 (8)  d_reclen @16 (2) d_type @18 (1) d_name @19
    // ---------------------------------------------------------------------------

    private static CompatFunc Readdir64()
    {
        // Two references are patched by relocations: the call to the device readdir (PLT32, +1 in its
        // instruction) and the local-exec load of the per-thread block's address (TPOFF32, the lea's disp32).
        const int callSite = 2;      // operand of `call readdir`
        const int tlsSite = 28;      // disp32 of `lea rdi, [rax + buf@tpoff]`
        byte[] code =
        [
            0x53,                                           // 0   push rbx
            0xE8, 0x00, 0x00, 0x00, 0x00,                   // 1   call readdir            (rdi already holds DIR*)
            0x48, 0x85, 0xC0,                               // 6   test rax, rax
            0x75, 0x02,                                     // 9   jnz +2  -> 13
            0x5B,                                           // 11  pop rbx
            0xC3,                                           // 12  ret                     (null: end of directory)
            0x48, 0x89, 0xC3,                               // 13  mov rbx, rax            (device entry)
            0x64, 0x48, 0x8B, 0x04, 0x25, 0, 0, 0, 0,       // 16  mov rax, fs:[0]         (thread pointer)
            0x48, 0x8D, 0xB8, 0, 0, 0, 0,                   // 25  lea rdi, [rax + buf@tpoff]  (block base)
            0x8B, 0x03,                                     // 32  mov eax, [rbx]          (d_fileno, zero-extended)
            0x48, 0x89, 0x07,                               // 34  mov [rdi], rax          (d_ino)
            0x31, 0xC0,                                     // 37  xor eax, eax
            0x48, 0x89, 0x47, 0x08,                         // 39  mov [rdi+8], rax        (d_off = 0)
            0x8A, 0x43, 0x06,                               // 43  mov al, [rbx+6]         (d_type)
            0x88, 0x47, 0x12,                               // 46  mov [rdi+18], al        (d_type)
            0x0F, 0xB6, 0x4B, 0x07,                         // 49  movzx ecx, byte [rbx+7] (d_namlen)
            0x8D, 0x41, 0x14,                               // 53  lea eax, [rcx+20]       (record length)
            0x66, 0x89, 0x47, 0x10,                         // 56  mov [rdi+16], ax        (d_reclen)
            0x48, 0x89, 0xF8,                               // 60  mov rax, rdi            (return value: block base)
            0x48, 0x8D, 0x73, 0x08,                         // 63  lea rsi, [rbx+8]        (source name)
            0x48, 0x83, 0xC7, 0x13,                         // 67  add rdi, 19             (dest name)
            0xFF, 0xC1,                                     // 71  inc ecx                 (copy the terminating null too)
            0xF3, 0xA4,                                     // 73  rep movsb
            0x5B,                                           // 75  pop rbx
            0xC3,                                           // 76  ret
        ];
        return new("readdir64", false, code, [(callSite, "readdir")], Tls: (tlsSite, ReaddirBufSymbol));
    }

    private static IReadOnlyList<CompatFunc> Functions =>
    [
        // Large-file variants: the device publishes the base name with a 64-bit offset already.
        Forward("open64", "open"),
        Forward("lseek64", "lseek"),
        Forward("mmap64", "mmap"),
        Forward("pread64", "pread"),
        Forward("fopen64", "fopen"),
        Readdir64(),
        Forward("getrlimit64", "getrlimit"),
        StatThunk("__fxstat64", "fstat", byFd: true),
        StatThunk("__xstat64", "stat", byFd: false),

        // Mapped to a device entry, or refused where there is no counterpart.
        Forward("__errno_location", "__error"),
        Forward("pthread_setname_np", "scePthreadRename"),
        ForwardShiftTwo("clock_nanosleep", "nanosleep"),
        Forward("pipe2", "pipe"),                   // the extra flags argument is dropped
        Refuse("statfs64"),
        Refuse("__getdelim"),
        Refuse("pthread_getattr_np"),

        // Realtime-signal bounds the runtime asks for when it picks a signal.
        Value("__libc_current_sigrtmin", 65),
        Value("__libc_current_sigrtmax", 126),

        // Weak no-ops the toolchain overrides: the SDK supplies its own start object and no profiler.
        WeakZero("__libc_start_main"),
        WeakZero("__gmon_start__"),
        WeakZero("_ITM_registerTMCloneTable"),
        WeakZero("_ITM_deregisterTMCloneTable"),

        // Fixed results an application module accepts.
        Refuse("fork"),                             // a module does not fork
        Zero("isatty"),                             // not a terminal
        Value("getpwuid_r", 2),                     // no such user
        Zero("dl_iterate_phdr"),                    // no callback is made
        DlAddr(),
        Zero("gai_strerror"),                       // no message
        Refuse("sysinfo"),
        Zero("prctl"),                              // process controls are no-ops
        Refuse("syscall"),
        Zero("sched_getcpu"),                       // the first processor
        Refuse("sched_getaffinity"),                // the runtime falls back to sysconf
        Value("__sched_cpucount", 1),

        // Further large-file variants that forward to a base name the device publishes.
        Forward("ftruncate64", "ftruncate"),
        Forward("pwrite64", "pwrite"),
        Forward("pwritev64", "pwritev"),
        Forward("preadv64", "preadv"),
        Forward("setrlimit64", "setrlimit"),
        StatThunk("__lxstat64", "lstat", byFd: false),

        // Advisory or best-effort calls that succeed as no-ops, and lookups with no counterpart.
        Zero("posix_fadvise64"),                    // advice is optional
        Zero("sched_setaffinity"),                  // affinity is fixed
        Zero("getauxval"),                          // no auxiliary value
        Zero("mkdtemp"),                            // no temporary directory
        Value("getgrgid_r", 2),                     // no such group
        Value("getpwnam_r", 2),                     // no such user

        // Calls an application module has no counterpart for; refused so the caller falls back.
        Refuse("__xmknod"),
        Refuse("fallocate64"),
        Refuse("fstatfs64"),
        Refuse("futimens"),
        Refuse("utimensat"),
        Refuse("getgrouplist"),
        Refuse("inotify_add_watch"),
        Refuse("inotify_init1"),
        Refuse("inotify_rm_watch"),
        Refuse("link"),
        Refuse("symlink"),
        Refuse("readlink"),
        Refuse("mkfifo"),
        Refuse("mkstemps64"),
        Refuse("pathconf"),
        Refuse("sendfile64"),
        Refuse("setgid"),
        Refuse("uname"),
        Refuse("vfork"),                            // a module does not fork
        Refuse("waitid"),

        // System queries the platform does not offer an application module. The bindings for these
        // exist because the query is a reasonable thing to want, but nothing publishes an entry point
        // for them, so each reports failure rather than binding to nothing.
        Refuse("sceKernelGetAllowedSdkVersionOnSystem"),
        Refuse("sceKernelGetOpenPsId"),
        Refuse("sceKernelGetProsperoSystemSwVersion"),
        Refuse("sysctlbyname"),

        // Entry points no module publishes. The runtime archives reach them from paths an application
        // module does not take - starting other processes, reading a terminal, handling signals the
        // platform owns - so each reports the failure its caller already handles rather than being left
        // as an import nothing can bind.
        SysConf(),
        Refuse("access"),
        Refuse("chdir"),
        Refuse("dup2"),
        Refuse("execv"),                            // a module does not start another program
        Refuse("getrlimit"),                        // no limit to report; the caller uses its default
        Refuse("getrusage"),
        Refuse("ioctl"),
        Refuse("lstat"),
        Refuse("pipe"),
        Refuse("poll"),
        Refuse("setrlimit"),
        Refuse("shm_open"),
        Refuse("shm_unlink"),
        Refuse("waitpid"),
        Mmap(),
        MProtect(),
        // Signals are the platform's to deliver; installing a handler succeeds and changes nothing.
        Zero("sigaction"),
        Zero("sigaddset"),
        Zero("sigemptyset"),
        Zero("signal"),                             // the previous handler, which is the default one
        Zero("pthread_kill"),
        // Nothing found, reported the way each caller expects: a null result.
        Zero("closedir"),
        Zero("dlopen"),
        Zero("dlsym"),
        Zero("getenv"),
        Zero("opendir"),
        Zero("readdir"),
        Zero("realpath"),
    ];

    /// <summary>
    /// The data objects this object defines: a pointer each, either left null or pointing at a name the
    /// C module publishes. The runtime reads these as variables rather than calling them.
    /// </summary>
    private static IReadOnlyList<CompatData> DataObjects =>
    [
        // The standard streams are published under their own names; the variables hold their addresses.
        new("stdout", "_Stdout"),
        new("stderr", "_Stderr"),
        // A module is started with no environment, so the list is empty.
        new("environ", null),
        // The marker the C module publishes to record that a module was linked against it. It carries
        // no behaviour; holding its address is what puts the name in the import table, which is what a
        // module built against the same library carries.
        new("__sce_libc_marker", "Need_sceLibc"),
    ];

    /// <summary>
    /// The names this object defines. A link resolves an imported name through the stub catalog first
    /// and falls back to these, so between them they have to cover everything the compiled image
    /// reaches.
    /// </summary>
    public static IReadOnlyList<string> DefinedNames =>
        [.. Functions.Select(f => f.Name), .. DataObjects.Select(d => d.Name)];

    /// <summary>Builds the compat object bytes.</summary>
    public static byte[] BuildObject()
    {
        IReadOnlyList<CompatFunc> funcs = Functions;

        // Lay out .text: each function on a 16-byte boundary. Record each function's offset.
        var text = new List<byte>();
        var textOffsets = new int[funcs.Count];
        for (int i = 0; i < funcs.Count; i++)
        {
            while (text.Count % 16 != 0)
                text.Add(0x90);   // nop padding
            textOffsets[i] = text.Count;
            text.AddRange(funcs[i].Code);
        }
        byte[] textBytes = [.. text];

        // The variables: one pointer each, in declaration order, then the module name dladdr reports.
        IReadOnlyList<CompatData> data = DataObjects;
        int moduleNameOffset = data.Count * 8;
        byte[] dataBytes = new byte[moduleNameOffset + ModuleNameText.Length];
        ModuleNameText.CopyTo(dataBytes, moduleNameOffset);

        // Section header indices.
        const int shText = 1, shTbss = 2, shRela = 3, shData = 4, shRelaData = 5, shSym = 6, shStr = 7, shShStr = 8;
        const int sectionCount = 9;

        // Symbols: the local symbols come first (ELF requires it) — [0] null, [1] the thread-local buffer
        // readdir64 translates into, and [2] the module name — then every defined function, then every
        // external target. sh_info is the count of leading locals.
        var strtab = new StringTable();
        var symIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var symbols = new List<(int NameOff, byte Info, int Shndx, ulong Value, ulong Size)>
        {
            (0, 0, 0, 0, 0),                                                                       // [0] null
            (strtab.Add(ReaddirBufSymbol), (BindLocal << 4) | TypeTls, shTbss, 0, ReaddirBufSize), // [1] tls buffer
            (strtab.Add(ModuleNameSymbol), (BindLocal << 4) | TypeObject, shData,                 // [2] module name
                (ulong)moduleNameOffset, (ulong)ModuleNameText.Length),
        };
        const int tlsSymbol = 1, LocalSymbolCount = 3;
        symIndex[ModuleNameSymbol] = 2;

        for (int i = 0; i < funcs.Count; i++)
        {
            CompatFunc f = funcs[i];
            byte bind = f.Weak ? BindWeak : BindGlobal;
            symIndex[f.Name] = symbols.Count;
            symbols.Add((strtab.Add(f.Name), (byte)((bind << 4) | TypeFunc), shText, (ulong)textOffsets[i], (ulong)f.Code.Length));
        }

        for (int i = 0; i < data.Count; i++)
        {
            symIndex[data[i].Name] = symbols.Count;
            symbols.Add((strtab.Add(data[i].Name), (BindGlobal << 4) | TypeObject, shData, (ulong)(i * 8), 8));
        }

        var externals = new List<string>();
        void AddExternal(string target, byte type)
        {
            if (symIndex.ContainsKey(target))
                return;
            symIndex[target] = symbols.Count;
            externals.Add(target);
            symbols.Add((strtab.Add(target), (byte)((BindGlobal << 4) | type), 0, 0, 0));
        }
        foreach (CompatFunc f in funcs)
            foreach ((int _, string target) in f.Relocs)
                AddExternal(target, TypeFunc);
        // A name whose address a variable holds is data, and has to say so: a module that calls it a
        // function invites the loader to bind it the way a function is bound.
        foreach (CompatData d in data)
            if (d.PointsAt is not null)
                AddExternal(d.PointsAt, TypeObject);

        // Relocations: a PLT32 per tail call, plus a TPOFF32 for the one function that names a thread-local.
        var relocs = new List<(int Offset, int Symbol, uint Type, long Addend)>();
        for (int i = 0; i < funcs.Count; i++)
        {
            foreach ((int off, string target) in funcs[i].Relocs)
                relocs.Add((textOffsets[i] + off, symIndex[target], RPlt32, -4));
            if (funcs[i].Tls is var (tlsOff, _))
                relocs.Add((textOffsets[i] + tlsOff, tlsSymbol, RTpOff32, 0));
        }

        // A variable holding an address needs a load-time fixup, the same as any other absolute
        // reference; one left empty needs none.
        var dataRelocs = new List<(int Offset, int Symbol, uint Type, long Addend)>();
        for (int i = 0; i < data.Count; i++)
            if (data[i].PointsAt is string target)
                dataRelocs.Add((i * 8, symIndex[target], RAbs64, 0));

        byte[] strtabBytes = strtab.ToBytes();

        byte[] symtab = new byte[24 * symbols.Count];
        for (int i = 0; i < symbols.Count; i++)
            WriteSym(symtab, i, symbols[i].NameOff, symbols[i].Info, symbols[i].Shndx, symbols[i].Value, symbols[i].Size);

        byte[] rela = new byte[24 * relocs.Count];
        for (int i = 0; i < relocs.Count; i++)
            WriteRela(rela, i, relocs[i].Offset, relocs[i].Symbol, relocs[i].Type, relocs[i].Addend);

        byte[] relaData = new byte[24 * dataRelocs.Count];
        for (int i = 0; i < dataRelocs.Count; i++)
            WriteRela(relaData, i, dataRelocs[i].Offset, dataRelocs[i].Symbol, dataRelocs[i].Type, dataRelocs[i].Addend);

        var shstr = new StringTable();
        int nText = shstr.Add(".text");
        int nTbss = shstr.Add(".tbss");
        int nRela = shstr.Add(".rela.text");
        int nData = shstr.Add(".data");
        int nRelaData = shstr.Add(".rela.data");
        int nSym = shstr.Add(".symtab");
        int nStr = shstr.Add(".strtab");
        int nShStr = shstr.Add(".shstrtab");
        byte[] shstrBytes = shstr.ToBytes();

        var body = new List<byte>();
        long textOff = Place(body, textBytes);
        long relaOff = Place(body, rela);
        long dataOff = Place(body, dataBytes);
        long relaDataOff = Place(body, relaData);
        long symOff = Place(body, symtab);
        long strOff = Place(body, strtabBytes);
        long shstrOff = Place(body, shstrBytes);
        Align(body, 8);
        // A no-bits section carries no file data; its offset just marks where it would sit.
        long tbssOff = 64 + body.Count;
        long shdrOff = 64 + body.Count;

        byte[] shdr = new byte[64 * sectionCount];
        WriteShdr(shdr, shText, nText, ShtProgBits, ShfAlloc | ShfExec, textOff, textBytes.Length, 0, 0, 16, 0);
        WriteShdr(shdr, shTbss, nTbss, ShtNoBits, ShfWrite | ShfAlloc | ShfTls, tbssOff, ReaddirBufSize, 0, 0, 8, 0);
        WriteShdr(shdr, shRela, nRela, ShtRela, 0, relaOff, rela.Length, shSym, shText, 8, 24);
        WriteShdr(shdr, shData, nData, ShtProgBits, ShfAlloc | ShfWrite, dataOff, dataBytes.Length, 0, 0, 8, 0);
        WriteShdr(shdr, shRelaData, nRelaData, ShtRela, 0, relaDataOff, relaData.Length, shSym, shData, 8, 24);
        WriteShdr(shdr, shSym, nSym, ShtSymTab, 0, symOff, symtab.Length, shStr, LocalSymbolCount, 8, 24);
        WriteShdr(shdr, shStr, nStr, ShtStrTab, 0, strOff, strtabBytes.Length, 0, 0, 1, 0);
        WriteShdr(shdr, shShStr, nShStr, ShtStrTab, 0, shstrOff, shstrBytes.Length, 0, 0, 1, 0);

        var output = new List<byte>(64 + body.Count + shdr.Length);
        output.AddRange(BuildHeader(shdrOff, shShStr, sectionCount));
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
        e[4] = 2; e[5] = 1; e[6] = 1; e[7] = 9;
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x12), 0x3E);
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
        table[b + 5] = 0;
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
        BinaryPrimitives.WriteUInt64LittleEndian(shdr.AsSpan(b + 16), 0);
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
