// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
            (83, [0x55, 0x48, 0x89, 0xE5]),         // processors: the routine that asks the platform
            (84, [0x55, 0x48, 0x89, 0xE5]),
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

        // Every answer that can be asked of the platform is asked of it. The processor count comes from
        // the set of processors the calling thread may run on, which is where the runtime's own count
        // comes from as well, so the two describe one machine. The two memory answers reach the pool,
        // and they are different questions: how much this module may have, and how much is still free.
        // None of the three may answer -1. The caller asking how much memory there is compares the
        // answer against -1 and refuses to start the runtime at all when it matches - which surfaces
        // only as the module reporting a non-zero result from its entry, with nothing to say why. The
        // caller asking how much is free does not check at all and reads -1 as sixteen million million
        // pages.
        Assert.Equal(
            [
                "scePthreadSelf",
                "scePthreadGetaffinity",
                "sceKernelConfiguredFlexibleMemorySize",
                "sceKernelAvailableFlexibleMemorySize",
            ],
            CallsOf(obj, "sysconf"));
        // The figure used when neither can answer is a fixed cautious one, deliberately not the
        // machine's own memory size: the pages the collector gets come from the pool this module maps
        // out of, a small fraction of what the machine has, so the larger figure would size every
        // decision above it against memory the module can never reach.
        Assert.DoesNotContain("sceKernelGetDirectMemorySize", CallsOf(obj, "sysconf"));
        ElfSymbol sysconfSym = Assert.Single(obj.Symbols, s => s.Name == "sysconf" && !s.IsUndefined);
        Assert.False(Holds(obj, sysconfSym, [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xC9]),
            "sysconf still refuses a memory question, which stops the runtime from starting");
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
        // The reason is read through the translating thunk, not the device's own place, so what comes
        // back is the number the caller was compiled to compare against.
        Assert.Equal(["nanosleep", "clock_gettime", "nanosleep", "__errno_location"], calls);

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
        // is what puts memory there. The protection change cannot pick its call by trying one and
        // watching it fail: a protection change over a held-but-empty range succeeds and attaches
        // nothing. So the range is asked about first, and its answer picks the call.
        ElfObject obj = Read();
        ElfSymbol mmap = Assert.Single(obj.Symbols, s => s.Name == "mmap" && !s.IsUndefined);
        ElfSymbol mprotect = Assert.Single(obj.Symbols, s => s.Name == "mprotect" && !s.IsUndefined);

        // The platform calls each entry point makes, in the order it makes them. The two names this
        // object defines itself - the numbering and the routine that records a reason - are left out
        // here and pinned separately below.
        List<string> PlatformCallsOf(ElfSymbol f) => obj.Relocations[f.SectionIndex]
            .Where(r => r.Offset > f.Value && r.Offset < f.Value + f.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .Where(n => !n.StartsWith("__sp_", StringComparison.Ordinal))
            .ToList();

        List<string> mmapCalls = PlatformCallsOf(mmap);
        // Two calls, chosen by whether any access was asked for. None means addresses are wanted and
        // nothing behind them, which is one call and covers both holding a fresh range and giving the
        // memory behind one back - mapping room over a range pinned releases what was there and leaves
        // the addresses held. Access asked for means memory out of the flexible budget.
        Assert.Equal(["sceKernelReserveVirtualRange", "sceKernelMapFlexibleMemory"], mmapCalls);
        // The pool answers neither: it hands out room carved from memory already put into it, and it
        // starts empty, so the collector's first reservation - larger than anything the module had
        // asked for - was refused before the runtime could start.
        Assert.DoesNotContain("sceKernelMemoryPoolReserve", mmapCalls);

        List<string> protCalls = PlatformCallsOf(mprotect);
        Assert.Equal([
            "sceKernelVirtualQuery",          // what is behind this address?
            "sceKernelMapFlexibleMemory",     // nothing yet: put memory behind it, pinned
            "sceKernelMprotect",              // anything else: protection only, nothing moves
        ], protCalls);

        // Neither of these leaves a reason where the runtime reads it on its own: they answer one coded
        // number and never reach a system call on the paths that refuse. So both take the platform's own
        // number out of that code, put it through the same numbering every other error goes through, and
        // record it - otherwise the caller reads whatever the last call to anything left behind.
        foreach (ElfSymbol f in (ElfSymbol[])[mmap, mprotect])
        {
            List<string> named = obj.Relocations[f.SectionIndex]
                .Where(r => r.Offset > f.Value && r.Offset < f.Value + f.Size)
                .Select(r => obj.Symbols[(int)r.SymbolIndex].Name).ToList();
            Assert.Contains("__sp_error_numbers", named);
            Assert.Contains("__sp_set_errno", named);
        }

        // The mapping call reads two registers past the ones it takes and refuses the request outright
        // when the first of them holds anything above three. Reached from the ordinary call that
        // register still holds the caller's file - which is -1 where no file backs the mapping - so
        // every request for memory was turned away before it arrived, and nothing said so. Both places
        // that ask for memory clear it first.
        foreach (ElfSymbol f in (ElfSymbol[])[mmap, mprotect])
            Assert.True(Holds(obj, f, [0x45, 0x31, 0xC0]),          // xor r8d, r8d
                $"{f.Name} does not clear the register the mapping call reads past its arguments");
        // The second of the two is read whenever no address was named, which is every request that lets
        // the system choose, and one range of values moves the mapping into a region kept for the
        // system. Only the call that can be reached without an address has to clear it.
        Assert.True(Holds(obj, mmap, [0x45, 0x31, 0xC9]),           // xor r9d, r9d
            "mmap does not clear the second register the mapping call reads past its arguments");

        // Two things have to agree before memory is placed over a range, because placing it replaces
        // whatever was there: a range whose addresses are merely held carries no protection at all, and
        // is none of the kinds that already have something behind them. Either one alone would call a
        // live range empty - this module's own data, say, whose protection the runtime changes while it
        // starts - and placing memory over that loses what it held.
        Assert.True(Holds(obj, mprotect, [0x8B, 0x45, 0xB8]),       // mov eax, [rbp-72]
            "mprotect does not read the protection out of the report");
        Assert.True(Holds(obj, mprotect, [0x0F, 0xB6, 0x45, 0xC0]), // movzbl [rbp-64], eax
            "mprotect does not read what kind of range the report describes");
        Assert.True(Holds(obj, mprotect, [0xA8, 0x0F]),             // test al, 15
            "mprotect does not rule out every kind of range that is already backed");

        // A range nothing could report on must never have memory placed over it either. Asking for the
        // protection change is the safe answer: the platform refuses it if the range is not real, and
        // nothing is lost either way. The report is asked for before anything else happens.
        Assert.Equal("sceKernelVirtualQuery", protCalls[0]);

        // Whole pages are enough for both. Rounding to the larger unit the pool is carved in was only
        // ever needed because memory came from the pool, and it made the smallest request the collector
        // makes four times larger than it asked for.
        byte[] roundToBlock = [0x48, 0x81, 0xE6, 0x00, 0x00, 0xFF, 0xFF];   // and rsi, -65536
        foreach (ElfSymbol f in (ElfSymbol[])[mmap, mprotect])
            Assert.False(Holds(obj, f, roundToBlock), $"{f.Name} still rounds to whole blocks");
        Assert.True(Holds(obj, mmap, [0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF]),
            "mmap does not round the length to whole pages");
        Assert.True(Holds(obj, mprotect, [0x48, 0x81, 0xE6, 0x00, 0xC0, 0xFF, 0xFF]),
            "mprotect no longer rounds to whole pages for the protection change");
        Assert.True(Holds(obj, mprotect, [0x48, 0x81, 0xE7, 0x00, 0xC0, 0xFF, 0xFF]),
            "mprotect does not move back to a page boundary");
    }

    [Fact]
    public void ThreadAttributesStayInsideTheFourBytesTheCallerReserved()
    {
        // An attribute object is a single word on this platform and four bytes to the runtime, so it
        // is the one object of its kind where the platform writes MORE than was reserved. Letting the
        // platform fill one put the upper half of an address it owns onto whatever the compiler had
        // placed next, which was the value guarding the return address; the routine then ran to the
        // end and died reporting a damaged frame, several calls away from the write that did it.
        //
        // So an attribute object never reaches the platform. It holds the setting itself, written four
        // bytes at a time, and the routines that consume it build the platform's own on their frame.
        ElfObject obj = Read();

        foreach (string name in (string[])["pthread_condattr_init", "pthread_mutexattr_init"])
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            // mov dword [rdi], setting - four bytes wide, not eight
            Assert.True(Holds(obj, f, [0xC7, 0x07]), $"{name} does not write the setting as four bytes");
            Assert.False(Holds(obj, f, [0x48, 0xC7, 0x07]), $"{name} writes eight bytes over four");
        }
        foreach (string name in (string[])["pthread_condattr_setclock", "pthread_mutexattr_settype"])
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            Assert.True(Holds(obj, f, [0x89, 0x37]), $"{name} does not keep the setting as four bytes");
            Assert.False(Holds(obj, f, [0x48, 0x89, 0x37]), $"{name} writes eight bytes over four");
        }

        // None of the four hands the caller's object to the platform, which is the whole point.
        foreach (string name in (string[])["pthread_condattr_init", "pthread_condattr_setclock",
                                           "pthread_condattr_destroy", "pthread_mutexattr_init",
                                           "pthread_mutexattr_settype", "pthread_mutexattr_destroy"])
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            Assert.DoesNotContain(obj.Relocations[f.SectionIndex],
                r => r.Offset >= f.Value && r.Offset < f.Value + f.Size);
        }

        // The two that consume the attributes build the platform's own, apply the setting, use it and
        // release it - so the setting still arrives without the caller's four bytes ever holding one.
        ElfSymbol cond = Assert.Single(obj.Symbols, s => s.Name == "pthread_cond_init" && !s.IsUndefined);
        Assert.Equal([
            "scePthreadCondattrInit",
            "scePthreadCondattrSetclock",   // the clock, translated to this platform's numbering
            "scePthreadCondInit",
            "scePthreadCondattrDestroy",
        ], obj.Relocations[cond.SectionIndex]
            .Where(r => r.Offset > cond.Value && r.Offset < cond.Value + cond.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .Where(n => n.StartsWith("scePthread")).ToList());

        ElfSymbol mutex = Assert.Single(obj.Symbols, s => s.Name == "pthread_mutex_init" && !s.IsUndefined);
        Assert.Equal([
            "scePthreadMutexattrInit",
            "scePthreadMutexattrSettype",
            "scePthreadMutexInit",
            "scePthreadMutexattrDestroy",
        ], obj.Relocations[mutex.SectionIndex]
            .Where(r => r.Offset > mutex.Value && r.Offset < mutex.Value + mutex.Size)
            .Select(r => obj.Symbols[(int)r.SymbolIndex].Name)
            .Where(n => n.StartsWith("scePthread")).ToList());

        // Both read the caller's setting four bytes at a time, and both name the object they build
        // rather than leaving the platform to read an address out of a register it was not given.
        foreach (ElfSymbol f in (ElfSymbol[])[cond, mutex])
        {
            Assert.True(Holds(obj, f, [0x44, 0x8B, 0x26]), $"{f.Name} does not read the setting as four bytes");
            Assert.True(Holds(obj, f, [0x48, 0x8D, 0x7D, 0xE8]), $"{f.Name} does not build attributes on its own frame");
        }

        // The kinds of mutex run the other way round here, so one is the other subtracted from three.
        // Passing the caller's numbering straight through would have asked for a re-entrant mutex
        // where an ordinary one was wanted, and a checked one where a re-entrant one was.
        Assert.True(Holds(obj, mutex, [0xBE, 0x03, 0x00, 0x00, 0x00]), "mutex kinds are not mirrored");
        Assert.True(Holds(obj, mutex, [0x44, 0x29, 0xE6]), "mutex kinds are not mirrored");
    }

    [Fact]
    public void ARefusalSaysWhyItRefused()
    {
        // A refusal that only answers -1 inherits whatever number the last call to anything left where
        // the runtime reads its errors. That matters because callers do not merely test the result:
        // many wrap the call in a loop that retries for as long as that number says the call was
        // interrupted. A stale interrupted-number therefore turns a refusal into a loop that retries a
        // call whose answer can never change, with no system call in it to slow it down - a module
        // that starts, stops responding, and burns a processor doing it, which is indistinguishable
        // from every other kind of freeze. Every refusal now says there is no such routine.
        ElfObject obj = Read();

        ElfSymbol setter = Assert.Single(obj.Symbols, s => s.Name == "__sp_set_errno" && !s.IsUndefined);
        Assert.Equal(["__error", "__sp_errno_written"], CallsOf(obj, "__sp_set_errno"));
        // It writes the runtime's own word, and clears the platform's place on the way. Without the
        // clearing, a failure the platform recorded earlier that nobody read would be taken as news on
        // the next read and translated over the number just written.
        Assert.True(Holds(obj, setter, [0x89, 0x1A]), "the number is not written into the runtime's word");
        Assert.True(Holds(obj, setter, [0xC7, 0x00, 0x00, 0x00, 0x00, 0x00]),
            "the platform's place is left holding something the next read would take as news");

        // Both shapes of refusal go through it, and both still answer -1 afterwards.
        foreach (string name in (string[])["poll", "ioctl", "chdir", "link", "symlink", "getrlimit"])
        {
            ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == name && !s.IsUndefined);
            Assert.Contains("__sp_set_errno", CallsOf(obj, name));
            Assert.True(Holds(obj, f, [0xBF, 38, 0x00, 0x00, 0x00]),
                $"{name} does not say there is no such routine");
        }
        // The wide refusal fills the whole register, so a caller comparing all 64 bits of a pointer or
        // a count does not read the refusal as a large positive number and take it for a success.
        ElfSymbol wide = Assert.Single(obj.Symbols, s => s.Name == "__getdelim" && !s.IsUndefined);
        Assert.True(Holds(obj, wide, [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF]),
            "the wide refusal no longer fills the whole register");
        Assert.Contains("__sp_set_errno", CallsOf(obj, "__getdelim"));

        // An entry that reports failure by answering nothing needs the same reason behind it, and one
        // of those sits inside a retry loop like the rest.
        foreach (string name in (string[])["realpath", "mkdtemp"])
            Assert.Contains("__sp_set_errno", CallsOf(obj, name));
        // Asking for a name that is not set is an ordinary answer rather than a failure, and the two
        // that load code report why through a call of their own, so those leave the number alone.
        foreach (string name in (string[])["getenv", "dlopen", "dlsym"])
            Assert.DoesNotContain("__sp_set_errno", CallsOf(obj, name));
    }

    [Fact]
    public void TheMessageForAnErrorComesBackAsTextRatherThanANumber()
    {
        // Both sides publish this name and disagree on what it answers. The runtime hands the answer
        // straight on as text; this platform answers zero when it filled the buffer and an error
        // number when it could not, which is what it does for any buffer of twenty-two bytes or fewer.
        // So a caller with a small buffer was handed the number thirty-four and read it as an address.
        // Nothing caught it because the one caller in the runtime always passes a thousand bytes,
        // which always succeeds and always answers zero - and zero reads as "no message", which is
        // handled. The number also has to go back the way it came, or the message describes a
        // different error than the one asked about.
        ElfObject obj = Read();
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "strerror_r" && !s.IsUndefined);
        Assert.Equal(["__sp_device_strerror_r", "__sp_platform_error_numbers"], CallsOf(obj, "strerror_r"));
        Assert.NotEqual(0xE9, obj.Sections[f.SectionIndex].Data[(int)f.Value]);   // no longer a bare jump
        Assert.True(Holds(obj, f, [0x48, 0x89, 0xD8]), "the buffer is not what comes back");
        Assert.True(Holds(obj, f, [0xC6, 0x03, 0x00]), "a buffer it could not fill is left as it was");

        // And the numbering read the other way round, so a number going back to the platform means
        // there what it meant here. The two that trade places are the ones that catch a reader out.
        ElfSymbol table = Assert.Single(obj.Symbols, s => s.Name == "__sp_platform_error_numbers");
        byte[] data = obj.Sections[table.SectionIndex].Data;
        int at = (int)table.Value;
        Assert.Equal(256, (int)table.Size);
        foreach ((byte runtime, byte device) in (ValueTuple<byte, byte>[])
                 [(0, 0), (35, 11), (11, 35), (22, 22), (110, 60), (38, 78), (95, 45)])
            Assert.True(data[at + runtime] == device,
                $"the runtime's {runtime} should read as the platform's {device}, not {data[at + runtime]}");
    }

    [Fact]
    public void AHintAboutMemoryIsTranslatedAndOneAboutThreadsIsAnswered()
    {
        ElfObject obj = Read();

        // The two sides agree on the first five hints about a range of memory and part company after.
        // The one the collector leans on - done with these pages, their contents are junk, take them
        // back - is the eighth number to the runtime and the fifth here, and the eighth here means
        // keep these pages out of the record written when the module dies. Passing it through did both
        // wrong things at once: the collector was told its memory had been taken back when it had not,
        // so a heap that should shrink after every collection only grew; and every range it asked
        // about was struck from the record that is the one place answers to questions like this come
        // from. So the hint is translated, and one with no counterpart is answered rather than passed.
        ElfSymbol adv = Assert.Single(obj.Symbols, s => s.Name == "madvise" && !s.IsUndefined);
        Assert.Equal(["__sp_memory_advice", "__sp_device_madvise"], CallsOf(obj, "madvise"));
        Assert.True(Holds(obj, adv, [0x83, 0xFA, 9]), "madvise does not bound the hint before indexing");
        Assert.True(Holds(obj, adv, [0x80, 0xFA, 0xFF]), "madvise passes on a hint with no counterpart");

        // And the numbering itself: the first five carry over unchanged, the eighth becomes the fifth,
        // and everything else is marked as having no counterpart here.
        ElfSymbol table = Assert.Single(obj.Symbols, s => s.Name == "__sp_memory_advice");
        byte[] hints = obj.Sections[table.SectionIndex].Data
            .AsSpan((int)table.Value, (int)table.Size).ToArray();
        Assert.Equal([0, 1, 2, 3, 4, 0xFF, 0xFF, 0xFF, 5], hints);

        // The runtime asks for a thread's own number through the general entry, keeps what it is told,
        // and files each thread under it. Refusing gave every thread the same number, so a later
        // lookup answered with whichever thread was filed first - and what is done with that answer is
        // to record where a thread was interrupted and to walk its memory for what is still in use.
        ElfSymbol sc = Assert.Single(obj.Symbols, s => s.Name == "syscall" && !s.IsUndefined);
        Assert.Equal(["__sp_set_errno", "scePthreadGetthreadid"], CallsOf(obj, "syscall"));
        Assert.True(Holds(obj, sc, [0x81, 0xFF, 186, 0x00, 0x00, 0x00]),
            "syscall does not single out the request for a thread's own number");
        // Everything else it is asked for stays refused. One of those is how the runtime asks whether
        // the machine offers a cheaper way to make one thread's writes visible to the rest; a refusal
        // is what makes it choose the way that works here.
        Assert.True(Holds(obj, sc, [0x48, 0xC7, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF]),
            "syscall no longer refuses the rest");
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
        // The system queries are not here. Each is published by a module on the console, so defining
        // one would shadow the real entry point with a routine that only reports failure, and the calls
        // that read the system version and the settings could never work.
        foreach (string name in (string[])["sceKernelGetOpenPsId", "sceKernelGetProsperoSystemSwVersion",
                                           "sceKernelGetAllowedSdkVersionOnSystem", "sysctlbyname"])
            Assert.DoesNotContain(name, defined);
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

        // The place handed back is this object's own, one word per thread, not the platform's.
        // Translating the platform's where it lies cannot work: the runtime does not only read that
        // place, it writes numbers of its own into it at fourteen points in what gets linked, and a
        // number the runtime wrote is already counted the runtime's way. Translating it again moves
        // it - two of the codes trade places, so each becomes the other - and one the runtime writes
        // has no counterpart here at all and would come back as the error with no name.
        //
        // What separates a number the platform wrote from one the runtime wrote is that the platform
        // writes only to report a failure, and no failure is numbered zero. So a number sitting there
        // is news: it is read, translated into the runtime's word, and cleared. Clearing is what lets
        // the same failure twice running read as two failures rather than one, and it is why saving
        // the number, calling something, and putting it back still works - the read happens before the
        // write, so a failure in between is taken first and then written over.
        Assert.Equal(["__error", "__sp_error_numbers", "__sp_errno_written"],
            CallsOf(obj, "__errno_location"));
        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "__errno_location" && !s.IsUndefined);
        Assert.NotEqual(0xE9, obj.Sections[f.SectionIndex].Data[(int)f.Value]);   // no longer a bare jump
        Assert.True(Holds(obj, f, [0x85, 0xC9]), "nothing distinguishes news from nothing to report");
        Assert.True(Holds(obj, f, [0xC7, 0x00, 0x00, 0x00, 0x00, 0x00]),
            "the platform's place is not cleared, so one failure reads as many");
        Assert.True(Holds(obj, f, [0x89, 0x0B]), "the number is not written into the runtime's own word");
        Assert.True(Holds(obj, f, [0x48, 0x89, 0xD8]), "the runtime's own word is not what is handed back");
        // And it never writes the translated number back into the platform's place, which is what made
        // the next read translate a number that had already been translated once.
        Assert.False(Holds(obj, f, [0x89, 0x08]),
            "the translated number is still written back where the platform keeps its own");
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
            ["_etext", "__executable_start", "__GNU_EH_FRAME_HDR", "__GNU_EH_FRAME_HDR_END",
             "__sp_module_name"],
            CallsOf(obj, "dl_iterate_phdr"));

        ElfSymbol f = Assert.Single(obj.Symbols, s => s.Name == "dl_iterate_phdr" && !s.IsUndefined);
        // The frame index is described as the header the unwinder looks for.
        Assert.True(Holds(obj, f, [0x50, 0xE5, 0x74, 0x64]), "no frame-index header is described");
        // The callback is reached through the register it arrived in.
        Assert.True(Holds(obj, f, [0x41, 0xFF, 0xD2]), "the callback is never called");

        // And it is described with a length, not only an address. The reader takes the far end of the
        // index from the length in this same header - the index records no size of its own - so a
        // length left at zero describes a range whose two ends meet, and that is refused before a byte
        // is read. The refusal is silent: nothing records where the frame information was, and every
        // later walk up the stack finds no method for the address it stands on and ends the module.
        // Both lengths are written even though only the one in memory is read, so the header agrees
        // with itself. Nothing else in the build would notice either going back to zero.
        Assert.True(Holds(obj, f, [0x48, 0x89, 0x44, 0x24, 56 + 40]),
            "the frame index is described without a length in memory");
        Assert.True(Holds(obj, f, [0x48, 0x89, 0x44, 0x24, 56 + 32]),
            "the frame index is described without a stored length");
        // The length is the distance between the two names the linker places, measured from the image.
        Assert.True(Holds(obj, f, [0x48, 0x2B, 0x44, 0x24, 56 + 16]),
            "the length is not measured from where the index starts");
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
        // Naming a thread goes to the entry that answers the way the caller counts, not the one that
        // answers a coded number the caller would read as a large positive result.
        foreach (string baseName in new[] { "open", "lseek", "pread", "fopen", "fstat", "stat", "__error", "pthread_rename_np", "nanosleep", "ftruncate", "pwrite", "pwritev", "preadv" })
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

        // Two per-thread places, reached through three local-exec relocations: the entry readdir64
        // translates into, and the record of what was last written to the error number - which is read
        // where the number is translated on the way out, and written again where a refusal puts a
        // number there itself, since a number written without that record is translated a second time.
        Assert.Equal(3, relocs.Count(r => r.Type == RelType.TpOff32));
        // Each variable holding an address carries one absolute fixup.
        int addresses = relocs.Count(r => r.Type == RelType.R64);
        Assert.Equal(3, addresses);
        // Everything else is a tail or forward call to a base name a module publishes.
        int calls = relocs.Count(r => r.Type == RelType.Plt32);
        Assert.Equal(relocs.Count - 3 - addresses, calls);
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
