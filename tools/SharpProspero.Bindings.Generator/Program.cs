// SharpProspero.Bindings.Generator - turns the SDK headers into C# interop bindings.
// Copyright (C) 2026 SvenGDK
//
// Two ways to produce bindings. The `prx` command reads a supplied module and emits a wrapper for
// its exports, needing nothing but the module. The `stub` command writes a link stub for a module.
// The header path (no subcommand) writes response files describing a header-to-C# run for external
// processing; it makes no external calls of its own.

using SharpProspero.Link;
using SharpProspero.Prx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SharpProspero.Bindings.Generator;

/// <summary>One module in the catalog: the header to parse and how to shape its bindings.</summary>
internal sealed class ModuleSpec
{
    public string Name { get; set; } = "";
    public string Header { get; set; } = "";
    public string Library { get; set; } = "";
    public string MethodClassName { get; set; } = "";
    public string? Namespace { get; set; }
    public string[]? Config { get; set; }
    public string[]? Exclude { get; set; }
    public Dictionary<string, string>? Remap { get; set; }
    public string[]? AdditionalArgs { get; set; }
}

internal sealed class Catalog
{
    public string[]? DefaultConfig { get; set; }
    public string[]? DefaultAdditionalArgs { get; set; }
    public List<ModuleSpec> Modules { get; set; } = [];
}

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> KnownVerbs = new(StringComparer.Ordinal)
    {
        "prx", "stub", "crt", "compat", "nid", "elf", "self", "offsets", "retarget", "sysver", "link", "diff",
    };

    private static int Main(string[] args)
    {
        // Report the tool version for a bare `version`, or for `--version` only when no command leads:
        // a leading command may take its own `--version` option (sysver settles a required system
        // version), which must not be shadowed by the global flag.
        bool versionQuery = (args.Length > 0 && string.Equals(args[0], "version", StringComparison.Ordinal))
            || (HasFlag(args, "--version") && (args.Length == 0 || !KnownVerbs.Contains(args[0])));
        if (versionQuery)
        {
            Console.WriteLine(ToolVersion());
            return 0;
        }

        // A help flag prints the usage of the named command when one leads, or the whole list otherwise.
        if (HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            if (args.Length > 0 && KnownVerbs.Contains(args[0]))
                PrintVerbUsage(args[0]);
            else
                PrintUsage();
            return 0;
        }

        // Bindings can be generated from a supplied module instead of from headers, so a user needs
        // only their own .prx to interact with it.
        if (args.Length > 0 && string.Equals(args[0], "prx", StringComparison.Ordinal))
            return RunPrx(args);

        // Generates a link stub so the linker resolves calls to a module the project supplies.
        if (args.Length > 0 && string.Equals(args[0], "stub", StringComparison.Ordinal))
            return RunStub(args);

        // Writes the start object that carries the program entry point.
        if (args.Length > 0 && string.Equals(args[0], "crt", StringComparison.Ordinal))
            return RunCrt(args);
        if (args.Length > 0 && string.Equals(args[0], "compat", StringComparison.Ordinal))
            return RunCompat(args);

        // Prints the identifier a module export is keyed by, for a plain symbol name.
        if (args.Length > 0 && string.Equals(args[0], "nid", StringComparison.Ordinal))
            return RunNid(args);

        // Prints an ELF module's header, without an external tool.
        if (args.Length > 0 && string.Equals(args[0], "elf", StringComparison.Ordinal))
            return RunElf(args);

        // Converts between the unsigned (.elf/.prx) and signed (.self/.sprx) forms, and reports which
        // a file is.
        if (args.Length > 0 && string.Equals(args[0], "self", StringComparison.Ordinal))
            return RunSelf(args);

        // Dumps a supplied module's export identifiers and addresses (and, with --coverage, how it
        // covers the names the SDK needs), so a firmware's facts can be contributed.
        if (args.Length > 0 && string.Equals(args[0], "offsets", StringComparison.Ordinal))
            return RunOffsets(args);

        // Rewrites the version a supplied module targets (and, optionally, a library version tag), so a
        // module built for one system can be retargeted to another.
        if (args.Length > 0 && string.Equals(args[0], "retarget", StringComparison.Ordinal))
            return RunRetarget(args);

        // Settles the system version an application requires against the modules it ships.
        if (args.Length > 0 && string.Equals(args[0], "sysver", StringComparison.Ordinal))
            return RunSysVer(args);

        // Resolves the symbol graph of a set of objects and archives.
        if (args.Length > 0 && string.Equals(args[0], "link", StringComparison.Ordinal))
            return RunLink(args);

        // Compares the export surfaces of two modules, across firmware versions.
        if (args.Length > 0 && string.Equals(args[0], "diff", StringComparison.Ordinal))
            return RunDiff(args);

        // A leading token that names no command is a mistyped verb: report it rather than falling through
        // to the header-generation default, which would fail later with an unrelated SDK-path message.
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'. Run with --help for the list of commands.");
            return 2;
        }

        string toolRoot = AppContext.BaseDirectory;
        string sdkInclude = GetOption(args, "--sdk") ?? DefaultSdkInclude();
        string modulesPath = GetOption(args, "--modules") ?? Path.Combine(toolRoot, "modules.json");
        string outDir = GetOption(args, "--out") ?? DefaultOutputDir();
        string responsesDir = GetOption(args, "--responses") ?? Path.Combine(outDir, "responses");

        if (!Directory.Exists(sdkInclude))
        {
            Console.Error.WriteLine($"SDK include folder not found: {sdkInclude}");
            Console.Error.WriteLine("Pass --sdk <folder> or set PROSPERO_SDK_DIR.");
            return 1;
        }
        if (!File.Exists(modulesPath))
        {
            Console.Error.WriteLine($"Module catalog not found: {modulesPath}");
            return 1;
        }

        Catalog catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(modulesPath), JsonOptions)
                          ?? new Catalog();

        Directory.CreateDirectory(responsesDir);
        Directory.CreateDirectory(outDir);

        int emitted = 0;
        foreach (ModuleSpec module in catalog.Modules)
        {
            string responsePath = Path.Combine(responsesDir, module.Name + ".rsp");
            string response = BuildResponse(module, catalog, sdkInclude, outDir);
            File.WriteAllText(responsePath, response);
            Console.WriteLine($"Wrote {responsePath}");
            emitted++;
        }

        Console.WriteLine();
        Console.WriteLine($"{emitted} response file(s) in {responsesDir}");
        Console.WriteLine("These describe a header-to-C# run for external processing. To generate bindings");
        Console.WriteLine("without the headers, use `prx --module <file.prx> --names <file>`.");
        return 0;
    }

    private static string BuildResponse(ModuleSpec module, Catalog catalog, string sdkInclude, string outDir)
    {
        string headerPath = Path.Combine(sdkInclude, module.Header);
        string ns = string.IsNullOrWhiteSpace(module.Namespace)
            ? $"SharpProspero.Interop.{module.Name}"
            : module.Namespace!;
        string outputFile = Path.Combine(outDir, module.Name, module.Name + ".g.cs");

        var sb = new StringBuilder();
        sb.AppendLine("# Generated by SharpProspero.Bindings.Generator. Edit modules.json, not this file.");
        AppendArg(sb, "--file", headerPath);
        AppendArg(sb, "--output", outputFile);
        AppendArg(sb, "--namespace", ns);
        AppendArg(sb, "--methodClassName", module.MethodClassName);
        AppendArg(sb, "--libraryPath", module.Library);
        AppendArg(sb, "--include-directory", sdkInclude);
        AppendArg(sb, "--traverse", headerPath);

        string[] config = module.Config ?? catalog.DefaultConfig ?? DefaultConfig;
        if (config.Length > 0)
        {
            sb.Append("--config");
            foreach (string token in config)
                sb.Append(' ').Append(token);
            sb.AppendLine();
        }

        if (module.Remap is { Count: > 0 })
        {
            sb.Append("--remap");
            foreach (KeyValuePair<string, string> pair in module.Remap)
                sb.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
            sb.AppendLine();
        }

        if (module.Exclude is { Length: > 0 })
        {
            sb.Append("--exclude");
            foreach (string name in module.Exclude)
                sb.Append(' ').Append(name);
            sb.AppendLine();
        }

        string[] additional = module.AdditionalArgs ?? catalog.DefaultAdditionalArgs ?? DefaultAdditionalArgs;
        if (additional.Length > 0)
        {
            sb.Append("--additional");
            foreach (string arg in additional)
                sb.Append(' ').Append(arg);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendArg(StringBuilder sb, string name, string value)
        => sb.Append(name).Append(' ').Append(Quote(value)).AppendLine();

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? "\"" + value + "\"" : value;

    private static string DefaultSdkInclude()
    {
        string? sdk = Environment.GetEnvironmentVariable("PROSPERO_SDK_DIR");
        return string.IsNullOrWhiteSpace(sdk) ? "" : Path.Combine(sdk, "target", "include");
    }

    private static string DefaultOutputDir()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SharpProspero", "Interop", "Generated"));

    private static readonly string[] DefaultConfig =
    [
        "generate-macro-bindings",
        "generate-file-scoped-namespaces",
        "generate-helper-types",
        "exclude-empty-records",
    ];

    private static readonly string[] DefaultAdditionalArgs =
    [
        "-std=c11",
        "-Wno-pragma-once-outside-header",
    ];

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => Array.Exists(args, a => string.Equals(a, name, StringComparison.Ordinal));

    // Generates bindings from a supplied module. `prx --inspect` lists the exports; `prx --names`
    // emits a wrapper for the named exports and verifies each is present.
    private static int RunPrx(string[] args)
    {
        string? module = GetOption(args, "--module");
        if (string.IsNullOrEmpty(module) || !File.Exists(module))
        {
            Console.Error.WriteLine("Pass --module <file.prx> to a plaintext module.");
            return 1;
        }

        PrxImage image;
        try { image = PrxImage.Load(module); }
        catch (Exception ex) when (ex is PrxFormatException or IOException)
        {
            Console.Error.WriteLine($"Cannot read module: {ex.Message}");
            return 2;
        }

        if (HasFlag(args, "--inspect"))
        {
            Console.WriteLine($"Exports: {image.Exports.Count}");
            foreach (PrxExport export in image.Exports)
                Console.WriteLine($"  {export.Nid}  lib={export.LibraryId} mod={export.ModuleId} {(export.IsFunction ? "func" : "data")} {export.LibraryName}");
            return 0;
        }

        string? namesPath = GetOption(args, "--names");
        if (string.IsNullOrEmpty(namesPath) || !File.Exists(namesPath))
        {
            Console.Error.WriteLine("Pass --names <file> (one export per line), or --inspect to list exports.");
            return 1;
        }

        string moduleFileName = GetOption(args, "--module-name") ?? Path.GetFileName(module);
        string className = GetOption(args, "--class") ?? SanitizeIdentifier(Path.GetFileNameWithoutExtension(module));
        string ns = GetOption(args, "--namespace") ?? "SharpProspero.Bindings";

        var bindings = new List<PrxBinding>();
        int missing = 0;
        foreach (string raw in File.ReadAllLines(namesPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            PrxBinding binding = ParseBinding(line);
            if (image.FindByName(binding.Name) is null)
            {
                Console.Error.WriteLine($"Warning: '{binding.Name}' is not exported by the module.");
                missing++;
            }
            bindings.Add(binding);
        }

        // With --strict a name the module does not export fails the run, so a build script catches a
        // wrapper that would bind a symbol the module cannot resolve. Without it the run still succeeds
        // and only warns, as before.
        if (missing > 0 && HasFlag(args, "--strict"))
        {
            Console.Error.WriteLine($"{missing} requested name(s) are not exported by the module; refusing to emit under --strict.");
            return 3;
        }

        string source = PrxBindingsEmitter.Emit(ns, className, moduleFileName, bindings);
        string? outPath = GetOption(args, "--out");
        if (outPath is null)
        {
            Console.WriteLine(source);
        }
        else
        {
            string full = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
            Console.WriteLine($"Wrote {full} ({bindings.Count} bindings, {missing} not found in the module).");
        }
        return 0;
    }

    // Resolves the symbol graph of the given objects and archives and reports it.
    private static int RunLink(string[] args)
    {
        var options = new LinkOptions();
        var exportNames = new List<string>();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--obj") options.Objects.Add(args[i + 1]);
            else if (args[i] == "--lib") options.Archives.Add(args[i + 1]);
            else if (args[i] == "--stub") options.Stubs.Add(args[i + 1]);
            else if (args[i] == "--export") exportNames.Add(args[i + 1]);
        }
        if (options.Objects.Count == 0)
        {
            Console.Error.WriteLine("Usage: link --obj <file.o> [--obj ...] [--lib <archive.a> ...] [--stub <stub.o> ...]");
            Console.Error.WriteLine("       [--self-contained] supplies the start object and the core module stubs.");
            Console.Error.WriteLine("       [--export <name> ...] exports the named defined symbols (for a --kind prx library).");
            return 1;
        }

        ModuleKind kind = string.Equals(GetOption(args, "--kind"), "prx", StringComparison.OrdinalIgnoreCase)
            ? ModuleKind.Library : ModuleKind.Executable;

        // The self-contained link supplies its own start object and its own stubs for the modules the
        // SDK imports from, so a build needs no start file or stub library from elsewhere. A library
        // module has no program entry, so it takes the stubs but not the start object.
        if (HasFlag(args, "--self-contained"))
        {
            if (kind == ModuleKind.Executable)
                options.ExtraObjects.Add(ElfObjectReader.Read(CrtEmitter.BuildStartObject(), "sharpprospero_crt.o"));
            // The compat object defines the C-library names the ahead-of-time runtime imports that the
            // device modules do not publish. It is needed only when the runtime archives are linked, so
            // a bare link (no runtime) does not carry it.
            if (options.Archives.Count > 0)
                options.ExtraObjects.Add(ElfObjectReader.Read(CompatEmitter.BuildObject(), "sharpprospero_compat.o"));
            foreach (StubCatalog.Entry entry in StubCatalog.Core)
                options.ExtraStubs.Add(StubLibrary.Parse(
                    PrxStubEmitter.BuildObject(entry.Library, entry.Exports, entry.ModuleVersion, entry.LibraryVersion, entry.ModuleName, entry.Soname),
                    entry.Library + ".prx"));
        }

        try
        {
            LinkResolution result = Linker.Resolve(options);
            Console.WriteLine($"Included objects: {result.Included.Count}");
            Console.WriteLine($"Defined symbols:  {result.Defined.Count}");
            Console.WriteLine($"Imports:          {result.Imports.Count}");
            foreach (ImportSymbol imp in result.Imports)
                Console.WriteLine($"  -> {imp.Name}  ({imp.ModuleName})");
            Console.WriteLine($"Unresolved:       {result.Unresolved.Count}");
            foreach (string name in result.Unresolved)
                Console.WriteLine($"  ? {name}");
            if (result.SkippedMembers.Count > 0)
            {
                Console.WriteLine($"Skipped members:  {result.SkippedMembers.Count}");
                foreach (string skip in result.SkippedMembers)
                    Console.WriteLine($"  ! {skip}");
            }

            string? outPath = GetOption(args, "--out");
            if (outPath is not null)
            {
                if (result.Unresolved.Count > 0)
                {
                    Console.Error.WriteLine($"{result.Unresolved.Count} symbol(s) are unresolved; nothing written.");
                    return 2;
                }
                // A self-contained executable starts at the injected start object's entry, not at
                // main; defaulting to main would set e_entry past the start object and skip the stack
                // alignment and the exit call it performs.
                string defaultEntry = HasFlag(args, "--self-contained") && kind == ModuleKind.Executable
                    ? CrtEmitter.StartSymbol : "main";
                string entry = GetOption(args, "--entry") ?? defaultEntry;
                string full = Path.GetFullPath(outPath);
                byte[] module = DynamicWriter.Write(result, entry, kind,
                    exportNames.Count > 0 ? exportNames : null, Path.GetFileName(full));
                File.WriteAllBytes(full, module);
                Console.WriteLine($"Wrote {full} ({module.Length} bytes"
                    + (exportNames.Count > 0 ? $", {exportNames.Count} export(s)" : "") + ").");
            }
            return 0;
        }
        catch (Exception ex) when (ex is ElfLinkException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    // Compares the export surfaces of two modules: which identifiers one has and the other does not, and
    // which are present in both but at a different address. Useful for seeing what changed between two
    // firmware builds of the same module.
    private static int RunDiff(string[] args)
    {
        string? a = GetOption(args, "--a");
        string? b = GetOption(args, "--b");
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || !File.Exists(a) || !File.Exists(b))
        {
            Console.Error.WriteLine("Usage: diff --a <module> --b <module>");
            Console.Error.WriteLine("  Reports the export identifiers added, removed, and moved from A to B.");
            return 1;
        }
        try
        {
            PrxImage imageA = PrxImage.Parse(ModuleFile.Read(a).Elf);
            PrxImage imageB = PrxImage.Parse(ModuleFile.Read(b).Elf);
            var mapA = new Dictionary<string, PrxExport>(StringComparer.Ordinal);
            foreach (PrxExport e in imageA.Exports) mapA[e.Nid] = e;
            var mapB = new Dictionary<string, PrxExport>(StringComparer.Ordinal);
            foreach (PrxExport e in imageB.Exports) mapB[e.Nid] = e;

            var removed = new List<string>();
            var moved = new List<string>();
            foreach (KeyValuePair<string, PrxExport> kv in mapA)
            {
                if (!mapB.TryGetValue(kv.Key, out PrxExport other))
                    removed.Add(Label(kv.Value));
                else if (other.Value != kv.Value.Value)
                    moved.Add($"{Label(kv.Value)}  0x{kv.Value.Value:x} -> 0x{other.Value:x}");
            }
            var added = new List<string>();
            foreach (KeyValuePair<string, PrxExport> kv in mapB)
                if (!mapA.ContainsKey(kv.Key))
                    added.Add(Label(kv.Value));
            removed.Sort(StringComparer.Ordinal);
            added.Sort(StringComparer.Ordinal);
            moved.Sort(StringComparer.Ordinal);

            Console.WriteLine($"A: {Path.GetFileName(a)} ({imageA.Exports.Count} exports)");
            Console.WriteLine($"B: {Path.GetFileName(b)} ({imageB.Exports.Count} exports)");
            Console.WriteLine($"Removed (in A, not B): {removed.Count}");
            foreach (string s in removed) Console.WriteLine($"  - {s}");
            Console.WriteLine($"Added (in B, not A): {added.Count}");
            foreach (string s in added) Console.WriteLine($"  + {s}");
            Console.WriteLine($"Moved (same identifier, different address): {moved.Count}");
            foreach (string s in moved) Console.WriteLine($"  ~ {s}");
            return removed.Count > 0 || added.Count > 0 ? 1 : 0;
        }
        catch (Exception ex) when (ex is PrxFormatException or IOException)
        {
            Console.Error.WriteLine($"Could not read a module: {ex.Message}");
            return 2;
        }

        static string Label(PrxExport e)
            => string.IsNullOrEmpty(e.LibraryName) ? e.Nid : $"{e.Nid} [{e.LibraryName}]";
    }

    // Prints the header, program headers, dependencies and, with --exports, the exported symbols of an
    // ELF module, without an external tool.
    private static int RunElf(string[] args)
    {
        string? file = GetOption(args, "--file");
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: elf --file <module> [--exports]");
            return 1;
        }
        try
        {
            // Reads the file once and classifies it: a signed container is unwrapped to its embedded
            // ELF, so a .self or .sprx reports the same fields as a .elf or .prx.
            ModuleFile mf = ModuleFile.Read(file);
            ElfInfo info = ElfInfo.Parse(mf.Elf);
            Console.WriteLine($"File:      {Path.GetFileName(file)}");
            Console.WriteLine($"Container: {(mf.IsSigned ? "signed (.self / .sprx)" : "unsigned ELF (.elf / .prx)")}");
            Console.WriteLine($"Class:     {(info.Is64Bit ? "ELF64" : "ELF32")}");
            Console.WriteLine($"OS/ABI:    {info.OsAbi}");
            Console.WriteLine($"Type:      {info.TypeName}");
            Console.WriteLine($"Machine:   0x{info.Machine:X2}{(info.Machine == 0x3E ? " (x86-64)" : "")}");
            Console.WriteLine($"Entry:     0x{info.Entry:X}");

            Console.WriteLine($"Program headers ({info.ProgramHeaders.Count}):");
            Console.WriteLine("  Type            Flags  VirtAddr           FileSize   MemSize");
            foreach (ElfProgramHeader ph in info.ProgramHeaders)
                Console.WriteLine($"  {ph.TypeName,-15} {ph.FlagsText}    0x{ph.VirtualAddress:X12}     0x{ph.FileSize:X8} 0x{ph.MemorySize:X8}");

            // Dynamic-module details when the file carries them; a plain object or executable does not.
            try
            {
                PrxImage image = PrxImage.Parse(mf.Elf);
                if (image.SdkVersion != 0)
                {
                    Console.WriteLine($"Built for: {image.RequiredSystemVersion} "
                        + $"(0x{image.SdkVersion:X8}) - a package shipping this module must require at least this.");
                }
                if (image.NeededModules.Count > 0)
                {
                    Console.WriteLine($"Needed modules ({image.NeededModules.Count}):");
                    foreach (string module in image.NeededModules)
                        Console.WriteLine($"  {module}");
                }
                Console.WriteLine($"Exports: {image.Exports.Count}");
                if (HasFlag(args, "--exports"))
                {
                    foreach (PrxExport export in image.Exports)
                        Console.WriteLine($"  {export.Nid}  lib={export.LibraryId} mod={export.ModuleId} {(export.IsFunction ? "func" : "data")} {export.LibraryName}");
                }
            }
            catch (PrxFormatException)
            {
                // Not a dynamic module (no dynamic segment); the header and program headers stand alone.
            }
            return 0;
        }
        catch (Exception ex) when (ex is PrxFormatException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    // Converts between the two forms and reports which a file is. Without an action it inspects;
    // --sign wraps an ELF in a signed container a development console accepts; --extract recovers the
    // ELF from a signed container.
    private static int RunSelf(string[] args)
    {
        string? file = GetOption(args, "--file") ?? GetOption(args, "--in");
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: self --inspect --file <file>");
            Console.Error.WriteLine("       self --sign --in <file.elf|.prx> --out <file.self|.sprx> [--app-version 0xNN --fw-version 0xNN --authority 0xNN]");
            Console.Error.WriteLine("       self --extract --in <file.self|.sprx> --out <file.elf|.prx>");
            return 1;
        }

        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch (IOException ex) { Console.Error.WriteLine($"Cannot read {file}: {ex.Message}"); return 3; }

        bool sign = HasFlag(args, "--sign");
        bool extract = HasFlag(args, "--extract");
        if (!sign && !extract)
            return InspectSelf(file, data);

        string? outPath = GetOption(args, "--out");
        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Pass --out <file> for --sign and --extract.");
            return 1;
        }
        string full = Path.GetFullPath(outPath);

        try
        {
            byte[] result;
            string kind;
            if (sign)
            {
                // A version or authority given but not a valid hex number is a mistake (a dotted "9.00",
                // say); reject it rather than signing with a silently defaulted zero.
                if (!ValidHexOrAbsent(args, "--app-version", out ulong appVersion)
                    || !ValidHexOrAbsent(args, "--fw-version", out ulong firmwareVersion)
                    || !ValidHexOrAbsent(args, "--authority", out ulong authority))
                {
                    Console.Error.WriteLine("--app-version, --fw-version and --authority take a hex value like 0x02000000.");
                    return 1;
                }
                var options = new SelfSignOptions
                {
                    AppVersion = appVersion,
                    FirmwareVersion = firmwareVersion,
                    AuthorityId = GetOption(args, "--authority") is not null ? authority : null,
                    NormalizeHeader = !HasFlag(args, "--no-normalize"),
                };
                result = SelfContainer.Sign(data, options);
                kind = "signed container";
            }
            else
            {
                result = SelfContainer.ExtractElf(data);
                kind = "unsigned ELF";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, result);
            Console.WriteLine($"Wrote {full} ({result.Length} bytes, {kind}).");
            return 0;
        }
        catch (PrxFormatException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (IOException ex) { Console.Error.WriteLine(ex.Message); return 3; }
    }

    // Reports which of the forms a file is, and the little it can read of each.
    private static int InspectSelf(string file, byte[] data)
    {
        Console.WriteLine($"File:      {Path.GetFileName(file)}");
        switch (SelfContainer.Classify(data))
        {
            case ModuleForm.SignedPlaintext:
                SelfImage image;
                try { image = SelfContainer.Parse(data); }
                catch (PrxFormatException ex) { Console.Error.WriteLine(ex.Message); return 2; }

                Console.WriteLine("Container: signed (.self / .sprx), readable");
                Console.WriteLine($"Segments:  {image.Segments.Count}");
                foreach (SelfSegment seg in image.Segments)
                {
                    var attrs = new List<string>();
                    if (seg.Blocked) attrs.Add("payload");
                    if (seg.Compressed) attrs.Add("compressed");
                    if (seg.Encrypted) attrs.Add("encrypted");
                    if (seg.Signed) attrs.Add("signed");
                    Console.WriteLine($"  segment {seg.Id}: {seg.FileSize} bytes{(attrs.Count > 0 ? " (" + string.Join(", ", attrs) + ")" : "")}");
                }
                if (image.ExtInfo is SelfExtInfo ext)
                {
                    Console.WriteLine($"Authority: 0x{ext.AuthorityId:X16}");
                    Console.WriteLine($"Prog type: 0x{ext.ProgramType:X16}");
                    Console.WriteLine($"App ver:   0x{ext.AppVersion:X16}");
                    Console.WriteLine($"Fw ver:    0x{ext.FirmwareVersion:X16}");
                    Console.WriteLine($"Digest:    {Convert.ToHexString(ext.Digest)}");
                }
                try
                {
                    ElfInfo info = ElfInfo.Parse(SelfContainer.ExtractElf(data));
                    Console.WriteLine($"ELF type:  {info.TypeName}");
                    SelfIntegrity integrity = SelfContainer.CheckIntegrity(data);
                    Console.WriteLine($"Integrity: {(!integrity.HasDigest ? "no stored digest" : integrity.Matches ? "ok (digest matches the embedded ELF)" : "MISMATCH (the stored digest does not match)")}");
                }
                catch (PrxFormatException) { }
                return 0;

            case ModuleForm.SignedEncrypted:
                Console.WriteLine("Container: signed and encrypted (.self / .sprx), for a retail console");
                Console.WriteLine("Data:      encrypted; its contents cannot be read without its key");
                return 0;

            case ModuleForm.UnsignedElf:
                Console.WriteLine("Container: unsigned ELF (.elf / .prx)");
                Console.WriteLine($"ELF type:  {ElfInfo.Parse(data).TypeName}");
                return 0;

            default:
                Console.Error.WriteLine("File is neither an ELF nor a signed container.");
                return 2;
        }
    }

    // Dumps a supplied module's export surface so a firmware's facts can be contributed. Reads any of
    // the four forms (a signed container is unwrapped first); --coverage reports how it covers the
    // names the SDK needs, and --text prints a human-readable form instead of JSON.
    private static int RunOffsets(string[] args)
    {
        string? file = GetOption(args, "--file") ?? GetOption(args, "--module");
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: offsets --file <module> [--firmware NN.NN] [--coverage] [--library <name>] [--text]");
            Console.Error.WriteLine("  Reads a .prx/.sprx/.elf/.self and dumps its export identifiers and addresses as JSON,");
            Console.Error.WriteLine("  so a firmware's facts can be contributed. --coverage matches the names the SDK needs");
            Console.Error.WriteLine("  and reports which are present; --library <name> targets one SDK catalog module.");
            Console.Error.WriteLine("  --text prints a human-readable form instead of JSON.");
            return 1;
        }

        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch (IOException ex) { Console.Error.WriteLine($"Cannot read {file}: {ex.Message}"); return 3; }

        string? firmware = GetOption(args, "--firmware");
        bool coverage = HasFlag(args, "--coverage");
        string? library = GetOption(args, "--library");

        OffsetReport report;
        try { report = OffsetReport.Create(Path.GetFileName(file), data, firmware, coverage, library); }
        catch (PrxFormatException ex) { Console.Error.WriteLine(ex.Message); return 2; }

        if (HasFlag(args, "--text"))
            Console.Write(report.ToText());
        else
            Console.WriteLine(OffsetsToJson(report));

        // A coverage run that found a name the SDK needs but the module does not export is the signal
        // worth a non-zero exit, so a contribution script can notice a firmware that moved a symbol.
        return report.Coverage is { Missing.Count: > 0 } ? 4 : 0;
    }

    private static string OffsetsToJson(OffsetReport report)
    {
        var root = new JsonObject
        {
            ["file"] = report.File,
            ["container"] = report.Container,
            ["firmware"] = report.Firmware,
            ["builtAgainst"] = report.BuiltAgainst,
            ["exportsReadable"] = report.ExportsReadable,
        };
        if (report.Note is not null)
            root["note"] = report.Note;

        if (report.ExportsReadable)
        {
            root["moduleName"] = report.ModuleName;
            root["libraryVersion"] = $"0x{report.LibraryVersion:X4}";

            var needed = new JsonArray();
            foreach (string module in report.NeededModules)
                needed.Add(module);
            root["neededModules"] = needed;

            var exports = new JsonArray();
            foreach (PrxExport export in report.Exports)
                exports.Add(new JsonObject
                {
                    ["nid"] = export.Nid,
                    ["library"] = export.LibraryName,
                    ["kind"] = export.IsFunction ? "func" : "data",
                    ["address"] = $"0x{export.Value:X}",
                });
            root["exports"] = exports;
        }

        if (report.Coverage is OffsetCoverage coverage)
        {
            var symbols = new JsonArray();
            foreach (OffsetSymbol symbol in coverage.Symbols)
            {
                var node = new JsonObject
                {
                    ["name"] = symbol.Name,
                    ["nid"] = symbol.Nid,
                    ["present"] = symbol.Present,
                };
                if (symbol.Present)
                {
                    node["kind"] = symbol.IsFunction ? "func" : "data";
                    node["address"] = $"0x{symbol.Address:X}";
                }
                symbols.Add(node);
            }

            var missing = new JsonArray();
            foreach (string name in coverage.Missing)
                missing.Add(name);

            root["coverage"] = new JsonObject
            {
                ["matchedLibrary"] = coverage.MatchedLibrary,
                ["required"] = coverage.RequiredCount,
                ["present"] = coverage.PresentCount,
                ["missing"] = missing,
                ["symbols"] = symbols,
            };
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        return root.ToJsonString(options);
    }

    // Retargets a module to another system: rewrites the version it records it was built against (the
    // load-time gate) and, optionally, a needed library's recorded version. A signed module is unwrapped,
    // edited, and re-signed. With no action it reports what the module currently targets.
    private static int RunRetarget(string[] args)
    {
        string? file = GetOption(args, "--file") ?? GetOption(args, "--module");
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: retarget --file <module.prx|.sprx> [--to NN.NN] [--set-lib-version <name>=0xNNNN ...] [--out <file>]");
            Console.Error.WriteLine("  With no action it reports the version the module targets and its library version tags.");
            Console.Error.WriteLine("  --to rewrites the version the module records it was built against (the load-time gate), so a");
            Console.Error.WriteLine("  module built for a newer system can load on an older one. --set-lib-version rewrites a needed");
            Console.Error.WriteLine("  library's recorded version. A signed module is unwrapped, edited, and re-signed.");
            return 1;
        }

        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch (IOException ex) { Console.Error.WriteLine($"Cannot read {file}: {ex.Message}"); return 3; }

        ModuleForm form = SelfContainer.Classify(data);
        if (form == ModuleForm.SignedEncrypted)
        {
            Console.Error.WriteLine("This is a signed and encrypted module; its contents cannot be read or edited without its key.");
            return 2;
        }
        if (form == ModuleForm.Unknown)
        {
            Console.Error.WriteLine("File is neither an ELF nor a signed container.");
            return 2;
        }

        bool signed = form == ModuleForm.SignedPlaintext;
        byte[] elf;
        try { elf = signed ? SelfContainer.ExtractElf(data) : (byte[])data.Clone(); }
        catch (PrxFormatException ex) { Console.Error.WriteLine(ex.Message); return 2; }

        string? toText = GetOption(args, "--to");
        var libVersions = new List<(string Name, ushort Version)>();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--set-lib-version", StringComparison.Ordinal))
                continue;
            string spec = args[i + 1];
            int eq = spec.IndexOf('=');
            if (eq <= 0 || !TryParseHexUShort(spec[(eq + 1)..], out ushort v))
            {
                Console.Error.WriteLine($"--set-lib-version expects <name>=0xNNNN, got '{spec}'.");
                return 1;
            }
            libVersions.Add((spec[..eq], v));
        }

        ModuleTargetInfo before;
        try { before = ModuleEditor.Read(elf); }
        catch (PrxFormatException ex) { Console.Error.WriteLine(ex.Message); return 2; }

        bool editing = toText is not null || libVersions.Count > 0;
        if (!editing)
        {
            Console.WriteLine($"File:       {Path.GetFileName(file)}");
            Console.WriteLine($"Container:  {(signed ? "signed (.self / .sprx)" : "unsigned ELF (.elf / .prx)")}");
            Console.WriteLine($"Targets:    {(before.SdkVersion == 0
                ? "no recorded version (loads on any system)"
                : $"{PrxImage.FormatSystemVersion(before.SdkVersion)} (0x{before.SdkVersion:X8})")}");
            foreach (LibraryTag tag in before.Libraries)
                Console.WriteLine($"  {tag.Kind,-15} {tag.Name,-28} v{(tag.Version >> 8) & 0xFF}.{tag.Version & 0xFF} (0x{tag.Version:X4})");
            return 0;
        }

        ushort? targetPacked = null;
        if (toText is not null)
        {
            if (!SystemVersion.TryParse(toText, out SystemVersion target))
            {
                Console.Error.WriteLine($"'{toText}' is not a system version. Use MM.mm, for example 09.00.");
                return 1;
            }
            targetPacked = target.Packed;
        }

        string? outPath = GetOption(args, "--out");
        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Pass --out <file> to write the retargeted module.");
            return 1;
        }

        if (targetPacked is ushort packed)
        {
            if (!ModuleEditor.SetSdkVersion(elf, packed))
            {
                Console.WriteLine("The module records no version block, so it already loads on any system; version unchanged.");
            }
            else
            {
                ushort currentHigh = (ushort)(before.SdkVersion >> 16);
                string direction = packed < currentHigh ? "Downgraded" : packed > currentHigh ? "Upgraded" : "Set";
                Console.WriteLine($"{direction} target version to {PrxImage.FormatSystemVersion((uint)packed << 16)}.");
            }
        }

        foreach ((string name, ushort v) in libVersions)
        {
            int n = ModuleEditor.SetLibraryVersion(elf, name, v);
            Console.WriteLine(n > 0
                ? $"Set {name} version to 0x{v:X4} in {n} record(s)."
                : $"Warning: '{name}' is not a needed or imported library of this module; nothing changed.");
        }

        byte[] result = elf;
        string kind = "unsigned ELF";
        if (signed)
        {
            var options = new SelfSignOptions();
            try
            {
                SelfImage original = SelfContainer.Parse(data);
                if (original.ExtInfo is SelfExtInfo ext)
                    options = new SelfSignOptions { AuthorityId = ext.AuthorityId };
            }
            catch (PrxFormatException)
            {
                // Fall back to the default authority when the original header cannot be read.
            }
            try
            {
                result = SelfContainer.Sign(elf, options);
            }
            catch (PrxFormatException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            kind = "signed container";
        }

        string full = Path.GetFullPath(outPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, result);
        }
        catch (IOException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        Console.WriteLine($"Wrote {full} ({result.Length} bytes, {kind}).");
        return 0;
    }

    private static bool TryParseHexUShort(string text, out ushort value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    // True when the option is absent (value stays zero) or present with a valid hex value; false only
    // when it is present but does not parse, so a malformed value is caught rather than silently zeroed.
    private static bool ValidHexOrAbsent(string[] args, string name, out ulong value)
    {
        value = 0;
        string? text = GetOption(args, name);
        if (string.IsNullOrEmpty(text))
            return true;
        return TryParseHexOption(args, name, out value);
    }

    private static bool TryParseHexOption(string[] args, string name, out ulong value)
    {
        value = 0;
        string? text = GetOption(args, name)?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    // Writes a link stub. The versions matter: an import records the module and library version, and a
    // module whose versions differ will not bind. Passing --module takes them from the module itself.
    private static int RunStub(string[] args)
    {
        string? library = GetOption(args, "--lib");
        string? namesPath = GetOption(args, "--names");
        string? outPath = GetOption(args, "--out");
        string? modulePath = GetOption(args, "--module");

        if (string.IsNullOrEmpty(namesPath) || string.IsNullOrEmpty(outPath) ||
            (string.IsNullOrEmpty(library) && string.IsNullOrEmpty(modulePath)))
        {
            Console.Error.WriteLine("Usage: stub --lib <libraryName> --names <file> --out <file.a>");
            Console.Error.WriteLine("       stub --module <file.prx> --names <file> --out <file.a>");
            Console.Error.WriteLine("  --module takes the library name and its versions from the module, so the");
            Console.Error.WriteLine("  stub matches what the module publishes. --lib assumes the usual versions.");
            Console.Error.WriteLine("  --module-version / --library-version override either way.");
            return 1;
        }
        if (!File.Exists(namesPath))
        {
            Console.Error.WriteLine($"Names file not found: {namesPath}");
            return 1;
        }

        var names = new List<string>();
        foreach (string raw in File.ReadAllLines(namesPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int split = line.IndexOfAny(['=', ':', '(']);
            names.Add(split < 0 ? line : line[..split].Trim());
        }

        ushort moduleVersion = PrxStubEmitter.DefaultModuleVersion;
        ushort libraryVersion = PrxStubEmitter.DefaultLibraryVersion;

        if (!string.IsNullOrEmpty(modulePath))
        {
            if (!File.Exists(modulePath))
            {
                Console.Error.WriteLine($"Module not found: {modulePath}");
                return 1;
            }
            PrxImage image;
            try { image = PrxImage.Load(modulePath); }
            catch (Exception ex) when (ex is PrxFormatException or IOException)
            {
                Console.Error.WriteLine($"Cannot read module: {ex.Message}");
                return 2;
            }

            if (string.IsNullOrEmpty(library))
                library = image.ModuleName.Length > 0
                    ? image.ModuleName
                    : Path.GetFileNameWithoutExtension(modulePath);
            libraryVersion = image.LibraryVersion;

            int missing = 0;
            foreach (string name in names)
            {
                if (image.FindByName(name) is null)
                {
                    Console.Error.WriteLine($"Warning: '{name}' is not exported by the module.");
                    missing++;
                }
            }
            Console.WriteLine($"Read {Path.GetFileName(modulePath)}: library '{library}', "
                + $"library version 0x{libraryVersion:X4}, {image.Exports.Count} export(s), {missing} name(s) not found.");
        }

        if (ushort.TryParse(GetOption(args, "--module-version"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort mv))
            moduleVersion = mv;
        if (ushort.TryParse(GetOption(args, "--library-version"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort lv))
            libraryVersion = lv;

        string full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        PrxStubEmitter.WriteStub(library!, names, full, moduleVersion, libraryVersion);
        Console.WriteLine($"Wrote {full} ({names.Count} exports, module version 0x{moduleVersion:X4}, "
            + $"library version 0x{libraryVersion:X4}).");
        return 0;
    }

    // Prints the identifier for one or more plain symbol names. A module keys its exports by this
    // identifier, so it is how a name is matched against a module that carries no plain names.
    private static int RunNid(string[] args)
    {
        var names = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Length)
            {
                names.Add(args[i + 1]);
                i++;
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                names.Add(args[i]);
            }
        }

        string? namesPath = GetOption(args, "--names");
        if (namesPath is not null && File.Exists(namesPath))
        {
            foreach (string raw in File.ReadAllLines(namesPath))
            {
                string line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith('#'))
                    names.Add(line);
            }
        }

        if (names.Count == 0)
        {
            Console.Error.WriteLine("Usage: nid --name <symbol> [--name <symbol> ...] | nid --names <file>");
            return 1;
        }

        foreach (string name in names)
            Console.WriteLine($"{SceNid.Compute(name)}  {name}");
        return 0;
    }

    // Settles the system version an application requires. A module records the system it was built
    // against, and an application that ships it has to require at least as much: the system installs
    // an application whose requirement is too low and then fails to load the module.
    private static int RunSysVer(string[] args)
    {
        string? folder = GetOption(args, "--folder");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Usage: sysver --folder <module-folder> [--policy match|upgrade|downgrade|keep] [--version NN.NN] [--apply]");
            return 1;
        }

        string policyName = GetOption(args, "--policy") ?? "match";
        if (!Enum.TryParse(policyName, ignoreCase: true, out SystemVersionPolicy policy))
        {
            Console.Error.WriteLine($"Unknown policy '{policyName}'. Use match, upgrade, downgrade or keep.");
            return 1;
        }

        SystemVersion target = default;
        string? versionText = GetOption(args, "--version");
        if (versionText is not null && !SystemVersion.TryParse(versionText, out target))
        {
            Console.Error.WriteLine($"'{versionText}' is not a system version. Use MM.mm, for example 11.20.");
            return 1;
        }

        string paramPath = Path.Combine(folder, "sce_sys", "param.json");
        JsonObject? document = ReadParamJson(paramPath);
        // Read the requirement only when it is actually a string; a hand-edited file could hold a number
        // or other type there, and GetValue<string> would throw on it.
        string? current = document?["requiredSystemSoftwareVersion"] is JsonValue currentNode
            && currentNode.TryGetValue(out string? currentValue) ? currentValue : null;

        SystemVersionPlan plan;
        try
        {
            plan = SystemVersionPlanner.Plan(folder, current, policy, target);
        }
        catch (ArgumentException ex)
        {
            // The message is written for a person; the parameter name it carries is for a caller.
            int suffix = ex.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
            Console.Error.WriteLine(suffix < 0 ? ex.Message : ex.Message[..suffix]);
            return 1;
        }

        foreach (ModuleRequirement module in plan.Modules)
            Console.WriteLine($"  {module.FileName,-32} {(module.Version.HasValue ? module.Version.ToString() : "no requirement")}");
        foreach (string name in plan.Unreadable)
            Console.WriteLine($"  {name,-32} unreadable");
        if (plan.Modules.Count > 0 || plan.Unreadable.Count > 0)
            Console.WriteLine();

        Console.WriteLine($"Current:  {Show(plan.Current)}");
        Console.WriteLine($"Modules:  {Show(plan.Needed)}");
        Console.WriteLine($"Result:   {Show(plan.Result)}  {plan.Result.ToPackageValue()}");

        foreach (string message in plan.Messages)
            Console.WriteLine($"  {message}");

        if (!HasFlag(args, "--apply"))
            return plan.Unloadable.Count > 0 ? 4 : 0;

        if (document is null)
        {
            Console.Error.WriteLine($"No application metadata to write: {paramPath} was not found.");
            return 1;
        }
        if (!plan.Changed)
        {
            Console.WriteLine("Nothing to write.");
            return plan.Unloadable.Count > 0 ? 4 : 0;
        }

        document["requiredSystemSoftwareVersion"] = plan.Result.ToPackageValue();
        WriteParamJson(paramPath, document);
        Console.WriteLine($"Wrote {paramPath}");
        return plan.Unloadable.Count > 0 ? 4 : 0;

        static string Show(SystemVersion v) => v.HasValue ? v.ToString() : "none";
    }

    private static JsonObject? ReadParamJson(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Written back the way the document is read elsewhere: two-space indentation, UTF-8 with no
    // byte-order mark, and the keys left in the order they were parsed in.
    private static void WriteParamJson(string path, JsonObject document)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(path, document.ToJsonString(options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // Writes the start object: the entry point the linker includes ahead of the compiled object.
    private static int RunCrt(string[] args)
    {
        string? outPath = GetOption(args, "--out");
        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Usage: crt --out <file.o>");
            return 1;
        }
        string full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, CrtEmitter.BuildStartObject());
        Console.WriteLine($"Wrote {full} (start object).");
        return 0;
    }

    private static int RunCompat(string[] args)
    {
        string? outPath = GetOption(args, "--out");
        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Usage: compat --out <file.o>");
            return 1;
        }
        string full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, CompatEmitter.BuildObject());
        Console.WriteLine($"Wrote {full} (runtime-support compat object).");
        return 0;
    }

    private static PrxBinding ParseBinding(string line)
    {
        int split = line.IndexOfAny(['=', ':']);
        if (split < 0)
            return new PrxBinding(line.Trim(), "", []);

        string name = line[..split].Trim();
        string signature = line[(split + 1)..].Trim();
        int open = signature.IndexOf('(');
        if (open < 0)
            return new PrxBinding(name, signature, []);

        string returnType = signature[..open].Trim();
        int close = signature.IndexOf(')', open + 1);
        string inside = close > open ? signature[(open + 1)..close] : signature[(open + 1)..];
        var parameters = new List<string>();
        foreach (string part in inside.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            parameters.Add(part);
        return new PrxBinding(name, returnType, parameters);
    }

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string result = sb.ToString();
        return result.Length > 0 && char.IsLetter(result[0]) ? result : "Module" + result;
    }

    // The tool's build version, for a build log to record which toolchain produced a module.
    private static string ToolVersion()
    {
        Assembly assembly = typeof(Program).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
        return $"sharpprospero-bindgen {version}";
    }

    // The full options of one command, shown for "<command> --help".
    private static void PrintVerbUsage(string verb)
    {
        switch (verb)
        {
            case "prx":
                Console.WriteLine("Usage: prx --module <file.prx> --inspect");
                Console.WriteLine("       prx --module <file.prx> --names <file> [--class N] [--namespace NS] [--out F.cs] [--strict]");
                Console.WriteLine("  Emits C# interop for the named exports. --strict fails when a name is not exported.");
                break;
            case "stub":
                Console.WriteLine("Usage: stub --lib <libraryName> --names <file> --out <file.a>");
                Console.WriteLine("       stub --module <file.prx> --names <file> --out <file.a>");
                Console.WriteLine("  --lib assumes the usual module and library versions; --module reads them from the module.");
                break;
            case "crt":
                Console.WriteLine("Usage: crt --out <file.o>    Writes the start object that carries the program entry point.");
                break;
            case "compat":
                Console.WriteLine("Usage: compat --out <file.o>    Writes the compatibility object bridging the runtime's C calls.");
                break;
            case "nid":
                Console.WriteLine("Usage: nid --name <symbol> [--name <symbol> ...] | nid --names <file>");
                break;
            case "elf":
                Console.WriteLine("Usage: elf --file <module> [--exports]    Prints an ELF module's header (and its exports).");
                break;
            case "diff":
                Console.WriteLine("Usage: diff --a <module> --b <module>    Reports the exports added, removed, and moved from A to B.");
                break;
            case "self":
                Console.WriteLine("Usage: self --inspect --file <file>");
                Console.WriteLine("       self --sign --in <file.elf|.prx> --out <file.self|.sprx> [--app-version 0xNN] [--fw-version 0xNN] [--authority 0xNN] [--no-normalize]");
                Console.WriteLine("       self --extract --in <file.self|.sprx> --out <file.elf|.prx>");
                break;
            case "offsets":
                Console.WriteLine("Usage: offsets --file <module> [--firmware NN.NN] [--coverage] [--library <name>] [--text]");
                break;
            case "retarget":
                Console.WriteLine("Usage: retarget --file <module.prx|.sprx> [--to NN.NN] [--set-lib-version <name>=0xNNNN ...] [--out <file>]");
                break;
            case "sysver":
                Console.WriteLine("Usage: sysver --folder <module-folder> [--policy match|upgrade|downgrade|keep] [--version NN.NN] [--apply]");
                break;
            case "link":
                Console.WriteLine("Usage: link --obj <file.o> [--obj ...] [--lib <archive.a> ...] [--stub <stub.o> ...] [--self-contained]");
                Console.WriteLine("            [--kind eboot|prx] [--entry <symbol>] [--export <name> ...] --out <module>");
                Console.WriteLine("  --self-contained supplies the start object and the core module stubs.");
                break;
            default:
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: sharpprospero-bindgen [options]");
        Console.WriteLine("       sharpprospero-bindgen prx --module <file.prx> --inspect");
        Console.WriteLine("       sharpprospero-bindgen prx --module <file.prx> --names <file> [--class N --namespace NS --out F.cs --strict]");
        Console.WriteLine("       sharpprospero-bindgen stub --lib <libraryName> --names <file> --out <file.a>");
        Console.WriteLine("       sharpprospero-bindgen crt --out <file.o>");
        Console.WriteLine("       sharpprospero-bindgen compat --out <file.o>");
        Console.WriteLine("       sharpprospero-bindgen link [--self-contained] --obj <file.o> [--lib <archive.a>] [--kind eboot|prx] [--entry <sym>] [--export <name>] --out <module>");
        Console.WriteLine("       sharpprospero-bindgen elf --file <module> [--exports]");
        Console.WriteLine("       sharpprospero-bindgen diff --a <module> --b <module>");
        Console.WriteLine("       sharpprospero-bindgen self --inspect --file <file>");
        Console.WriteLine("       sharpprospero-bindgen self --sign --in <file.elf|.prx> --out <file.self|.sprx> [--app-version 0xNN --fw-version 0xNN --authority 0xNN --no-normalize]");
        Console.WriteLine("       sharpprospero-bindgen self --extract --in <file.self|.sprx> --out <file.elf|.prx>");
        Console.WriteLine("       sharpprospero-bindgen offsets --file <module> [--firmware NN.NN] [--coverage] [--library <name>] [--text]");
        Console.WriteLine("       sharpprospero-bindgen retarget --file <module> [--to NN.NN] [--set-lib-version <name>=0xNNNN] [--out <file>]");
        Console.WriteLine("       sharpprospero-bindgen nid --name <symbol>");
        Console.WriteLine("       sharpprospero-bindgen sysver --folder <module-folder> [--policy P --version NN.NN --apply]");
        Console.WriteLine();
        Console.WriteLine("Run '<command> --help' for a command's full options, or --version for the tool version.");
        Console.WriteLine();
        Console.WriteLine("sysver settles the system version an application requires against the modules it ships:");
        Console.WriteLine("  --policy match        Require what the modules need. Never lowers. The default.");
        Console.WriteLine("  --policy upgrade      Raise the requirement to --version.");
        Console.WriteLine("  --policy downgrade    Lower the requirement to --version, and report what stops loading.");
        Console.WriteLine("  --policy keep         Leave it alone, and report a module that needs more.");
        Console.WriteLine("  --apply               Write the result to sce_sys/param.json. Prints the plan without it.");
        Console.WriteLine();
        Console.WriteLine("  --sdk <folder>        SDK include tree. Default: %PROSPERO_SDK_DIR%/target/include.");
        Console.WriteLine("  --out <folder>        Output folder for generated bindings.");
        Console.WriteLine("  --modules <file>      Module catalog. Default: modules.json next to the tool.");
        Console.WriteLine("  --responses <folder>  Where response files are written. Default: <out>/responses.");
    }
}
