// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

// The compat object must parse as a relocatable object the linker understands, define exactly the
// runtime-support names, and leave the device-provided base names undefined so the linker imports them.
public sealed class CompatEmitterTests
{
    private static ElfObject Read() => ElfObjectReader.Read(CompatEmitter.BuildObject(), "compat.o");

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
        // The full compat surface the runtime archives need.
        Assert.Equal(66, defined.Count);
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
        foreach (string baseName in new[] { "open", "lseek", "mmap", "pread", "fopen", "readdir", "getrlimit", "fstat", "stat", "__error", "scePthreadRename", "nanosleep", "pipe", "ftruncate", "pwrite", "pwritev", "preadv", "setrlimit", "lstat" })
            Assert.Contains(baseName, undefined);
    }

    [Fact]
    public void EveryForwardHasACallRelocationPlusOneThreadLocalReference()
    {
        ElfObject obj = Read();
        var relocs = obj.Relocations.Values.SelectMany(list => list).ToList();

        // readdir64 loads its per-thread buffer address through a single local-exec relocation.
        Assert.Equal(1, relocs.Count(r => r.Type == RelType.TpOff32));
        // Every other relocation is a tail/forward call to a device-provided base name.
        int calls = relocs.Count(r => r.Type == RelType.Plt32);
        Assert.Equal(relocs.Count - 1, calls);
        Assert.True(calls >= 19, $"expected at least 19 forward calls, found {calls}");
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

        // readdir64 is defined and still leaves the device readdir undefined for import.
        Assert.Contains(obj.Symbols, s => s.Name == "readdir64" && !s.IsUndefined && s.Type == SymType.Func);
        Assert.Contains(obj.Symbols, s => s.Name == "readdir" && s.IsUndefined);
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
            Assert.Contains(obj.Symbols, s => s.Name == baseName && s.IsUndefined);
        }
    }
}
