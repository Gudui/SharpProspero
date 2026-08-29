using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SharpProspero.Tests;

public class PayloadWriterTests
{
    // A minimal payload object: main() calls an outside function and returns. With withConstructor it also
    // carries a .init_array with one entry pointing at a constructor.
    private static ElfObject BuildPayloadObject(bool withConstructor = false)
    {
        var nullSec = new ElfSection { Name = "", Type = 0, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        byte[] code = [0xE8, 0, 0, 0, 0, 0xC3]; // call puts ; ret
        var text = new ElfSection { Name = ".text", Type = 1, Flags = 0x2 | 0x4, Address = 0, Size = 6, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = code };
        var syms = new List<ElfSymbol>
        {
            new() { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 },
            new() { Name = "main", Info = (1 << 4) | 2, Other = 0, SectionIndex = 1, Value = 0, Size = 6 },
            new() { Name = "puts", Info = (1 << 4) | 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 },
        };
        var relocs = new Dictionary<int, IReadOnlyList<ElfRelocation>>
        {
            [1] = new List<ElfRelocation> { new(Offset: 1, SymbolIndex: 2, Type: RelType.Plt32, Addend: -4) },
        };
        if (!withConstructor)
            return new ElfObject { Origin = "payload", Sections = [nullSec, text], Symbols = syms, Relocations = relocs };

        // .init_array holds one absolute pointer to a constructor (here, main itself).
        var initArray = new ElfSection { Name = ".init_array", Type = 1, Flags = 0x2 | 0x1, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 8, EntSize = 8, Data = new byte[8] };
        relocs[2] = new List<ElfRelocation> { new(Offset: 0, SymbolIndex: 1, Type: RelType.R64, Addend: 0) };
        return new ElfObject { Origin = "payload", Sections = [nullSec, text, initArray], Symbols = syms, Relocations = relocs };
    }

    private static byte[] BuildPayload(bool withConstructor = false)
    {
        var crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        var options = new LinkOptions();
        options.ExtraObjects.Add(crt);
        options.ExtraObjects.Add(BuildPayloadObject(withConstructor));
        LinkResolution res = Linker.Resolve(options);
        return PayloadWriter.Write(res, PayloadCrtEmitter.StartSymbol);
    }

    private static int RelativeCount(byte[] elf)
    {
        Assert.True(TryFindRela(elf, out int off, out int size));
        return size / 24;
    }

    [Fact]
    public void Writes_PositionIndependentExecutable()
    {
        byte[] elf = BuildPayload();

        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x10)));  // ET_DYN
        Assert.Equal(0x3E, BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x12))); // x86-64
        // The first load segment is based at VA zero and _start is its first symbol.
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x18))); // entry
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x38)));   // 3 LOAD + DYNAMIC
    }

    [Fact]
    public void HasThreeLoadSegments()
    {
        byte[] elf = BuildPayload();
        ulong phoff = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x20));
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x38));
        int loads = 0;
        var flags = new List<uint>();
        for (int i = 0; i < phnum; i++)
        {
            int p = (int)phoff + i * 0x38;
            if (BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p)) == 1) // PT_LOAD
            {
                loads++;
                flags.Add(BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p + 4)));
            }
        }
        Assert.Equal(3, loads);
        Assert.Equal([7u, 6u, 6u], flags); // corpus payload shape: RWE text, RW relro, RW data
    }

    [Fact]
    public void RelocationSectionHoldsRelativeFixupsThenDynamicImports()
    {
        byte[] elf = BuildPayload();
        Assert.True(TryFindRela(elf, out int relaOff, out int relaSize));
        Assert.True(relaSize >= 4 * 24); // import table (name + slot) plus the table bounds

        int count = relaSize / 24;
        for (int i = 0; i < count; i++)
        {
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(relaOff + i * 24 + 8));
            uint type = (uint)info;
            Assert.Contains(type, new uint[] { 6, 8 }); // GLOB_DAT or RELATIVE
            if (type == 8)
                Assert.Equal(0u, info >> 32);
            else
                Assert.NotEqual(0u, info >> 32);
        }
    }

    [Fact]
    public void RecordsTheOutsideReferenceName()
    {
        byte[] elf = BuildPayload();
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("puts\0")));
    }

    [Fact]
    public void StartCodeResolvesTheThreadedFlag()
    {
        // The start code sets the C runtime's __isthreaded, so the name it resolves is present.
        byte[] elf = BuildPayload();
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("__isthreaded\0")));
    }

    [Fact]
    public void ConstructorsGetBaseRelativeFixups()
    {
        // The dynamic table names the constructor range directly. Only the constructor pointer stored
        // in the array needs a base-relative record.
        int withoutCtor = RelativeCount(BuildPayload(withConstructor: false));
        int withCtor = RelativeCount(BuildPayload(withConstructor: true));
        Assert.Equal(withoutCtor + 1, withCtor);
    }

    [Fact]
    public void StartCodeCarriesTheThreadBootstrapPrimitives()
    {
        // The v0.8 start code installs its own TCB directly, preserves the host FS base, and primes
        // pthread_self before managed code runs.
        byte[] elf = BuildPayload();
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("pthread_self\0")));
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("__sp_saved_fsbase\0")));
    }

    [Fact]
    public void LocalExecThreadLocalReferenceIsBakedToAThreadPointerOffset()
    {
        // The template is .tdata (32) then .tbss (16), align 16, so the aligned size is 48. The variable
        // sits at template offset 16, so its thread-pointer offset is 16 - 48 = -32 (0xFFFFFFE0), written
        // straight into the instruction field with no relocation.
        byte[] elf = BuildTlsPayload(RelType.TpOff32);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x10))); // still ET_DYN
        Assert.True(ContainsBytes(elf, [0xE0, 0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void InitialExecThreadLocalOffsetRidesAGotSlotWithoutARelocation()
    {
        // Through the global-offset table the same -32 offset lands in a slot. It is a fixed offset, not
        // an address, so it takes no base-relative relocation: every relocation stays a relative fixup and
        // the offset is present in the file.
        byte[] elf = BuildTlsPayload(RelType.GotTpOff);
        Assert.True(TryFindRela(elf, out int relaOff, out int relaSize));
        for (int i = 0; i < relaSize / 24; i++)
            Assert.Equal(8u, BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(relaOff + i * 24 + 8)) & 0xFFFFFFFF);
        Assert.True(ContainsBytes(elf, [0xE0, 0xFF, 0xFF, 0xFF]));
    }

    // A payload object whose main reads a thread-local variable. The template is .tdata (32 bytes) then
    // .tbss (16 bytes), align 16; the variable is at template offset 16.
    private static ElfObject BuildTlsPayloadObject(uint relType)
    {
        var nullSec = new ElfSection { Name = "", Type = 0, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        byte[] code = [0xB8, 0, 0, 0, 0, 0xC3]; // mov eax, <tls offset> ; ret
        var text = new ElfSection { Name = ".text", Type = 1, Flags = 0x2 | 0x4, Address = 0, Size = 6, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = code };
        var tdata = new ElfSection { Name = ".tdata", Type = 1, Flags = 0x2 | 0x1 | 0x400, Address = 0, Size = 32, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[32] };
        var tbss = new ElfSection { Name = ".tbss", Type = 8, Flags = 0x2 | 0x1 | 0x400, Address = 0, Size = 16, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = [] };
        var syms = new List<ElfSymbol>
        {
            new() { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 },
            new() { Name = "main", Info = (1 << 4) | 2, Other = 0, SectionIndex = 1, Value = 0, Size = 6 },
            new() { Name = "tlsVar", Info = (1 << 4) | 6, Other = 0, SectionIndex = 2, Value = 16, Size = 0 },
        };
        var relocs = new Dictionary<int, IReadOnlyList<ElfRelocation>>
        {
            [1] = new List<ElfRelocation> { new(Offset: 1, SymbolIndex: 2, Type: relType, Addend: 0) },
        };
        return new ElfObject { Origin = "tls-payload", Sections = [nullSec, text, tdata, tbss], Symbols = syms, Relocations = relocs };
    }

    private static byte[] BuildTlsPayload(uint relType)
    {
        var crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        var options = new LinkOptions();
        options.ExtraObjects.Add(crt);
        options.ExtraObjects.Add(BuildTlsPayloadObject(relType));
        LinkResolution res = Linker.Resolve(options);
        return PayloadWriter.Write(res, PayloadCrtEmitter.StartSymbol);
    }

    [Fact]
    public void GotReferenceToADefinedDataSymbolResolvesToItsAddressNotTheBase()
    {
        // A GOT read of a symbol defined in the same object must fill the slot with the symbol's real
        // address. Moving the symbol 64 bytes within .data moves that slot's base-relative addend by 64,
        // so the two payloads' relative-fixup addends differ. With the address resolved to the load base
        // instead (the defect), both would hold the same zero addend and the sets would match.
        List<ulong> atZero = RelativeAddends(BuildGotDefinedDataPayload(0));
        List<ulong> at64 = RelativeAddends(BuildGotDefinedDataPayload(64));
        Assert.NotEqual(atZero, at64);
    }

    private static byte[] BuildGotDefinedDataPayload(ulong symbolValue)
    {
        var nullSec = new ElfSection { Name = "", Type = 0, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        byte[] code = [0x48, 0x8B, 0x05, 0, 0, 0, 0, 0xC3]; // mov rax,[rip+g@GOTPCREL] ; ret
        var text = new ElfSection { Name = ".text", Type = 1, Flags = 0x2 | 0x4, Address = 0, Size = 8, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = code };
        var data = new ElfSection { Name = ".data", Type = 1, Flags = 0x2 | 0x1, Address = 0, Size = 128, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = new byte[128] };
        var syms = new List<ElfSymbol>
        {
            new() { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 },
            new() { Name = "main", Info = (1 << 4) | 2, Other = 0, SectionIndex = 1, Value = 0, Size = 8 },
            new() { Name = "g", Info = (1 << 4) | 1, Other = 0, SectionIndex = 2, Value = symbolValue, Size = 4 },
        };
        var relocs = new Dictionary<int, IReadOnlyList<ElfRelocation>>
        {
            [1] = new List<ElfRelocation> { new(Offset: 3, SymbolIndex: 2, Type: RelType.GotPcRelX, Addend: -4) },
        };
        var obj = new ElfObject { Origin = "got-payload", Sections = [nullSec, text, data], Symbols = syms, Relocations = relocs };
        var crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        var options = new LinkOptions();
        options.ExtraObjects.Add(crt);
        options.ExtraObjects.Add(obj);
        LinkResolution res = Linker.Resolve(options);
        return PayloadWriter.Write(res, PayloadCrtEmitter.StartSymbol);
    }

    private static List<ulong> RelativeAddends(byte[] elf)
    {
        Assert.True(TryFindRela(elf, out int off, out int size));
        var addends = new List<ulong>();
        for (int i = 0; i < size / 24; i++)
            addends.Add(BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(off + i * 24 + 16)));
        addends.Sort();
        return addends;
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    private static bool TryFindRela(byte[] elf, out int offset, out int size)
    {
        offset = 0; size = 0;
        ulong shoff = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x28));
        int shnum = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x3C));
        for (int i = 0; i < shnum; i++)
        {
            int s = (int)shoff + i * 64;
            if (BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(s + 4)) == 4) // SHT_RELA
            {
                offset = (int)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(s + 24));
                size = (int)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(s + 32));
                return true;
            }
        }
        return false;
    }
}
