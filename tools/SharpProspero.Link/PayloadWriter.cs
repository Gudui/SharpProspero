// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK
//
// Writes a position-independent payload: a plain ET_DYN executable an ELF loader maps at a fresh base
// and runs in an existing process. Unlike an application module, a payload has no dynamic linker to bind
// its references: the loader applies only base-relative fix-ups, so every reference to an outside symbol
// is resolved at run time through a resolver the loader hands the entry point. The writer emits the
// relative relocations in a relocation section (the loader reads section headers to find them), routes
// each outside reference through a slot the start code fills, and records the names to resolve.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpProspero.Link;

/// <summary>Writes a position-independent payload from a resolved graph whose outside references resolve at run time.</summary>
public static class PayloadWriter
{
    private const ulong SegAlign = 0x4000;
    private const uint PfX = 1, PfW = 2, PfR = 4;
    private const uint PtLoad = 1;
    private const uint RRelative = 8;            // R_X86_64_RELATIVE
    private const int ShtProgBits = 1, ShtStrTab = 3, ShtRela = 4, ShtNoBits = 8;
    private const ulong ShfAlloc = 0x2, ShfWrite = 0x1, ShfExec = 0x4;
    private const ushort ShnAbs = 0xFFF1, ShnCommon = 0xFFF2;

    // The start object marks where the writer records the run-time resolution table, so the start code
    // can walk it. The writer fills the two pointers and relocates them.
    private const string ImportTableSymbol = "__prospero_payload_imports";

    private readonly record struct Relative(ulong Offset, ulong Addend);

    private sealed class Extern
    {
        public required string Name { get; init; }
        public ulong GotAddress { get; set; }     // the slot the start code fills through the resolver
        public ulong PltAddress { get; set; }     // a stub that jumps through the slot
    }

    /// <summary>
    /// Writes the payload for <paramref name="resolution"/>, with <paramref name="entrySymbol"/> as the
    /// entry the loader jumps to. Outside references become resolver slots the start code fills.
    /// </summary>
    public static byte[] Write(LinkResolution resolution, string? entrySymbol)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        // Outside references, one resolver slot each. An import the graph attributes to a module and a
        // reference nothing defines are the same to a payload: a name resolved at run time. A payload has
        // no dynamic linker, so an unresolved reference is not an error - it is a name for the resolver.
        var externByName = new Dictionary<string, Extern>(StringComparer.Ordinal);
        var externs = new List<Extern>();
        void AddExtern(string name)
        {
            if (name.Length == 0 || externByName.ContainsKey(name)) return;
            var e = new Extern { Name = name };
            externByName[name] = e;
            externs.Add(e);
        }
        foreach (ImportSymbol imp in resolution.Imports)
            AddExtern(imp.Name);
        foreach (string name in resolution.Unresolved)
            AddExtern(name);

        // A section-boundary name the writer itself provides is not an outside reference; hold those out.
        var sectionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ElfObject o in resolution.Included)
            foreach (ElfSection s in o.Sections)
                if (s.IsAlloc && s.Name.Length > 0)
                    sectionNames.Add(s.Name);

        foreach (ElfObject o in resolution.Included)
            foreach (IReadOnlyList<ElfRelocation> relocs in o.Relocations.Values)
                foreach (ElfRelocation r in relocs)
                    if (r.SymbolIndex < o.Symbols.Count)
                    {
                        ElfSymbol s = o.Symbols[(int)r.SymbolIndex];
                        if (s.IsUndefined && s.Name.Length > 0 && !resolution.Defined.ContainsKey(s.Name)
                            && s.Name != ImportTableSymbol
                            && !Linker.IsEncapsulationSymbol(s.Name, sectionNames, out _, out _))
                            AddExtern(s.Name);
                    }

        // Lay out the allocatable sections into three groups: executable, read-only, writable. The
        // init/fini arrays lead the writable group so their run can be named by a contiguous range.
        var offsetInGroup = new Dictionary<(ElfObject, int), ulong>();
        ulong textLen = 0, roLen = 0, dataMem = 0, dataFile = 0;
        ulong initStart = 0, initEnd = 0, finiStart = 0, finiEnd = 0;
        bool haveInit = false, haveFini = false;
        void PlaceArray(string name, ref ulong start, ref ulong end, ref bool have)
        {
            // A constructor array given a priority carries that priority in its name, so it belongs to
            // this array and has to be placed inside its run - matching the bare name skips it, and the
            // run then covers only part of what should be walked. Within the run the order is the one
            // the priorities ask for, lowest first, with the plain name last.
            var members = new List<(ElfObject Obj, int Index)>();
            foreach (ElfObject obj in resolution.Included)
                for (int i = 0; i < obj.Sections.Count; i++)
                {
                    ElfSection sec = obj.Sections[i];
                    if (sec is not { IsAlloc: true, IsTls: false, IsWritable: true }
                        || DynamicWriter.OutputSectionName(sec.Name) != name) continue;
                    members.Add((obj, i));
                }
            foreach ((ElfObject obj, int i) in members.OrderBy(m => DynamicWriter.ArrayPriority(m.Obj.Sections[m.Index].Name)))
            {
                ElfSection sec = obj.Sections[i];
                ulong o = Align(dataMem, sec.AddrAlign);
                offsetInGroup[(obj, i)] = o;
                dataMem = o + sec.Size;
                if (!sec.IsNoBits) dataFile = dataMem;
                if (!have) { start = o; have = true; }
                end = dataMem;
            }
        }
        PlaceArray(".init_array", ref initStart, ref initEnd, ref haveInit);
        PlaceArray(".fini_array", ref finiStart, ref finiEnd, ref haveFini);

        foreach (ElfObject obj in resolution.Included)
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (!sec.IsAlloc || sec.IsTls || offsetInGroup.ContainsKey((obj, i))) continue;
                if (sec.IsExecutable) { offsetInGroup[(obj, i)] = textLen = Align(textLen, sec.AddrAlign); textLen += sec.Size; }
                else if (sec.IsWritable)
                {
                    ulong o = Align(dataMem, sec.AddrAlign);
                    offsetInGroup[(obj, i)] = o; dataMem = o + sec.Size;
                    if (!sec.IsNoBits) dataFile = dataMem;
                }
                else { offsetInGroup[(obj, i)] = roLen = Align(roLen, sec.AddrAlign); roLen += sec.Size; }
            }

        // The thread-local template: the initialized sections first (so the file image is contiguous),
        // then the zero-filled sections. A payload has no dynamic linker, so the start code copies this
        // template into a fresh block and points the thread pointer at it; a thread-local reference is
        // baked to a fixed offset from that pointer at link time.
        var tlsOffset = new Dictionary<(ElfObject, int), ulong>();
        ulong tlsMemLen = 0, tlsFileLen = 0, tlsAlign = 1;
        for (int pass = 0; pass < 2; pass++)
            foreach (ElfObject obj in resolution.Included)
                for (int i = 0; i < obj.Sections.Count; i++)
                {
                    ElfSection sec = obj.Sections[i];
                    if (!sec.IsAlloc || !sec.IsTls) continue;
                    if ((pass == 0) == sec.IsNoBits) continue;          // initialized sections in pass 0
                    ulong o = Align(tlsMemLen, sec.AddrAlign);
                    tlsOffset[(obj, i)] = o; tlsMemLen = o + sec.Size;
                    if (!sec.IsNoBits) tlsFileLen = tlsMemLen;
                    if (sec.AddrAlign > tlsAlign) tlsAlign = sec.AddrAlign;
                }
        bool hasTls = tlsMemLen > 0;
        ulong tlsAlignedMem = Align(tlsMemLen, tlsAlign);

        // The template's initialized bytes ride in the writable segment. The thread-pointer offset of a
        // symbol is its template offset minus the aligned size, since the block sits below the pointer.
        ulong tlsTemplateOffsetInData = 0;
        if (hasTls)
        {
            tlsTemplateOffsetInData = Align(dataMem, tlsAlign);
            dataMem = tlsTemplateOffsetInData + tlsMemLen;
            if (tlsFileLen > 0) dataFile = tlsTemplateOffsetInData + tlsFileLen;
        }

        // The stub table follows the object text; each outside reference gets a sixteen-byte stub.
        ulong pltBase = Align(textLen, 16);
        ulong textSize = pltBase + (ulong)externs.Count * 16;

        // Read-only group: the object read-only sections, then the resolver name strings.
        var nameOffset = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var nameBytes = new List<byte>();
        ulong namesBase = Align(roLen, 1);
        foreach (Extern e in externs)
        {
            nameOffset[e.Name] = namesBase + (ulong)nameBytes.Count;
            nameBytes.AddRange(Encoding.ASCII.GetBytes(e.Name));
            nameBytes.Add(0);
        }
        ulong roSize = namesBase + (ulong)nameBytes.Count;

        // Writable group: the object data, then the global-offset table, then the resolver table (two
        // pointers per outside reference: the name and the slot to fill). The offset table holds one slot
        // per outside reference plus one for each internal reference the relocation pass routes through it
        // (a data load of a defined address, or a thread-local offset); reserve for both so the resolver
        // table that follows is not overrun.
        int internalGotSlots = CountInternalGotSlots(resolution, externByName);
        ulong gotBase = Align(dataMem, 8);
        ulong importTableBase = gotBase + (ulong)(externs.Count + internalGotSlots) * 8;
        ulong dataMemEnd = importTableBase + (ulong)externs.Count * 16;
        ulong dataFileEnd = dataMemEnd; // the slots and table are written, not zero-filled

        // Segment addresses on the load grid.
        ulong textAddr = SegAlign;
        ulong roAddr = Align(textAddr + textSize, SegAlign);
        ulong dataAddr = Align(roAddr + roSize, SegAlign);
        ulong namesAddr = roAddr + namesBase;
        ulong gotAddr = dataAddr + gotBase;
        ulong importTableAddr = dataAddr + importTableBase;
        ulong tlsTemplateAddr = hasTls ? dataAddr + tlsTemplateOffsetInData : 0;

        for (int i = 0; i < externs.Count; i++)
        {
            externs[i].GotAddress = gotAddr + (ulong)i * 8;
            externs[i].PltAddress = textAddr + pltBase + (ulong)i * 16;
        }

        ulong SectionAddr(ElfObject o, int i)
        {
            if (i == ShnCommon)
                throw new ElfLinkException($"{o.Origin}: a common (uninitialized global) symbol has no storage. Compile with -fno-common.");
            if ((uint)i >= (uint)o.Sections.Count)
                throw new ElfLinkException($"{o.Origin}: a symbol refers to section index {i}, which the object does not define.");
            ElfSection s = o.Sections[i];
            if (s.IsTls) return tlsTemplateAddr + tlsOffset[(o, i)]; // in the template, riding the data segment
            ulong bas = s.IsExecutable ? textAddr : s.IsWritable ? dataAddr : roAddr;
            return bas + offsetInGroup[(o, i)];
        }

        // Resolve and fix up the object sections. Outside references route through the stub (a call) or
        // the resolver slot (a data load); every absolute pointer collects a base-relative relocation.
        var relatives = new List<Relative>();
        var internalGot = new Dictionary<string, ulong>(StringComparer.Ordinal); // name -> got slot address for defined targets
        var extraGotSlots = new List<(ulong Slot, ulong Value)>();
        var tlsGotSlots = new List<(ulong Slot, ulong Value)>(); // initial-exec slots hold a thread-pointer offset, not an address
        Dictionary<(ElfObject, int), byte[]> sectionData = Relocate(
            resolution, externByName, SectionAddr, relatives, internalGot, extraGotSlots, tlsGotSlots,
            gotAddr, externs.Count, importTableAddr, tlsOffset, tlsAlignedMem);

        // The resolver slots for the outside references start empty; the start code fills them. The
        // internal global-offset slots hold a defined address and take a base-relative relocation. A
        // thread-local slot holds a fixed thread-pointer offset, so it is written but not relocated.
        int totalGot = externs.Count + internalGot.Count;
        byte[] gotData = new byte[totalGot * 8];
        foreach ((ulong slot, ulong value) in extraGotSlots)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(gotData.AsSpan((int)(slot - gotAddr)), value);
            relatives.Add(new Relative(slot, value));
        }
        foreach ((ulong slot, ulong value) in tlsGotSlots)
            BinaryPrimitives.WriteUInt64LittleEndian(gotData.AsSpan((int)(slot - gotAddr)), value);

        // The resolver table: for each outside reference, the name pointer and the slot pointer, both
        // base-relative.
        byte[] importTable = new byte[externs.Count * 16];
        for (int i = 0; i < externs.Count; i++)
        {
            ulong namePtr = namesAddr + (nameOffset[externs[i].Name] - namesBase);
            ulong slotPtr = externs[i].GotAddress;
            BinaryPrimitives.WriteUInt64LittleEndian(importTable.AsSpan(i * 16), namePtr);
            BinaryPrimitives.WriteUInt64LittleEndian(importTable.AsSpan(i * 16 + 8), slotPtr);
            relatives.Add(new Relative(importTableAddr + (ulong)i * 16, namePtr));
            relatives.Add(new Relative(importTableAddr + (ulong)i * 16 + 8, slotPtr));
        }

        // The stubs: jmp *slot(rip).
        byte[] pltData = new byte[externs.Count * 16];
        for (int i = 0; i < externs.Count; i++)
        {
            int p = i * 16;
            pltData[p] = 0xFF; pltData[p + 1] = 0x25;
            long disp = (long)externs[i].GotAddress - (long)(externs[i].PltAddress + 6);
            BinaryPrimitives.WriteInt32LittleEndian(pltData.AsSpan(p + 2), unchecked((int)disp));
        }

        // Fill the start object's header: the resolver-table bounds, then the global-constructor bounds.
        // Each pointer is base-relative. When there are no constructors the two array pointers stay zero,
        // so the start code's loop over an empty range runs nothing.
        if (TryFindSymbol(resolution, ImportTableSymbol, out ElfObject? hdrObj, out ElfSymbol? hdrSym))
        {
            ulong hdrAddr = SectionAddr(hdrObj!, hdrSym!.SectionIndex) + hdrSym.Value;
            if (sectionData.TryGetValue((hdrObj!, hdrSym.SectionIndex), out byte[]? hbytes))
            {
                int at = (int)(hdrAddr - SectionAddr(hdrObj!, hdrSym.SectionIndex));
                ulong importsEnd = importTableAddr + (ulong)externs.Count * 16;
                BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at), importTableAddr);
                BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 8), importsEnd);
                relatives.Add(new Relative(hdrAddr, importTableAddr));
                relatives.Add(new Relative(hdrAddr + 8, importsEnd));
                if (haveInit)
                {
                    ulong initA = dataAddr + initStart, initB = dataAddr + initEnd;
                    BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 16), initA);
                    BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 24), initB);
                    relatives.Add(new Relative(hdrAddr + 16, initA));
                    relatives.Add(new Relative(hdrAddr + 24, initB));
                }
                if (hasTls)
                {
                    // The thread-local template: where the initialized bytes live, how many to copy, and
                    // the block size below the thread pointer. Only the address is base-relative; the two
                    // sizes are plain values the start code reads to build and install the block.
                    BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 32), tlsTemplateAddr);
                    BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 40), tlsFileLen);
                    BinaryPrimitives.WriteUInt64LittleEndian(hbytes.AsSpan(at + 48), tlsAlignedMem);
                    relatives.Add(new Relative(hdrAddr + 32, tlsTemplateAddr));
                }
            }
        }

        ulong entry = 0;
        if (!string.IsNullOrEmpty(entrySymbol) && TryFindSymbol(resolution, entrySymbol, out ElfObject? eo, out ElfSymbol? es))
            entry = SectionAddr(eo!, es!.SectionIndex) + es.Value;

        return WriteFile(resolution, entry, sectionData, SectionAddr,
            textAddr, textSize, pltBase, pltData, roAddr, roSize, namesBase, [.. nameBytes],
            dataAddr, dataFileEnd, dataMemEnd, gotBase, gotData, importTableBase, importTable, relatives);
    }

    private static Dictionary<(ElfObject, int), byte[]> Relocate(
        LinkResolution resolution, Dictionary<string, Extern> externByName, Func<ElfObject, int, ulong> sectionAddr,
        List<Relative> relatives, Dictionary<string, ulong> internalGot, List<(ulong, ulong)> extraGotSlots,
        List<(ulong, ulong)> tlsGotSlots, ulong gotAddr, int externCount, ulong importTableAddr,
        Dictionary<(ElfObject, int), ulong> tlsOffset, ulong tlsAlignedMem)
    {
        var result = new Dictionary<(ElfObject, int), byte[]>();
        ulong NextInternalGot() => gotAddr + (ulong)(externCount + internalGot.Count) * 8;

        foreach (ElfObject obj in resolution.Included)
        {
            foreach (KeyValuePair<int, IReadOnlyList<ElfRelocation>> kv in obj.Relocations)
            {
                int idx = kv.Key;
                if (idx >= obj.Sections.Count) continue;
                ElfSection sec = obj.Sections[idx];
                if (!sec.IsAlloc) continue;
                byte[] bytes = sec.IsNoBits ? new byte[sec.Size] : (byte[])sec.Data.Clone();
                ulong secAddr = sectionAddr(obj, idx);

                // A dynamic thread-local sequence is a lea and a call __tls_get_addr; relaxing the lea
                // folds the call away. The call sits eight bytes past a general-dynamic lea's relocation
                // and five past a local-dynamic one.
                HashSet<ulong>? foldedTlsCall = null;
                foreach (ElfRelocation probe in kv.Value)
                {
                    if (probe.Type == RelType.TlsGd) (foldedTlsCall ??= []).Add(probe.Offset + 8);
                    else if (probe.Type == RelType.TlsLd) (foldedTlsCall ??= []).Add(probe.Offset + 5);
                }

                foreach (ElfRelocation r in kv.Value)
                {
                    if (r.SymbolIndex >= (uint)obj.Symbols.Count) continue;
                    ElfSymbol sym = obj.Symbols[(int)r.SymbolIndex];
                    ulong place = secAddr + r.Offset;
                    int at = (int)r.Offset;

                    if (foldedTlsCall is not null && foldedTlsCall.Contains(r.Offset))
                        continue; // the __tls_get_addr call, folded into the local-exec load below

                    if (r.Type == RelType.TlsGd)
                    {
                        // The payload is self-contained, so a general-dynamic load relaxes to local-exec:
                        // read the thread pointer and add the symbol's fixed offset, leaving it in rax.
                        if (at - 4 < 0 || at + 12 > bytes.Length)
                            throw new ElfLinkException($"{obj.Origin}: a thread-local sequence on '{sym.Name}' runs past the section.");
                        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong gdTemplateOff))
                            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' has no template section in the payload.");
                        long le = (long)gdTemplateOff - (long)tlsAlignedMem;
                        ReadOnlySpan<byte> localExec = [0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x80];
                        localExec.CopyTo(bytes.AsSpan(at - 4)); // mov %fs:0,%rax ; lea ...(%rax),%rax
                        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 8), checked((int)le));
                        continue;
                    }

                    if (r.Type == RelType.TlsLd)
                    {
                        // The module base becomes the thread pointer; the members (its DTPOFF relocations)
                        // become local-exec offsets. The nop prefixes keep the replacement the same size.
                        if (at - 3 < 0 || at + 9 > bytes.Length)
                            throw new ElfLinkException($"{obj.Origin}: a thread-local base sequence on '{sym.Name}' runs past the section.");
                        ReadOnlySpan<byte> threadPointer = [0x66, 0x66, 0x66, 0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00];
                        threadPointer.CopyTo(bytes.AsSpan(at - 3)); // (nop) mov %fs:0,%rax
                        continue;
                    }

                    int width = r.Type is RelType.R64 or RelType.TpOff64 or RelType.Pc64 or RelType.DtpOff64 ? 8 : 4;
                    if (at < 0 || at + width > bytes.Length) continue;

                    if (r.Type == RelType.GotTpOff)
                    {
                        // Initial-exec through the global-offset table: the slot holds the fixed
                        // thread-pointer offset; the reference loads it PC-relative and adds the pointer.
                        ulong tslot = TlsGotSlotFor(resolution, sectionAddr, obj, sym, internalGot, tlsGotSlots, NextInternalGot, tlsOffset, tlsAlignedMem);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(tslot + (ulong)r.Addend - place));
                        continue;
                    }
                    if (r.Type is RelType.TpOff32 or RelType.TpOff64 or RelType.DtpOff32 or RelType.DtpOff64)
                    {
                        // Local-exec: the value is the symbol's template offset minus the aligned template
                        // size, since the block sits below the thread pointer on this target. A module-block
                        // offset (DTPOFF, once its base is relaxed to the thread pointer) is the same value.
                        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong templateOff))
                            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' has no template section in the payload.");
                        long tp = (long)templateOff - (long)tlsAlignedMem + r.Addend;
                        if (r.Type is RelType.TpOff64 or RelType.DtpOff64) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)tp);
                        else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), unchecked((uint)(int)tp));
                        continue;
                    }

                    if (RelType.IsGotPcRel(r.Type))
                    {
                        ulong slot = GotSlotFor(resolution, externByName, sectionAddr, obj, sym, gotAddr, externCount, internalGot, extraGotSlots, NextInternalGot);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(slot + (ulong)r.Addend - place));
                        continue;
                    }

                    ulong s = SymbolValue(resolution, externByName, sectionAddr, obj, sym);
                    switch (r.Type)
                    {
                        case RelType.None:
                            break;
                        case RelType.R64:
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), s + (ulong)r.Addend);
                            // Any absolute pointer needs a base-relative fix-up unless it resolves to zero.
                            if (s + (ulong)r.Addend != 0 && !(sym.IsWeak && sym.IsUndefined && !externByName.ContainsKey(sym.Name) && !resolution.Defined.ContainsKey(sym.Name)))
                                relatives.Add(new Relative(place, s + (ulong)r.Addend));
                            break;
                        case RelType.Pc32:
                        case RelType.Plt32:
                            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(s + (ulong)r.Addend - place)); break;
                        case RelType.Pc64:
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)((long)(s + (ulong)r.Addend) - (long)place)); break;
                        case RelType.R32:
                        case RelType.R32S:
                            throw new ElfLinkException(
                                $"A 32-bit absolute relocation on '{sym.Name}' cannot be fixed up in a payload; compile position-independent code.");
                        default:
                            throw new ElfLinkException(
                                $"{obj.Origin}: unsupported relocation type {r.Type} on '{sym.Name}'. A payload resolves absolute, PC-relative, PLT and GOT-relative references.");
                    }
                }
                result[(obj, idx)] = bytes;
            }
            for (int i = 0; i < obj.Sections.Count; i++)
                if (obj.Sections[i].IsAlloc && !result.ContainsKey((obj, i)))
                    result[(obj, i)] = obj.Sections[i].IsNoBits ? new byte[obj.Sections[i].Size] : (byte[])obj.Sections[i].Data.Clone();
        }
        return result;
    }

    // How many internal global-offset slots the relocation pass will create, so the writable layout can
    // reserve for them before the resolver table. Keyed exactly as the relocation pass keys them: a
    // data load of a defined address routes through one slot per target, a thread-local offset through
    // one slot per thread-local symbol; an outside reference reuses its resolver slot and adds none.
    private static int CountInternalGotSlots(LinkResolution resolution, Dictionary<string, Extern> externByName)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ElfObject obj in resolution.Included)
            foreach (KeyValuePair<int, IReadOnlyList<ElfRelocation>> kv in obj.Relocations)
            {
                if (kv.Key >= obj.Sections.Count || !obj.Sections[kv.Key].IsAlloc) continue;
                foreach (ElfRelocation r in kv.Value)
                {
                    if (r.SymbolIndex >= (uint)obj.Symbols.Count) continue;
                    ElfSymbol sym = obj.Symbols[(int)r.SymbolIndex];
                    string tail = sym.Name.Length > 0 ? sym.Name : $"#{sym.SectionIndex}:{sym.Value}";
                    if (r.Type == RelType.GotTpOff) keys.Add("%tls%" + tail);
                    else if (RelType.IsGotPcRel(r.Type) && !externByName.ContainsKey(sym.Name)) keys.Add(tail);
                }
            }
        return keys.Count;
    }

    // The GOT slot a reference reads: the resolver slot for an outside reference, or an internal slot
    // holding a defined address (with a base-relative fix-up recorded through extraGotSlots).
    private static ulong GotSlotFor(
        LinkResolution resolution, Dictionary<string, Extern> externByName, Func<ElfObject, int, ulong> sectionAddr,
        ElfObject obj, ElfSymbol sym, ulong gotAddr, int externCount, Dictionary<string, ulong> internalGot,
        List<(ulong, ulong)> extraGotSlots, Func<ulong> nextInternalGot)
    {
        if (externByName.TryGetValue(sym.Name, out Extern? e))
            return e.GotAddress;
        string key = sym.Name.Length > 0 ? sym.Name : $"#{sym.SectionIndex}:{sym.Value}";
        if (internalGot.TryGetValue(key, out ulong slot))
            return slot;
        slot = nextInternalGot();
        internalGot[key] = slot;
        ulong value = SymbolValueDefined(resolution, sectionAddr, obj, sym);
        extraGotSlots.Add((slot, value));
        return slot;
    }

    // The GOT slot an initial-exec thread-local reference reads: it holds the symbol's fixed offset from
    // the thread pointer (its template offset minus the aligned template size), written at link time and
    // never relocated - it is a constant offset, not an address.
    private static ulong TlsGotSlotFor(
        LinkResolution resolution, Func<ElfObject, int, ulong> sectionAddr, ElfObject obj, ElfSymbol sym,
        Dictionary<string, ulong> internalGot, List<(ulong, ulong)> tlsGotSlots, Func<ulong> nextInternalGot,
        Dictionary<(ElfObject, int), ulong> tlsOffset, ulong tlsAlignedMem)
    {
        string key = "%tls%" + (sym.Name.Length > 0 ? sym.Name : $"#{sym.SectionIndex}:{sym.Value}");
        if (internalGot.TryGetValue(key, out ulong slot)) return slot;
        slot = nextInternalGot();
        internalGot[key] = slot;
        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong templateOffset))
            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' referenced through the GOT has no template section in the payload.");
        tlsGotSlots.Add((slot, (ulong)((long)templateOffset - (long)tlsAlignedMem)));
        return slot;
    }

    // The offset of a thread-local symbol within the template: its section's template offset plus the
    // symbol's value, resolving an undefined reference against the defining object.
    private static bool TryTlsTemplateOffset(
        LinkResolution resolution, Dictionary<(ElfObject, int), ulong> tlsOffset, ElfObject obj, ElfSymbol sym, out ulong result)
    {
        result = 0;
        if (!sym.IsUndefined)
        {
            if (!tlsOffset.TryGetValue((obj, sym.SectionIndex), out ulong o)) return false;
            result = o + sym.Value;
            return true;
        }
        if (resolution.Defined.TryGetValue(sym.Name, out ElfObject? defObj))
            foreach (ElfSymbol d in defObj.Symbols)
                if (!d.IsUndefined && d.Name == sym.Name && tlsOffset.TryGetValue((defObj, d.SectionIndex), out ulong o))
                {
                    result = o + d.Value;
                    return true;
                }
        return false;
    }

    private static ulong SymbolValue(
        LinkResolution resolution, Dictionary<string, Extern> externByName,
        Func<ElfObject, int, ulong> sectionAddr, ElfObject obj, ElfSymbol sym)
    {
        if (sym.SectionIndex == ShnAbs) return sym.Value;
        if (sym.Type == SymType.Section) return sectionAddr(obj, sym.SectionIndex);
        if (!sym.IsUndefined) return sectionAddr(obj, sym.SectionIndex) + sym.Value;
        if (resolution.Defined.TryGetValue(sym.Name, out ElfObject? defObj))
            foreach (ElfSymbol d in defObj.Symbols)
                if (!d.IsUndefined && d.Name == sym.Name)
                    return d.SectionIndex == ShnAbs ? d.Value : sectionAddr(defObj, d.SectionIndex) + d.Value;
        if (externByName.TryGetValue(sym.Name, out Extern? e)) return e.PltAddress;
        if (TryEncapsulationAddress(resolution, sectionAddr, sym.Name, out ulong enc)) return enc;
        if (sym.IsWeak) return 0;
        throw new ElfLinkException($"Unresolved symbol '{sym.Name}'.");
    }

    // The address an internal global-offset slot holds. A symbol defined in the referencing object
    // resolves through that object; an undefined reference resolves through its defining object.
    private static ulong SymbolValueDefined(LinkResolution resolution, Func<ElfObject, int, ulong> sectionAddr, ElfObject obj, ElfSymbol sym)
    {
        if (sym.SectionIndex == ShnAbs) return sym.Value;
        if (sym.Type == SymType.Section) return sectionAddr(obj, sym.SectionIndex);
        if (!sym.IsUndefined) return sectionAddr(obj, sym.SectionIndex) + sym.Value;
        if (resolution.Defined.TryGetValue(sym.Name, out ElfObject? defObj))
            foreach (ElfSymbol d in defObj.Symbols)
                if (!d.IsUndefined && d.Name == sym.Name)
                    return d.SectionIndex == ShnAbs ? d.Value : sectionAddr(defObj, d.SectionIndex) + d.Value;
        return 0;
    }

    private static bool TryEncapsulationAddress(LinkResolution resolution, Func<ElfObject, int, ulong> sectionAddr, string name, out ulong addr)
    {
        addr = 0;
        bool isStop;
        string section;
        if (name.StartsWith("__start_", StringComparison.Ordinal)) { isStop = false; section = name["__start_".Length..]; }
        else if (name.StartsWith("__stop_", StringComparison.Ordinal)) { isStop = true; section = name["__stop_".Length..]; }
        else return false;
        if (section.Length == 0) return false;
        bool found = false;
        ulong min = ulong.MaxValue, max = 0;
        foreach (ElfObject o in resolution.Included)
            for (int i = 0; i < o.Sections.Count; i++)
            {
                ElfSection s = o.Sections[i];
                if (!s.IsAlloc || s.Name != section) continue;
                ulong a = sectionAddr(o, i);
                if (a < min) min = a;
                if (a + s.Size > max) max = a + s.Size;
                found = true;
            }
        if (!found) return false;
        addr = isStop ? max : min;
        return true;
    }

    private static bool TryFindSymbol(LinkResolution resolution, string name, out ElfObject? obj, out ElfSymbol? sym)
    {
        obj = null; sym = null;
        if (!resolution.Defined.TryGetValue(name, out ElfObject? defObj)) return false;
        foreach (ElfSymbol s in defObj.Symbols)
            if (!s.IsUndefined && s.Name == name) { obj = defObj; sym = s; return true; }
        return false;
    }

    private static byte[] WriteFile(
        LinkResolution resolution, ulong entry, Dictionary<(ElfObject, int), byte[]> sectionData, Func<ElfObject, int, ulong> sectionAddr,
        ulong textAddr, ulong textSize, ulong pltBase, byte[] pltData,
        ulong roAddr, ulong roSize, ulong namesBase, byte[] nameBytes,
        ulong dataAddr, ulong dataFileEnd, ulong dataMemEnd, ulong gotBase, byte[] gotData,
        ulong importTableBase, byte[] importTable, List<Relative> relatives)
    {
        // Three load segments plus a relocation section the loader reads by section header. The header
        // and program headers occupy the first segment's start.
        const int phCount = 3;
        ulong textFileOff = SegAlign;
        ulong roFileOff = textFileOff + Align(textSize, SegAlign);
        ulong dataFileOff = roFileOff + Align(roSize, SegAlign);
        ulong afterData = dataFileOff + dataFileEnd;

        // Relocation section, then the section-header string table, then the section headers.
        byte[] relaBytes = new byte[relatives.Count * 24];
        relatives.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 0; i < relatives.Count; i++)
        {
            int b = i * 24;
            BinaryPrimitives.WriteUInt64LittleEndian(relaBytes.AsSpan(b), relatives[i].Offset);
            BinaryPrimitives.WriteUInt64LittleEndian(relaBytes.AsSpan(b + 8), RRelative);
            BinaryPrimitives.WriteUInt64LittleEndian(relaBytes.AsSpan(b + 16), relatives[i].Addend);
        }
        ulong relaFileOff = Align(afterData, 8);

        var shstr = new List<byte> { 0 };
        int AddShName(string s) { int o = shstr.Count; shstr.AddRange(Encoding.ASCII.GetBytes(s)); shstr.Add(0); return o; }
        int nText = AddShName(".text"), nRodata = AddShName(".rodata"), nData = AddShName(".data");
        int nRela = AddShName(".rela.dyn"), nShStr = AddShName(".shstrtab");
        ulong shstrFileOff = relaFileOff + (ulong)relaBytes.Length;
        ulong shdrFileOff = Align(shstrFileOff + (ulong)shstr.Count, 8);

        ulong fileEnd = shdrFileOff + 6 * 64;
        byte[] file = new byte[Align(fileEnd, 16)];

        // ELF header: a shared-object (position-independent) executable.
        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1; file[7] = 9; file[8] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 3);     // ET_DYN
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E);  // x86-64
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x18), entry);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x20), 0x40);              // e_phoff
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x28), shdrFileOff);       // e_shoff
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x34), 0x40);              // e_ehsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x36), 0x38);              // e_phentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x38), phCount);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3A), 0x40);              // e_shentsize
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3C), 6);                 // e_shnum
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3E), 5);                 // e_shstrndx

        int ph = 0x40;
        void WritePh(uint flags, ulong off, ulong va, ulong filesz, ulong memsz)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph), PtLoad);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph + 4), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 8), off);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 16), va);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 24), va);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 32), filesz);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 40), memsz);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 48), SegAlign);
            ph += 0x38;
        }
        WritePh(PfR | PfX, textFileOff, textAddr, textSize, textSize);
        WritePh(PfR, roFileOff, roAddr, roSize, roSize);
        WritePh(PfR | PfW, dataFileOff, dataAddr, dataFileEnd, dataMemEnd);

        void Put(ulong segFileOff, ulong segBase, ulong addr, byte[] bytes)
            => bytes.AsSpan().CopyTo(file.AsSpan((int)(segFileOff + (addr - segBase))));

        foreach (ElfObject obj in resolution.Included)
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (!sec.IsAlloc || sec.IsNoBits || !sectionData.TryGetValue((obj, i), out byte[]? bytes)) continue;
                ulong a = sectionAddr(obj, i);
                (ulong segFileOff, ulong segBase) = sec.IsExecutable ? (textFileOff, textAddr)
                    : sec.IsWritable ? (dataFileOff, dataAddr) : (roFileOff, roAddr);
                Put(segFileOff, segBase, a, bytes);
            }
        Put(textFileOff, textAddr, textAddr + pltBase, pltData);
        Put(roFileOff, roAddr, roAddr + namesBase, nameBytes);
        Put(dataFileOff, dataAddr, dataAddr + gotBase, gotData);
        Put(dataFileOff, dataAddr, dataAddr + importTableBase, importTable);
        relaBytes.AsSpan().CopyTo(file.AsSpan((int)relaFileOff));
        shstr.ToArray().AsSpan().CopyTo(file.AsSpan((int)shstrFileOff));

        // Section headers: null, .text, .rodata, .data, .rela.dyn, .shstrtab. The loader reads these to
        // find the relocation section.
        void WriteShdr(int index, int nameOff, uint type, ulong flags, ulong addr, ulong off, ulong size, int link, int align, int entsize)
        {
            int b = (int)shdrFileOff + index * 64;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(b), (uint)nameOff);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(b + 4), type);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 8), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 16), addr);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 24), off);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 32), size);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(b + 40), (uint)link);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 48), (ulong)align);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(b + 56), (ulong)entsize);
        }
        WriteShdr(1, nText, ShtProgBits, ShfAlloc | ShfExec, textAddr, textFileOff, textSize, 0, 16, 0);
        WriteShdr(2, nRodata, ShtProgBits, ShfAlloc, roAddr, roFileOff, roSize, 0, 1, 0);
        WriteShdr(3, nData, ShtProgBits, ShfAlloc | ShfWrite, dataAddr, dataFileOff, dataFileEnd, 0, 8, 0);
        WriteShdr(4, nRela, ShtRela, 0, 0, relaFileOff, (ulong)relaBytes.Length, 0, 8, 24);
        WriteShdr(5, nShStr, ShtStrTab, 0, 0, shstrFileOff, (ulong)shstr.Count, 0, 1, 0);
        return file;
    }

    private static ulong Align(ulong v, ulong a) => a <= 1 ? v : (v + a - 1) / a * a;
}
