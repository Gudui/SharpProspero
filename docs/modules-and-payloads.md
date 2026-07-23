---
title: Modules and payloads
nav_order: 3
parent: Toolchain
---

# Modules and payloads

The toolchain builds two execution forms from the same C#. They reach the console in different ways and
are run by different loaders, and that decides which one fits a given program.

## The two forms

**An application module** is what the console installs and launches. The build links an `eboot.bin`, the
packager wraps it in a signed container and assembles an installable package, and the console installs it
and shows it on the home screen. It runs as its own process with a full lifecycle: install, launch, run,
exit.

**A payload** is a position-independent executable a small loader maps into a process that is already
running, over a network connection, and jumps to. There is no package and no signature. It runs inside
the host process and ends when its entry returns.

| | Application module | Payload |
|---|---|---|
| Output | `eboot.bin` in a package | a single `.elf` |
| Signed and packaged | yes | no |
| Installed | yes, on the home screen | no, sent each run |
| Runs as | its own process | inside a running host process |
| Loaded by | the console's program loader and dynamic linker | a small loader over a network connection |
| Lifecycle | install, launch, run, exit | maps, runs once, returns |
| Delivery | install the package | send over the network each time |

## Why they differ

The decisive difference is what loads each one.

An application module is loaded by the console's own program loader and dynamic linker. That loader binds
the module's imports to the device libraries by name, gives the module its thread-local storage, registers
its exception frames, and runs its global constructors before the entry. The module declares what it needs
and the system provides it.

A payload's loader does none of that. It maps the segments, applies only base-relative fix-ups, and calls
the entry: no import binding, no thread-local set-up, no constructors. So a payload must be self-contained.
It resolves every function it calls at run time through a resolver the loader hands it, and its start code
performs the bring-up the dynamic linker would otherwise do: it runs the global constructors, marks the C
runtime threaded, and allocates and installs the thread-local block the runtime needs. Each thread-local
access is baked to a fixed offset from the thread pointer at link time, and the start code sets the thread
pointer to a block it allocates and fills from the module's template.

Signing follows the same split. An application module is signed and packaged so the console can install and
launch it on its own. A payload rides an already-running host process, so it needs no signature or package,
but it also cannot exist on its own: something must already be running to load it.

Both forms carry the same ahead-of-time runtime, so a payload's `.elf` is as large as a module's
`eboot.bin`; it loads over the network rather than from storage.

## Which to build

Choose by how the program is meant to run.

- **An application module** for a program the user installs and launches from the home screen: a game, a
  tool, a file manager, a media player. It has a normal lifecycle and can reach the full device-service
  surface.
- **A payload** for a program that runs once inside a host process, launched from outside with no install:
  a background service such as a file-transfer, log, or web server; a one-shot action; or a bring-up
  experiment.

## Building each

An application module (compile, link `eboot.bin`, pack) is the default of `build-app.ps1`:

```
pwsh build/build-app.ps1 -ProjectPath MyApp.csproj
```

See [Build pipeline](build-pipeline.md) for the three steps and their options.

A payload (compile, link `--kind payload`, send) adds the `-Payload` switch, then a send:

```
pwsh build/build-app.ps1 -ProjectPath MyServer.csproj -Payload
sharpprospero-bindgen payload --send --host 192.168.1.10 --file MyServer.elf
```

The link is self-contained, so it supplies its own start code; the default port for the send is 9021.
`dotnet new prospero-payload` and the other payload templates start a project already set up for this
path. The template [gallery](templates.md) marks which templates build a payload.
