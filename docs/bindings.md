---
title: Bindings
nav_order: 6
---

# Bindings

The SDK talks to the device through interop bindings in `SharpProspero.Interop`. A binding is a
`partial` class of `[LibraryImport]` methods, plus the enums, structures and constants that go with
the service. The library name matches a loadable module; the linker resolves the symbols against a
stub it generates for that module.

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
  `off_t`, pointers for `void*` and `T*`. Blittable signatures generate no marshalling code.
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
generates for `libSceVideoOut`. The linker generates a stub for each module in its catalog, so a
service already in the catalog needs only its `DirectPInvoke` entry. To add a module the catalog does
not yet cover, list its export names under that module in the linker's catalog; a link then reports
any name still missing so it can be added.

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
pwsh tools/SharpProspero.Bindings.Generator/generate.ps1
```

Each response file names the header to parse, the output namespace (`SharpProspero.Interop.<Name>`),
the method class name, and the library. The response files land under
`src/SharpProspero/Interop/Generated/responses`.

## Built-in bindings

| Namespace | Service | Key entry points |
|---|---|---|
| `Interop.Kernel` | Direct memory, files, timing, modules, system version | reserve, map, release, open, read, seek, clock, load module, system software version, allowed SDK version |
| `Interop.VideoOut` | Display output | open, set attribute, register, submit flip, wait vblank |
| `Interop.Pad` | Controller | init, open, read, vibration, light bar, close |
| `Interop.Keyboard` | USB keyboard | init, open, read state, close |
| `Interop.Mouse` | USB mouse | init, open, read, close |
| `Interop.Net` | Network status, sockets, HTTP download, TLS | status; socket, bind, listen, accept, connect, send, receive, poller, name resolver; pool, ssl, request, read |
| `Interop.Audio` | Audio output and input | output: init, open, output, set volume; input: open, capture, silent state, close |
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

## Result codes

Service calls return a 32-bit result. Non-negative is success; negative is an error. `SceResult`
interprets them: `Succeeded`, `Failed`, and `ThrowIfFailed`, which raises a `ProsperoException`
carrying the operation name and raw code.
