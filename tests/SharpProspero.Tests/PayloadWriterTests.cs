using SharpProspero.Link;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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
        Assert.NotEqual(0ul, BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x18))); // entry
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x38)));   // three load segments
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
        Assert.Contains(4u | 1u, flags); // R+X text
        Assert.Contains(4u, flags);      // R rodata
        Assert.Contains(4u | 2u, flags); // R+W data
    }

    [Fact]
    public void RelocationSectionHoldsOnlyRelativeFixups()
    {
        byte[] elf = BuildPayload();
        Assert.True(TryFindRela(elf, out int relaOff, out int relaSize));
        Assert.True(relaSize >= 4 * 24); // import table (name + slot) plus the table bounds

        int count = relaSize / 24;
        for (int i = 0; i < count; i++)
        {
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(relaOff + i * 24 + 8));
            Assert.Equal(8u, info & 0xFFFFFFFF);  // R_X86_64_RELATIVE
            Assert.Equal(0u, info >> 32);          // no symbol
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
        // A payload with one .init_array entry adds three base-relative records over one without: the
        // constructor pointer itself, and the two header pointers naming the constructor range.
        int withoutCtor = RelativeCount(BuildPayload(withConstructor: false));
        int withCtor = RelativeCount(BuildPayload(withConstructor: true));
        Assert.Equal(withoutCtor + 3, withCtor);
    }

    [Fact]
    public void StartCodeResolvesTheThreadPointerPrimitives()
    {
        // The start code allocates and installs the thread-local block, so the allocator and the
        // thread-pointer setter it resolves are named in the file.
        byte[] elf = BuildPayload();
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("calloc\0")));
        Assert.True(ContainsBytes(elf, Encoding.ASCII.GetBytes("amd64_set_fsbase\0")));
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

    [Fact]
    public void LinkerProvidedSymbolsAreNotResolvedAsOutsideReferences()
    {
        // The image start, the code end and the exception-index bounds are names the writer settles once
        // the layout is fixed, not outside references a payload resolves at run time. A payload has no
        // definition for them and no dynamic linker, so left as resolver names they would resolve to a
        // stub and read as garbage - and the first exception unwound through the index would end the
        // process. Referencing them must therefore leave no resolver name behind.
        byte[] elf = BuildLinkerProvidedPayload();
        foreach (string name in new[] { "__executable_start", "_etext", "__GNU_EH_FRAME_HDR", "__GNU_EH_FRAME_HDR_END" })
            Assert.False(ContainsBytes(elf, Encoding.ASCII.GetBytes(name + "\0")), $"'{name}' was left as a resolver name.");
    }

    [Fact]
    public void LinkerProvidedReferencesTakeBaseRelativeFixups()
    {
        // Resolved to real link-time addresses, references to the self-description names collect base-
        // relative fix-ups like any absolute pointer - never a symbolic relocation the loader cannot apply.
        byte[] elf = BuildLinkerProvidedPayload();
        Assert.True(TryFindRela(elf, out int relaOff, out int relaSize));
        for (int i = 0; i < relaSize / 24; i++)
        {
            ulong info = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(relaOff + i * 24 + 8));
            Assert.Equal(8u, info & 0xFFFFFFFF); // R_X86_64_RELATIVE
            Assert.Equal(0u, info >> 32);         // no symbol
        }
    }

    [Fact]
    public void ImageStartAndCodeEndResolveToTheirRealAddresses()
    {
        // The image start is the text segment address; the code end is one past it. Both must appear as
        // base-relative addends - the writer filled the pointers with real addresses. Were either left a
        // resolver name it would resolve to a stub inside the code group, not to these addresses.
        byte[] elf = BuildLinkerProvidedPayload();
        (ulong textAddr, ulong textSize) = ReadTextSegment(elf);
        List<ulong> addends = RelativeAddends(elf);
        Assert.Contains(textAddr, addends);            // __executable_start
        Assert.Contains(textAddr + textSize, addends); // _etext
    }

    // A payload object whose data holds pointers to the four names the linker settles: the image start,
    // the code end, and the exception-index bounds. Each pointer takes an absolute relocation, so a
    // working writer fills it with the real address and a broken one would route it through a resolver
    // slot and record the name.
    private static byte[] BuildLinkerProvidedPayload()
    {
        string[] names = ["__executable_start", "_etext", "__GNU_EH_FRAME_HDR", "__GNU_EH_FRAME_HDR_END"];
        var nullSec = new ElfSection { Name = "", Type = 0, Flags = 0, Address = 0, Size = 0, Link = 0, Info = 0, AddrAlign = 0, EntSize = 0, Data = [] };
        byte[] code = [0xC3]; // ret
        var text = new ElfSection { Name = ".text", Type = 1, Flags = 0x2 | 0x4, Address = 0, Size = 1, Link = 0, Info = 0, AddrAlign = 16, EntSize = 0, Data = code };
        var data = new ElfSection { Name = ".data", Type = 1, Flags = 0x2 | 0x1, Address = 0, Size = (ulong)(names.Length * 8), Link = 0, Info = 0, AddrAlign = 8, EntSize = 0, Data = new byte[names.Length * 8] };
        var syms = new List<ElfSymbol>
        {
            new() { Name = "", Info = 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 },
            new() { Name = "main", Info = (1 << 4) | 2, Other = 0, SectionIndex = 1, Value = 0, Size = 1 },
        };
        var dataRelocs = new List<ElfRelocation>();
        for (int i = 0; i < names.Length; i++)
        {
            syms.Add(new ElfSymbol { Name = names[i], Info = (1 << 4) | 0, Other = 0, SectionIndex = 0, Value = 0, Size = 0 });
            dataRelocs.Add(new ElfRelocation(Offset: (ulong)(i * 8), SymbolIndex: (uint)(2 + i), Type: RelType.R64, Addend: 0));
        }
        var relocs = new Dictionary<int, IReadOnlyList<ElfRelocation>> { [2] = dataRelocs };
        var obj = new ElfObject { Origin = "linker-provided-payload", Sections = [nullSec, text, data], Symbols = syms, Relocations = relocs };
        var crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        var options = new LinkOptions();
        options.ExtraObjects.Add(crt);
        options.ExtraObjects.Add(obj);
        LinkResolution res = Linker.Resolve(options);
        return PayloadWriter.Write(res, PayloadCrtEmitter.StartSymbol);
    }

    // The executable load segment's virtual address and file size: the image start and, added, the code end.
    private static (ulong Addr, ulong Size) ReadTextSegment(byte[] elf)
    {
        ulong phoff = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x20));
        int phnum = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x38));
        for (int i = 0; i < phnum; i++)
        {
            int p = (int)phoff + i * 0x38;
            if (BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p)) != 1) continue;        // PT_LOAD
            if ((BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p + 4)) & 1) == 0) continue; // PF_X
            ulong vaddr = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(p + 16));
            ulong filesz = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(p + 32));
            return (vaddr, filesz);
        }
        throw new InvalidOperationException("the payload has no executable load segment.");
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

    [Fact]
    public void DefinedNamesMatchesTheCrtObjectsGlobalSymbols()
    {
        ElfObject crt = ElfObjectReader.Read(PayloadCrtEmitter.BuildStartObject(), "crt");
        string[] defined = crt.Symbols
            .Where(s => !s.IsUndefined && s.Name.Length > 0 && (s.Info >> 4) == 1)
            .Select(s => s.Name)
            .Order()
            .ToArray();
        string[] declared = PayloadCrtEmitter.DefinedNames.Order().ToArray();
        Assert.Equal(declared, defined);
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
