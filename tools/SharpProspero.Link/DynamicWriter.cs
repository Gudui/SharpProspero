// SharpProspero.Link - a linker for module output.
// Copyright (C) 2026 SvenGDK
//
// Writes a dynamic module: an application that imports functions from other modules. It builds the
// dynamic symbol and string tables with the mangled import names, a procedure-linkage table and its
// global-offset entries, the import relocations, the dynamic table with one needed record per
// imported module, the process parameters, and the module note, and lays them out with the load,
// dynamic, and process-parameter program headers.
//
// Four load segments: executable code, read-only data, writable data, and a fourth that requests no
// memory protection and holds the dynamic-linking tables. That fourth segment is what tells the
// loader where the linking data is; a module that names a dynamic table without carrying one is
// turned away while its program headers are scanned, so it is part of the required shape rather than
// a convenience.
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
    private const int EntryStubSize = 44; // the constructor-running entry prepended to the executable's start
    private const int ReservedNoteLen = 0x48; // the non-loaded note area a linked module reserves in its file tail
    private const uint PtLoad = 1, PtDynamic = 2, PtNote = 4, PtTls = 7, PtSceProcParam = 0x61000001, PtGnuEhFrame = 0x6474E550;
    private const uint PtGnuRelro = 0x6474E552, PtSceComment = 0x6FFFFF00, PtSceVersion = 0x6FFFFF01;

    private const long DtNeeded = 1, DtHash = 4, DtStrTab = 5, DtSymTab = 6, DtStrSz = 10, DtSymEnt = 11;
    private const long DtPltGot = 3, DtPltRelSz = 2, DtPltRel = 20, DtJmpRel = 23;
    private const long DtSceModuleInfo = 0x61000043, DtSceNeededModule = 0x61000045, DtSceImportLib = 0x61000049, DtSceImportLibAttr = 0x61000019;
    private const long DtSceOrigFilename = 0x61000041, DtSceExportLib = 0x61000047, DtSceExportLibAttr = 0x61000017;
    private const long DtSceModuleAttr = 0x61000011, DtSceSymTabSz = 0x6100003f, DtSceHashSz = 0x6100003d, DtNull = 0;
    private const long DtRela = 7, DtRelaSz = 8, DtRelaEnt = 9, DtRelaCount = 0x6ffffff9;
    private const long DtInitArray = 25, DtFiniArray = 26, DtInitArraySz = 27, DtFiniArraySz = 28;
    private const uint RJumpSlot = 7, RGlobDat = 6, RRelative = 8, RAbs64 = 1, RIRelative = 37;
    private const ushort ShnAbs = 0xFFF1; // an absolute symbol: its value is its address, not a section offset
    private const ushort ShnCommon = 0xFFF2; // a common (tentative, uninitialized global) symbol with no section
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

        // The init and fini arrays are laid out first, contiguously, so their combined address and size can
        // name a DT_INIT_ARRAY / DT_FINI_ARRAY for the loader to run global constructors and destructors.
        // Data sections are position-independent (their pointers are relocated), so placing these first is
        // safe. The runs stay contiguous because nothing else is placed into the data group between them.
        ulong initArrayOff = 0, initArrayEnd = 0, finiArrayOff = 0, finiArrayEnd = 0;
        bool haveInit = false, haveFini = false;
        void PlaceArray(string name, ref ulong start, ref ulong end, ref bool have)
        {
            foreach (ElfObject obj in resolution.Included)
                for (int i = 0; i < obj.Sections.Count; i++)
                {
                    ElfSection sec = obj.Sections[i];
                    if (sec is not { IsAlloc: true, IsTls: false, IsWritable: true } || sec.Name != name)
                        continue;
                    ulong o = Align(dataMem, sec.AddrAlign);
                    sectionOffsetInGroup[(obj, i)] = o;
                    dataMem = o + sec.Size;
                    if (!sec.IsNoBits) dataLen = dataMem;
                    if (!have) { start = o; have = true; }
                    end = dataMem;
                }
        }
        PlaceArray(".init_array", ref initArrayOff, ref initArrayEnd, ref haveInit);
        PlaceArray(".fini_array", ref finiArrayOff, ref finiArrayEnd, ref haveFini);

        // An executable runs its own global constructors: the loader runs the init array of a shared
        // library it loads, but not of the main executable - that is the start code's job. Without this
        // the module's initializers (including the runtime's own registration) never run, so it starts
        // and then fails on the first managed call. A small entry runs the array and continues to the
        // compiled start. A library keeps the init-array tag so the loader runs it as before.
        bool wantInitStub = haveInit && kind != ModuleKind.Library;

        foreach (ElfObject obj in resolution.Included)
        {
            for (int i = 0; i < obj.Sections.Count; i++)
            {
                ElfSection sec = obj.Sections[i];
                if (!sec.IsAlloc || sec.IsTls || sectionOffsetInGroup.ContainsKey((obj, i))) continue;
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
                    // A GOT-relative data load and an initial-exec thread-local load both need a GOT slot;
                    // the thread-local slot holds a link-time offset rather than an address (filled below).
                    if (RelType.IsGotPcRel(r.Type) || r.Type == RelType.GotTpOff)
                    {
                        string n = o.Symbols[(int)r.SymbolIndex].Name;
                        if (n.Length > 0 && gotDataIndex.TryAdd(n, gotDataOrder.Count))
                        {
                            gotDataOrder.Add(n);
                            gotDataSym.Add((o, (int)r.SymbolIndex));
                        }
                    }
                    else if (r.Type == RelType.R64 && ProducesDynReloc(resolution, importByName, o.Symbols[(int)r.SymbolIndex]))
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
        // A GOT-data slot carries a load-time relocation unless it targets an unresolved weak symbol,
        // which stays null with no fixup; count the ones that actually emit a record so the table is
        // sized exactly rather than padded with ignored entries.
        int gotDataRelocCount = 0;
        for (int i = 0; i < gotDataOrder.Count; i++)
        {
            ElfSymbol gs = gotDataSym[i].Obj.Symbols[gotDataSym[i].SymIndex];
            // A thread-local slot holds a fixed link-time offset with no load-time fixup.
            if (gs.Type != SymType.Tls && ProducesDynReloc(resolution, importByName, gs))
                gotDataRelocCount++;
        }
        byte[] relaDynBytes = new byte[(gotDataRelocCount + abs64Count) * 24];
        byte[] pltBytes = new byte[16 + imports.Count * 16 + (wantInitStub ? EntryStubSize : 0)];
        byte[] gotBytes = new byte[24 + imports.Count * 8 + gotDataOrder.Count * 8];
        byte[] procParam = BuildProcParam();
        byte[] note = BuildNote();
        string ownFileName = moduleFileName ?? (kind == ModuleKind.Library ? "prospero_module.prx" : "eboot.bin");
        byte[] comment = BuildComment(ownFileName);
        byte[] versionBlob = BuildVersion(ownFileName);

        // Assign segment base addresses and file offsets on the grid; the header occupies the first page.
        // The code group ends after the procedure-linkage table, which is aligned past the code, so the
        // group that follows starts from that end rather than from the unaligned sum. Starting from the
        // sum can place the next group below the end of this one when the code length is not already
        // aligned, which would overlap the two.
        ulong textAddr = 0;
        ulong pltAddr = Align(textAddr + textLen, 16);
        ulong textSegEndAddr = pltAddr + (ulong)pltBytes.Length;

        // Read-only group: rodata then the exception-frame index.
        ulong roAddr = Align(textSegEndAddr, SegAlign);
        ulong ehFrameHdrAddr = Align(roAddr + roLen, 4);
        ulong roEndAddr = ehFrameHdrSize > 0 ? ehFrameHdrAddr + (ulong)ehFrameHdrSize : roAddr + roLen;
        // First writable group: data and the global-offset table. A relocation-read-only header covers
        // exactly this group, which is the shape every module carries.
        ulong dataAddr = Align(roEndAddr, SegAlign);
        ulong tlsAddr = dataAddr + tlsTemplateOffsetInData;
        ulong gotAddr = dataAddr + dataMem;
        ulong gotDataAddr = gotAddr + 24 + (ulong)imports.Count * 8;
        ulong dataEndAddr = gotAddr + (ulong)gotBytes.Length;

        // Second writable group, holding the process parameters. A module carries two writable load
        // segments, the second reserving more memory than it stores; this one reserves a page and
        // stores only the parameters, which keeps the parameters inside a writable segment as they
        // have to be.
        ulong procAddr = Align(dataEndAddr, SegAlign);
        ulong procSegMem = SegAlign;

        // The dynamic-linking group holds every table the loader reads to bind the module: the symbol
        // and string tables, the hash, both relocation tables, the note, and the dynamic table itself.
        // It is a load segment carrying no memory protection, which is what marks it as linking data
        // rather than image content. A module that names a dynamic table without also carrying this
        // segment is rejected while its program headers are scanned, before any of its code runs, so
        // the group is not an optional nicety - the module does not start without it.
        ulong dynlibAddr = procAddr + procSegMem;
        ulong dynsymAddr = dynlibAddr;
        ulong dynstrAddr = Align(dynsymAddr + (ulong)dynsymBytes.Length, 8);
        ulong hashAddr = Align(dynstrAddr + (ulong)dynstrBytes.Length, 8);
        ulong relaAddr = Align(hashAddr + (ulong)hashBytes.Length, 8);
        ulong relaDynAddr = Align(relaAddr + (ulong)relaBytes.Length, 8);
        ulong noteAddr = Align(relaDynAddr + (ulong)relaDynBytes.Length, 4);
        ulong dynamicAddr = Align(noteAddr + (ulong)note.Length, 8);

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
            // A common (tentative) symbol carries no section; modern compilers place these in .bss by
            // default, so a clear message beats indexing past the sections with a bare index number.
            if (i == ShnCommon)
                throw new ElfLinkException($"{o.Origin}: a common (uninitialized global) symbol has no storage. Compile the object with -fno-common, the default on current compilers.");
            // A reserved or out-of-range section index has no section address; report it as a link
            // error rather than indexing past the sections.
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
            (ElfObject tobj, int tsi) = gotDataSym[i];
            ElfSymbol tsym = tobj.Symbols[tsi];
            if (tsym.Type == SymType.Tls)
            {
                // Initial-exec: the slot holds a fixed thread-pointer offset (template offset minus the
                // aligned template size, since the block sits below the thread pointer on this target), so
                // it is written into the slot at link time and needs no load-time relocation.
                if (!TryTlsTemplateOffset(resolution, tlsOffset, tobj, tsym, out ulong templateOffset))
                    throw new ElfLinkException($"Thread-local symbol '{tsym.Name}' referenced through the GOT has no template section.");
                int slotByte = 24 + imports.Count * 8 + i * 8;
                BinaryPrimitives.WriteUInt64LittleEndian(gotBytes.AsSpan(slotByte), (ulong)((long)templateOffset - (long)tlsAlignedMem));
                continue;
            }
            if (importByName.TryGetValue(gotDataOrder[i], out Import? imp))
                dynRelocs.Add(new DynReloc(slot, RGlobDat, (uint)imp.DynSymIndex, 0));
            else
            {
                // Resolve with the defining object's context so a file-local symbol reaches its true
                // address, not the global table (which holds only global and weak names). An unresolved
                // weak target keeps its zero-initialized slot, with no base-relative fixup, so that the
                // address-taken idiom sees null rather than the load base.
                (ElfObject o, int si) = gotDataSym[i];
                if (ProducesDynReloc(resolution, importByName, o.Symbols[si]))
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

        // The init/fini arrays sit in the data segment. A library advertises its init array so the loader
        // runs its constructors; an executable runs its own from the entry below, so its init-array tag is
        // left off.
        (ulong Address, ulong Size) initArray = haveInit ? (dataAddr + initArrayOff, initArrayEnd - initArrayOff) : (0, 0);
        (ulong Address, ulong Size) finiArray = haveFini ? (dataAddr + finiArrayOff, finiArrayEnd - finiArrayOff) : (0, 0);

        ulong entry = 0;
        bool entryFound = false;
        if (!string.IsNullOrEmpty(entrySymbol) && resolution.Defined.TryGetValue(entrySymbol, out ElfObject? eo))
            foreach (ElfSymbol s in eo.Symbols)
                if (!s.IsUndefined && s.Name == entrySymbol)
                {
                    entry = SymbolValue(resolution, importByName, SectionAddr, eo, s);
                    entryFound = true;
                }

        // Run the executable's constructors from a small entry that then continues to the compiled start,
        // and make it the module entry point. The stub bytes live in the tail of the procedure-linkage
        // table's space, already inside the executable segment and accounted for in the layout. The entry
        // address can legitimately be the module base (zero), so this keys on the symbol being found.
        if (wantInitStub && entryFound)
        {
            ulong stubAddr = pltAddr + 16 + (ulong)imports.Count * 16;
            BuildEntryStub(stubAddr, initArray.Address, initArray.Address + initArray.Size, entry)
                .CopyTo(pltBytes.AsSpan(16 + imports.Count * 16));
            entry = stubAddr;
        }

        byte[] dynamicBytes = BuildDynamic(moduleRecords, moduleInfoName,
            dynsymAddr, dynstrAddr, (ulong)dynstrBytes.Length, hashAddr, (ulong)hashBytes.Length,
            relaAddr, (ulong)relaBytes.Length, gotAddr, dynsymBytes.Length,
            relaDynAddr, (ulong)relaDynBytes.Length, relativeCount,
            hasExports, origFileNameOff, moduleInfoName, exportLibId,
            wantInitStub ? (0UL, 0UL) : initArray, finiArray);

        // Assemble the file.
        return WriteFile(resolution, kind, entry, sectionData, SectionAddr,
            text: (textAddr, textLen), pltAddr, pltBytes,
            roAddr, roLen, dynlibAddr, dynsymAddr, dynsymBytes, dynstrAddr, dynstrBytes, hashAddr, hashBytes,
            relaAddr, relaBytes, relaDynAddr, relaDynBytes, procAddr, procSegMem, procParam, noteAddr, note,
            ehFrameHdrAddr, ehFrameHdr, hasTls, tlsAddr, tlsFileLen, tlsMemLen, tlsAlign,
            dataAddr, dataLen, dataMem, gotAddr, gotBytes, dynamicAddr, dynamicBytes, comment, versionBlob);
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

                // A general- or local-dynamic thread-local sequence is a lea followed by a call
                // __tls_get_addr. Relaxing the lea rewrites both instructions, so the call's relocation is
                // folded away rather than applied on its own. The call sits eight bytes past a general-
                // dynamic lea's relocation and five past a local-dynamic one.
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
                        // Relax to local-exec: replace the 16-byte lea/call pair with a read of the thread
                        // pointer and a fixed offset load, leaving the address in rax as the call did.
                        if (at - 4 < 0 || at + 12 > bytes.Length)
                            throw new ElfLinkException($"{obj.Origin}: a thread-local sequence on '{sym.Name}' runs past the section.");
                        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong gdTemplateOff))
                            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' has no template section.");
                        long le = (long)gdTemplateOff - (long)tlsAlignedMem;
                        ReadOnlySpan<byte> localExec = [0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x80];
                        localExec.CopyTo(bytes.AsSpan(at - 4)); // mov %fs:0,%rax ; lea ...(%rax),%rax
                        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 8), checked((int)le));
                        continue;
                    }

                    if (r.Type == RelType.TlsLd)
                    {
                        // Relax the module-base lookup to a thread-pointer read: the block is this module's,
                        // so each member (its DTPOFF relocations below) becomes a local-exec offset from the
                        // thread pointer. The nop prefixes keep the replacement the same size as the pair.
                        if (at - 3 < 0 || at + 9 > bytes.Length)
                            throw new ElfLinkException($"{obj.Origin}: a thread-local base sequence on '{sym.Name}' runs past the section.");
                        ReadOnlySpan<byte> threadPointer = [0x66, 0x66, 0x66, 0x64, 0x48, 0x8B, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00];
                        threadPointer.CopyTo(bytes.AsSpan(at - 3)); // (nop) mov %fs:0,%rax
                        continue;
                    }

                    int width = r.Type is RelType.R64 or RelType.TpOff64 or RelType.Pc64 or RelType.DtpOff64 ? 8 : 4;
                    if (at < 0 || at + width > bytes.Length) continue;

                    if (RelType.IsGotPcRel(r.Type) || r.Type == RelType.GotTpOff)
                    {
                        // The reference is fixed to point at the symbol's GOT entry, not the symbol. The
                        // relaxable data variants (GOTPCRELX/REX_GOTPCRELX) and the initial-exec
                        // thread-local variant (GOTTPOFF) all resolve to the same slot address; what the
                        // slot holds (an address versus a thread-pointer offset) is filled separately.
                        // Only named symbols get a GOT slot (the collection pass skips empty names), so a
                        // GOT reference to an unnamed section symbol is unsupported rather than a crash.
                        if (!gotDataIndex.TryGetValue(sym.Name, out int gotDataSlot))
                            throw new ElfLinkException("GOT-relative relocation against an unnamed symbol is not supported.");
                        ulong gotSlot = gotDataAddr + (ulong)gotDataSlot * 8;
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(gotSlot + (ulong)r.Addend - place));
                        continue;
                    }

                    if (r.Type is RelType.TpOff32 or RelType.TpOff64 or RelType.DtpOff32 or RelType.DtpOff64)
                    {
                        // Local-exec thread-local reference: the value is the symbol's offset within the
                        // template minus the aligned template size, since the block sits below the thread
                        // pointer on this target. A module-block offset (DTPOFF, once its base is relaxed to
                        // the thread pointer above) resolves to the same value.
                        if (!TryTlsTemplateOffset(resolution, tlsOffset, obj, sym, out ulong templateOff))
                            throw new ElfLinkException($"Thread-local symbol '{sym.Name}' has no template section.");
                        long tp = (long)templateOff - (long)tlsAlignedMem + r.Addend;
                        if (r.Type is RelType.TpOff64 or RelType.DtpOff64)
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)tp);
                        else
                            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), unchecked((uint)(int)tp));
                        continue;
                    }

                    ulong s = SymbolValue(resolution, importByName, sectionAddr, obj, sym);
                    switch (r.Type)
                    {
                        case RelType.None:
                            break;
                        case RelType.R64:
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), s + (ulong)r.Addend);
                            // An absolute 64-bit reference needs a load-time fixup: a symbol record for
                            // an imported target, a base-relative record for a defined one. An indirect
                            // function is special: the loader calls its resolver and stores the result, so
                            // the record is an irelative whose addend is the resolver address. An
                            // unresolved weak reference resolves to absolute zero and needs no fixup, so
                            // leave it out rather than read the load base.
                            if (sym.Type == SymType.GnuIfunc && !sym.IsUndefined)
                                dynRelocs.Add(new DynReloc(place, RIRelative, 0, s + (ulong)r.Addend));
                            else if (sym.IsUndefined && importByName.TryGetValue(sym.Name, out Import? imp))
                                dynRelocs.Add(new DynReloc(place, RAbs64, (uint)imp.DynSymIndex, (ulong)r.Addend));
                            else if (ProducesDynReloc(resolution, importByName, sym))
                                dynRelocs.Add(new DynReloc(place, RRelative, 0, s + (ulong)r.Addend));
                            break;
                        case RelType.Pc32:
                        case RelType.Plt32:
                            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)(long)(s + (ulong)r.Addend - place)); break;
                        case RelType.Pc64:
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)((long)(s + (ulong)r.Addend) - (long)place)); break;
                        case RelType.R32:
                        case RelType.R32S:
                            throw new ElfLinkException(
                                $"A 32-bit absolute relocation on '{sym.Name}' cannot be fixed up in a relocatable module; compile position-independent code.");
                        default:
                            // Fail loudly on an unhandled type rather than silently leaving the target
                            // bytes at their compiler placeholder, which would miscompile without warning.
                            throw new ElfLinkException(
                                $"{obj.Origin}: unsupported relocation type {r.Type} on '{sym.Name}'. The linker resolves absolute, PC-relative, PLT, GOT-relative and local-exec thread-local references.");
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

    // Whether an absolute or GOT-data reference to this symbol emits a load-time dynamic relocation.
    // A symbol defined here or in another included object needs a base-relative fixup, and an imported
    // one resolves through the dynamic symbol table. An unresolved weak reference resolves to absolute
    // zero and emits nothing, so an address-taken weak symbol reads as null rather than the load base.
    private static bool ProducesDynReloc(LinkResolution resolution, Dictionary<string, Import> importByName, ElfSymbol sym)
        => !sym.IsUndefined || importByName.ContainsKey(sym.Name) || resolution.Defined.ContainsKey(sym.Name);

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
        ulong roAddr, ulong roLen, ulong dynlibAddr, ulong dynsymAddr, byte[] dynsym, ulong dynstrAddr, byte[] dynstr,
        ulong hashAddr, byte[] hash, ulong relaAddr, byte[] rela, ulong relaDynAddr, byte[] relaDyn,
        ulong procAddr, ulong procSegMem, byte[] proc, ulong noteAddr, byte[] note, ulong ehFrameHdrAddr, byte[] ehFrameHdr,
        bool hasTls, ulong tlsAddr, ulong tlsFileLen, ulong tlsMemLen, ulong tlsAlign,
        ulong dataAddr, ulong dataLen, ulong dataMem, ulong gotAddr, byte[] got, ulong dynamicAddr, byte[] dynamic,
        byte[] comment, byte[] versionBlob)
    {
        // Five load segments: [code|plt] execute-only, [rodata|eh_frame] read, [data|got] read-write
        // covered by a relro header, [procparam] read-write reserving a page, and the dynamic-linking
        // tables in a segment with no protection. Each group starts a page past the end of the one
        // before it in both address and file offset, so the two stay a fixed distance apart.
        ulong textFileOff = SegAlign;
        ulong textSegEnd = pltAddr + (ulong)plt.Length;
        ulong roFileOff = textFileOff + Align(textSegEnd, SegAlign);
        ulong roSegEnd = ehFrameHdr.Length > 0 ? ehFrameHdrAddr + (ulong)ehFrameHdr.Length : roAddr + roLen;
        ulong dataFileOff = roFileOff + Align(roSegEnd - roAddr, SegAlign);
        ulong dataSegEnd = gotAddr + (ulong)got.Length;
        ulong procFileOff = dataFileOff + Align(dataSegEnd - dataAddr, SegAlign);
        ulong dynlibFileOff = procFileOff + procSegMem;
        // The version record closes the linking segment rather than sitting past it. Only content that
        // belongs to a stored segment survives the container round-trip, and a record left outside every
        // segment would come back zero-filled, so the image would no longer match its own digest.
        ulong versionAddr = Align(dynamicAddr + (ulong)dynamic.Length, 16);
        ulong dynlibSegEnd = versionAddr + (ulong)versionBlob.Length;

        // The file tail, outside every load segment: the comment, which the container stores as a
        // segment of its own, and then the reserved note. Both are 16-aligned and the image ends exactly
        // at the extent the last program header records.
        ulong commentFileOff = Align(dynlibFileOff + (dynlibSegEnd - dynlibAddr), 16);
        ulong reservedNoteFileOff = Align(commentFileOff + (ulong)comment.Length, 16);
        byte[] file = new byte[reservedNoteFileOff + ReservedNoteLen];

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
        // The code, writable, parameter and dynamic-linking load segments, the relro header covering
        // the writable group, the dynamic and process-parameter headers, the comment, the version
        // record, the module note and the reserved note - eleven. The read-only segment is added only
        // when it carries something, and the frame index and thread-local template when present.
        bool hasRo = roSegEnd > roAddr;
        int phnum = 11 + (hasRo ? 1 : 0) + (ehFrameHdr.Length > 0 ? 1 : 0) + (hasTls ? 1 : 0);
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
        // Execute without read. A load segment that asks for both is refused outright, so the code
        // segment carries the execute bit alone; the loader grants the read access the processor needs
        // when it maps the segment. Read-only content belongs in the segment below, not beside code.
        WritePh(PtLoad, PfX, textFileOff, text.Addr, textSegEnd - text.Addr, textSegEnd - text.Addr, SegAlign);
        if (hasRo)
            WritePh(PtLoad, PfR, roFileOff, roAddr, roSegEnd - roAddr, roSegEnd - roAddr, SegAlign);
        ulong dataSegLen = dataSegEnd - dataAddr;
        WritePh(PtLoad, PfR | PfW, dataFileOff, dataAddr, dataSegLen, dataSegLen, SegAlign);
        // A relro header is only accepted when a writable load segment matches it on offset, address
        // and both sizes, so it is written to cover the writable group exactly.
        WritePh(PtGnuRelro, PfR, dataFileOff, dataAddr, dataSegLen, dataSegLen, 1);
        // The second writable segment reserves more memory than it stores; the process parameters are
        // the only thing it carries in the file.
        WritePh(PtLoad, PfR | PfW, procFileOff, procAddr, (ulong)proc.Length, procSegMem, SegAlign);
        WritePh(PtSceProcParam, PfR, procFileOff, procAddr, (ulong)proc.Length, (ulong)proc.Length, 8);
        WritePh(PtDynamic, PfR | PfW, dynlibFileOff + (dynamicAddr - dynlibAddr), dynamicAddr, (ulong)dynamic.Length, (ulong)dynamic.Length, 8);
        if (hasTls)
            WritePh(PtTls, PfR, dataFileOff + (tlsAddr - dataAddr), tlsAddr, tlsFileLen, tlsMemLen, tlsAlign);
        if (ehFrameHdr.Length > 0)
            WritePh(PtGnuEhFrame, PfR, roFileOff + (ehFrameHdrAddr - roAddr), ehFrameHdrAddr, (ulong)ehFrameHdr.Length, (ulong)ehFrameHdr.Length, 4);
        // The dynamic-linking segment requests no protection: the loader reads it to bind the module
        // rather than mapping it into the running image.
        WritePh(PtLoad, 0, dynlibFileOff, dynlibAddr, dynlibSegEnd - dynlibAddr, dynlibSegEnd - dynlibAddr, SegAlign);
        // The comment reserves no memory at all, which is what the loader checks it for.
        WritePh(PtSceComment, 0, commentFileOff, 0, (ulong)comment.Length, 0, 0x10);
        WritePh(PtSceVersion, 0, dynlibFileOff + (versionAddr - dynlibAddr), 0, (ulong)versionBlob.Length, (ulong)versionBlob.Length, 1);
        WritePh(PtNote, 0, dynlibFileOff + (noteAddr - dynlibAddr), noteAddr, (ulong)note.Length, (ulong)note.Length, 4);
        // The reserved note carries no load address (memsz 0); it is present in the file only.
        WritePh(PtNote, 0, reservedNoteFileOff, 0, ReservedNoteLen, 0, 4);

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
        if (ehFrameHdr.Length > 0)
            Put(roFileOff, roAddr, ehFrameHdrAddr, ehFrameHdr);
        Put(dataFileOff, dataAddr, gotAddr, got);
        Put(procFileOff, procAddr, procAddr, proc);
        comment.AsSpan().CopyTo(file.AsSpan((int)commentFileOff));
        Put(dynlibFileOff, dynlibAddr, versionAddr, versionBlob);
        Put(dynlibFileOff, dynlibAddr, dynsymAddr, dynsym);
        Put(dynlibFileOff, dynlibAddr, dynstrAddr, dynstr);
        Put(dynlibFileOff, dynlibAddr, hashAddr, hash);
        Put(dynlibFileOff, dynlibAddr, relaAddr, rela);
        Put(dynlibFileOff, dynlibAddr, relaDynAddr, relaDyn);
        Put(dynlibFileOff, dynlibAddr, noteAddr, note);
        Put(dynlibFileOff, dynlibAddr, dynamicAddr, dynamic);

        // Stamp the build-id note's descriptor with a content fingerprint of the finished image, so
        // the module carries a real, reproducible identifier rather than a run of zeros. The 20-byte
        // descriptor sits 16 bytes into the note (after its name/size/type header and the "GNU" name);
        // it is hashed while still zero, so the same inputs always yield the same identifier.
        int noteFileOff = (int)(dynlibFileOff + (noteAddr - dynlibAddr));
        byte[] buildId = System.Security.Cryptography.SHA1.HashData(file);
        buildId.AsSpan(0, 20).CopyTo(file.AsSpan(noteFileOff + 16, 20));
        return file;
    }

    private static byte[] BuildDynamic(
        (int SonameOff, int ModuleNameOff, int LibraryNameOff, int ModuleId, int LibraryId, ushort ModuleVersion, ushort LibraryVersion)[] modules, int moduleInfoName,
        ulong symtab, ulong strtab, ulong strsz, ulong hash, ulong hashsz,
        ulong jmprel, ulong pltrelsz, ulong pltgot, int dynsymSize,
        ulong rela, ulong relasz, int relativeCount,
        bool hasExports, int origFileNameOff, int exportLibNameOff, int exportLibId,
        (ulong Address, ulong Size) initArray, (ulong Address, ulong Size) finiArray)
    {
        // Record value packs: nameOffset | (version << 32) | (id << 48). The module's own info and its
        // export library carry this module's version; each needed record carries the version that
        // module exports, so an import binds to the library the provider actually publishes.
        var e = new List<(long, ulong)>
        {
            (DtSceModuleInfo, (ulong)(uint)moduleInfoName | ((ulong)StubLibrary.DefaultModuleVersion << 32)),
            (DtSceModuleAttr, 0),
        };
        // Every module records its own file name, whether or not it exports anything.
        e.Add((DtSceOrigFilename, (uint)origFileNameOff));
        // A module that exports symbols also records the library it publishes them under.
        if (hasExports)
        {
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
        // The tables are named by the ordinary tags. A module whose linking segment carries an address
        // must not also name them through the module-specific aliases: the loader routes an alias and
        // its ordinary tag to the same handler, so the alias reads as a duplicate and the module is
        // refused while its dynamic table is being read. Only the two size tags below have no ordinary
        // equivalent, and both are required.
        e.Add((DtHash, hash)); e.Add((DtSceHashSz, hashsz));
        e.Add((DtSymTab, symtab)); e.Add((DtSceSymTabSz, (ulong)dynsymSize));
        e.Add((DtSymEnt, 24));
        e.Add((DtStrTab, strtab)); e.Add((DtStrSz, strsz));
        e.Add((DtPltGot, pltgot)); e.Add((DtPltRel, 7 /* DT_RELA */)); e.Add((DtPltRelSz, pltrelsz)); e.Add((DtJmpRel, jmprel));
        if (initArray.Size > 0) { e.Add((DtInitArray, initArray.Address)); e.Add((DtInitArraySz, initArray.Size)); }
        if (finiArray.Size > 0) { e.Add((DtFiniArray, finiArray.Address)); e.Add((DtFiniArraySz, finiArray.Size)); }
        // The relocation table is named even when it is empty. The loader checks that the table, its
        // size and its entry size were all named and refuses the module if any is missing, so leaving
        // them out for a module with nothing to relocate would make that module unloadable. An empty
        // table is expressed as a zero size, which the loader accepts.
        e.Add((DtRela, rela));
        e.Add((DtRelaSz, relasz));
        e.Add((DtRelaEnt, 24));
        e.Add((DtRelaCount, (ulong)relativeCount));
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

    // A position-independent entry that runs the global constructors in [initStart, initEnd) - each a
    // relocated function pointer, skipping any left null - then jumps to the compiled start with the stack
    // the loader set up intact. It addresses the array and the start with instruction-relative
    // displacements, so it needs no load-time relocation. The loader enters it with a 16-aligned stack, and
    // the two saved registers keep every call aligned. See <see cref="EntryStubSize"/> for the byte count.
    private static byte[] BuildEntryStub(ulong stubAddr, ulong initStart, ulong initEnd, ulong start)
    {
        // Byte offsets, used for the branch displacements: loop=16, skip=31, done=37, and the code ends at
        // 44. rbx walks the array, rbp marks its end.
        byte[] c = new byte[EntryStubSize];
        int i = 0;
        c[i++] = 0x53;                                     // 0:  push rbx
        c[i++] = 0x55;                                     // 1:  push rbp
        c[i++] = 0x48; c[i++] = 0x8D; c[i++] = 0x1D;       // 2:  lea rbx, [rip + (initStart - next)]
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(i), checked((int)((long)initStart - (long)(stubAddr + 9)))); i += 4;
        c[i++] = 0x48; c[i++] = 0x8D; c[i++] = 0x2D;       // 9:  lea rbp, [rip + (initEnd - next)]
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(i), checked((int)((long)initEnd - (long)(stubAddr + 16)))); i += 4;
        c[i++] = 0x48; c[i++] = 0x39; c[i++] = 0xEB;       // 16: loop: cmp rbx, rbp
        c[i++] = 0x73; c[i++] = 0x10;                      // 19: jae done      (-> 37)
        c[i++] = 0x48; c[i++] = 0x8B; c[i++] = 0x03;       // 21: mov rax, [rbx]
        c[i++] = 0x48; c[i++] = 0x85; c[i++] = 0xC0;       // 24: test rax, rax
        c[i++] = 0x74; c[i++] = 0x02;                      // 27: jz skip       (-> 31, past the call)
        c[i++] = 0xFF; c[i++] = 0xD0;                      // 29: call rax
        c[i++] = 0x48; c[i++] = 0x83; c[i++] = 0xC3; c[i++] = 0x08; // 31: skip: add rbx, 8
        c[i++] = 0xEB; c[i++] = 0xEB;                      // 35: jmp loop      (-> 16)
        c[i++] = 0x5D;                                     // 37: done: pop rbp
        c[i++] = 0x5B;                                     // 38: pop rbx
        c[i++] = 0xE9;                                     // 39: jmp start
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(i), checked((int)((long)start - (long)(stubAddr + 44)))); i += 4;
        return c;
    }

    private static byte[] BuildProcParam()
    {
        byte[] p = new byte[0x60];
        BinaryPrimitives.WriteUInt64LittleEndian(p, 0x60);
        p[8] = (byte)'O'; p[9] = (byte)'R'; p[10] = (byte)'B'; p[11] = (byte)'I';
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x0C), 5);
        // Two version words the platform runtime reads from the process parameters. The first is a
        // fixed legacy-compatibility stamp; the second is the meaningful one - the SDK version the
        // module targets, kept in step with the platform version header (major 2, revision 0x26).
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x10), 0x08050001);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x14), 0x02000026);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x58), 1);
        return p;
    }

    // The comment segment. Its content is not read - the loader checks only that it reserves no
    // memory and that its size and offset fit - but every module carries one, so one is written. The
    // form follows what a module carries: a tag, the length of the text, then the text.
    private static byte[] BuildComment(string moduleFileName)
    {
        byte[] text = Encoding.ASCII.GetBytes(moduleFileName);
        byte[] blob = new byte[AlignInt(12 + text.Length, 16)];
        Encoding.ASCII.GetBytes("PATH\\").CopyTo(blob, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(8), (uint)text.Length);
        text.CopyTo(blob, 12);
        return blob;
    }

    // The version segment, recording the module's own name and version. The loader passes over it; it
    // is written because every module carries one.
    private static byte[] BuildVersion(string moduleFileName)
    {
        byte[] name = Encoding.ASCII.GetBytes(moduleFileName + ":");
        byte[] blob = new byte[AlignInt(4 + name.Length + 8, 16)];
        BinaryPrimitives.WriteUInt16LittleEndian(blob, (ushort)(name.Length + 8));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2), StubLibrary.DefaultModuleVersion);
        name.CopyTo(blob, 4);
        return blob;
    }

    private static int AlignInt(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

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
