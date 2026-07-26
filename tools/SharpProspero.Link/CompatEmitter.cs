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
    // The per-thread block holds the directory entry first and the error number after it.
    private const int ErrnoShadowOffset = ReaddirBufSize, ErrnoShadowSize = 4;
    private const int ThreadBlockSize = ErrnoShadowOffset + ErrnoShadowSize;

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

    // A refusal that fills the whole register rather than its lower half. An entry declared to return a
    // pointer or a 64-bit count is compared over all 64 bits by its caller, and writing only the lower
    // half leaves the upper half as it was - zero, on the path that reaches these - so the result reads
    // as 0x00000000FFFFFFFF, a large positive number, and the caller takes the refusal for a success.
    private static CompatFunc RefuseWide(string name) =>
        new(name, false, [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3], []);   // mov rax,-1 ; ret

    /// <summary>
    /// The start of the image, at link-time address zero. Reached instruction-relative it reads back
    /// as the address the module was loaded at. Every linker defines this name, so an object using it
    /// links the same way whichever one builds the module.
    /// </summary>
    public const string ModuleBaseSymbol = "__executable_start";

    /// <summary>
    /// The end of the code group, at its link-time address. With the image start it gives the extent
    /// the unwinder matches a program counter against.
    /// </summary>
    public const string TextEndSymbol = "_etext";

    /// <summary>
    /// The frame-lookup index, at its link-time address, or the image start when the module carries
    /// none. The unwinder finds it through the header this name stands for.
    /// </summary>
    public const string FrameIndexSymbol = "__GNU_EH_FRAME_HDR";

    /// <summary>
    /// The marker the C module publishes to record that a module was linked against it. The
    /// process parameters name it, so the linker has to be able to find it among the imports.
    /// </summary>
    public const string LibcMarkerName = "Need_sceLibc";

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

    // dl_iterate_phdr(callback, argument): describe each loaded module to the callback.
    //
    // One module is one description. What reads it is the unwinder, and what it needs from the
    // description is where the module was placed, how far its code reaches, and where its frame index
    // is - the index the linker already builds and already declares a header for. Answering nothing,
    // which is what this did, tells the unwinder there is no frame information anywhere in the process:
    // every walk up the stack stops at the first frame, so an exception thrown through a frame that has
    // to be unwound ends the module instead of reaching the handler that would have caught it, and
    // nothing says why.
    //
    // The two headers are built here rather than read out of the image. A module's own header table
    // lies in the head of the code group, and that group is mapped to execute without read - reading it
    // faults - so both addresses are reached instruction-relative from names the linker places instead.
    // The description and the headers live on this frame and are only read while the callback runs.
    private const uint PtLoad = 1, PtGnuEhFrame = 0x6474E550;
    private const int PhdrSize = 56, PhdrInfoSize = 64, DlFrame = 2 * PhdrSize + PhdrInfoSize;
    private const int InfoAt = 2 * PhdrSize;

    private static CompatFunc DlIteratePhdr()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x81, 0xEC); a.Emit32(DlFrame);        // sub rsp, the frame
        a.Emit(0x49, 0x89, 0xFA);                           // mov r10, rdi   (the callback)
        a.Emit(0x49, 0x89, 0xF3);                           // mov r11, rsi   (its argument)
        // Everything not written below reads as zero, which is what each of those fields means here.
        a.Emit(0x48, 0x89, 0xE7);                           // mov rdi, rsp
        a.Emit(0xB9); a.Emit32(DlFrame);                    // mov ecx, the frame
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.Emit(0xF3, 0xAA);                                 // rep stosb

        // The first header covers the code, from the image start to the end of the group. Its address
        // is zero because the image starts there, and the reader adds where the module was placed.
        a.Emit(0xC7, 0x04, 0x24); a.Emit32((int)PtLoad);            // mov dword [rsp], loadable
        a.Emit(0xC7, 0x44, 0x24, 0x04); a.Emit32(5);               // mov dword [rsp+4], read|execute
        a.Emit(0x48, 0x8D, 0x05, 0, 0, 0, 0);               // lea rax, [rip + the end of the code]
        a.Emit(0x48, 0x8D, 0x0D, 0, 0, 0, 0);               // lea rcx, [rip + the image start]
        a.Emit(0x48, 0x29, 0xC8);                           // sub rax, rcx
        a.Emit(0x48, 0x89, 0x44, 0x24, 0x20);               // mov [rsp+32], rax   (stored length)
        a.Emit(0x48, 0x89, 0x44, 0x24, 0x28);               // mov [rsp+40], rax   (length in memory)

        // The second covers the frame index. A module built without one names the image start for it,
        // and there is then nothing to describe, so only the first header is offered.
        a.Emit(0xC7, 0x44, 0x24, PhdrSize); a.Emit32(unchecked((int)PtGnuEhFrame));
        a.Emit(0xC7, 0x44, 0x24, PhdrSize + 4); a.Emit32(4);        // read
        a.Emit(0x48, 0x8D, 0x05, 0, 0, 0, 0);               // lea rax, [rip + the frame index]
        a.Emit(0x48, 0x29, 0xC8);                           // sub rax, rcx
        a.Emit(0x48, 0x85, 0xC0); a.JumpIfEqual("codeonly"); // test rax, rax
        a.Emit(0x48, 0x89, 0x44, 0x24, PhdrSize + 16);      // mov [rsp+.. +16], rax  (its address)
        a.Emit(0x66, 0xC7, 0x84, 0x24); a.Emit32(InfoAt + 24); a.Emit(0x02, 0x00);   // two headers
        a.JumpIfAlways("described");
        a.Mark("codeonly");
        a.Emit(0x66, 0xC7, 0x84, 0x24); a.Emit32(InfoAt + 24); a.Emit(0x01, 0x00);   // one header

        a.Mark("described");
        a.Emit(0x48, 0x89, 0x8C, 0x24); a.Emit32(InfoAt);   // mov [info+0], rcx   (where it was placed)
        a.Emit(0x48, 0x8D, 0x05, 0, 0, 0, 0);               // lea rax, [rip + the name]
        a.Emit(0x48, 0x89, 0x84, 0x24); a.Emit32(InfoAt + 8);   // mov [info+8], rax
        a.Emit(0x48, 0x89, 0xE0);                           // mov rax, rsp
        a.Emit(0x48, 0x89, 0x84, 0x24); a.Emit32(InfoAt + 16);  // mov [info+16], rax  (the headers)
        // One module, and it is never unloaded: the count of modules added is one, of modules removed
        // zero. A reader that caches what it is told uses these to notice the set has changed.
        a.Emit(0x48, 0xC7, 0x84, 0x24); a.Emit32(InfoAt + 32); a.Emit32(1);

        a.Emit(0x48, 0x8D, 0xBC, 0x24); a.Emit32(InfoAt);   // lea rdi, [the description]
        a.Emit(0xBE); a.Emit32(PhdrInfoSize);               // mov esi, how big it is
        a.Emit(0x4C, 0x89, 0xDA);                           // mov rdx, r11
        a.Emit(0x41, 0xFF, 0xD2);                           // call r10
        a.Emit(0xC9, 0xC3);                                 // leave ; ret

        (byte[] code, (int Offset, string Target)[] relocs) = a.Build();
        // The four instruction-relative addresses, found by their position rather than counted by hand.
        int[] leas = [.. Enumerable.Range(0, code.Length - 6)
            .Where(i => code[i] == 0x48 && code[i + 1] == 0x8D
                     && (code[i + 2] == 0x05 || code[i + 2] == 0x0D)
                     && code[i + 3] == 0 && code[i + 4] == 0 && code[i + 5] == 0 && code[i + 6] == 0)];
        if (leas.Length != 4)
            throw new ElfLinkException("The module description does not name four addresses.");
        return new("dl_iterate_phdr", false, code,
        [
            .. relocs,
            (leas[0] + 3, TextEndSymbol),
            (leas[1] + 3, ModuleBaseSymbol),
            (leas[2] + 3, FrameIndexSymbol),
            (leas[3] + 3, ModuleNameSymbol),
        ]);
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
    // Holding a range at a named address, refusing rather than replacing one that is already held, and
    // the protection bits.
    private const byte MapFixed = 0x10, ProtReadWrite = 0x02, PosixReadWrite = 0x03;
    // Two bits of the range report. The fourth says the range is pool room; the fifth says it has been
    // filled. The fifth alone does not mean "memory is behind this address" - for a range that is
    // neither pool room nor machine memory it reports whether the range is pinned, and ordinary memory
    // never is - so the two are only meaningful together.
    private const byte PooledFlag = 0x08, CommittedFlag = 0x10;
    // The memory kind a processor-visible range is committed as.
    private const byte CpuMemoryType = 11;

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
        // The caller's own flags are kept before the register holding them is reused: whether the
        // caller pinned the range is what separates the two requests that ask for no access, and it can
        // only be read from that word. Both sides spell the pinning bit the same.
        a.Emit(0x41, 0x89, 0xCA);                           // mov r10d, ecx
        a.Emit(0x31, 0xC9);                                 // xor ecx, ecx  (place it anywhere)
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("placed");   // test rdi, rdi
        a.Emit(0xB9, MapFixed, 0x00, 0x00, 0x00);           // mov ecx, fixed
        a.Mark("placed");

        // Asking for no access is asking for room, not for memory: backing the whole of it would spend
        // the pool on a range nothing has written to yet. There are two ways to hold room and they are
        // not interchangeable. One holds addresses and nothing else - it asks for no protection at all
        // and marks the range as holding nothing - and a range held that way can never have memory put
        // behind it afterwards. The other holds the addresses **as pool room**: it asks for every
        // protection and leaves the range attached to the pool, which is what lets memory be committed
        // to it later, one piece at a time. This takes the second. It reads the address as a value and
        // writes back where the range landed through a slot handed in last, so the address stays in
        // place and the slot goes in the fifth argument.
        a.Emit(0x85, 0xD2); a.JumpIfNotEqual("access");      // test edx, edx

        // Asking for no access is two different requests, and only the caller's flags tell them apart.
        // A range asked for without pinning it is a request for room, whether or not an address is
        // suggested. A range **pinned to an address the caller already holds** is the opposite: the
        // caller is giving the memory behind that range back and keeping the range itself. Answering
        // the second with a fresh reservation releases nothing and cannot even succeed, since those
        // addresses are already taken - so the memory stayed held for as long as the module ran, and a
        // collector that gives memory back and asks for it again a moment later ran the machine out.
        // Giving it back is its own call, and it leaves the reservation to be filled again. Its flags
        // argument is the register just tested, which is zero.
        a.Emit(0x41, 0xF6, 0xC2, MapFixed); a.JumpIfEqual("reserve");   // test r10b, pinned
        a.Call("sceKernelMemoryPoolDecommit");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.Emit(0x48, 0x8B, 0x04, 0x24);                     // mov rax, [rsp]  (still the caller's range)
        a.Emit(0xC9, 0xC3);                                 // leave ; ret

        // No address named, so this is a request for room rather than a release.
        a.Mark("reserve");
        a.Emit(0x31, 0xD2);                                 // xor edx, edx  (any alignment)
        a.Emit(0x49, 0x89, 0xE0);                           // mov r8, rsp   (where it landed)
        a.Call("sceKernelMemoryPoolReserve");
        a.JumpIfAlways("settle");

        a.Mark("access");
        a.Emit(0x48, 0x89, 0xE7);                           // mov rdi, rsp  (the address slot)
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
    // reserved range is first written to.
    //
    // The two cases need opposite calls and cannot be told apart by trying one and watching it fail, so
    // the range is asked about first.
    //
    // A protection change over a range that is held but empty **succeeds** and attaches nothing, so
    // going to it first leaves the memory untaken, tells the caller the range is his, and faults on the
    // first write. Going to the mapping call first does not separate them either: a held range already
    // occupies the address space, so a mapping that refuses to overwrite is turned away and lands right
    // back on the protection change. What distinguishes them is not the outcome of either call but
    // whether anything is behind the address, and the range report says so directly.
    private static CompatFunc MProtect()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x83, 0xEC, 0x70);                     // sub rsp, 112
        // The frame holds the three arguments at [rbp-8], [rbp-16] and [rbp-24], the range report from
        // [rbp-0x60] to [rbp-0x18], and the address slot the mapping call reads and writes at [rsp].
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

        // Ask what is at that address rather than guess. A range can be held without memory behind it,
        // and the two cases need opposite calls, so the report is what decides. Its committed bit is the
        // fifth in the word of flags at offset 0x20, and the whole report is 0x48 bytes.
        a.Emit(0x48, 0x8B, 0x7D, 0xF8);                     // mov rdi, [rbp-8]
        a.Emit(0x31, 0xF6);                                 // xor esi, esi   (no search)
        a.Emit(0x48, 0x8D, 0x55, 0xA0);                     // lea rdx, [rbp-96]
        a.Emit(0xB9, 0x48, 0x00, 0x00, 0x00);               // mov ecx, 0x48
        a.Call("sceKernelVirtualQuery");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("nothing");    // no report at all: nothing is there
        // Two bits decide this, not one. The committed bit does not mean "memory is behind this
        // address" for every kind of range: for anything that is not pool room or machine memory it
        // reports whether the range is pinned, which ordinary memory never is. Reading it alone calls a
        // live range empty and the step below would then place fresh memory over it, losing what it
        // held. Only a range that is pool room *and* not yet filled takes that step.
        a.Emit(0x0F, 0xB6, 0x45, 0xC0);                     // movzbl [rbp-64], eax
        a.Emit(0x83, 0xE0, PooledFlag | CommittedFlag);     // and eax, pooled|committed
        a.Emit(0x83, 0xF8, PooledFlag);                     // cmp eax, pooled
        a.JumpIfNotEqual("keepit");

        // Nothing behind that range yet, so put memory behind it. A range that was reserved is filled
        // by committing to it, which is how a reservation is meant to be backed and the step the
        // protection call cannot do: that one reports success over a reservation and leaves it as empty
        // as it found it.
        a.Mark("take");
        a.Emit(0x48, 0x8B, 0x7D, 0xF8);                     // mov rdi, [rbp-8]
        a.Emit(0x48, 0x8B, 0x75, 0xF0);                     // mov rsi, [rbp-16]
        a.Emit(0xBA, CpuMemoryType, 0x00, 0x00, 0x00);      // mov edx, processor memory
        a.Emit(0x8B, 0x4D, 0xE8);                           // mov ecx, [rbp-24]
        a.Emit(0x45, 0x31, 0xC0);                           // xor r8d, r8d
        a.Call("sceKernelMemoryPoolCommit");
        a.Emit(0x85, 0xC0); a.JumpIfEqual("done");          // test eax, eax

        // The pool starts empty - it is opened the first time room is held and holds nothing until
        // memory is put into it - so a commit is refused until it has some. Grow it by what this call
        // needs, out of the memory the machine has, and commit again. Growing is what makes the memory
        // available; committing is what puts it behind these addresses. The amount is rounded up to the
        // size the machine's memory is carved in, which is larger than a page.
        a.Call("sceKernelGetDirectMemorySize");
        a.Emit(0x48, 0x89, 0xC6);                           // mov rsi, rax  (search to the end of it)
        a.Emit(0x31, 0xFF);                                 // xor edi, edi  (search from the start)
        a.Emit(0x48, 0x8B, 0x55, 0xF0);                     // mov rdx, [rbp-16]  (this much)
        a.Emit(0x48, 0x81, 0xC2, 0xFF, 0xFF, 0x00, 0x00);   // add rdx, block - 1
        a.Emit(0x48, 0x81, 0xE2, 0x00, 0x00, 0xFF, 0xFF);   // and rdx, -block
        a.Emit(0x31, 0xC9);                                 // xor ecx, ecx  (any alignment)
        a.Emit(0x4C, 0x8D, 0x45, 0x98);                     // lea r8, [rbp-104]  (where it came from)
        a.Call("sceKernelMemoryPoolExpand");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.Emit(0x48, 0x8B, 0x7D, 0xF8);                     // mov rdi, [rbp-8]
        a.Emit(0x48, 0x8B, 0x75, 0xF0);                     // mov rsi, [rbp-16]
        a.Emit(0xBA, CpuMemoryType, 0x00, 0x00, 0x00);      // mov edx, processor memory
        a.Emit(0x8B, 0x4D, 0xE8);                           // mov ecx, [rbp-24]
        a.Emit(0x45, 0x31, 0xC0);                           // xor r8d, r8d
        a.Call("sceKernelMemoryPoolCommit");
        a.Emit(0x85, 0xC0); a.JumpIfEqual("done");          // test eax, eax

        a.JumpIfAlways("refuse");

        // No report came back, so nothing is at that address at all. This is the only case where memory
        // may be placed there: over a range the report resolved, placing memory would lose whatever it
        // already held.
        a.Mark("nothing");
        a.Emit(0x48, 0x8B, 0x45, 0xF8);                     // mov rax, [rbp-8]
        a.Emit(0x48, 0x89, 0x04, 0x24);                     // mov [rsp], rax
        a.Emit(0x48, 0x89, 0xE7);                           // mov rdi, rsp
        a.Emit(0x48, 0x8B, 0x75, 0xF0);                     // mov rsi, [rbp-16]
        a.Emit(0x8B, 0x55, 0xE8);                           // mov edx, [rbp-24]
        a.Emit(0xB9, MapFixed, 0x00, 0x00, 0x00);           // mov ecx, fixed
        a.Call("sceKernelMapFlexibleMemory");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.JumpIfAlways("done");

        // Memory is already there, so this is a protection change on what it holds and nothing moves.
        a.Mark("keepit");
        a.Emit(0x48, 0x8B, 0x7D, 0xF8);                     // mov rdi, [rbp-8]
        a.Emit(0x48, 0x8B, 0x75, 0xF0);                     // mov rsi, [rbp-16]
        a.Emit(0x8B, 0x55, 0xE8);                           // mov edx, [rbp-24]
        a.Call("sceKernelMprotect");
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

        /// <summary>Appends a four-byte immediate, so a constant is never split by hand.</summary>
        public void Emit32(int value) =>
            _code.AddRange([(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]);

        // Every branch carries a full-width displacement rather than the short one. A short branch
        // reaches 127 bytes, and a routine that grows past that turns a correct jump into one that does
        // not reach - which is a build failure at best and, if the width were chosen by hand, a jump
        // into the middle of an instruction at worst. The wide form always reaches, and four bytes of
        // displacement in a handful of branches costs nothing.
        /// <summary>A conditional jump to a label placed later, taken when the last compare was equal.</summary>
        public void JumpIfEqual(string label)
        {
            _code.AddRange([0x0F, 0x84]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        public void JumpIfAlways(string label)
        {
            _code.Add(0xE9);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        public void JumpIfNotEqual(string label)
        {
            _code.AddRange([0x0F, 0x85]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last result was zero or above.</summary>
        public void JumpIfNotNegative(string label)
        {
            _code.AddRange([0x0F, 0x89]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last compare was at or above, counting without a sign.</summary>
        public void JumpIfAtOrAbove(string label)
        {
            _code.AddRange([0x0F, 0x83]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last compare was below, counting without a sign.</summary>
        public void JumpIfBelow(string label)
        {
            _code.AddRange([0x0F, 0x82]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last result was zero or below, counting with a sign.</summary>
        public void JumpIfNotPositive(string label)
        {
            _code.AddRange([0x0F, 0x8E]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last compare was above, counting without a sign.</summary>
        public void JumpIfAbove(string label)
        {
            _code.AddRange([0x0F, 0x87]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
        }

        /// <summary>A jump taken when the last result was below zero.</summary>
        public void JumpIfNegative(string label)
        {
            _code.AddRange([0x0F, 0x88]);
            _fixups.Add((_code.Count, label));
            _code.AddRange([0, 0, 0, 0]);
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
                if (!_labels.TryGetValue(label, out int target))
                    throw new ElfLinkException($"A branch names '{label}', which is never placed.");
                BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at), target - (at + 4));
            }
            return (code, [.. _calls]);
        }
    }

    // clock_nanosleep(clock, flags, request, remainder): sleep until a moment, or for a length of time.
    //
    // The device publishes only the call that sleeps for a length, so the two have to be bridged here,
    // and the bridge is the whole point. With the deadline flag set, the request is the moment to wake
    // at, not how long to wait: handing it to the length-based call unchanged asks for a sleep of
    // however long the clock has been running, which is the difference between a millisecond and hours.
    // The moment is turned into a length by reading the same clock the caller built it from and
    // subtracting - reading the same clock is what makes this right whatever that clock counts. A
    // moment already past becomes no wait at all rather than an enormous one.
    //
    // The result convention differs too: the length-based call answers -1 and leaves the reason
    // elsewhere, while this one answers the reason itself, so a refusal is fetched and returned.
    private const byte DeadlineFlag = 0x01;

    private static CompatFunc ClockNanosleep()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx        (keeps the stack aligned)
        a.Emit(0x48, 0x83, 0xEC, 0x28);                     // sub rsp, 40     (two moments)
        a.Emit(0x40, 0xF6, 0xC6, DeadlineFlag);             // test sil, deadline
        a.JumpIfNotEqual("deadline");

        // A length was asked for, which is what the device's call already takes.
        a.Emit(0x48, 0x89, 0xD7);                           // mov rdi, rdx
        a.Emit(0x48, 0x89, 0xCE);                           // mov rsi, rcx
        a.Call("nanosleep");
        a.JumpIfAlways("settle");

        // A moment was asked for. Read the same clock it was measured against, take the difference,
        // and wait that long.
        a.Mark("deadline");
        a.Emit(0x48, 0x89, 0xD3);                           // mov rbx, rdx    (the moment)
        a.Emit(0x48, 0x8D, 0x75, 0xD0);                     // lea rsi, [rbp-48] -> now
        a.Call("clock_gettime");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("settle");     // test eax, eax
        a.Emit(0x48, 0x8B, 0x03);                           // mov rax, [rbx]      seconds asked for
        a.Emit(0x48, 0x2B, 0x45, 0xD0);                     // sub rax, [rbp-48]   less seconds now
        a.Emit(0x48, 0x8B, 0x4B, 0x08);                     // mov rcx, [rbx+8]    nanoseconds asked for
        a.Emit(0x48, 0x2B, 0x4D, 0xD8);                     // sub rcx, [rbp-40]   less nanoseconds now
        // A negative nanosecond part borrows a whole second from the seconds part.
        a.Emit(0x48, 0x85, 0xC9); a.JumpIfNotNegative("whole"); // test rcx, rcx
        a.Emit(0x48, 0x81, 0xC1, 0x00, 0xCA, 0x9A, 0x3B);   // add rcx, 1000000000
        a.Emit(0x48, 0x83, 0xE8, 0x01);                     // sub rax, 1
        a.Mark("whole");
        a.Emit(0x48, 0x85, 0xC0); a.JumpIfNotNegative("ahead"); // test rax, rax
        a.Emit(0x31, 0xC0);                                 // xor eax, eax          already past
        a.JumpIfAlways("done");
        a.Mark("ahead");
        a.Emit(0x48, 0x89, 0x45, 0xE0);                     // mov [rbp-32], rax     seconds to wait
        a.Emit(0x48, 0x89, 0x4D, 0xE8);                     // mov [rbp-24], rcx     nanoseconds to wait
        a.Emit(0x48, 0x8D, 0x7D, 0xE0);                     // lea rdi, [rbp-32]
        a.Emit(0x31, 0xF6);                                 // xor esi, esi          no remainder
        a.Call("nanosleep");

        // The device's call answers -1 and leaves the reason elsewhere; this one answers the reason.
        a.Mark("settle");
        a.Emit(0x85, 0xC0); a.JumpIfEqual("done");          // test eax, eax
        a.Call("__error");
        a.Emit(0x8B, 0x00);                                 // mov eax, [rax]
        a.Mark("done");
        a.Emit(0x48, 0x83, 0xC4, 0x28, 0x5B, 0x5D, 0xC3);   // add rsp,40; pop rbx; pop rbp; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("clock_nanosleep", false, code, relocs);
    }

    // A signal set on this platform is four 32-bit words, one bit per signal, numbered from one: the
    // word is (signal - 1) / 32 and the bit is (signal - 1) % 32, for signals 1 to 128. Emptying and
    // adding are a few instructions each, and reporting success without doing either is the worst of
    // both: the caller reads back whatever was on the stack and hands that to the device as a set of
    // signals to act on.
    private const int SignalWords = 4, MaxSignal = 128;

    // sigemptyset(set): clear the four words and report success.
    private static CompatFunc SigEmptySet()
    {
        var a = new Asm();
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("refuse");  // test rdi, rdi
        a.Emit(0x48, 0xC7, 0x07, 0x00, 0x00, 0x00, 0x00);   // mov qword [rdi], 0
        a.Emit(0x48, 0xC7, 0x47, 0x08, 0x00, 0x00, 0x00, 0x00); // mov qword [rdi+8], 0
        a.Emit(0x31, 0xC0, 0xC3);                           // xor eax, eax ; ret
        a.Mark("refuse");
        a.Emit(0xB8, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3);         // mov eax, -1 ; ret
        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("sigemptyset", false, code, relocs);
    }

    // sigaddset(set, signal): set the one bit, or refuse a signal outside the range.
    private static CompatFunc SigAddSet()
    {
        var a = new Asm();
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("refuse");  // test rdi, rdi
        a.Emit(0xFF, 0xCE);                                 // dec esi          (signal - 1)
        a.Emit(0x81, 0xFE, MaxSignal, 0x00, 0x00, 0x00);    // cmp esi, 128
        a.JumpIfAtOrAbove("refuse");
        a.Emit(0x89, 0xF0);                                 // mov eax, esi
        a.Emit(0xC1, 0xE8, 0x05);                           // shr eax, 5       (which word)
        a.Emit(0x89, 0xF1);                                 // mov ecx, esi
        a.Emit(0x83, 0xE1, 0x1F);                           // and ecx, 31      (which bit)
        a.Emit(0xBA, 0x01, 0x00, 0x00, 0x00);               // mov edx, 1
        a.Emit(0xD3, 0xE2);                                 // shl edx, cl
        a.Emit(0x09, 0x14, 0x87);                           // or [rdi + rax*4], edx
        a.Emit(0x31, 0xC0, 0xC3);                           // xor eax, eax ; ret
        a.Mark("refuse");
        a.Emit(0xB8, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3);         // mov eax, -1 ; ret
        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("sigaddset", false, code, relocs);
    }

    // open(path, flags, mode): the flags word does not mean the same thing on both sides.
    //
    // The compiled runtime was built against a library whose open flags carry one set of values, and
    // the device reads another - they agree only on the three access-mode bits at the bottom. Handing
    // the word over unchanged is not a near miss: the bit that asks for a file to be created reads as
    // the one that asks for signal-driven input, so creating a file quietly fails; the bit that asks
    // for an existing file to be emptied reads as the create bit; and the bit that refuses to follow a
    // link reads as the one that demands a directory, so opening an ordinary file fails outright.
    //
    // Each bit is moved to where the device expects it. Everything is a shift of a masked bit, so there
    // are no branches, and the access mode at the bottom is carried across untouched. The accumulator
    // avoids the first argument, the third, and the byte that counts vector registers for a call taking
    // a variable argument, all of which have to reach the device exactly as the caller left them.
    private static readonly (uint Ours, uint Theirs)[] OpenFlagMap =
    [
        (0x000040, 0x000200),   // create if it is not there
        (0x000080, 0x000800),   // fail if it is
        (0x000100, 0x008000),   // do not make it the controlling terminal
        (0x000200, 0x000400),   // empty it
        (0x000400, 0x000008),   // append
        (0x000800, 0x000004),   // do not wait
        (0x001000, 0x001000),   // write data through
        (0x002000, 0x000040),   // signal when ready
        (0x004000, 0x010000),   // bypass the cache
        (0x010000, 0x020000),   // it must be a directory
        (0x020000, 0x000100),   // do not follow a link
        (0x080000, 0x100000),   // close it when the module is replaced
        (0x100000, 0x000080),   // write everything through
        // The bit asking for large files is dropped: the device is already counting in 64 bits, and
        // left in place it would read as the controlling-terminal bit.
    ];

    private static CompatFunc Open64()
    {
        var a = new Asm();
        a.Emit(0x41, 0x89, 0xF0);                           // mov r8d, esi
        a.Emit(0x41, 0x81, 0xE0, 0x03, 0x00, 0x00, 0x00);   // and r8d, access mode
        foreach ((uint ours, uint theirs) in OpenFlagMap)
        {
            a.Emit(0x41, 0x89, 0xF1);                       // mov r9d, esi
            a.Emit(0x41, 0x81, 0xE1);                       // and r9d, ours
            a.Emit((byte)ours, (byte)(ours >> 8), (byte)(ours >> 16), (byte)(ours >> 24));
            int ourBit = System.Numerics.BitOperations.TrailingZeroCount(ours);
            int theirBit = System.Numerics.BitOperations.TrailingZeroCount(theirs);
            if (theirBit > ourBit)
                a.Emit(0x41, 0xC1, 0xE1, (byte)(theirBit - ourBit));   // shl r9d, n
            else if (theirBit < ourBit)
                a.Emit(0x41, 0xC1, 0xE9, (byte)(ourBit - theirBit));   // shr r9d, n
            a.Emit(0x45, 0x09, 0xC8);                       // or r8d, r9d
        }
        a.Emit(0x44, 0x89, 0xC6);                           // mov esi, r8d
        a.Emit(0xE9, 0x00, 0x00, 0x00, 0x00);               // jmp open
        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("open64", false, code, [.. relocs, (code.Length - 4, "open")]);
    }

    private static CompatFunc SysConf()
    {
        const byte ScPageSize = 30, ScProcessorsConf = 83, ScProcessorsOnline = 84,
                   ScPhysPages = 85, ScAvailPages = 86;
        var a = new Asm();
        a.Emit(0x83, 0xFF, ScPageSize); a.JumpIfEqual("page");              // cmp edi, _SC_PAGESIZE
        a.Emit(0x83, 0xFF, ScProcessorsConf); a.JumpIfEqual("cpus");
        a.Emit(0x83, 0xFF, ScProcessorsOnline); a.JumpIfEqual("cpus");
        a.Emit(0x83, 0xFF, ScPhysPages); a.JumpIfEqual("pages");
        a.Emit(0x83, 0xFF, ScAvailPages); a.JumpIfEqual("avail");
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
        a.JumpIfAlways("settle");

        // How much of that is still free. The same question with a different answer, and the one the
        // collector sizes each of its regions against rather than the total. Left unanswered it took
        // the refusal for a count, so the free memory came out as sixteen million million pages and
        // every sizing decision above it was made against a figure the machine could not honour.
        a.Mark("avail");
        a.Emit(0x55, 0x48, 0x89, 0xE5);                                     // push rbp ; mov rbp, rsp
        a.Emit(0x48, 0x83, 0xEC, 0x10);                                     // sub rsp, 16
        a.Emit(0x48, 0xC7, 0x04, 0x24, 0, 0, 0, 0);                         // mov qword [rsp], 0
        a.Emit(0x48, 0x89, 0xE7);                                           // mov rdi, rsp
        a.Call("sceKernelAvailableFlexibleMemorySize");

        a.Mark("settle");
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

    // ---------------------------------------------------------------------------
    // Directory streams.
    //
    // Nothing the toolchain lets a module link against offers one: opening a directory, reading the
    // next entry and closing it are absent from every library, and each was left reporting nothing.
    // Reporting nothing is not a refusal - an empty answer is exactly what an empty directory gives -
    // so every enumeration in the runtime succeeded and found no files, which is worse than failing.
    //
    // What is published is the call the three are built on. It fills a caller's buffer with as many
    // whole entries as fit, answers how many bytes it wrote, and moves the descriptor on by itself, so
    // a second call continues where the first stopped and no seeking is needed. A stream is that call
    // plus the bookkeeping its caller would otherwise repeat.
    //
    // The whole stream is one allocation, so closing it frees one thing:
    //   +0 the descriptor   +8 how far the reader has got   +16 how much was read   +24 the buffer
    // ---------------------------------------------------------------------------

    private const byte DirFd = 0, DirLoc = 8, DirSize = 16, DirBuf = 24;
    // The buffer the platform's own library gives a stream that is read straight through.
    private const int DirBufSize = 0x10000;
    private const int DirBlockSize = DirBuf + DirBufSize;
    // Read only, do not wait, it must be a directory, and drop it if the module is replaced - the
    // four the platform's own library asks for.
    private const int DirOpenFlags = 0x120004;

    // opendir(path): open the directory, take the block, and hand back a stream positioned at its start.
    private static CompatFunc OpenDir()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx
        a.Emit(0x48, 0x83, 0xEC, 0x08);                     // sub rsp, 8   (keeps the stack aligned)
        a.Emit(0xBE); a.Emit32(DirOpenFlags);               // mov esi, the four flags
        a.Emit(0x31, 0xC0);                                 // xor eax, eax (no vector arguments follow)
        a.Call("open");
        a.Emit(0x85, 0xC0); a.JumpIfNegative("none");       // test eax, eax
        a.Emit(0x89, 0xC3);                                 // mov ebx, eax  (the descriptor)
        a.Emit(0xBF); a.Emit32(DirBlockSize);               // mov edi, the whole block
        a.Call("malloc");
        a.Emit(0x48, 0x85, 0xC0); a.JumpIfEqual("giveback"); // test rax, rax
        a.Emit(0x89, 0x18);                                 // mov [rax], ebx
        a.Emit(0x48, 0xC7, 0x40, DirLoc, 0x00, 0x00, 0x00, 0x00);   // mov qword [rax+8], 0
        a.Emit(0x48, 0xC7, 0x40, DirSize, 0x00, 0x00, 0x00, 0x00);  // mov qword [rax+16], 0
        a.JumpIfAlways("out");

        // No room for the bookkeeping, so the descriptor goes back rather than being leaked.
        a.Mark("giveback");
        a.Emit(0x89, 0xDF);                                 // mov edi, ebx
        a.Call("close");
        a.Mark("none");
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.Mark("out");
        a.Emit(0x48, 0x83, 0xC4, 0x08, 0x5B, 0x5D, 0xC3);   // add rsp,8 ; pop rbx ; pop rbp ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("opendir", false, code, relocs);
    }

    // readdir(stream): the next entry, or nothing at the end of the directory.
    private static CompatFunc ReadDir()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx
        a.Emit(0x48, 0x83, 0xEC, 0x08);                     // sub rsp, 8
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("none");    // test rdi, rdi
        a.Emit(0x48, 0x89, 0xFB);                           // mov rbx, rdi

        a.Mark("next");
        a.Emit(0x48, 0x8B, 0x43, DirLoc);                   // mov rax, [rbx+8]   how far it has got
        a.Emit(0x48, 0x3B, 0x43, DirSize);                  // cmp rax, [rbx+16]  against how much was read
        a.JumpIfBelow("have");

        // Nothing of what was read is left, so read more from where the descriptor now stands.
        a.Emit(0x8B, 0x3B);                                 // mov edi, [rbx]
        a.Emit(0x48, 0x8D, 0x73, DirBuf);                   // lea rsi, [rbx+24]
        a.Emit(0xBA); a.Emit32(DirBufSize);                 // mov edx, the buffer
        a.Call("getdents");
        a.Emit(0x85, 0xC0); a.JumpIfNotPositive("none");    // test eax, eax  (nothing more, or refused)
        a.Emit(0x48, 0x63, 0xC0);                           // movsxd rax, eax
        a.Emit(0x48, 0x89, 0x43, DirSize);                  // mov [rbx+16], rax
        a.Emit(0x48, 0xC7, 0x43, DirLoc, 0x00, 0x00, 0x00, 0x00);   // mov qword [rbx+8], 0
        a.Emit(0x31, 0xC0);                                 // xor eax, eax   (back at the start of it)

        // One entry. Its own recorded length is what moves the reader on, so a length of zero would
        // never move and a length running past what was read would read beyond it: both are treated as
        // the end of the directory rather than trusted.
        a.Mark("have");
        a.Emit(0x48, 0x8D, 0x53, DirBuf);                   // lea rdx, [rbx+24]
        a.Emit(0x48, 0x01, 0xC2);                           // add rdx, rax    (this entry)
        a.Emit(0x0F, 0xB7, 0x4A, 0x04);                     // movzx ecx, word [rdx+4]  (its length)
        a.Emit(0x48, 0x85, 0xC9); a.JumpIfEqual("none");    // test rcx, rcx
        a.Emit(0x48, 0x01, 0xC8);                           // add rax, rcx
        a.Emit(0x48, 0x3B, 0x43, DirSize);                  // cmp rax, [rbx+16]
        a.JumpIfAbove("none");
        a.Emit(0x48, 0x89, 0x43, DirLoc);                   // mov [rbx+8], rax
        // An entry whose file number is zero is one that has been removed; it is skipped, not reported.
        a.Emit(0x83, 0x3A, 0x00); a.JumpIfEqual("next");    // cmp dword [rdx], 0
        a.Emit(0x48, 0x89, 0xD0);                           // mov rax, rdx
        a.JumpIfAlways("out");

        a.Mark("none");
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.Mark("out");
        a.Emit(0x48, 0x83, 0xC4, 0x08, 0x5B, 0x5D, 0xC3);   // add rsp,8 ; pop rbx ; pop rbp ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("readdir", false, code, relocs);
    }

    // closedir(stream): give back the descriptor and the block.
    private static CompatFunc CloseDir()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx
        a.Emit(0x48, 0x83, 0xEC, 0x08);                     // sub rsp, 8
        a.Emit(0x48, 0x85, 0xFF); a.JumpIfEqual("out");     // test rdi, rdi
        a.Emit(0x48, 0x89, 0xFB);                           // mov rbx, rdi
        a.Emit(0x8B, 0x3B);                                 // mov edi, [rbx]
        a.Call("close");
        a.Emit(0x48, 0x89, 0xDF);                           // mov rdi, rbx
        a.Call("free");
        a.Mark("out");
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.Emit(0x48, 0x83, 0xC4, 0x08, 0x5B, 0x5D, 0xC3);   // add rsp,8 ; pop rbx ; pop rbp ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("closedir", false, code, relocs);
    }

    // sched_getaffinity(process, set size, set): which processors this thread may run on.
    //
    // The platform publishes the same question asked of a thread rather than a process, and a module is
    // one process, so the running thread's answer is the module's. The mask is a single 64-bit word
    // there and a byte array of the caller's size here, so the caller's is cleared first and the word
    // written into its bottom - a set left holding whatever was on the stack would report processors
    // that are not there.
    //
    // Refusing instead was not free: the count the caller derives from this is how many threads the
    // runtime starts with, and the answer it falls back to is a different call that need not agree.
    private static CompatFunc SchedGetAffinity()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx
        a.Emit(0x48, 0x83, 0xEC, 0x18);                     // sub rsp, 24  (the word, and alignment)
        a.Emit(0x48, 0x85, 0xD2); a.JumpIfEqual("refuse");  // test rdx, rdx   (nowhere to put it)
        a.Emit(0x48, 0x83, 0xFE, 0x08); a.JumpIfBelow("refuse");  // cmp rsi, 8  (too small to hold it)
        a.Emit(0x48, 0x89, 0xD3);                           // mov rbx, rdx
        a.Emit(0x48, 0x89, 0xD7);                           // mov rdi, rdx
        a.Emit(0x48, 0x89, 0xF1);                           // mov rcx, rsi
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.Emit(0xF3, 0xAA);                                 // rep stosb    (clear the caller's set)
        a.Call("scePthreadSelf");
        a.Emit(0x48, 0x89, 0xC7);                           // mov rdi, rax
        a.Emit(0x48, 0x8D, 0x75, 0xE0);                     // lea rsi, [rbp-32]
        a.Call("scePthreadGetaffinity");
        a.Emit(0x85, 0xC0); a.JumpIfNotEqual("refuse");     // test eax, eax
        a.Emit(0x48, 0x8B, 0x45, 0xE0);                     // mov rax, [rbp-32]
        a.Emit(0x48, 0x89, 0x03);                           // mov [rbx], rax
        a.Emit(0x31, 0xC0);                                 // xor eax, eax
        a.JumpIfAlways("out");
        a.Mark("refuse");
        a.Emit(0xB8, 0xFF, 0xFF, 0xFF, 0xFF);               // mov eax, -1
        a.Mark("out");
        a.Emit(0x48, 0x83, 0xC4, 0x18, 0x5B, 0x5D, 0xC3);   // add rsp,24 ; pop rbx ; pop rbp ; ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("sched_getaffinity", false, code, relocs);
    }

    // __sched_cpucount(set size, set): how many processors are in the set. Answering a fixed one while
    // the processor count answers eight left the runtime sizing its thread pool for a single processor
    // on a machine with eight, and the two answers describing different machines.
    private static CompatFunc SchedCpuCount()
    {
        var a = new Asm();
        a.Emit(0x31, 0xC0);                                 // xor eax, eax    (none counted yet)
        a.Emit(0x48, 0x85, 0xF6); a.JumpIfEqual("done");    // test rsi, rsi
        a.Emit(0x31, 0xC9);                                 // xor ecx, ecx
        a.Mark("byte");
        a.Emit(0x48, 0x39, 0xF9); a.JumpIfAtOrAbove("done"); // cmp rcx, rdi
        a.Emit(0x0F, 0xB6, 0x14, 0x0E);                     // movzx edx, byte [rsi + rcx]
        a.Emit(0xF3, 0x0F, 0xB8, 0xD2);                     // popcnt edx, edx
        a.Emit(0x01, 0xD0);                                 // add eax, edx
        a.Emit(0x48, 0xFF, 0xC1);                           // inc rcx
        a.JumpIfAlways("byte");
        a.Mark("done");
        a.Emit(0xC3);                                       // ret

        (byte[] code, (int, string)[] relocs) = a.Build();
        return new("__sched_cpucount", false, code, relocs);
    }

    // ---------------------------------------------------------------------------
    // Error numbers.
    //
    // Both sides count their errors from one, and they stop agreeing at eleven: there it means a
    // deadlock would occur on one side and try-again on the other, and the two swap places. From
    // thirty-five on they diverge wholesale. The numbers are exactly what the runtime compares against,
    // so handing its own place over unchanged - which is what a plain forward did - makes every one of
    // those comparisons a comparison against a different error.
    //
    // It is not a quiet difference. The number the device writes for a call it does not implement is
    // the number the runtime reads as "that is not a socket", so a runtime that probes for a missing
    // call by testing for the first concludes the call is there, caches that, and never takes the way
    // that works. The same trade in reverse marks a working feature unsupported.
    //
    // Translating the value alone will not do, because what is handed over is a **place** and the
    // runtime writes to it as well as reading it. So this keeps a place of its own, one per thread, and
    // moves the device's number into it - translated - whenever it is asked. Taking a number clears the
    // device's own, so the same error arriving twice is seen twice, and a place the runtime cleared
    // itself stays clear until the device puts something new there. That is the behaviour the runtime
    // was written against.
    //
    // Both columns below are read off their own side rather than derived from one another - the
    // device's from the platform headers, cross-checked against the table its own C++ library carries;
    // the runtime's from the numbering it was compiled with - so a wrong pairing takes two independent
    // mistakes. A device number with no counterpart the runtime names is reported as a number above
    // everything it knows, which it reads as an error it has no name for. That is honest, and better
    // than passing it through to be read as some unrelated error that happens to share the number.
    // ---------------------------------------------------------------------------

    private const string ErrnoShadowSymbol = "__sp_errno";
    private const string ErrorTableSymbol = "__sp_error_numbers";
    private const byte UnnamedError = 132;      // above every number the runtime has a name for
    private const int ErrorTableSize = 256;

    private static readonly (string Name, byte Device, byte Runtime)[] ErrorNumbers =
    [
        ("EPERM", 1, 1), ("ENOENT", 2, 2), ("ESRCH", 3, 3), ("EINTR", 4, 4), ("EIO", 5, 5),
        ("ENXIO", 6, 6), ("E2BIG", 7, 7), ("ENOEXEC", 8, 8), ("EBADF", 9, 9), ("ECHILD", 10, 10),
        // The one that trips a reader who assumes the low numbers agree: these two are swapped.
        ("EDEADLK", 11, 35), ("EAGAIN", 35, 11),
        ("ENOMEM", 12, 12), ("EACCES", 13, 13), ("EFAULT", 14, 14), ("ENOTBLK", 15, 15),
        ("EBUSY", 16, 16), ("EEXIST", 17, 17), ("EXDEV", 18, 18), ("ENODEV", 19, 19),
        ("ENOTDIR", 20, 20), ("EISDIR", 21, 21), ("EINVAL", 22, 22), ("ENFILE", 23, 23),
        ("EMFILE", 24, 24), ("ENOTTY", 25, 25), ("ETXTBSY", 26, 26), ("EFBIG", 27, 27),
        ("ENOSPC", 28, 28), ("ESPIPE", 29, 29), ("EROFS", 30, 30), ("EMLINK", 31, 31),
        ("EPIPE", 32, 32), ("EDOM", 33, 33), ("ERANGE", 34, 34),
        ("EINPROGRESS", 36, 115), ("EALREADY", 37, 114), ("ENOTSOCK", 38, 88),
        ("EDESTADDRREQ", 39, 89), ("EMSGSIZE", 40, 90), ("EPROTOTYPE", 41, 91),
        ("ENOPROTOOPT", 42, 92), ("EPROTONOSUPPORT", 43, 93), ("ESOCKTNOSUPPORT", 44, 94),
        ("EOPNOTSUPP", 45, 95), ("EPFNOSUPPORT", 46, 96), ("EAFNOSUPPORT", 47, 97),
        ("EADDRINUSE", 48, 98), ("EADDRNOTAVAIL", 49, 99), ("ENETDOWN", 50, 100),
        ("ENETUNREACH", 51, 101), ("ENETRESET", 52, 102), ("ECONNABORTED", 53, 103),
        ("ECONNRESET", 54, 104), ("ENOBUFS", 55, 105), ("EISCONN", 56, 106), ("ENOTCONN", 57, 107),
        ("ESHUTDOWN", 58, 108), ("ETOOMANYREFS", 59, 109), ("ETIMEDOUT", 60, 110),
        ("ECONNREFUSED", 61, 111), ("ELOOP", 62, 40), ("ENAMETOOLONG", 63, 36),
        ("EHOSTDOWN", 64, 112), ("EHOSTUNREACH", 65, 113), ("ENOTEMPTY", 66, 39),
        ("EUSERS", 68, 87), ("EDQUOT", 69, 122), ("ESTALE", 70, 116), ("EREMOTE", 71, 66),
        ("ENOLCK", 77, 37), ("ENOSYS", 78, 38), ("EIDRM", 82, 43), ("ENOMSG", 83, 42),
        ("EOVERFLOW", 84, 75), ("ECANCELED", 85, 125), ("EILSEQ", 86, 84),
        // The runtime carries no name of its own for an absent attribute; the one it reads for absent
        // data is the same number and the same meaning to every caller that tests it.
        ("ENOATTR", 87, 61),
        ("EBADMSG", 89, 74), ("EMULTIHOP", 90, 72), ("ENOLINK", 91, 67), ("EPROTO", 92, 71),
        ("ENOTRECOVERABLE", 107, 131), ("EOWNERDEAD", 108, 130),
    ];

    /// <summary>The device's numbering indexed straight, holding the runtime's number for each.</summary>
    private static byte[] BuildErrorTable()
    {
        byte[] table = new byte[ErrorTableSize];
        Array.Fill(table, UnnamedError);
        table[0] = 0;                            // no error stays no error
        foreach ((string _, byte device, byte runtime) in ErrorNumbers)
            table[device] = runtime;
        return table;
    }

    // __errno_location(): the place the runtime reads its last error from.
    private static CompatFunc ErrnoLocation()
    {
        var a = new Asm();
        a.Emit(0x55, 0x48, 0x89, 0xE5);                     // push rbp ; mov rbp, rsp
        a.Emit(0x53);                                       // push rbx
        a.Emit(0x48, 0x83, 0xEC, 0x08);                     // sub rsp, 8
        a.Emit(0x64, 0x48, 0x8B, 0x04, 0x25, 0, 0, 0, 0);   // mov rax, fs:[0]   (this thread)
        int tlsAt = a.Length + 3;
        a.Emit(0x48, 0x8D, 0x98, 0, 0, 0, 0);               // lea rbx, [rax + our place]
        a.Call("__error");
        a.Emit(0x8B, 0x08);                                 // mov ecx, [rax]    (what the device left)
        a.Emit(0x85, 0xC9); a.JumpIfEqual("keep");          // test ecx, ecx
        a.Emit(0xC7, 0x00, 0x00, 0x00, 0x00, 0x00);         // mov dword [rax], 0  (take it)
        a.Emit(0x81, 0xF9); a.Emit32(ErrorTableSize);       // cmp ecx, the numbering's reach
        a.JumpIfAtOrAbove("unnamed");
        int tableAt = a.Length + 3;
        a.Emit(0x48, 0x8D, 0x15, 0, 0, 0, 0);               // lea rdx, [rip + the numbering]
        a.Emit(0x0F, 0xB6, 0x0C, 0x0A);                     // movzx ecx, byte [rdx + rcx]
        a.JumpIfAlways("store");
        a.Mark("unnamed");
        a.Emit(0xB9, UnnamedError, 0x00, 0x00, 0x00);       // mov ecx, no name for it
        a.Mark("store");
        a.Emit(0x89, 0x0B);                                 // mov [rbx], ecx
        a.Mark("keep");
        a.Emit(0x48, 0x89, 0xD8);                           // mov rax, rbx
        a.Emit(0x48, 0x83, 0xC4, 0x08, 0x5B, 0x5D, 0xC3);   // add rsp,8 ; pop rbx ; pop rbp ; ret

        (byte[] code, (int Offset, string Target)[] relocs) = a.Build();
        return new("__errno_location", false, code, [.. relocs, (tableAt, ErrorTableSymbol)],
            Tls: (tlsAt, ErrnoShadowSymbol));
    }

    private static IReadOnlyList<CompatFunc> Functions =>
    [
        // Large-file variants: the device publishes the base name with a 64-bit offset already.
        Open64(),
        Forward("lseek64", "lseek"),
        Forward("mmap64", "mmap"),
        Forward("pread64", "pread"),
        Forward("fopen64", "fopen"),
        Readdir64(),
        Forward("getrlimit64", "getrlimit"),
        StatThunk("__fxstat64", "fstat", byFd: true),
        StatThunk("__xstat64", "stat", byFd: false),

        // Mapped to a device entry, or refused where there is no counterpart.
        ErrnoLocation(),
        Forward("pthread_setname_np", "scePthreadRename"),
        // Reading a thread's own attributes. The device publishes the same call under the name the
        // system it descends from uses, and the two agree exactly - a thread handle first, a place to
        // put the attributes second, zero or an error number back - so the arguments and the result
        // pass straight through. Refusing instead left the attributes at the defaults that were put
        // there before the call, so the caller read the template's stack address and size rather than
        // the running thread's, and every later check against those bounds asked about the wrong range.
        Forward("pthread_getattr_np", "pthread_attr_get_np"),
        ClockNanosleep(),
        Forward("pipe2", "pipe"),                   // the extra flags argument is dropped
        Refuse("statfs64"),
        RefuseWide("__getdelim"),                   // a count, so the whole register has to say -1

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
        DlIteratePhdr(),
        DlAddr(),
        Zero("gai_strerror"),                       // no message
        Refuse("sysinfo"),
        Zero("prctl"),                              // process controls are no-ops
        RefuseWide("syscall"),                      // a machine word, so all of it has to say -1
        // Which processor this thread is on. The platform publishes exactly that question.
        Forward("sched_getcpu", "sceKernelGetCurrentCpu"),
        SchedGetAffinity(),
        SchedCpuCount(),

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
        RefuseWide("readlink"),                     // a count, declared as a machine word
        Refuse("mkfifo"),
        Refuse("mkstemps64"),
        RefuseWide("pathconf"),                     // a limit, declared as a machine word
        RefuseWide("sendfile64"),                   // a count, declared as a machine word
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

        // Taking a compute queue directly from the graphics driver. The toolchain offers both of these
        // and the module that would answer them carries neither, which is a difference worth being
        // careful about: an import of a name the module does not carry cannot bind, and a module whose
        // imports do not all bind never reaches its first instruction - so naming them would cost far
        // more than the two calls are worth. Refused here instead, which the caller already handles by
        // going without a compute queue.
        Refuse("sceAgcDriverAcquireComputeQueue"),
        Refuse("sceAgcDriverReleaseComputeQueue"),

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
        // Asking about a name without following a link where it leads. The toolchain publishes no such
        // call, and refusing it failed every check of whether a file exists, because the large-file
        // variant below is what those checks reach and it forwards here. The call that does follow a
        // link is published and answers the same question about the same name; the two differ only
        // where a name is a link, and the difference is worth far less than the answer.
        Forward("lstat", "stat"),
        Refuse("pipe"),
        Refuse("poll"),
        Refuse("setrlimit"),
        Refuse("shm_open"),
        Refuse("shm_unlink"),
        Refuse("waitpid"),
        // The device publishes the counterparts that change protection and release memory, but not the
        // one that takes it: no library the toolchain links against carries that name, so a module has
        // to bring its own. The protection change is defined here too, because the two have to agree -
        // this one hands back address space with no memory behind it, so the change that grants access
        // is what takes the memory.
        Mmap(),
        MProtect(),
        // Signals are the platform's to deliver; installing a handler succeeds and changes nothing.
        Zero("sigaction"),
        SigEmptySet(),
        SigAddSet(),
        Zero("signal"),                             // the previous handler, which is the default one
        Zero("pthread_kill"),
        // Directory streams, which nothing published offers, built on the call underneath them.
        OpenDir(),
        ReadDir(),
        CloseDir(),
        // Nothing found, reported the way each caller expects: a null result.
        Zero("dlopen"),
        Zero("dlsym"),
        Zero("getenv"),
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
        new("__sce_libc_marker", LibcMarkerName),
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

        // The variables: one pointer each, in declaration order, then the module name dladdr reports,
        // then the error numbering, which is read like any other constant.
        IReadOnlyList<CompatData> data = DataObjects;
        int moduleNameOffset = data.Count * 8;
        int errorTableOffset = moduleNameOffset + ModuleNameText.Length;
        byte[] dataBytes = new byte[errorTableOffset + ErrorTableSize];
        ModuleNameText.CopyTo(dataBytes, moduleNameOffset);
        BuildErrorTable().CopyTo(dataBytes, errorTableOffset);

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
            (strtab.Add(ErrnoShadowSymbol), (BindLocal << 4) | TypeTls, shTbss,                    // [2] error number
                ErrnoShadowOffset, ErrnoShadowSize),
            (strtab.Add(ModuleNameSymbol), (BindLocal << 4) | TypeObject, shData,                  // [3] module name
                (ulong)moduleNameOffset, (ulong)ModuleNameText.Length),
            (strtab.Add(ErrorTableSymbol), (BindLocal << 4) | TypeObject, shData,                  // [4] the numbering
                (ulong)errorTableOffset, ErrorTableSize),
        };
        const int LocalSymbolCount = 5;
        symIndex[ReaddirBufSymbol] = 1;
        symIndex[ErrnoShadowSymbol] = 2;
        symIndex[ModuleNameSymbol] = 3;
        symIndex[ErrorTableSymbol] = 4;

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
            if (funcs[i].Tls is var (tlsOff, tlsName))
                relocs.Add((textOffsets[i] + tlsOff, symIndex[tlsName], RTpOff32, 0));
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
        WriteShdr(shdr, shTbss, nTbss, ShtNoBits, ShfWrite | ShfAlloc | ShfTls, tbssOff, ThreadBlockSize, 0, 0, 8, 0);
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
