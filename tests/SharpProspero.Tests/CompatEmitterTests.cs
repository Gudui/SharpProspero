// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

// The compat object must parse as a relocatable object the linker understands, define exactly the
// runtime-support names, and leave the device-provided base names undefined so the linker imports them.
public sealed class CompatEmitterTests
{
    private static ElfObject Read() => ElfObjectReader.Read(CompatEmitter.BuildObject(), "compat.o");

    /// <summary>Whether the body of <paramref name="func"/> contains the given instruction bytes.</summary>
    private static bool Holds(ElfObject obj, ElfSymbol func, byte[] instruction)
    {
        ReadOnlySpan<byte> body = obj.Sections[func.SectionIndex].Data.AsSpan((int)func.Value, (int)func.Size);
        for (int i = 0; i + instruction.Length <= body.Length; i++)
            if (body.Slice(i, instruction.Length).SequenceEqual(instruction))
                return true;
        return false;
    }

    [Fact]
    public void SysConfSendsEveryQuestionToItsOwnAnswer()
    {
        // Each question is a compare followed by a jump, and the jump has to reach the answer that
        // belongs to it. One counted by hand sent the memory-size question to the processor-count
        // answer, so the module reported 128 KB of memory and the collector refused to start - which
        // looks exactly like the question being refused outright. Every branch carries a full-width
        // displacement, so a routine growing past a short branch's reach can never bend one of these.
        ElfObject obj = Read();
        ElfSymbol sysconf = Assert.Single(obj.Symbols, s => s.Name == "sysconf" && !s.IsUndefined);
        byte[] code = obj.Sections[sysconf.SectionIndex].Data;
        int at = (int)sysconf.Value;

        // (question, the first bytes of the answer it must reach)
        (byte Question, byte[] Answer)[] expected =
        [
            (30, [0xB8, 0x00, 0x40, 0x00, 0x00]),   // page size: 16384
            (83, [0xB8, 0x08, 0x00, 0x00, 0x00]),   // processors
            (84, [0xB8, 0x08, 0x00, 0x00, 0x00]),
            (85, [0x55, 0x48, 0x89, 0xE5]),         // memory: the routine that asks the pool
            (86, [0x55, 0x48, 0x89, 0xE5]),         // free memory: its own routine, not the one above
        ];

        foreach ((byte question, byte[] answer) in expected)
        {
            // cmp edi, <question>  =  83 FF xx, then 74 <rel8>
            int cmp = -1;
            for (int i = at; i < at + 0x40; i++)
                if (code[i] == 0x83 && code[i + 1] == 0xFF && code[i + 2] == question)
                { cmp = i; break; }
            Assert.True(cmp >= 0, $"no question for {question}");
            // cmp edi, <question> is three bytes, then je <rel32> is six.
            Assert.Equal(0x0F, code[cmp + 3]);
            Assert.Equal(0x84, code[cmp + 4]);

            int target = cmp + 9 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(cmp + 5));
            Assert.Equal(answer, code[target..(target + answer.Length)]);
        }

        // Both memory answers must reach the pool rather than report a number of their own, and they
        // are different questions: how much this module may have, and how much of it is still free.
        // The free one was left unanswered, so the caller took the refusal for a count and sized itself
        // against sixteen million million pages.
        Assert.Equal(
            ["sceKernelConfiguredFlexibleMemorySize", "sceKernelAvailableFlexibleMemorySize"],
            CallsOf(obj, "sysconf"));
    }

    [Fact]
    public void DlAddrReportsWhereTheModuleWasLoadedRatherThanFailing()
    {
        // The runtime takes what this reports as the handle identifying the module, so answering
        // "not found" registers the module under a null handle - one no address can be matched back
        // to. It reads the image start instruction-relative, which is the only way to learn the
        // address the module was placed at.
        ElfObject obj = Read();

        ElfSymbol dladdr = Assert.Single(obj.Symbols, s => s.Name == "dladdr" && !s.IsUndefined);
        var refs = obj.Relocations[dladdr.SectionIndex]
            .Where(r => r.Offset >= dladdr.Value && r.Offset < dladdr.Value + dladdr.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .ToList();
        // The base, and a name for the file. The one caller that reads the name measures its length,
        // so a null there faults where an approximate name costs nothing.
        Assert.Equal([CompatEmitter.ModuleBaseSymbol, "__sp_module_name"], refs);
        ElfSymbol name = Assert.Single(obj.Symbols, s => s.Name == "__sp_module_name");
        Assert.False(name.IsUndefined);
        Assert.True(name.Size > 1, "the name must be a real string");
        // It has to be readable at run time: the code group is mapped execute-only.
        Assert.False(obj.Sections[name.SectionIndex].IsExecutable);
        byte[] text = obj.Sections[name.SectionIndex].Data;
        Assert.Equal(0, text[(int)(name.Value + name.Size) - 1]);

        // Not the shape that answers zero and returns: xor eax,eax / ret.
        byte[] code = obj.Sections[dladdr.SectionIndex].Data;
        Assert.NotEqual(new byte[] { 0x31, 0xC0, 0xC3 }, code[(int)dladdr.Value..((int)dladdr.Value + 3)]);
    }

    [Fact]
    public void ClockNanosleepTurnsADeadlineIntoALength()
    {
        // The device publishes only the call that sleeps for a length of time. With the deadline flag
        // set the request names a moment instead, so handing it over unchanged asks for a sleep of
        // however long the clock has been running - the difference between a millisecond and hours.
        // The moment is turned into a length by reading the same clock and subtracting.
        ElfObject obj = Read();
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "clock_nanosleep" && !s.IsUndefined);
        var calls = obj.Relocations[f.SectionIndex]
            .Where(r => r.Offset > f.Value && r.Offset < f.Value + f.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name).ToList();
        Assert.Equal(["nanosleep", "clock_gettime", "nanosleep", "__error"], calls);

        // The flag is tested in the low byte of the second argument, which needs the wider encoding -
        // without it the byte tested belongs to a different register entirely.
        Assert.True(Holds(obj, f, [0x40, 0xF6, 0xC6, 0x01]), "the deadline flag is not tested");
        // A whole second is borrowed when the nanosecond part goes negative.
        Assert.True(Holds(obj, f, [0x48, 0x81, 0xC1, 0x00, 0xCA, 0x9A, 0x3B]), "no borrow of a second");
    }

    [Fact]
    public void SignalSetsAreBuiltRatherThanReportedDone()
    {
        // Reporting success without emptying or setting leaves the caller handing whatever was on its
        // stack to the device as a set of signals to act on. A set here is four 32-bit words, one bit
        // per signal numbered from one, so both are a few instructions.
        ElfObject obj = Read();
        foreach (string name in new[] { "sigemptyset", "sigaddset" })
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            byte[] code = obj.Sections[f.SectionIndex].Data;
            Assert.NotEqual(new byte[] { 0x31, 0xC0, 0xC3 }, code[(int)f.Value..((int)f.Value + 3)]);
        }
        // Emptying clears sixteen bytes; adding places one bit in the word that holds it.
        ElfSymbol empty = Assert.Single(obj.Symbols, s => s.Name == "sigemptyset" && !s.IsUndefined);
        Assert.True(Holds(obj, empty, [0x48, 0xC7, 0x47, 0x08, 0x00, 0x00, 0x00, 0x00]),
            "sigemptyset does not clear the whole set");
        ElfSymbol add = Assert.Single(obj.Symbols, s => s.Name == "sigaddset" && !s.IsUndefined);
        Assert.True(Holds(obj, add, [0x09, 0x14, 0x87]), "sigaddset does not place the bit");
        Assert.True(Holds(obj, add, [0x81, 0xFE, 0x80, 0x00, 0x00, 0x00]), "sigaddset does not bound the signal");
    }

    [Fact]
    public void ThreadAttributesComeFromTheDeviceRatherThanBeingRefused()
    {
        // The device publishes this under the name the system it descends from uses. Refusing left the
        // attributes at the defaults set just before the call, so the caller read a stack address and
        // size belonging to nothing.
        ElfObject obj = Read();
        var defined = obj.Symbols.Where(s => !s.IsUndefined).Select(s => s.Name).ToHashSet();
        Assert.Contains("pthread_getattr_np", defined);
        Assert.Contains(obj.Symbols, s => s.Name == "pthread_attr_get_np" && s.IsUndefined);
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "pthread_getattr_np" && !s.IsUndefined);
        Assert.Equal(0xE9, obj.Sections[f.SectionIndex].Data[(int)f.Value]);   // a tail jump, nothing else
    }

    [Fact]
    public void MProtectAsksWhatIsThereBeforeItChoosesACall()
    {
        // No library the toolchain links against publishes the call that takes memory, so this object
        // brings its own; it hands back address space with nothing behind it, and the protection change
        // is what puts memory there. Two things decide whether that works. Room has to be held as pool
        // room - the other way of holding addresses marks the range as holding nothing, and no memory
        // can ever be put behind it. And the protection change cannot pick its call by trying one and
        // watching it fail: a protection change over a held-but-empty range succeeds and attaches
        // nothing. So the range is asked about first, and its answer picks the call.
        ElfObject obj = Read();
        ElfSymbol mmap = Assert.Single(obj.Symbols, s => s.Name == "mmap" && !s.IsUndefined);
        ElfSymbol mprotect = Assert.Single(obj.Symbols, s => s.Name == "mprotect" && !s.IsUndefined);

        var mmapCalls = obj.Relocations[mmap.SectionIndex]
            .Where(r => r.Offset > mmap.Value && r.Offset < mmap.Value + mmap.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name).ToList();
        // Three calls, because asking for no access is two different requests. Pinned to an address the
        // caller already holds it is a release, and answering that with a reservation releases nothing
        // and cannot succeed either, since those addresses are taken - so the memory would stay held for
        // as long as the module ran. Unpinned it is a request for room, whether or not an address is
        // suggested. With access asked for it is an ordinary mapping.
        Assert.Equal(
            ["sceKernelMemoryPoolDecommit", "sceKernelMemoryPoolReserve", "sceKernelMapFlexibleMemory"],
            mmapCalls);

        var protCalls = obj.Relocations[mprotect.SectionIndex]
            .Where(r => r.Offset > mprotect.Value && r.Offset < mprotect.Value + mprotect.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name).ToList();
        Assert.Equal([
            "sceKernelVirtualQuery",          // what is behind this address?
            "sceKernelMemoryPoolCommit",      // nothing: put memory behind it
            "sceKernelGetDirectMemorySize",   // refused: the pool may simply be empty
            "sceKernelMemoryPoolExpand",      // so grow it
            "sceKernelMemoryPoolCommit",      // and commit again
            "sceKernelMapFlexibleMemory",     // never held as pool room: take it outright
            "sceKernelMprotect",              // memory is there: protection only
        ], protCalls);

        // Two bits of the report decide this, not one. The filled bit alone does not mean memory is
        // behind the address: for a range that is neither pool room nor machine memory it reports
        // whether the range is pinned, and ordinary memory never is. Reading it alone calls a live
        // range empty, and placing memory over a live range loses what it held. Only pool room that is
        // not yet filled - the fourth bit set and the fifth clear - takes that step.
        Assert.True(Holds(obj, mprotect, [0x0F, 0xB6, 0x45, 0xC0]), "mprotect does not read the report");
        Assert.True(Holds(obj, mprotect, [0x83, 0xE0, 0x18]), "mprotect does not mask both bits");
        Assert.True(Holds(obj, mprotect, [0x83, 0xF8, 0x08]), "mprotect does not require pool room");
        // A reserved range is filled by committing to it, as processor-visible memory.
        Assert.True(Holds(obj, mprotect, [0xBA, 0x0B, 0x00, 0x00, 0x00]),
            "mprotect does not commit the range as processor memory");
        // A range that was never reserved is taken from the pool, held at that exact address.
        Assert.True(Holds(obj, mprotect, [0xB9, 0x10, 0x00, 0x00, 0x00]),
            "mprotect does not take the memory at the address it was given");

        // Both round the length to whole pages, and the change also moves back to a page boundary.
        byte[] roundLength = [0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF];    // and rsi, -16384
        foreach (ElfSymbol f in (ElfSymbol[])[mmap, mprotect])
            Assert.True(Holds(obj, f, roundLength), $"{f.Name} does not round its length to whole pages");
        Assert.True(Holds(obj, mprotect, [0x48, 0x81, 0xE7, 0x00, 0xC0, 0xFF, 0xFF]),
            "mprotect does not move back to a page boundary");
    }

    [Fact]
    public void ParsesAsARelocatableObject()
    {
        ElfObject obj = Read();
        Assert.NotEmpty(obj.Sections);
        Assert.NotEmpty(obj.Symbols);
    }

    [Fact]
    public void DefinesEveryRuntimeSupportName()
    {
        ElfObject obj = Read();
        var defined = obj.Symbols.Where(s => !s.IsUndefined && s.Type == 2).Select(s => s.Name).ToHashSet();

        // A representative mix across the categories: large-file forwarders, arg-shuffled variants,
        // mapped names, weak no-ops, and refusals.
        string[] expected =
        [
            "open64", "mmap64", "getrlimit64", "__fxstat64", "__xstat64", "__lxstat64", "ftruncate64",
            "pwrite64", "preadv64", "setrlimit64", "pthread_setname_np", "__errno_location",
            "clock_nanosleep", "pipe2", "__libc_start_main", "__gmon_start__", "fork", "vfork",
            "isatty", "getpwuid_r", "getgrgid_r", "sysinfo", "syscall", "sched_getcpu", "sched_setaffinity",
            "getauxval", "inotify_init1", "uname", "waitid", "readlink",
        ];
        foreach (string name in expected)
            Assert.Contains(name, defined);
        // The system queries nothing publishes an entry point for; each reports failure rather than
        // leaving an import the loader cannot bind.
        foreach (string name in (string[])["sceKernelGetOpenPsId", "sceKernelGetProsperoSystemSwVersion",
                                           "sceKernelGetAllowedSdkVersionOnSystem", "sysctlbyname"])
            Assert.Contains(name, defined);
        // The variables the runtime reads rather than calls. Two hold the address of a stream the C
        // module publishes, one starts out empty, and the last holds the address of the marker that
        // records the module was linked against that library.
        var objects = obj.Symbols.Where(s => !s.IsUndefined && s.Type == SymType.Object && s.Bind != SymBind.Local).ToList();
        Assert.Equal(4, objects.Count);
        foreach (string name in (string[])["stdout", "stderr", "environ", "__sce_libc_marker"])
            Assert.Contains(objects, s => s.Name == name && s.Size == 8);
        // The full compat surface, which is what the emitter says it defines.
        Assert.Equal(CompatEmitter.DefinedNames.Count, defined.Count + objects.Count);
    }

    [Fact]
    public void OverrideableNamesAreWeak()
    {
        ElfObject obj = Read();
        foreach (string name in new[] { "__libc_start_main", "__gmon_start__", "_ITM_registerTMCloneTable", "_ITM_deregisterTMCloneTable" })
            Assert.True(obj.Symbols.Single(s => s.Name == name && !s.IsUndefined).IsWeak, $"{name} should be weak");
    }

    /// <summary>The names a function reaches, in the order it reaches them.</summary>
    private static string[] CallsOf(ElfObject obj, string name)
    {
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
        return [.. obj.Relocations[f.SectionIndex]
            .Where(r => r.Offset > f.Value && r.Offset < f.Value + f.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)];
    }

    [Fact]
    public void EnumeratingADirectoryReadsItRatherThanReportingItEmpty()
    {
        // All three of these reported nothing, and reporting nothing is not a refusal: an empty answer
        // is what an empty directory gives, so every enumeration succeeded and found no files. Nothing
        // published offers a directory stream, but the call underneath one is published, and so is
        // everything else a stream needs.
        ElfObject obj = Read();
        Assert.Equal(["open", "malloc", "close"], CallsOf(obj, "opendir"));
        Assert.Equal(["getdents"], CallsOf(obj, "readdir"));
        Assert.Equal(["close", "free"], CallsOf(obj, "closedir"));

        // The translation into what the runtime reads is reached from a real reader, not a null one,
        // and lands in a place of its own per thread so two threads reading directories never share it.
        Assert.Equal(["readdir", "__sp_readdir64_buf"], CallsOf(obj, "readdir64"));

        // A directory is opened as one. Without that bit the platform opens an ordinary file and every
        // read of it fails; the other three are what the platform's own library asks for.
        ElfSymbol open = Assert.Single(obj.Symbols, s => s.Name == "opendir" && !s.IsUndefined);
        Assert.True(Holds(obj, open, [0xBE, 0x04, 0x00, 0x12, 0x00]), "opendir does not ask for a directory");
    }

    [Fact]
    public void ARefusalWhoseResultIsAWordSetsAllOfIt()
    {
        // An entry declared to return a pointer or a count is compared over all sixty-four bits. Setting
        // only the lower half leaves a large positive number, which the caller reads as success.
        ElfObject obj = Read();
        byte[] wide = [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF];   // mov rax, -1
        foreach (string name in new[] { "__getdelim", "syscall", "readlink", "pathconf", "sendfile64" })
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            Assert.True(Holds(obj, f, wide), $"{name} refuses in half a register");
        }
    }

    [Fact]
    public void TheErrorNumberIsTranslatedRatherThanHandedOver()
    {
        // The two sides number their errors differently and the numbers are what the runtime compares
        // against, so handing its own place over unchanged makes every comparison a comparison against
        // a different error. Each pairing below is read off its own side: the device's numbering from
        // the platform headers, cross-checked against the table its own C++ library carries; the
        // runtime's from the numbering it was compiled with.
        ElfObject obj = Read();
        ElfSymbol table = Assert.Single(obj.Symbols, s => s.Name == "__sp_error_numbers");
        byte[] data = obj.Sections[table.SectionIndex].Data;
        int at = (int)table.Value;
        Assert.Equal(256, (int)table.Size);

        (byte Device, byte Runtime, string Name)[] pairs =
        [
            (0, 0, "no error"),
            (11, 35, "a deadlock would occur"),      // the swap that catches a reader out
            (35, 11, "try again"),
            (22, 22, "not valid"),                   // the low ones that do agree
            (2, 2, "no such file"),
            (36, 115, "in progress"),
            (38, 88, "not a socket"),
            (45, 95, "not supported"),
            (60, 110, "timed out"),
            (62, 40, "too many links to follow"),
            (63, 36, "name too long"),
            (78, 38, "not implemented"),
            (85, 125, "cancelled"),
            (86, 84, "not a valid sequence"),
            (107, 131, "not recoverable"),
            (108, 130, "the owner died"),
        ];
        foreach ((byte device, byte runtime, string what) in pairs)
            Assert.True(data[at + device] == runtime,
                $"{what}: the device says {device}, which should read as {runtime}, not {data[at + device]}");

        // A number with no counterpart the runtime names reads as an error it has no name for, rather
        // than passing through to be taken for whatever unrelated error shares that number.
        foreach (byte platformOnly in new byte[] { 96, 99, 103, 160, 205 })
            Assert.True(data[at + platformOnly] == 132, $"{platformOnly} is passed through unchanged");

        // The place handed back is this object's own, per thread, not the device's.
        Assert.Equal(["__error", "__sp_error_numbers", "__sp_errno"], CallsOf(obj, "__errno_location"));
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "__errno_location" && !s.IsUndefined);
        Assert.NotEqual(0xE9, obj.Sections[f.SectionIndex].Data[(int)f.Value]);   // no longer a bare jump
    }

    [Fact]
    public void TheModuleDescribesItselfToWhateverWalksTheStack()
    {
        // Answering nothing here tells the unwinder there is no frame information anywhere in the
        // process, so an exception thrown through a frame that has to be unwound ends the module
        // instead of reaching its handler. The description cannot be read out of the image's own header
        // table: that table is in the code group, and the code group is mapped to execute without read.
        // Both addresses are reached instruction-relative from names the linker places instead.
        ElfObject obj = Read();
        Assert.Equal(
            ["_etext", "__executable_start", "__GNU_EH_FRAME_HDR", "__sp_module_name"],
            CallsOf(obj, "dl_iterate_phdr"));

        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "dl_iterate_phdr" && !s.IsUndefined);
        // The frame index is described as the header the unwinder looks for.
        Assert.True(Holds(obj, f, [0x50, 0xE5, 0x74, 0x64]), "no frame-index header is described");
        // The callback is reached through the register it arrived in.
        Assert.True(Holds(obj, f, [0x41, 0xFF, 0xD2]), "the callback is never called");
    }

    [Fact]
    public void TheTwoAnswersAboutProcessorsDescribeTheSameMachine()
    {
        // One said a single processor while the other said eight, so the runtime sized its thread pool
        // for a machine that was not the one it was running on. Both now come from the device.
        ElfObject obj = Read();
        Assert.Equal(["scePthreadSelf", "scePthreadGetaffinity"], CallsOf(obj, "sched_getaffinity"));
        Assert.Equal(["sceKernelGetCurrentCpu"], CallsOf(obj, "sched_getcpu"));

        // Counting the set is a count, not a constant: a fixed answer here contradicts the set above it.
        ElfSymbol count = Assert.Single(obj.Symbols, s => s.Name == "__sched_cpucount" && !s.IsUndefined);
        Assert.True(Holds(obj, count, [0xF3, 0x0F, 0xB8, 0xD2]), "__sched_cpucount does not count anything");
    }

    [Fact]
    public void ForwardsLeaveBaseNamesUndefinedForImport()
    {
        ElfObject obj = Read();
        var undefined = obj.Symbols.Where(s => s.IsUndefined && s.Name.Length > 0).Select(s => s.Name).ToHashSet();
        // The base names a module publishes stay undefined here, so the link imports them.
        foreach (string baseName in new[] { "open", "lseek", "pread", "fopen", "fstat", "stat", "__error", "scePthreadRename", "nanosleep", "ftruncate", "pwrite", "pwritev", "preadv" })
            Assert.Contains(baseName, undefined);
        // The ones nothing publishes are defined here instead, so the link never imports them.
        var defined = obj.Symbols.Where(s => !s.IsUndefined).Select(s => s.Name).ToHashSet();
        foreach (string local in new[] { "mmap", "readdir", "getrlimit", "setrlimit", "lstat", "pipe" })
        {
            Assert.Contains(local, defined);
            Assert.DoesNotContain(local, undefined);
        }
    }

    [Fact]
    public void EveryForwardHasACallRelocationPlusOneThreadLocalReference()
    {
        ElfObject obj = Read();
        var relocs = obj.Relocations.Values.SelectMany(list => list).ToList();

        // Two per-thread places, each loaded through one local-exec relocation: the entry readdir64
        // translates into, and the error number the runtime reads back.
        Assert.Equal(2, relocs.Count(r => r.Type == RelType.TpOff32));
        // Each variable holding an address carries one absolute fixup.
        int addresses = relocs.Count(r => r.Type == RelType.R64);
        Assert.Equal(3, addresses);
        // Everything else is a tail or forward call to a base name a module publishes.
        int calls = relocs.Count(r => r.Type == RelType.Plt32);
        Assert.Equal(relocs.Count - 2 - addresses, calls);
        Assert.True(calls >= 15, $"expected at least 15 forward calls, found {calls}");
    }

    // readdir returns a pointer into a structure whose fields sit at different offsets than the runtime
    // reads; the entry is translated into a per-thread block so concurrent directory reads never share it.
    [Fact]
    public void ReaddirTranslatesThroughAThreadLocalBuffer()
    {
        ElfObject obj = Read();

        ElfSection tbss = obj.Sections.Single(s => s.Name == ".tbss");
        Assert.True(tbss.IsTls, ".tbss must be thread-local");
        Assert.True(tbss.IsNoBits, ".tbss must be no-bits");
        Assert.True(tbss.Size >= 280, "the block must hold a full directory entry");

        ElfSymbol buf = obj.Symbols.Single(s => s.Name == "__sp_readdir64_buf");
        Assert.False(buf.IsUndefined);
        Assert.Equal(SymType.Tls, buf.Type);
        Assert.Equal(SymBind.Local, buf.Bind);

        // readdir64 is defined here, and so is the entry it translates.
        Assert.Contains(obj.Symbols, s => s.Name == "readdir64" && !s.IsUndefined && s.Type == SymType.Func);
        // Nothing publishes the entry it translates, so this object defines that too.
        Assert.Contains(obj.Symbols, s => s.Name == "readdir" && !s.IsUndefined);
    }

    // Each versioned stat variant translates a device status structure into the runtime's, so it is a real
    // body (call, test, field copies) rather than a bare tail call.
    [Fact]
    public void StatVariantsAreTranslatingBodies()
    {
        ElfObject obj = Read();
        foreach ((string name, string baseName) in new[] { ("__xstat64", "stat"), ("__lxstat64", "lstat"), ("__fxstat64", "fstat") })
        {
            ElfSymbol sym = obj.Symbols.Single(s => s.Name == name && !s.IsUndefined);
            Assert.True(sym.Size > 32, $"{name} should be a translating body, not a forward");
            Assert.Contains(obj.Symbols, s => s.Name == baseName);
        }
    }
}
