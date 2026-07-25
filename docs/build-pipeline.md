---
title: Build pipeline
nav_order: 2
parent: Toolchain
---

# Build pipeline

An application goes through three steps to become a package: compile the C# to an object, link the
object into an ELF module, and pack the module. The first two are driven by MSBuild files in
`build/`; the third is the packager. `src/SharpProspero.Sample/build.ps1` runs all three.

## Inputs

Nothing outside the .NET SDK is set up. `build.ps1` gathers the ahead-of-time runtime from the .NET
SDK's own NativeAOT runtime pack (restored by the compile step) and links through the SDK's own linker,
which supplies its own start object, a compat object for the C-library names the runtime needs that the
device does not publish, and stubs for the service modules. So there is no runtime pack to assemble, no
`PROSPERO_RUNTIME_PACK` to set, and no separate linker, start file, or stub library.

The one host requirement is that the compile step runs on Linux (see step 1). On Windows `build.ps1`
runs that step through WSL automatically, so a Windows user builds in place.

## Step 1: compile

An application imports `build/Prospero.App.props`, which configures the ahead-of-time compile:

- Trimming is set to full and the size preference is on, so unused framework surface does not survive.
- Framework features an on-device module never uses are switched off: globalization data, resource
  strings, event sources, the metadata updater, activity propagation and the debugger hooks.
- The garbage collector is the workstation, non-concurrent collector, with a hard ceiling baked into
  the image through `System.GC.HeapHardLimit`. Set the ceiling per project with
  `ProsperoHeapHardLimitBytes`.
- Every service module is declared a direct import (`DirectPInvoke`), so each binding call becomes a
  direct reference the linker resolves against a stub it generates for that module.

Publish with an x86_64 runtime identifier:

```
dotnet publish -c Release -r linux-x64
```

The compiler writes an object under `obj/Release/net10.0/linux-x64/native`. It targets the x86_64
instruction set; the runtime it links against (below) matches the device ABI.

One constraint on this step: the ahead-of-time compiler emits an object only for the operating system
it runs on, so it does not cross-compile to Linux from a Windows host. `build.ps1` therefore runs the
publish through WSL on Windows — the object still lands in the project's `obj` folder, which both sides
share — and runs it directly on Linux. The link and pack steps below are the toolchain itself and run
wherever the script is started.

## Step 2: link

`build/Prospero.App.targets` defines the `ProsperoLink` target. It is not part of a normal build, so
`dotnet build` never touches it; run it directly or through `build.ps1`. The target:

1. Checks the runtime archives and the compiled object exist, stopping with a specific message if one
   is missing.
2. Runs the SDK's linker over the application object and the runtime archives. The
   linker supplies its own start object (which carries the `_start` entry point) and its own stubs for
   the modules the SDK imports from. It reads each object and archive, resolves the symbol graph, lays
   the sections into segments, applies the relocations, and writes `eboot.bin` — the exception-frame
   index and the thread-local template included.

A project can add stubs for its own modules through `ProsperoUserStubLibrary` (generated from a `.prx`
by the stub tool); those let an application link against libraries it supplies. Run the target
directly like this:

```
dotnet msbuild src/SharpProspero.Sample/SharpProspero.Sample.csproj /t:ProsperoLink ^
  /p:ProsperoObjectFile=<path-to-object> /p:OutputPath=<module-folder>/
```

Override the runtime archive list and its order with `ProsperoRuntimeLibraries` (semicolon-separated)
when the default folder scan is not the order you want.

### What the linker lays out

The module the linker writes carries four load segments, in this order:

| Segment | Protection | Holds |
|---|---|---|
| Code | read + execute | the compiled code and the procedure-linkage table |
| Read-only | read | read-only data and the exception-frame index |
| Writable | read + write | writable data, the global-offset table, and the process parameters |
| Linking | **none** | the symbol, string and hash tables, both relocation tables, the module note, and the dynamic table |

The fourth one is the part that is easy to get wrong. It is a load segment that requests no memory
protection at all, and that is exactly what marks it as linking data rather than image content: the
loader reads it to bind the module instead of mapping it into the running process. **A module that
names a dynamic table without also carrying this segment does not start.** Its program headers are
scanned before any of its code runs, the pair is found inconsistent, and the launch is refused with a
segment-header error while the application sits on a splash screen.

Alongside it, two placements are equally load-bearing: the dynamic table and the module note must
both lie *inside* that linking segment, and the process parameters must lie inside a writable load
segment.

You do not configure any of this — the linker always produces this shape. It is documented because
it is what the loader checks, so it is what a hand-built or externally produced module has to match
to launch. Confirm a module's shape with the inspector:

```
sharpprospero-bindgen elf --file eboot.bin
```

## Step 3: wrap the module

The linker's output is a plain ELF, and **a plain ELF does not launch** — the loader authenticates a
module's container header before running any of its code, so an unwrapped `eboot.bin` is turned away
and the application never starts. The build therefore wraps the module it produced, after settling
the system version and before either output is written:

```
== Sign ==
Wrote .../module/eboot.bin (1090320 bytes, signed container).
```

Only the module built here is wrapped. Anything the project ships in `sce_module/` already arrives
wrapped, and the step leaves an already-wrapped file untouched, so re-running a build is safe. The
packager reports the result as part of its launch-readiness summary:

```
Launch readiness: ready
  module:      wrapped for the loader, contents readable
```

If that line reads *a plain ELF*, the module was not wrapped and the application will not start.
[Unsigned and signed](signed-and-unsigned.md) covers the forms in full.

## Step 4: output

The build gathers `eboot.bin` next to the `sce_sys` metadata and any `sce_module` libraries, then
writes one of two outputs. `build-app.ps1` picks with `-Output`:

```
pwsh build/build-app.ps1 -ProjectPath MyApp.csproj                    # Package (the default)
pwsh build/build-app.ps1 -ProjectPath MyApp.csproj -Output Folder     # every file in one folder
```

**Package** hands the folder to the packager and writes an installable `*.pkg`:

```
dotnet run --project tools/SharpProspero.Packager -- --in <module-folder> --out <output-folder>
```

**Folder** stops after gathering, leaving `eboot.bin`, `sce_sys` and `sce_module` together in one
folder ready to copy or inspect. Nothing is packed, so there is no content id or passcode to set.

## Where the runtime comes from

The linked module needs the ahead-of-time runtime — the garbage collector, exception handling, and the
bootstrap that runs before the managed entry. These are the standard NativeAOT runtime archives, and
the `dotnet publish` compile step restores them into the .NET SDK's package cache as its own runtime
pack. `build.ps1` gathers those archives from the cache and hands them to the linker, so nothing is
assembled or downloaded separately.

The runtime archives call a set of C-library and operating-system functions. The device's own C and
kernel modules already publish most of them (the whole `pthread` family, the memory and file calls,
timing, and the C library), so the linker resolves those as ordinary imports against the module stubs.
The rest — a small set the runtime asks for by a name the device does not publish, such as the
large-file variants of the file calls — are provided by a compat object the linker emits itself: each
is a thin forwarder to the name the device does publish, or a fixed result an application module can
accept. The upshot is that the runtime's operating-system surface is satisfied entirely by the device
modules and the toolchain, with no platform layer to build.

The linker also defines the section-boundary symbols the runtime reads to walk its own managed-code and
module tables (`__start_<section>` / `__stop_<section>`), the way the system linker does.

A plain `dotnet build` of the solution and the tests need none of this; it applies only to the link
step, which runs after the compile step has restored the runtime pack.

## Payloads

Besides an application module and a library, the linker builds a *payload*: a position-independent
executable a small loader maps into an existing process at run time and jumps to, over a network
connection. A payload is not signed and not packaged; it has no dynamic linker, so it resolves the
functions it calls at run time rather than importing them from modules. [Modules and
payloads](modules-and-payloads.md) covers how the two forms differ and which one to build; this section
covers the build command.

Build one with the payload output kind. The link is self-contained, so it supplies its own start code:

```
sharpprospero-bindgen link --kind payload --self-contained --obj app.o --lib runtime.a --out app.elf
```

Every reference the objects leave open becomes a name the payload resolves at start-up through the
resolver the loader hands it; the start code fills a table of these before calling `main`. The output is
a plain shared-object executable with base-relative relocations only.

Send a built payload to a listening loader:

```
sharpprospero-bindgen payload --send --host 192.168.1.10 --file app.elf
```

The loader reads the whole file, maps it, applies the relocations, and runs it. The default port is 9021.

{: .note }
> The file format of both an application module and a payload is checked against the console's own loader
> and libraries: the header identification, the segment and dynamic-table layout, and the relocation
> section are the forms the loader accepts. The payload start code resolves its references, allocates and
> installs the managed runtime's thread-local storage, runs the global constructors, and marks the C
> runtime threaded before `main`. Producing, sending, and running the file is the complete path; the
> thread-local set-up runs on the entry thread and is exercised on hardware.

## Keeping the heap in bounds

Memory maps are limited, so the heap ceiling matters. Set `ProsperoHeapHardLimitBytes` to the largest
managed heap the module should use; the value is written into the image through `System.GC.HeapHardLimit`.
Read usage at runtime with `SharpProspero.Memory.HeapMonitor`, and prefer drawing into pre-allocated
framebuffers and reusing buffers over allocating each frame.
