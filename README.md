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
- **A drawing layer**: a framebuffer surface with rectangle, circle, triangle and polygon fills,
  blended rectangle and circle fills, linear and radial gradients, multi-stop color ramps and palettes,
  rounded rectangles, thin and thick
  lines, outlines, opaque, alpha-blended, scaled (nearest or smooth) and rotated surface copies, and
  sub-region clipping; in-place image effects (grayscale, invert,
  brightness, contrast, tint, flip and blur); PNG, JPEG, BMP, TGA and animated GIF image decoding, PNG, JPEG, BMP and
  TGA encoding for screenshots; off-screen buffers you own for pre-rendering and caching; ellipses, arcs,
  pies and rings, connected lines, thick outlines and nine-part panel stretching; an 8x8 bitmap
  font and a scalable TrueType/OpenType font for antialiased text, with an outlined form that stays
  readable over a photo or a video frame, over a double-buffered display device that presents on the
  vertical blank.
- **A 2D scene**: a movable, zoomable camera that converts between world and screen coordinates and
  clamps to a map's edges, a tile map drawn from a sprite sheet through the camera (loaded from CSV,
  with tile-collision queries), a sprite-sheet animation player, a particle system for effects,
  A* grid pathfinding for routing around walls, a quadtree for fast range queries over a crowded scene,
  and Bezier and Catmull-Rom splines for curved motion paths.
- **A GPU graphics layer (Agc)**: the complete flat-C command interface (192 command builders and 79
  driver calls) under `SharpProspero.Interop.Agc`, and a managed layer over it: a `DrawCommandBuffer` that
  records register writes, draws and synchronization; shader, format and device helpers; the present path;
  surface layout for every tile mode (`AgcSurface`); the sixteen-register color render-target block with
  typed field setters and a description-driven setup (`CxRenderTarget`, `AgcRenderTargetSetup`); and a
  pixel tiler that converts an image between linear and hardware-tiled order for texture upload and
  framebuffer read-back (`AgcTiler`). See [docs/graphics-and-memory.md](docs/graphics-and-memory.md).
- **Text that fits**: wrap a paragraph to a width, place a line left, centred or right, measure the
  wrapped block, and shorten a label that will not fit. It measures through a font abstraction, so the
  same layout serves the built-in text and a loaded outline font.
- **An application host**: derive from `ProsperoApp`, override `OnFrame`, and call `Run`. The host
  opens the display and controller, drives a paced loop, and tears everything down on exit. For the
  logic on top, a state machine runs the app as named states (a menu, a level, a pause screen) with
  paired enter and exit work, an in-process event bus lets parts of the app talk without holding
  references to each other, an undo/redo history backs editor-style tools, a fixed-timestep accumulator
  advances a simulation deterministically, and a localization table keeps user-facing text in data.
- **An interface toolkit**: build screens from labels, buttons, lists, checkboxes, radio groups,
  sliders and progress bars, driven by the controller with automatic layout and focus, so an
  application does not draw its interface by hand. Move between pages with a back-stack of screens,
  stack controls down a column or across a row, wrap a paragraph, divide a tool into tabbed pages,
  scroll content taller than the screen, show a name and its value on one line, put a panel over
  everything that takes the controller until it is answered (with one-call message and confirm
  panels), fill a round meter to a known amount, turn a ring while work is under way, repeat a held
  direction for fast scrolling, and raise a short message that takes itself down.
  See [docs/ui.md](docs/ui.md).
- **Memory tools** for the constrained heap: a direct-memory region with deterministic release, a
  heap monitor that reads usage against the configured ceiling, and an object pool that reuses
  short-lived objects so a hot loop allocates less.
- **Timing and files**: a monotonic clock for frame pacing and measurement, game-logic timers
  (cooldowns, intervals and countdowns) driven by the frame delta, a scheduler that runs a callback after
  a delay or on a repeat, a wall-clock reader for the calendar
  date and time, a reader for the assets bundled with the module, a filesystem layer
  that lists directories, walks and copies whole trees, and creates, moves and removes entries, path
  helpers that pull a path apart and join it back, an INI settings store for a module's own
  configuration, a JSON reader and writer for a config file, a manifest or a reply from a service, an XML
  reader, writer and small document model for a config or data file, a CSV
  reader and writer for a table or an export, a tar reader for unpacking a bundle of assets, an in-memory
  data table to sort, filter and group those rows for a list or grid, a versioned save with forward
  migration, endian-aware binary buffers for reading and writing a save file, a header or a message, ring
  buffers for a rolling history or a byte stream, and base-N (hex, Base32, Base64) encodings for a token
  or a blob.
- **Text and web glue**: format the strings an application shows — a file size, a duration, a
  filename-friendly sort, aligned columns, a hex dump — fuzzy-match a pattern to filter and rank a list
  the way a type-to-find box does, and encode, decode, build and parse URL query strings and form bodies
  for the HTTP client and server.
- **Background work**: run slow work off the frame loop — a one-shot background operation you poll for
  its result, and a small pool of worker threads that run the jobs you queue, so the screen keeps
  drawing while a file loads or a request is in flight; and a hand-off point that runs a callback back on
  the frame thread, so a worker can apply its result to the drawing state safely.
- **Randomness and math**: a reproducible generator for gameplay (ranges, booleans, picking and
  shuffling) seeded from the system entropy source, a two-dimensional vector type for positions,
  velocities and directions, a rectangle type with the point, rectangle and circle overlap tests game
  code reaches for, and the small floating-point helpers that go with them (blend, remap, smooth-step,
  move-towards, angle wrapping).
- **Settings and users**: a reader for the user's system settings (language, date and time formats,
  time zone), and the signed-in users with their display names.
- **System features**: play a media file or a network stream and pull its decoded audio, open the
  system browser over the running application, and install a package file. Read and write the values
  the system keeps for itself, by identifier, where the running build is permitted to. Services a
  title does not link against are loaded at run time and resolved by name.
- **Media decoding**: turn compressed audio (Layer III, Advanced Audio Coding) into samples an audio
  port takes, and decode H.264 a unit at a time to get the pictures themselves — for a stream an
  application receives, or anywhere the frames are wanted rather than playback.
- **App and content management**: install, size, check, uninstall and launch an application by its
  title id; list the photos and videos in the content library, export a file into it, and read a
  file's metadata; find connected USB drives and where they are mounted (mapping one on request), to
  browse them with the file APIs; and let the user pick a save through the save-data dialog.
- **Trophies**: read a title's trophy set and the signed-in player's progress — the set title, the
  unlocked count and completion, and each trophy's grade, name and unlock state — show the system trophy
  list, and unlock a trophy or report an activity or statistic by posting an event through the
  universal-data-system.
- **Audio and input**: a stereo audio-output port that paces the caller to the audio clock; a tone and
  effect generator (sine, square, triangle, sawtooth, noise) for beeps and simple sound effects with no
  audio file; a mixer that layers several sounds at once, spreading mono to stereo and retuning a clip
  recorded at another rate; a microphone-input port for recording and level metering; 16-bit WAV file
  reading and writing with no module; controller vibration and light-bar control; controller samples
  decoded down to motion (orientation, acceleration, angular velocity) and touch-pad contacts; and an
  action map that names the controls (single button, chord, or alternatives) so game code reads clearly
  and controls are easy to rebind.
- **Networking**: TCP and UDP sockets for a client or a server, a poller for serving many connections
  from one thread, a small HTTP/1.1 server for a control panel or a file browser, and a name resolver,
  alongside the connection-status reader and the HTTP downloader.
- **Integrity and capture**: file checksums and digests (SHA-256, SHA-512, SHA-1, MD5, CRC-32, SHA-3) and
  keyed digests (HMAC over any of them) with no module needed; screenshot and photo export of a
  drawing surface to PNG or JPEG; system capture of the whole
  composited screen to the gallery as a 2K or 4K screenshot or a video clip; live capture of the
  finished screen's audio and video for a recorder or stream (an advanced, privileged surface); and
  app-loop control such as keeping the console awake through a long operation, reacting to system
  events, and chain-loading another module.
- **Logging and timing**: a small leveled logging facility with a file sink and a console sink, so a
  module can record progress and errors to a file the user reads back or to the development console;
  and a frame-rate and frame-time tracker that reads out the recent pace and graphs it over the screen.
- **Optical disc**: browse a mounted disc's filesystem under `/mnt/disc` with the file APIs, and open
  the raw drive device for a sector read or a full dump, where the running module has the privilege to
  reach the drive.
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
| `templates/` | `dotnet new` templates for an application, an interface app, a toolbox, and a library. |
| `runtime/` | Notes on how the ahead-of-time runtime is sourced and its operating-system surface met. |
| `docs/` | The documentation, and the Jekyll site built from it. |

## Requirements

- .NET 10 SDK, on Windows, Linux or macOS (64-bit).
- The compile step runs on Linux; on Windows the build runs it through WSL automatically, so no host
  switch is needed. Nothing else is set up: the runtime comes from the .NET SDK's own runtime pack, and
  the SDK's linker supplies its own start object, a compat object, and the module stubs.

See [docs/setup.md](docs/setup.md) for the full per-operating-system install.

The link runs through the SDK's own linker; a build needs no runtime pack, separate linker, start file,
or stub library.

## Quick start

Check the setup, then build and test:

```
pwsh doctor.ps1
dotnet build SharpProspero.slnx
dotnet test tests/SharpProspero.Tests/SharpProspero.Tests.csproj
```

`doctor.ps1` reports the .NET SDK and, on Windows, the WSL host the compile step uses, and prints what
to set for anything missing. A plain build and the tests need only .NET 10.

Scaffold your own project from a template — an application, an interface app, a toolbox, or a library:

```
dotnet new install templates/prospero-app
dotnet new prospero-app -n MyGame --title "My Game"
setx SHARPPROSPERO_ROOT "<this folder>"
pwsh MyGame/build.ps1
```

The other templates are `prospero-game` (a real-time game), `prospero-ui`, `prospero-launcher` (a
carousel launcher front end), `prospero-filemanager` (a file browser), `prospero-tool`, `prospero-media`
(a media player), `prospero-server` (an HTTP control panel) and `prospero-prx`; see
[docs/templates.md](docs/templates.md).

Or build the sample. Pick the output you want:

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

Start with these:

- [Getting started](docs/getting-started.md) — from nothing to a running module.
- [Setup](docs/setup.md) — the full install for Windows, Linux and macOS (64-bit).
- [Templates](docs/templates.md) — starting points for each kind of project.
- [Guides and tips](docs/guides.md) — everyday recipes and troubleshooting.
- [Architecture](docs/architecture.md) and [Build pipeline](docs/build-pipeline.md) — how it all fits.

The rest cover the drawing surface, networking, utilities, the interface toolkit, bindings, modules,
firmware compatibility, and the signed and unsigned module forms.

The pages under `docs/` are both the repository documentation and a Jekyll site.

## License

GPL-3.0-or-later. Copyright © SvenGDK 2026.
