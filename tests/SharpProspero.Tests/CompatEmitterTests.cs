// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System;
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
        // Each question is a compare followed by a short jump, and the jump has to reach the answer
        // that belongs to it. One counted by hand sent the memory-size question to the processor-count
        // answer, so the module reported 128 KB of memory and the collector refused to start - which
        // looks exactly like the question being refused outright.
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
        ];

        foreach ((byte question, byte[] answer) in expected)
        {
            // cmp edi, <question>  =  83 FF xx, then 74 <rel8>
            int cmp = -1;
            for (int i = at; i < at + 0x20; i++)
                if (code[i] == 0x83 && code[i + 1] == 0xFF && code[i + 2] == question)
                { cmp = i; break; }
            Assert.True(cmp >= 0, $"no question for {question}");
            Assert.Equal(0x74, code[cmp + 3]);

            int target = cmp + 5 + (sbyte)code[cmp + 4];
            Assert.Equal(answer, code[target..(target + answer.Length)]);
        }

        // The memory answer must reach the pool, not report a number of its own.
        Assert.Contains(obj.Symbols, s => s.Name == "sceKernelConfiguredFlexibleMemorySize" && s.IsUndefined);
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
    public void MmapReachesTheFlexibleMemoryPoolRatherThanReportingFailure()
    {
        // The runtime reserves its heap through mmap and gives up when it fails, so this cannot be a
        // stub that reports failure: the module then loads, runs, and leaves main with a non-zero
        // status before a line of application code. It has to reach the pool this platform maps from.
        ElfObject obj = Read();

        ElfSymbol mmap = Assert.Single(obj.Symbols, s => s.Name == "mmap" && !s.IsUndefined);
        Assert.Contains(obj.Symbols, s => s.Name == "sceKernelMapFlexibleMemory" && s.IsUndefined);

        // Asking for no access reserves room; asking for access takes memory from the pool.
        int section = mmap.SectionIndex;
        var calls = obj.Relocations[section]
            .Where(r => r.Offset > mmap.Value && r.Offset < mmap.Value + mmap.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .ToList();
        Assert.Equal(["sceKernelReserveVirtualRange", "sceKernelMapFlexibleMemory"], calls);

        // A reserved range is first written to as a protection change, so that has to take memory too
        // rather than pass the refusal on.
        ElfSymbol mprotect = Assert.Single(obj.Symbols, s => s.Name == "mprotect" && !s.IsUndefined);
        var protCalls = obj.Relocations[mprotect.SectionIndex]
            .Where(r => r.Offset > mprotect.Value && r.Offset < mprotect.Value + mprotect.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .ToList();
        Assert.Equal(["sceKernelMprotect", "sceKernelMapFlexibleMemory"], protCalls);

        // Not the shape that answers -1 and returns: mov rax, -1 / ret.
        byte[] code = obj.Sections[section].Data;
        byte[] refuse = [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3];
        Assert.NotEqual(refuse, code[(int)mmap.Value..((int)mmap.Value + refuse.Length)]);

        // Both round the length to whole pages before asking. The ordinary call answers a request for a
        // few hundred bytes with a page; the platform's refuses a length that is not a multiple of one,
        // so without the rounding every small request fails.
        byte[] roundLength = [0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF];    // and rsi, -16384
        foreach (ElfSymbol f in (ElfSymbol[])[mmap, mprotect])
            Assert.True(Holds(obj, f, roundLength), $"{f.Name} does not round its length to whole pages");
        // The protection change also moves back to the start of the page it was given an address inside.
        byte[] roundAddress = [0x48, 0x81, 0xE7, 0x00, 0xC0, 0xFF, 0xFF];   // and rdi, -16384
        Assert.True(Holds(obj, mprotect, roundAddress), "mprotect does not move back to a page boundary");
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

        // readdir64 loads its per-thread buffer address through a single local-exec relocation.
        Assert.Equal(1, relocs.Count(r => r.Type == RelType.TpOff32));
        // Each variable holding an address carries one absolute fixup.
        int addresses = relocs.Count(r => r.Type == RelType.R64);
        Assert.Equal(3, addresses);
        // Everything else is a tail or forward call to a base name a module publishes.
        int calls = relocs.Count(r => r.Type == RelType.Plt32);
        Assert.Equal(relocs.Count - 1 - addresses, calls);
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
