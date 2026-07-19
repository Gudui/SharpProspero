---
title: Guides and tips
nav_order: 12
---

# Guides and tips

Everyday recipes for building homebrew with SharpProspero, and how to get out of trouble.

## Load the modules a feature needs

Some services are not resident until loaded. Load them once at startup and dispose at shutdown; loading
an already-loaded module succeeds, so this is safe to repeat.

```csharp
using var pngEnc = SystemModule.Load(SystemModuleId.PngEnc);   // needed by PngEncoder
using var jpegEnc = SystemModule.Load(SystemModuleId.JpegEnc); // needed by JpegEncoder
using var font = SystemModule.Load(SystemModuleId.Font);       // needed by TrueTypeFont
using var fontFt = SystemModule.Load(SystemModuleId.FontFt);
```

`SystemModule.IsLoaded(id)` reports whether one is present. The drawing surface, controller, audio out,
kernel and system services are always available and need no load.

## Read the files bundled with your module

Assets you ship live under the package root `/app0`. Read them with the file APIs:

```csharp
byte[] level = FileSystem.ReadAllBytes("/app0/assets/level.bin");
using var logo = PngImage.Decode(FileSystem.ReadAllBytes("/app0/assets/logo.png"));
```

Writable storage is elsewhere (for example `/data`); the package root is read-only.

## Keep memory in bounds

The console's memory maps are limited, so cap the managed heap and avoid churn:

- Set `<ProsperoHeapHardLimitBytes>` in the project to the largest heap the module should use.
- Read usage at run time with `SharpProspero.Memory.HeapMonitor`.
- Draw into the pre-allocated back buffer each frame and reuse buffers rather than allocating in the
  frame loop. Pull GPU-visible buffers from `SharpProspero.Memory.DirectMemoryRegion`.

## Log while developing

Attach a file sink at startup and read the log back after a run:

```csharp
Log.MinimumLevel = LogLevel.Debug;
Log.AddSink(FileLogSink.Open("/data/app.log"));
Log.AddSink(new ConsoleLogSink());   // also to the development console, if attached
Log.Information("started");
```

See [Utilities](utilities.md) for the full logging surface.

## Run across firmware versions

A module built with the SDK targets the earliest supported system and runs on later ones. Read the
running version, and resolve services by name so one build adapts instead of pinning an address:

```csharp
FirmwareVersion version = FirmwareSupport.RunningVersion;
if (!FirmwareSupport.Provides(feature))
    ShowUnsupported();
```

See [Firmware compatibility](firmware.md).

## Deploy to the console

- Build a `*.pkg` and install it on a console in the mode that accepts unsigned packages, then launch it
  from the home screen.
- While iterating, build with `-Output Folder` to get `eboot.bin`, `sce_sys` and any `sce_module`
  together in one folder to copy directly or inspect with the `elf` tool.

## Ship a library with your application

Build the library as a `.prx` (the `prospero-prx` template), drop it in the application's `sce_module`
folder, and load it by name:

```csharp
using PrxModule lib = PrxModule.LoadFromPackage("mylib.prx");
nint doThing = lib.GetExport("myLibDoThing");
```

See [Modules and libraries](modules.md).

## Troubleshooting the build

| Symptom | Cause and fix |
|---|---|
| "No compiled object was produced" | The compile runs on Linux. On Windows the build uses WSL for it — make sure WSL and the .NET 10 SDK are installed inside it (`doctor.ps1` checks). On macOS, run the compile in a Linux container. See [Setup](setup.md). |
| The link step reports no runtime archives | Run the compile step once so the .NET SDK restores its runtime pack, which `build.ps1` then gathers; on Windows this happens inside WSL. |
| The package installs but the module will not load | The system version the application requires is lower than a module it ships needs. `build-app.ps1` settles this with `-SystemVersionPolicy`; the default `Match` raises the requirement to what the modules need. See [Firmware compatibility](firmware.md). |
| A binding call is unresolved at link time | The module's export is not in the linker's catalog. Add it, or supply a stub for your own module with `ProsperoUserStubLibrary`. See [Bindings](bindings.md). |
| `dotnet new prospero-app` not found | Install the template: `dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-app`. |

## Tips

- Keep `OnFrame` allocation-free: build strings and buffers once, reuse them each frame.
- Prefer the interface toolkit ([UI](ui.md)) over drawing menus by hand; it handles layout and focus.
- Use `Surface.Region` to draw a panel in its own local coordinates and clip to it.
- Verify a module's exports with `elf --file <module> --exports` before depending on them.
- Run `doctor.ps1` first on any new machine; it tells you exactly what is missing.
