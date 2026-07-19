// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK
//
// Writes a dynamic module: an application that imports functions from other modules. It builds the
// dynamic symbol and string tables with the mangled import names, a procedure-linkage table and its
// global-offset entries, the import relocations, the dynamic table with one needed record per
// imported module, the process parameters, and the module note, and lays them out with the load,
// dynamic, and process-parameter program headers.
//
// Structure is validated against the reference module format. Loading on the device is the final step.

using SharpProspero.Prx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace SharpProspero.Link;

/// <summary>Writes a dynamic module from a resolved graph that has imports.</summary>
public static class DynamicWriter
{
    private const ulong SegAlign = 0x4000;
    private const ushort TypeSceDynExec = 0xFE10;
    private const ushort TypeSceDynamic = 0xFE18;
    private const uint PfX = 1, PfW = 2, PfR = 4;
    private const uint PtLoad = 1, PtDynamic = 2, PtNote = 4, PtTls = 7, PtSceProcParam = 0x61000001, PtGnuEhFrame = 0x6474E550;

    private const long DtNeeded = 1, DtHash = 4, DtStrTab = 5, DtSymTab = 6, DtStrSz = 10, DtSymEnt = 11;
    private const long DtPltGot = 3, DtPltRelSz = 2, DtPltRel = 20, DtJmpRel = 23;
    private const long DtSceModuleInfo = 0x61000043, DtSceNeededModule = 0x61000045, DtSceImportLib = 0x61000049, DtSceImportLibAttr = 0x61000019;
    private const long DtSceOrigFilename = 0x61000041, DtSceExportLib = 0x61000047, DtSceExportLibAttr = 0x61000017;
    private const long DtSceModuleAttr = 0x61000011, DtSceSymTabSz = 0x6100003f, DtSceHashSz = 0x6100003d, DtNull = 0;
    private const long DtSceStrTab = 0x61000035, DtSceStrSz = 0x61000037, DtSceSymTab = 0x61000039, DtSceSymEnt = 0x6100003b;
    private const long DtRela = 7, DtRelaSz = 8, DtRelaEnt = 9, DtRelaCount = 0x6ffffff9;
    private const long DtSceRela = 0x6100002f, DtSceRelaSz = 0x61000031;
    private const uint RJumpSlot = 7, RGlobDat = 6, RRelative = 8, RAbs64 = 1;
    private const ushort ShnAbs = 0xFFF1; // an absolute symbol: its value is its address, not a section offset
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-";

    // One entry in the dynamic relocation table: the slot to patch, the x86-64 relocation type, the
    // dynamic-symbol index (zero for base-relative records), and the addend.
    private readonly record struct DynReloc(ulong Offset, uint Type, uint Sym, ulong Addend);

    private sealed class Import
    {
        public required string PlainName { get; init; }
        public required string ModuleName { get; init; }
        public int ModuleId { get; set; }
        public int LibraryId { get; set; }
        public string MangledName { get; set; } = "";
        public int DynSymIndex { get; set; }
        public ulong GotAddress { get; set; }
        public ulong PltAddress { get; set; }
    }

    private sealed class Block
    {
        public required string Name { get; init; }
        public required byte[] Data { get; set; }
        public required uint Flags { get; init; }
        public ulong Address { get; set; }
        public ulong FileOffset { get; set; }
    }

    /// <summary>Writes the dynamic module for <paramref name="resolution"/>.</summary>
    /// <param name="resolution">The resolved symbol graph.</param>
    /// <param name="entrySymbol">The entry symbol for an executable, or null for a library.</param>
    /// <param name="kind">Whether the output is an application or a library module.</param>
    /// <param name="exportSymbols">Defined symbols the module exports for other modules to import.</param>
    /// <param name="moduleFileName">The module's own file name, recorded in the module for self-reference.</param>
    public static byte[] Write(LinkResolution resolution, string? entrySymbol, ModuleKind kind = ModuleKind.Executable,
        IReadOnlyList<string>? exportSymbols = null, string? moduleFileName = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.Unresolved.Count > 0)
            throw new ElfLinkException($"{resolution.Unresolved.Count} symbol(s) are unresolved (e.g. {resolution.Unresolved[0]}).");

        // Module ids and mangled names for the imports. Imported modules number from 1 (0 is the
        // module itself); each has one library numbered from 0. The versions come from the stub that
        // provided the name, since an import must record the version the module actually exports.
        // Imports are grouped by the module file (its soname), unique per module. Each group carries
        // the module and library names the providing module publishes, which usually match but a few
        // modules publish under names that differ from the file and from each other.
        var moduleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var moduleData = new Dictionary<string, (string Module, string Library, ushort ModuleVersion, ushort LibraryVersion)>(StringComparer.Ordinal);
        var imports = new List<Import>();
        foreach (ImportSymbol imp in resolution.Imports)
        {
            if (!moduleIndex.TryGetValue(imp.Soname, out int n)) { n = moduleIndex.Count; moduleIndex[imp.Soname] = n; }
            moduleData.TryAdd(imp.Soname, (imp.ModuleName, imp.LibraryName, imp.ModuleVersion, imp.LibraryVersion));
            imports.Add(new Import { PlainName = imp.Name, ModuleName = imp.Soname, ModuleId = n + 1, LibraryId = n });
        }
        foreach (Import imp in imports)
            imp.MangledName = $"{SceNid.Compute(imp.PlainName)}#{Encode(imp.LibraryId)}#{Encode(imp.ModuleId)}";
        var importByName = new Dictionary<string, Import>(StringComparer.Ordinal);
        foreach (Import imp in imports)
            importByName[imp.PlainName] = imp;

        // Exported symbols: the module's own functions and data other modules can import. Each is a
        // defined symbol given a mangled export name under the module's own export library, numbered
        // after the import libraries. Their addresses are filled in once the layout is fixed.
        int exportLibId = moduleIndex.Count;
        var exports = new List<(string Mangled, ElfObject Obj, ElfSymbol Sym, bool IsFunc)>();
        if (exportSymbols is not null)
        {
            foreach (string name in exportSymbols)
            {
                if (!TryFindDefinedSymbol(resolution, name, out ElfObject? defObj, out ElfSymbol? defSym))
                    throw new ElfLinkException($"Export symbol '{name}' is not defined by any included object.");
                bool isFunc = defSym!.Type != SymType.Object;
                exports.Add(($"{SceNid.Compute(name)}#{Encode(exportLibId)}#{Encode(0)}", defObj!, defSym, isFunc));
            }
        }
        bool hasExports = exports.Count > 0;

        // Assign each application alloc section to a segment class and record its offset. Thread-local
        // sections are held out; they form a separate template laid out below.
        var sectionOffsetInGroup = new Dictionary<(ElfObject, int), ulong>();
        ulong textLen = 0, roLen = 0, dataLen = 0, dataMem = 0;
        foreach (ElfObject obj in resolution.Included)
        {
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (!sec.IsAlloc || sec.IsTls) continue;
                if (sec.IsExecutable) { sectionOffsetInGroup[(obj, i)] = textLen = Align(textLen, sec.AddrAlign); textLen += sec.Size; }
                else if (sec.IsWritable)
                {
                    ulong o = Align(dataMem, sec.AddrAlign);
                    sectionOffsetInGroup[(obj, i)] = o; dataMem = o + sec.Size;
                    if (!sec.IsNoBits) dataLen = dataMem;
                }
                else { sectionOffsetInGroup[(obj, i)] = roLen = Align(roLen, sec.AddrAlign); roLen += sec.Size; }
            }
        }

        // The thread-local template: the initialized sections first so their file image is contiguous,
        // then the zero-filled sections. Each thread receives a copy of this template at run time.
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

        // The template's initialized bytes ride in the data segment; the TLS program header points at
        // them. The thread-pointer offset of a symbol is its template offset minus the aligned size.
        ulong tlsTemplateOffsetInData = 0;
        if (hasTls)
        {
            tlsTemplateOffsetInData = Align(dataMem, tlsAlign);
            dataMem = tlsTemplateOffsetInData + tlsMemLen;
            if (tlsFileLen > 0) dataLen = tlsTemplateOffsetInData + tlsFileLen;
        }

        // The exception-frame index is built from the frame sections when the compiler emitted them in
        // a form the index covers; otherwise it is omitted and the frames still resolve by linear scan.
        var ehFrames = new List<(ElfObject Obj, int Index)>();
        foreach (ElfObject obj in resolution.Included)
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (sec is { IsAlloc: true, IsExecutable: false, IsWritable: false, IsNoBits: false } && sec.Name == ".eh_frame")
                    ehFrames.Add((obj, i));
            }
        int ehFrameCount = 0;
        bool ehFrameOk = ehFrames.Count > 0;
        foreach ((ElfObject obj, int i) in ehFrames)
        {
            var probe = new List<EhFrame.Entry>();
            if (!EhFrame.TryParse(obj.Sections[i].Data, 0, probe)) { ehFrameOk = false; break; }
            ehFrameCount += probe.Count;
        }
        int ehFrameHdrSize = ehFrameOk && ehFrameCount > 0 ? 12 + ehFrameCount * 8 : 0;

        // Build dynamic-metadata bytes. The needed record uses the module file name; the module and
        // library records use the bare name (no extension), matching the reference layout.
        var dynstr = new StringTable();
        int moduleInfoName = dynstr.Add(kind == ModuleKind.Library ? "prospero_module" : "eboot");
        int origFileNameOff = dynstr.Add(moduleFileName ?? (kind == ModuleKind.Library ? "prospero_module.prx" : "eboot.bin"));
        var moduleRecords = new (int SonameOff, int ModuleNameOff, int LibraryNameOff, int ModuleId, int LibraryId, ushort ModuleVersion, ushort LibraryVersion)[moduleIndex.Count];
        foreach ((string soname, int n) in moduleIndex)
        {
            (string moduleName, string libraryName, ushort moduleVersion, ushort libraryVersion) = moduleData[soname];
            moduleRecords[n] = (dynstr.Add(soname), dynstr.Add(moduleName), dynstr.Add(libraryName), n + 1, n, moduleVersion, libraryVersion);
        }

        // Symbols reached through the global-offset table (data or position-independent access) and
        // the count of absolute 64-bit references. Both feed the dynamic relocation table; a module
        // that only calls imported functions has neither, so the table stays empty.
        var gotDataOrder = new List<string>();
        var gotDataSym = new List<(ElfObject Obj, int SymIndex)>();
        var gotDataIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int abs64Count = 0;
        foreach (ElfObject o in resolution.Included)
            foreach (KeyValuePair<int, IReadOnlyList<ElfRelocation>> kv in o.Relocations)
            {
                if (kv.Key >= o.Sections.Count || !o.Sections[kv.Key].IsAlloc)
                    continue;
                foreach (ElfRelocation r in kv.Value)
                {
                    if (r.SymbolIndex >= (uint)o.Symbols.Count)
                        continue;
                    if (r.Type == RelType.GotPcRel)
                    {
                        string n = o.Symbols[(int)r.SymbolIndex].Name;
                        if (n.Length > 0 && gotDataIndex.TryAdd(n, gotDataOrder.Count))
                        {
                            gotDataOrder.Add(n);
                            gotDataSym.Add((o, (int)r.SymbolIndex));
                        }
                    }
                    else if (r.Type == RelType.R64)
                        abs64Count++;
                }
            }

        var dynsym = new List<byte>(new byte[24]);
        int di = 1;
        foreach (Import imp in imports)
        {
            byte[] e = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(e, (uint)dynstr.Add(imp.MangledName));
            e[4] = (2 << 4) | 2; // GLOBAL FUNC, UND
            dynsym.AddRange(e);
            imp.DynSymIndex = di++;
        }
        // Export entries follow the imports; the placeholder section index marks them defined, and
        // their addresses are patched in once the layout assigns section addresses.
        var exportDynIndex = new List<(int Index, ElfObject Obj, ElfSymbol Sym)>();
        foreach ((string mangled, ElfObject obj, ElfSymbol sym, bool isFunc) in exports)
        {
            byte[] e = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(e, (uint)dynstr.Add(mangled));
            e[4] = (byte)((1 << 4) | (isFunc ? 2 : 1)); // GLOBAL, FUNC or OBJECT
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(6), 1);
            BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(16), sym.Size);
            dynsym.AddRange(e);
            exportDynIndex.Add((di, obj, sym));
            di++;
        }
        byte[] dynstrBytes = dynstr.ToBytes();
        byte[] dynsymBytes = [.. dynsym];
        byte[] hashBytes = BuildSysVHash(imports.Count + exports.Count + 1);
        byte[] relaBytes = new byte[imports.Count * 24];
        byte[] relaDynBytes = new byte[(gotDataOrder.Count + abs64Count) * 24];
        byte[] pltBytes = new byte[16 + imports.Count * 16];
        byte[] gotBytes = new byte[24 + imports.Count * 8 + gotDataOrder.Count * 8];
        byte[] procParam = BuildProcParam();
        byte[] note = BuildNote();

        // Assign segment base addresses and file offsets on the grid; the header occupies the first page.
        ulong textAddr = 0;
        ulong roAddr = Align(textAddr + Align(textLen, 1) + (ulong)pltBytes.Length, SegAlign);
        // Read-only metadata order: rodata, dynsym, dynstr, hash, rela, procparam, note.
        ulong dynsymAddr = roAddr + roLen;
        ulong dynstrAddr = Align(dynsymAddr + (ulong)dynsymBytes.Length, 8);
        ulong hashAddr = Align(dynstrAddr + (ulong)dynstrBytes.Length, 8);
        ulong relaAddr = Align(hashAddr + (ulong)hashBytes.Length, 8);
        ulong relaDynAddr = Align(relaAddr + (ulong)relaBytes.Length, 8);
        ulong procAddr = Align(relaDynAddr + (ulong)relaDynBytes.Length, 8);
        ulong noteAddr = Align(procAddr + (ulong)procParam.Length, 4);
        ulong ehFrameHdrAddr = Align(noteAddr + (ulong)note.Length, 4);
        ulong roEndAddr = ehFrameHdrSize > 0 ? ehFrameHdrAddr + (ulong)ehFrameHdrSize : noteAddr + (ulong)note.Length;

        ulong pltAddr = Align(textAddr + textLen, 16);
        ulong dataAddr = Align(roEndAddr, SegAlign);
        ulong tlsAddr = dataAddr + tlsTemplateOffsetInData;
        ulong gotAddr = dataAddr + dataMem;
        ulong gotDataAddr = gotAddr + 24 + (ulong)imports.Count * 8;
        ulong dynamicAddr = Align(gotAddr + (ulong)gotBytes.Length, 8);
        ulong dataEndAddr = dynamicAddr + 0; // dynamic size added below

        // Import addresses (GOT slot + PLT entry).
        for (int i = 0; i < imports.Count; i++)
        {
            imports[i].GotAddress = gotAddr + 24 + (ulong)i * 8;
            imports[i].PltAddress = pltAddr + 16 + (ulong)i * 16;
        }
        // JUMP_SLOT relocations and PLT entries.
        for (int i = 0; i < imports.Count; i++)
        {
            int b = i * 24;
            BinaryPrimitives.WriteUInt64LittleEndian(relaBytes.AsSpan(b), imports[i].GotAddress);
            BinaryPrimitives.WriteUInt64LittleEndian(relaBytes.AsSpan(b + 8), ((ulong)imports[i].DynSymIndex << 32) | RJumpSlot);
            int p = 16 + i * 16;
            pltBytes[p] = 0xFF; pltBytes[p + 1] = 0x25;
            long disp = (long)imports[i].GotAddress - (long)(imports[i].PltAddress + 6);
            BinaryPrimitives.WriteInt32LittleEndian(pltBytes.AsSpan(p + 2), unchecked((int)disp));
        }

        // Section address resolver and application relocation (imports through their PLT entry,
        // GOT-data references through the GOT). Absolute references collect a dynamic relocation.
        ulong SectionAddr(ElfObject o, int i)
        {
            if (tlsOffset.TryGetValue((o, i), out ulong tlsO)) return tlsAddr + tlsO;
            // A reserved or out-of-range section index (a common-block or otherwise unsupported symbol)
            // has no section address; report it as a link error rather than indexing past the sections.
            if ((uint)i >= (uint)o.Sections.Count)
                throw new ElfLinkException($"{o.Origin}: a symbol refers to section index {i}, which the object does not define.");
            ElfSection s = o.Sections[i];
            ulong bas = s.IsExecutable ? textAddr : s.IsWritable ? dataAddr : roAddr;
            return bas + sectionOffsetInGroup[(o, i)];
        }
        var dynRelocs = new List<DynReloc>(gotDataOrder.Count + abs64Count);
        var sectionData = RelocateApp(resolution, importByName, SectionAddr, gotDataAddr, gotDataIndex, dynRelocs, tlsOffset, tlsAlignedMem);

        // Add the global-offset-table slots to the dynamic relocations: an imported symbol resolves
        // through the dynamic symbol table (GLOB_DAT); a defined symbol is relative to the load base.
        for (int i = 0; i < gotDataOrder.Count; i++)
        {
            ulong slot = gotDataAddr + (ulong)i * 8;
            if (importByName.TryGetValue(gotDataOrder[i], out Import? imp))
                dynRelocs.Add(new DynReloc(slot, RGlobDat, (uint)imp.DynSymIndex, 0));
            else
            {
                // Resolve with the defining object's context so a file-local symbol reaches its true
                // address, not the global table (which holds only global and weak names).
                (ElfObject o, int si) = gotDataSym[i];
                dynRelocs.Add(new DynReloc(slot, RRelative, 0, SymbolValue(resolution, importByName, SectionAddr, o, o.Symbols[si])));
            }
        }

        // Order the table so every base-relative record leads it: the loader treats the first
        // relative-count records as relative and fast-paths them.
        int relativeCount = 0, w = 0;
        foreach (DynReloc d in dynRelocs)
            if (d.Type == RRelative) { WriteRela(relaDynBytes, w++, d); relativeCount++; }
        foreach (DynReloc d in dynRelocs)
            if (d.Type != RRelative) WriteRela(relaDynBytes, w++, d);

        // Build the exception-frame index from the relocated frame bytes: their function pointers are
        // now resolved to runtime addresses.
        byte[] ehFrameHdr = [];
        if (ehFrameHdrSize > 0)
        {
            var entries = new List<EhFrame.Entry>();
            bool ok = true;
            foreach ((ElfObject obj, int i) in ehFrames)
            {
                byte[] bytes = sectionData.TryGetValue((obj, i), out byte[]? d) ? d : obj.Sections[i].Data;
                if (!EhFrame.TryParse(bytes, SectionAddr(obj, i), entries)) { ok = false; break; }
            }
            if (ok && entries.Count > 0)
                ehFrameHdr = EhFrame.Build(ehFrameHdrAddr, SectionAddr(ehFrames[0].Obj, ehFrames[0].Index), entries);
        }

        // Fill each export's address now that the layout is fixed.
        foreach ((int index, ElfObject obj, ElfSymbol sym) in exportDynIndex)
            BinaryPrimitives.WriteUInt64LittleEndian(dynsymBytes.AsSpan(index * 24 + 8),
                SymbolValue(resolution, importByName, SectionAddr, obj, sym));

        byte[] dynamicBytes = BuildDynamic(moduleRecords, moduleInfoName,
            dynsymAddr, dynstrAddr, (ulong)dynstrBytes.Length, hashAddr, (ulong)hashBytes.Length,
            relaAddr, (ulong)relaBytes.Length, gotAddr, dynsymBytes.Length,
            relaDynAddr, (ulong)relaDynBytes.Length, relativeCount,
            hasExports, origFileNameOff, moduleInfoName, exportLibId);

        ulong entry = 0;
        if (!string.IsNullOrEmpty(entrySymbol) && resolution.Defined.TryGetValue(entrySymbol, out ElfObject? eo))
            foreach (ElfSymbol s in eo.Symbols)
                if (!s.IsUndefined && s.Name == entrySymbol)
                    entry = SymbolValue(resolution, importByName, SectionAddr, eo, s);

        // Assemble the file.
        return WriteFile(resolution, kind, entry, sectionData, SectionAddr,
            text: (textAddr, textLen), pltAddr, pltBytes,
            roAddr, roLen, dynsymAddr, dynsymBytes, dynstrAddr, dynstrBytes, hashAddr, hashBytes,
            relaAddr, relaBytes, relaDynAddr, relaDynBytes, procAddr, procParam, noteAddr, note,
            ehFrameHdrAddr, ehFrameHdr, hasTls, tlsAddr, tlsFileLen, tlsMemLen, tlsAlign,
            dataAddr, dataLen, dataMem, gotAddr, gotBytes, dynamicAddr, dynamicBytes);
    }

    private static Dictionary<(ElfObject, int), byte[]> RelocateApp(
        LinkResolution resolution, Dictionary<string, Import> importByName, Func<ElfObject, int, ulong> sectionAddr,
        ulong gotDataAddr, Dictionary<string, int> gotDataIndex, List<DynReloc> dynRelocs,
        Dictionary<(ElfObject, int), ulong> tlsOffset, ulong tlsAlignedMem)
    {
        var result = new Dictionary<(ElfObject, int), byte[]>();
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
                foreach (ElfRelocation r in kv.Value)
                {
                    if (r.SymbolIndex >= (uint)obj.Symbols.Count) continue;
                    ElfSymbol sym = obj.Symbols[(int)r.SymbolIndex];
                    ulong place = secAddr + r.Offset;
                    int at = (int)r.Offset;
                    int width = r.Type is RelType.R64 or RelType.TpOff64 ? 8 : 4;
                    if (at < 0 || at + width > bytes.Length) continue;

                    if (r.Type == RelType.GotPcRel)
                    {
                        // The reference is fixed to point at the symbol's GOT entry, not the symbol.
                        // Only named symbols get a GOT slot (the collection pass skips empty names), so
                        // a GOT reference to an unnamed section symbol is unsupported rather than a crash.
                        if (!gotDataIndex.TryGetValue(sym.Name, out int gotDataSlot))
                            throw new ElfLinkException("GOT-relative relocation against an unnamed symbol is not supported.");
                        ulong gotSlot = gotDataAddr + (ulong)gotDataSlot * 8;
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(gotSlot + (ulong)r.Addend - place));
                        continue;
                    }

                    if (r.Type is RelType.TpOff32 or RelType.TpOff64)
                    {
                        // Local-exec thread-local reference: the value is the symbol's offset within the
                        // template minus the aligned template size, since the block sits below the
                        // thread pointer on this target.
                        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong templateOff))
                            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' has no template section.");
                        long tp = (long)templateOff - (long)tlsAlignedMem + r.Addend;
                        if (r.Type == RelType.TpOff64)
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)tp);
                        else
                            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), unchecked((uint)(int)tp));
                        continue;
                    }

                    ulong s = SymbolValue(resolution, importByName, sectionAddr, obj, sym);
                    switch (r.Type)
                    {
                        case RelType.R64:
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), s + (ulong)r.Addend);
                            // An absolute 64-bit reference needs a load-time fixup: a symbol record for
                            // an imported target, a base-relative record for a defined one.
                            if (sym.IsUndefined && importByName.TryGetValue(sym.Name, out Import? imp))
                                dynRelocs.Add(new DynReloc(place, RAbs64, (uint)imp.DynSymIndex, (ulong)r.Addend));
                            else
                                dynRelocs.Add(new DynReloc(place, RRelative, 0, s + (ulong)r.Addend));
                            break;
                        case RelType.Pc32:
                        case RelType.Plt32:
                            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(s + (ulong)r.Addend - place)); break;
                        case RelType.R32:
                        case RelType.R32S:
                            throw new ElfLinkException(
                                $"A 32-bit absolute relocation on '{sym.Name}' cannot be fixed up in a relocatable module; compile position-independent code.");
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

    private static ulong SymbolValue(
        LinkResolution resolution, Dictionary<string, Import> importByName,
        Func<ElfObject, int, ulong> sectionAddr, ElfObject obj, ElfSymbol sym)
    {
        // An absolute symbol's value is its final address; it is not relative to any section.
        if (sym.SectionIndex == ShnAbs) return sym.Value;
        if (sym.Type == SymType.Section) return sectionAddr(obj, sym.SectionIndex);
        if (!sym.IsUndefined) return sectionAddr(obj, sym.SectionIndex) + sym.Value;
        if (resolution.Defined.TryGetValue(sym.Name, out ElfObject? defObj))
            foreach (ElfSymbol d in defObj.Symbols)
                if (!d.IsUndefined && d.Name == sym.Name)
                    return d.SectionIndex == ShnAbs ? d.Value : sectionAddr(defObj, d.SectionIndex) + d.Value;
        if (importByName.TryGetValue(sym.Name, out Import? imp)) return imp.PltAddress;
        if (TryEncapsulationAddress(resolution, sectionAddr, sym.Name, out ulong enc)) return enc;
        if (sym.IsWeak) return 0;
        throw new ElfLinkException($"Unresolved symbol '{sym.Name}'.");
    }

    // The address of a section-boundary symbol: the lowest start (for __start_) or the highest end (for
    // __stop_) of the allocated sections that carry the named section across every included object.
    private static bool TryEncapsulationAddress(
        LinkResolution resolution, Func<ElfObject, int, ulong> sectionAddr, string name, out ulong addr)
    {
        addr = 0;
        bool isStop;
        string section;
        if (name.StartsWith("__start_", StringComparison.Ordinal)) { isStop = false; section = name["__start_".Length..]; }
        else if (name.StartsWith("__stop_", StringComparison.Ordinal)) { isStop = true; section = name["__stop_".Length..]; }
        else return false;
        if (section.Length == 0)
            return false;

        // The boundary spans the section's start to its end. The section is placed per object rather
        // than coalesced across objects, so a contiguous span is only well defined when a single object
        // carries the section. That is the case for the sections these symbols mark (the compiler emits
        // each in one object); a second contributor would leave the span covering unrelated data in
        // between, so it is refused rather than silently miscomputed.
        bool found = false;
        int contributors = 0;
        ulong min = ulong.MaxValue, max = 0;
        foreach (ElfObject o in resolution.Included)
            for (int i = 0; i < o.Sections.Count; i++)
            {
                ElfSection s = o.Sections[i];
                if (!s.IsAlloc || s.Name != section)
                    continue;
                ulong a = sectionAddr(o, i);
                if (a < min) min = a;
                if (a + s.Size > max) max = a + s.Size;
                found = true;
                contributors++;
            }
        if (!found)
            return false;
        if (contributors > 1)
            throw new ElfLinkException(
                $"Section '{section}' is carried by more than one object; the boundary symbol '{name}' would span unrelated data. Coalescing same-named sections is not supported.");
        addr = isStop ? max : min;
        return true;
    }

    // Finds the object and symbol that define a global name, for resolving an export to its address.
    private static bool TryFindDefinedSymbol(LinkResolution resolution, string name, out ElfObject? obj, out ElfSymbol? sym)
    {
        obj = null;
        sym = null;
        if (!resolution.Defined.TryGetValue(name, out ElfObject? defObj))
            return false;
        foreach (ElfSymbol s in defObj.Symbols)
            if (!s.IsUndefined && s.Name == name)
            {
                obj = defObj;
                sym = s;
                return true;
            }
        return false;
    }

    // The offset of a thread-local symbol within the template: its section's template offset plus the
    // symbol value, resolving a defined symbol through its own object.
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

    // Writes one 24-byte RELA record: the slot address, the packed symbol/type word, and the addend.
    private static void WriteRela(byte[] table, int index, DynReloc d)
    {
        int b = index * 24;
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b), d.Offset);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b + 8), ((ulong)d.Sym << 32) | d.Type);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(b + 16), d.Addend);
    }

    private static byte[] WriteFile(
        LinkResolution resolution, ModuleKind kind, ulong entry,
        Dictionary<(ElfObject, int), byte[]> sectionData, Func<ElfObject, int, ulong> sectionAddr,
        (ulong Addr, ulong Len) text, ulong pltAddr, byte[] plt,
        ulong roAddr, ulong roLen, ulong dynsymAddr, byte[] dynsym, ulong dynstrAddr, byte[] dynstr,
        ulong hashAddr, byte[] hash, ulong relaAddr, byte[] rela, ulong relaDynAddr, byte[] relaDyn,
        ulong procAddr, byte[] proc, ulong noteAddr, byte[] note, ulong ehFrameHdrAddr, byte[] ehFrameHdr,
        bool hasTls, ulong tlsAddr, ulong tlsFileLen, ulong tlsMemLen, ulong tlsAlign,
        ulong dataAddr, ulong dataLen, ulong dataMem, ulong gotAddr, byte[] got, ulong dynamicAddr, byte[] dynamic)
    {
        // Three load segments: [text|plt] RX, [rodata|metadata] R, [data|got|dynamic] RW.
        ulong textFileOff = SegAlign;
        ulong textSegEnd = pltAddr + (ulong)plt.Length;
        ulong roFileOff = textFileOff + Align(textSegEnd, SegAlign);
        ulong roSegEnd = ehFrameHdr.Length > 0 ? ehFrameHdrAddr + (ulong)ehFrameHdr.Length : noteAddr + (ulong)note.Length;
        ulong dataFileOff = roFileOff + Align(roSegEnd - roAddr, SegAlign);
        ulong dataSegEnd = dynamicAddr + (ulong)dynamic.Length;

        ulong fileEnd = dataFileOff + (dataSegEnd - dataAddr);
        byte[] file = new byte[Align(fileEnd, 16)];

        // ELF header.
        file[0] = 0x7F; file[1] = (byte)'E'; file[2] = (byte)'L'; file[3] = (byte)'F';
        file[4] = 2; file[5] = 1; file[6] = 1; file[7] = 9; file[8] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), kind == ModuleKind.Library ? TypeSceDynamic : TypeSceDynExec);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), 0x3E);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x18), entry);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x20), 0x40);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x34), 0x40);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x36), 0x38);
        int phnum = 6 + (ehFrameHdr.Length > 0 ? 1 : 0) + (hasTls ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x38), (ushort)phnum);

        // Program headers.
        int ph = 0x40;
        void WritePh(uint type, uint flags, ulong off, ulong va, ulong filesz, ulong memsz, ulong align)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph), type);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(ph + 4), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 8), off);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 16), va);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 24), va);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 32), filesz);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 40), memsz);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(ph + 48), align);
            ph += 0x38;
        }
        WritePh(PtLoad, PfR | PfX, textFileOff, text.Addr, textSegEnd - text.Addr, textSegEnd - text.Addr, SegAlign);
        WritePh(PtLoad, PfR, roFileOff, roAddr, roSegEnd - roAddr, roSegEnd - roAddr, SegAlign);
        WritePh(PtLoad, PfR | PfW, dataFileOff, dataAddr, dataSegEnd - dataAddr, dataSegEnd - dataAddr, SegAlign);
        WritePh(PtDynamic, PfR | PfW, dataFileOff + (dynamicAddr - dataAddr), dynamicAddr, (ulong)dynamic.Length, (ulong)dynamic.Length, 8);
        WritePh(PtSceProcParam, PfR, roFileOff + (procAddr - roAddr), procAddr, (ulong)proc.Length, (ulong)proc.Length, 8);
        WritePh(PtNote, PfR, roFileOff + (noteAddr - roAddr), noteAddr, (ulong)note.Length, (ulong)note.Length, 4);
        if (ehFrameHdr.Length > 0)
            WritePh(PtGnuEhFrame, PfR, roFileOff + (ehFrameHdrAddr - roAddr), ehFrameHdrAddr, (ulong)ehFrameHdr.Length, (ulong)ehFrameHdr.Length, 4);
        if (hasTls)
            WritePh(PtTls, PfR, dataFileOff + (tlsAddr - dataAddr), tlsAddr, tlsFileLen, tlsMemLen, tlsAlign);

        // Segment data.
        void Put(ulong segFileOff, ulong segBase, ulong addr, byte[] bytes)
            => bytes.AsSpan().CopyTo(file.AsSpan((int)(segFileOff + (addr - segBase))));

        foreach (ElfObject obj in resolution.Included)
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (!sec.IsAlloc || sec.IsNoBits || !sectionData.TryGetValue((obj, i), out byte[]? bytes)) continue;
                ulong a = sectionAddr(obj, i);
                (ulong segFileOff, ulong segBase) = sec.IsExecutable ? (textFileOff, text.Addr)
                    : sec.IsWritable ? (dataFileOff, dataAddr) : (roFileOff, roAddr);
                Put(segFileOff, segBase, a, bytes);
            }
        Put(textFileOff, text.Addr, pltAddr, plt);
        Put(roFileOff, roAddr, dynsymAddr, dynsym);
        Put(roFileOff, roAddr, dynstrAddr, dynstr);
        Put(roFileOff, roAddr, hashAddr, hash);
        Put(roFileOff, roAddr, relaAddr, rela);
        Put(roFileOff, roAddr, relaDynAddr, relaDyn);
        Put(roFileOff, roAddr, procAddr, proc);
        Put(roFileOff, roAddr, noteAddr, note);
        if (ehFrameHdr.Length > 0)
            Put(roFileOff, roAddr, ehFrameHdrAddr, ehFrameHdr);
        Put(dataFileOff, dataAddr, gotAddr, got);
        Put(dataFileOff, dataAddr, dynamicAddr, dynamic);
        return file;
    }

    private static byte[] BuildDynamic(
        (int SonameOff, int ModuleNameOff, int LibraryNameOff, int ModuleId, int LibraryId, ushort ModuleVersion, ushort LibraryVersion)[] modules, int moduleInfoName,
        ulong symtab, ulong strtab, ulong strsz, ulong hash, ulong hashsz,
        ulong jmprel, ulong pltrelsz, ulong pltgot, int dynsymSize,
        ulong rela, ulong relasz, int relativeCount,
        bool hasExports, int origFileNameOff, int exportLibNameOff, int exportLibId)
    {
        // Record value packs: nameOffset | (version << 32) | (id << 48). The module's own info and its
        // export library carry this module's version; each needed record carries the version that
        // module exports, so an import binds to the library the provider actually publishes.
        var e = new List<(long, ulong)>
        {
            (DtSceModuleInfo, (ulong)(uint)moduleInfoName | ((ulong)StubLibrary.DefaultModuleVersion << 32)),
            (DtSceModuleAttr, 0),
        };
        // A module that exports symbols records its own file name and its export library.
        if (hasExports)
        {
            e.Add((DtSceOrigFilename, (uint)origFileNameOff));
            e.Add((DtSceExportLib, (ulong)(uint)exportLibNameOff | ((ulong)StubLibrary.DefaultLibraryVersion << 32) | ((ulong)(uint)exportLibId << 48)));
            e.Add((DtSceExportLibAttr, ((ulong)(uint)exportLibId << 48) | 0x01));
        }
        foreach ((int sonameOff, int moduleNameOff, int libraryNameOff, int moduleId, int libraryId, ushort moduleVersion, ushort libraryVersion) in modules)
        {
            e.Add((DtNeeded, (uint)sonameOff));
            e.Add((DtSceNeededModule, (ulong)(uint)moduleNameOff | ((ulong)moduleVersion << 32) | ((ulong)(uint)moduleId << 48)));
            e.Add((DtSceImportLib, (ulong)(uint)libraryNameOff | ((ulong)libraryVersion << 32) | ((ulong)(uint)libraryId << 48)));
            e.Add((DtSceImportLibAttr, ((ulong)(uint)libraryId << 48) | 0x09));
        }
        e.Add((DtHash, hash)); e.Add((DtSceHashSz, hashsz));
        e.Add((DtSymTab, symtab)); e.Add((DtSceSymTab, symtab)); e.Add((DtSceSymTabSz, (ulong)dynsymSize));
        e.Add((DtSymEnt, 24)); e.Add((DtSceSymEnt, 24));
        e.Add((DtStrTab, strtab)); e.Add((DtSceStrTab, strtab)); e.Add((DtStrSz, strsz)); e.Add((DtSceStrSz, strsz));
        e.Add((DtPltGot, pltgot)); e.Add((DtPltRel, 7 /* DT_RELA */)); e.Add((DtPltRelSz, pltrelsz)); e.Add((DtJmpRel, jmprel));
        if (relasz > 0)
        {
            e.Add((DtRela, rela)); e.Add((DtSceRela, rela));
            e.Add((DtRelaSz, relasz)); e.Add((DtSceRelaSz, relasz));
            e.Add((DtRelaEnt, 24));
            if (relativeCount > 0) e.Add((DtRelaCount, (ulong)relativeCount));
        }
        e.Add((DtNull, 0));
        byte[] d = new byte[e.Count * 16];
        for (int i = 0; i < e.Count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(i * 16), e[i].Item1);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(i * 16 + 8), e[i].Item2);
        }
        return d;
    }

    // The symbol hash table, in the classic layout: nbucket, nchain, the buckets, then the chains.
    // <paramref name="count"/> is the number of symbol-table entries, including the null entry at
    // index 0. One bucket holds every symbol on a single chain a lookup walks by name: bucket[0]
    // points at the first real symbol, each chain slot points at the next, and the last points at the
    // undefined entry (0) to end the walk. A single bucket makes each lookup linear rather than
    // constant, which is unnoticeable for a module's symbol count and keeps the table small.
    private static byte[] BuildSysVHash(int count)
    {
        const int nbucket = 1;
        byte[] h = new byte[8 + nbucket * 4 + count * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(h, nbucket);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), (uint)count);

        int chainBase = 8 + nbucket * 4;
        if (count > 1)
        {
            // bucket[0] starts the chain at the first symbol after the null entry.
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8), 1);
            // chain[i] links symbol i to the next; chain[0] and the final chain entry stay 0 (the
            // undefined index), which both starts past the null symbol and terminates the walk.
            for (int i = 1; i < count - 1; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(chainBase + i * 4), (uint)(i + 1));
        }
        return h;
    }

    private static byte[] BuildProcParam()
    {
        byte[] p = new byte[0x60];
        BinaryPrimitives.WriteUInt64LittleEndian(p, 0x60);
        p[8] = (byte)'O'; p[9] = (byte)'R'; p[10] = (byte)'B'; p[11] = (byte)'I';
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x0C), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x10), 0x08050001);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x14), 0x02000009);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x58), 1);
        return p;
    }

    private static byte[] BuildNote()
    {
        byte[] n = new byte[0x24];
        BinaryPrimitives.WriteUInt32LittleEndian(n, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(n.AsSpan(4), 0x14);
        BinaryPrimitives.WriteUInt32LittleEndian(n.AsSpan(8), 3);
        n[12] = (byte)'G'; n[13] = (byte)'N'; n[14] = (byte)'U';
        return n;
    }

    private static string Encode(int id)
    {
        if (id == 0) return "A";
        var sb = new StringBuilder();
        while (id > 0) { sb.Insert(0, Alphabet[id % 64]); id /= 64; }
        return sb.ToString();
    }

    private static ulong Align(ulong v, ulong a) => a <= 1 ? v : (v + a - 1) / a * a;

    private sealed class StringTable
    {
        private readonly List<byte> _bytes = [0];
        private readonly Dictionary<string, int> _off = new(StringComparer.Ordinal);
        public int Add(string value)
        {
            if (_off.TryGetValue(value, out int o)) return o;
            int offset = _bytes.Count;
            _bytes.AddRange(Encoding.ASCII.GetBytes(value));
            _bytes.Add(0);
            _off[value] = offset;
            return offset;
        }
        public byte[] ToBytes() => [.. _bytes];
    }
}
