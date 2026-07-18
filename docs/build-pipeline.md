---
title: Build pipeline
nav_order: 5
---

# Build pipeline

An application goes through three steps to become a package: compile the C# to an object, link the
object into an ELF module, and pack the module. The first two are driven by MSBuild files in
`build/`; the third is the packager. `src/SharpProspero.Sample/build.ps1` runs all three.

## Inputs

One location is needed, resolvable from an environment variable so machine paths stay out of the
project files:

| Variable | Points at | Used by |
|---|---|---|
| `PROSPERO_RUNTIME_PACK` | Folder with the runtime archives for the device ABI. | Compile, link. |

The property `ProsperoRuntimePack` overrides the variable when set on the command line. The link runs
through the SDK's own linker, which supplies its own start object and its own stubs for the modules
the SDK imports from, so a build needs no separate linker, start file, or stub library.

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

The compiler writes an object under `obj/Release/net10.0/linux-x64/native`. The object targets the
x86_64 instruction set; the runtime code it contains comes from the runtime pack (below), so the
object matches the device ABI.

One constraint on this step: the stock compiler emits an object only for the operating system it runs
on, so it does not produce an ELF object from a Windows host on its own. Run the publish on Linux (or
WSL), or supply the object writer for the device ABI as part of the runtime pack. The link and pack
steps below run on any host.

## Step 2: link

`build/Prospero.App.targets` defines the `ProsperoLink` target. It is not part of a normal build, so
`dotnet build` never touches it; run it directly or through `build.ps1`. The target:

1. Checks the runtime pack and the compiled object exist, stopping with a specific message if one is
   missing.
2. Runs the SDK's linker over the application object and the runtime archives from the pack. The
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

## Step 3: output

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

## The runtime support pack

The compiler and linker need the ahead-of-time runtime built for the device ABI. On the desktop
frameworks these archives ship per platform; the device is not one of them, so the pack is supplied
separately and pointed at by `PROSPERO_RUNTIME_PACK`. It contains:

- The runtime archives the compiled object links against: the runtime core, the exception handling
  support, the bootstrap object that runs before the managed entry, and the platform layer that maps
  the runtime's memory, thread and time calls onto the device services.
- The object-writer support the compiler uses to emit an object in the device ABI.

The platform layer is the part that ties the runtime to the device. Its calls map onto the same
services the SDK binds:

| Runtime need | Device service |
|---|---|
| Reserve and map memory | `sceKernelAllocateDirectMemory`, `sceKernelMapDirectMemory` |
| Protect and unmap memory | the map and release calls with the CPU protection flags |
| Threads and synchronization | the pthread family in `libkernel` |
| Monotonic time | the process-time call in `libkernel` |

Until the pack is present, `dotnet build` of the solution still succeeds and the tests still run; it
is the link step that requires it, and it reports exactly what is missing when it is not set.

## Keeping the heap in bounds

Memory maps are limited, so the heap ceiling matters. Set `ProsperoHeapHardLimitBytes` to the largest
managed heap the module should use; the value is written into the image through `System.GC.HeapHardLimit`.
Read usage at runtime with `SharpProspero.Memory.HeapMonitor`, and prefer drawing into pre-allocated
framebuffers and reusing buffers over allocating each frame.
