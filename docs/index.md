---
title: Home
nav_order: 1
---

# SharpProspero

A C# SDK for building application modules that compile ahead of time to a standalone ELF and either
pack into an installable package or land in a plain folder. Write the application in C#; the
toolchain produces an `eboot.bin` with no managed runtime to deploy alongside it.

## Start here

1. [Getting started](getting-started.md) takes an empty project to a running module in a few steps.
2. [Setup](setup.md) is the full install for Windows, Linux and macOS (64-bit).
3. [Templates](templates.md) are ready-to-build starting points for each kind of project.
4. [Guides and tips](guides.md) collect everyday recipes and troubleshooting.
5. [Architecture](architecture.md) explains how the layers fit together, and
   [Build pipeline](build-pipeline.md) covers compile, link and output.

## What it gives you

- **Interop bindings** for the device services a module uses: direct memory, display output,
  controller input and output, audio output and microphone input, files and directories, image decode
  and encode, the real-time clock, the entropy source, and the user and system services.
- **A drawing layer**: a framebuffer surface with fills, lines, circles, outlines, opaque and
  alpha-blended copies, in-place image effects (grayscale, brightness, contrast, tint, flip, blur),
  PNG, JPEG and BMP decode and encode, a bitmap font and a scalable TrueType font, over a
  double-buffered display.
- **An application host**: derive from `ProsperoApp`, override `OnFrame`, call `Run`.
- **An interface toolkit**: build screens out of labels, buttons, lists, checkboxes and progress bars,
  driven by the controller, so you do not draw the interface by hand. See
  [Interface toolkit](ui.md).
- **Networking**: TCP and UDP sockets, a poller for serving many connections from one thread, a small
  HTTP/1.1 server for a control panel or a file browser, and a name resolver, on top of the connection
  status and HTTP download. See [Networking](networking.md).
- **Utilities**: file checksums and digests (SHA-256, SHA-1, MD5, CRC-32), screenshot and photo
  export, WAV audio reading and writing, an INI settings store, microphone capture, and app-loop
  control such as keeping the console awake. See [Utilities](utilities.md).
- **Firmware compatibility**: read the running system version, resolve system services by name so one
  build runs across versions, and check that a service resolves before using it. See
  [Firmware compatibility](firmware.md), and [Working with module offsets](offsets.md) for the
  hands-on guide to reading, adjusting, and using a module's offsets.
- **A toolchain that stands alone**: the linker supplies its own start object and its own module
  stubs, so a build needs no separate linker, start file, or stub library.

## The toolchain

| Piece | What it does |
|---|---|
| Linker | Reads objects and archives, resolves the symbol graph, and writes the module. |
| Module reader | Lists a module's exports and generates a C# wrapper for it. |
| Inspector | Prints a module's header, segments, dependencies and exports; reads a plain or a signed module alike. |
| Container tool | Reports whether a file is signed or unsigned, and converts between the two. |
| Offset dump | Reads a module and dumps its export identifiers and addresses, and how it covers the names the SDK needs, so a firmware's facts can be contributed. |
| Retarget | Rewrites the version a module records it was built against (and a library version tag), so a module built for one system can load on another. |
| Packager | Assembles the built module and its metadata into a package. |

A program and a library each have an unsigned and a signed form: `.elf`/`.self` and `.prx`/`.sprx`.
[Signed and unsigned](signed-and-unsigned.md) explains what each is and how to read and convert them.

## Requirements

- .NET 10 SDK.
- The compile step runs on Linux; on Windows the build uses WSL for it automatically. The runtime
  itself comes from the .NET SDK, with nothing else to set up. See [the build pipeline](build-pipeline.md).

## License

GPL-3.0-or-later. Copyright (C) SvenGDK 2026.
