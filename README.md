# SharpProspero

A C# SDK for building application modules that compile ahead of time to a standalone ELF and optionally packs
into an installable package with LibProsperoPkg. Write the application in C#; the toolchain produces
an `eboot.bin` with no managed runtime to deploy alongside it.

## What it gives you

- **Interop bindings** for the device services an application module uses: direct memory, display
  output, controller input and output, audio output and microphone input, network sockets,
  system-module loading, and the user and system services. Bindings are declared as direct imports; the
  linker resolves them at link time against stubs it generates for each module, so a build needs no stub
  library from elsewhere.
- **A drawing layer**: a framebuffer surface with rectangle and circle fills, lines, outlines, opaque
  and alpha-blended surface copies, PNG and JPEG image decoding, PNG and JPEG encoding for screenshots,
  an 8x8 bitmap font and a scalable TrueType/OpenType font for antialiased text, over a double-buffered
  display device that presents on the vertical blank.
- **An application host**: derive from `ProsperoApp`, override `OnFrame`, and call `Run`. The host
  opens the display and controller, drives a paced loop, and tears everything down on exit.
- **An interface toolkit**: build screens from labels, buttons, lists, checkboxes and progress bars,
  driven by the controller with automatic layout and focus, so an application does not draw its
  interface by hand. See [docs/ui.md](docs/ui.md).
- **Memory tools** for the constrained heap: a direct-memory region with deterministic release, and
  a heap monitor that reads usage against the configured ceiling.
- **Timing and files**: a monotonic clock for frame pacing and measurement, a wall-clock reader for
  the calendar date and time, a reader for the assets bundled with the module, and a filesystem layer
  that lists directories and creates, moves and removes entries.
- **Randomness and settings**: a reproducible generator for gameplay seeded from the system entropy
  source, a reader for the user's system settings (language, date and time formats, time zone), and the
  signed-in users with their display names.
- **System features**: play a media file and pull its decoded audio, open the system browser over the
  running application, and install a package file. Services a title does not link against are loaded
  at run time and resolved by name.
- **App and content management**: install, size, check, uninstall and launch an application by its
  title id; list the photos and videos in the content library, export a file into it, and read a
  file's metadata; find connected USB drives and where they are mounted (mapping one on request), to
  browse them with the file APIs; and let the user pick a save through the save-data dialog.
- **Audio and input**: a stereo audio-output port that paces the caller to the audio clock; a
  microphone-input port for recording and level metering; controller vibration and light-bar control;
  and controller samples decoded down to motion (orientation, acceleration, angular velocity) and
  touch-pad contacts.
- **Networking**: TCP and UDP sockets for a client or a server, a poller for serving many connections
  from one thread, and a name resolver, alongside the connection-status reader and the HTTP downloader.
- **Integrity and capture**: file checksums and digests (SHA-256, SHA-1, MD5, CRC-32) with no module
  needed, screenshot and photo export to PNG or JPEG, and app-loop control such as keeping the console
  awake through a long operation, reacting to system events, and chain-loading another module.
- **System-version range**: a module built with the SDK targets the earliest supported system by
  default and runs on every later one; raise the target only to call a function a later system added.
- **Firmware compatibility at run time**: read the running system version, resolve system services by
  name (so one build adapts across versions instead of pinning an address), and check that a service
  provides every export a feature needs before using it, refusing it with a specific reason otherwise.
  A single registry records the supported range and what each resolved-by-name service depends on. See
  [docs/firmware.md](docs/firmware.md).
- **Module support**: load a `.prx` you supply at run time, resolve its exports, and pack it in the
  application's `sce_module` folder. Build the application as an `eboot.bin` or a `.prx` library.
- **Signed and unsigned forms**: a program is a `.elf` or a signed `.self`, a library a `.prx` or a
  signed `.sprx`. The reader and inspector take either, unwrapping a signed module to its ELF first,
  and a container tool reports which form a file is and converts between them.
- **A module toolkit** that reads a `.prx` or `.sprx`, lists its exports, and generates a C# wrapper
  for it, so a project needs only its own module to interact with it.
- **Firmware tooling**: dump a module's export identifiers and addresses (and how it covers the names
  the SDK needs) so a firmware's facts can be contributed, and retarget a module's recorded version so
  one built for a newer system can load on an older one.
- **A binding generator** that turns the SDK headers into more bindings from a small catalog.
- **Two ways to ship**: pack the built module and its metadata into a `*.pkg`, or write every file
  into a single folder ready to copy (`-Output Folder`).

## Layout

| Path | Contents |
|---|---|
| `src/SharpProspero` | The SDK class library. |
| `src/SharpProspero.Sample` | A sample module and its build script. |
| `tools/SharpProspero.Prx` | Module reader, signed-container reader, identifier computer, and wrapper generator. |
| `tools/SharpProspero.Bindings.Generator` | Header-to-C# binding generator and the `prx`, `elf`, `self`, `offsets` and `retarget` commands. |
| `tools/SharpProspero.Packager` | Command-line packager over LibProsperoPkg. |
| `tests/SharpProspero.Tests` | Unit tests for the drawing, memory, and module code. |
| `build/` | The ahead-of-time compile and link pipeline (props and targets). |
| `runtime/` | The platform layer and how the runtime support pack is assembled. |
| `docs/` | The documentation, and the Jekyll site built from it. |

## Requirements

- .NET 10 SDK.
- A runtime support pack: the ahead-of-time runtime archives compiled for the device ABI, referenced
  through `PROSPERO_RUNTIME_PACK`. See [docs/build-pipeline.md](docs/build-pipeline.md).

The link runs through the SDK's own linker, which supplies its own start object and its own stubs for
the modules the SDK imports from. A build needs no separate linker, start file, or stub library.

## Quick start

Check the setup, then build and test:

```
pwsh doctor.ps1
dotnet build SharpProspero.slnx
dotnet test tests/SharpProspero.Tests/SharpProspero.Tests.csproj
```

`doctor.ps1` reports the .NET SDK and the runtime pack, and prints what to set for anything missing.
A plain build and the tests need only .NET 10.

Scaffold your own application from the template:

```
dotnet new install templates/prospero-app
dotnet new prospero-app -n MyGame --title "My Game"
setx SHARPPROSPERO_ROOT "<this folder>"
pwsh MyGame/build.ps1
```

Or build the sample (with the runtime pack set). Pick the output you want:

```
pwsh src/SharpProspero.Sample/build.ps1                  # an installable *.pkg
pwsh src/SharpProspero.Sample/build.ps1 -Output Folder   # every file in one folder
```

The application itself is a few lines:

```csharp
using SharpProspero.Application;
using SharpProspero.Graphics;

internal sealed class HelloApp : ProsperoApp
{
    protected override void OnFrame(FrameContext context)
    {
        Surface surface = context.Surface;
        surface.Clear(Color.FromRgb(0x0E, 0x11, 0x16));
        surface.DrawTextCentered("Hello from C#", 500, 5, Color.White);
    }
}

internal static class Program
{
    private static void Main() => new HelloApp().Run();
}
```

See [docs/getting-started.md](docs/getting-started.md) to go from an empty project to a build.

## Documentation

The pages under `docs/` are both the repository documentation and a Jekyll site.

## License

GPL-3.0-or-later. Copyright © SvenGDK 2026.
