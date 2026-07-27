---
title: Bindings
nav_order: 4
parent: Toolchain
---

# Bindings

The SDK talks to the device through interop bindings in `SharpProspero.Interop`. A binding is a
`partial` class of `[LibraryImport]` methods, plus the enums, structures and constants that go with
the service. The name in the attribute is a library, not a module file; a stub the linker generates
records both, and which module carries the library comes from the catalog. See below.

## How a binding is shaped

```csharp
public static unsafe partial class VideoOut
{
    private const string Lib = "libSceVideoOut";

    [LibraryImport(Lib)]
    public static partial int sceVideoOutOpen(int userId, int busType, int index, void* param);
}
```

Rules that keep bindings compatible with ahead-of-time compilation:

- Use blittable parameter and return types: the integer types, `nuint` for `size_t`, `long` for
  `off_t`, pointers for `void*` and `T*`. Blittable signatures generate no marshalling code. The one
  worthwhile exception is a NUL-terminated string argument: add
  `StringMarshalling = StringMarshalling.Utf8` to the attribute and take a `string`, which the
  generator converts without a runtime marshaller.
- Keep methods `static partial` in a `partial` class; the source generator writes the call.
- Pass buffers as pointers and let the caller pin or stack-allocate them.
- Match structure layout exactly with `[StructLayout(LayoutKind.Sequential)]` and the fields in
  header order, including reserved and padding fields.

## Direct imports

Each module an application uses is declared a direct import in `build/Prospero.App.props`:

```xml
<DirectPInvoke Include="libSceVideoOut" />
```

This turns the binding call into a direct symbol reference the linker resolves against the stub it
generates for `libSceVideoOut`. The linker generates a stub for each entry in its catalog, so a
service already covered needs only its `DirectPInvoke` entry. To add one the catalog does not yet
cover, list its export names under a new entry; a link then reports any name still missing so it can
be added.

### A module is not a library

A catalog entry is a **library**, not a module, and one module can publish several. `libkernel.prx`
publishes both `libkernel` and the portable-interface library `libScePosix`, so the catalog carries
two entries that name the same module file:

```csharp
new Entry("libkernel", Kernel),
new Entry("libScePosix", Posix, ModuleName: "libkernel", Soname: "libkernel.prx"),
```

An import records the module it comes from *and* the library within it, and the two are numbered
separately. A module publishing two libraries is named once in the needed list and carries an
import-library record for each.

Getting this wrong does not fail the link — it produces a module whose imports the loader cannot bind,
which installs, starts, and never reaches its first instruction with nothing written to any log.

**Only list a name a module really publishes.** A catalog entry asserts that it does, so a name listed
there that nothing publishes becomes exactly that unbindable import. A name the platform does not offer
belongs in the compat object instead, where it gets a definition — a forward, a fixed answer, or a
refusal its caller handles. A link reports what it cannot resolve rather than writing a module that
cannot load, and a test holds the line: every name the SDK imports is either named by the catalog or
defined by the compat object.

## Generating bindings from a module

The primary path needs nothing but the module you interact with. The generator reads a `.prx`,
computes the identifier for each name you list, verifies it is exported, and writes a wrapper. See
[modules.md](modules.md) for the full workflow:

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- \
  prx --module mylib.prx --names names.txt --class MyLib --namespace My.App --out MyLib.g.cs
```

This makes no external calls and needs no headers.

## Response files for header processing

For projects that already process headers separately, the generator can also write a response file
per module from a catalog (`modules.json`). It only writes the files; it invokes nothing.

```
pwsh tools/SharpProspero.Bindings.Generator/generate.ps1 -SdkInclude <folder>
```

`-SdkInclude` names the header tree. Without it the script reads `PROSPERO_SDK_DIR` and appends
`target/include`; with neither set it stops and says so. Running the tool directly takes the same
folder as `--sdk`, plus `--modules` for another catalog and `--responses` for another destination.

Each response file names the header to parse, the output namespace (`SharpProspero.Interop.<Name>`),
the method class name, and the library. The response files land under
`src/SharpProspero/Interop/Generated/responses`.

## Built-in bindings

| Namespace | Service | Key entry points |
|---|---|---|
| `Interop.Kernel` | Direct and flexible memory, memory info, files, timing, modules, system version and identity | reserve, map, release direct memory; map, release, protect flexible memory; available flexible and direct memory, virtual query; open, read, seek, clock, load module, system software version, allowed SDK version, a named system value; the console identifier, which the same module publishes under `libSceOpenPsId` rather than `libkernel` |
| `Interop.VideoOut` | Display output | open, set attribute, register, submit flip, wait vblank |
| `Interop.Pad` | Controller | init, open, read, vibration, light bar, close |
| `Interop.Keyboard` | USB keyboard | init, open, read state, close |
| `Interop.Mouse` | USB mouse | init, open, read, close |
| `Interop.Net` | Network status, sockets, HTTP download, TLS | status; socket, bind, listen, accept, connect, send, receive, poller, name resolver; pool, ssl, request, read |
| `Interop.Audio` | Audio output, input, decode, encode, synthesis | output: init, open, output, set volume; input: open, capture, silent state, close; decode (AAC/ATRAC9/MP3) create, decode; encode AAC-LC and ATRAC9; Ngs2 synthesis and mixing (system, rack, voice, stream) |
| `Interop.Audio` (spatial) | Object-based spatial audio (Audio3d) | initialize, open a port, reserve objects, set position and attributes, write a bed, mix to the audio-out |
| `Interop.Audio` (jobs) | The audio job manager (Ajm) | initialize, register memory and modules, create instances, build/start/wait batches for Opus, AAC, MP3, ATRAC9 decode and encode |
| `Interop.Text` | Character-encoding conversion (Ces) | EUC-JP, EUC-KR, Big5 and UHC to UTF-8 (use `System.Text.Encoding` between the Unicode forms) |
| `Interop.Video` | Video decode and recording | decode: create decoder, decode, flush, reset, query memory (H.264 and HEVC via the codec type); recording: status, query memory, open, start, stop, close |
| `Interop.Vision` | Camera depth | query memory, initialize, set command, set region, submit, wait, get image |
| `Interop.AvCapture` | Video capture | open a video channel, start, read frames, stop, close |
| `Interop.Device` | Message-bus device service | initialize, generation counter, event state, query device info |
| `Interop.Sysmodule` | System modules | load, unload, is-loaded |
| `Interop.Image` | Image decode and encode | PNG and JPEG decode; PNG and JPEG encode (for a screenshot) |
| `Interop.Font` | Scalable text | load a TrueType or OpenType font, scale it, render antialiased glyphs |
| `Interop.Rtc` | Real-time clock | current clock, local time, tick, resolution |
| `Interop.Random` | Entropy | random bytes |
| `Interop.Dialog` | Common dialog subsystem, browser dialog, on-screen keyboard, message dialog, error dialog, save-data dialog | initialize subsystem, open, status, result, close |
| `Interop.Media` | Media playback | init, add source, start, audio frames, close |
| `Interop.UserService` | Users | initialize, initial user, the signed-in users, a user's name, terminate |
| `Interop.SystemService` | System | hide splash, read a parameter, launch another title, keep awake, receive events, read status, safe area, load an executable |
| `Interop.SaveData` | Save data | mount, read the parameters, search directories, delete |
| `Interop.AppContent` | Additional content | initialize, read the boot parameters |
| `Interop.PlayGo` | Install progress | initialize, open, read the install progress |
| `Interop.Compression` | Compression | inflate a zlib or deflate stream |
| `Interop.Content` | Content library | delete by path or id, count, size, search photos and videos, export a file |
| `Interop.Share` | System capture | initialize, capture a screenshot or a video clip of the composited screen, recording status, screenshot overlay, permit or prohibit a feature |
| `Interop.Notification` | Notification service | send a message, send by id, show and hide the persistent PS-button banner |
| `Interop.Np` | Trophies and events | trophy2: contexts, game and group and trophy info, icons, show list; universal data system: post named events with properties |
| `Interop.Bluetooth` | Bluetooth HID driver | init; register callback and device; get report descriptor, device name, device info; get and set reports; interrupt output; disconnect (low-level, privileged) |
| `Interop.FeatureFlag` | Console feature flags | is a feature on, is it waiting for a reboot |
| `Interop.Agc` | Graphics command layer | 192 command builders: draw, dispatch, register writes, synchronization, shader create/link, register defaults, packet patching |
| `Interop.Agc` (driver) | Graphics submission | 79 driver calls: submit, queue management, display flip, wait-until-safe, resource registration, workload streams |

## Result codes

Service calls return a 32-bit result. Non-negative is success; negative is an error. `SceResult`
interprets them: `Succeeded`, `Failed`, and `ThrowIfFailed`, which raises a `ProsperoException`
carrying the operation name and raw code.
