---
title: Home
nav_order: 1
---

# SharpProspero

A C# SDK for building application modules that compile ahead of time to a standalone ELF and either
pack into an installable package or land in a plain folder. Write the application in C#; the toolchain
produces an `eboot.bin` with no separate runtime to deploy alongside it.

## Start here

1. [Setup](setup.md) installs the toolchain for Windows, Linux, and macOS.
2. [Getting started](getting-started.md) takes an empty project to a running module in a few steps.
3. [Templates](templates.md) are ready-to-build starting points for each kind of project.
4. [Guides and tips](guides.md) collect everyday recipes and troubleshooting.

## The documentation

| Section | What it covers |
|---|---|
| [Graphics](graphics.md) | The drawing surface, images, fonts, 2D scenes, and the GPU command layer. |
| [Input](input.md) | The controller, motion and touch, rumble and light bar, keyboard, and mouse. |
| [Audio](audio.md) | Output and microphone, decoding, encoding, synthesis, and mixing. |
| [Media](media.md) | Playing a media file and decoding video. |
| [Networking](networking.md) | TCP and UDP sockets, a poller, an HTTP client and server, and downloads. |
| [Memory](memory.md) | Direct and flexible memory, and the managed heap. |
| [Application](application.md) | The app host, timing, threading, diagnostics, and animation. |
| [Interface toolkit](ui.md) | Building screens from labels, buttons, lists, and other controls. |
| [Data and utilities](data.md) | Files, JSON and XML, buffers, numerics, text, and hashing. |
| [System services](system-services.md) | Save data, dialogs, trophies, capture, packages, and firmware. |
| [Modules and libraries](modules.md) | Loading a module at run time and building a library. |
| [Toolchain](toolchain.md) | Architecture, the build pipeline, bindings, and the package formats. |

Not sure where a type lives? Every page names the namespace it documents, and the search box (press
`s`) indexes the whole site.

## What it gives you

- **Interop bindings** for the device services a module uses: display output, controller input and
  output, audio output and microphone input, files and directories, image decode and encode, the
  real-time clock, the entropy source, and the user and system services.
- **A drawing layer**: a framebuffer surface with fills, lines, circles, opaque and alpha-blended
  copies, in-place image effects, PNG, JPEG, BMP, TGA, and GIF decode and encode, a bitmap font and a
  scalable TrueType font, over a double-buffered display. See [Graphics](graphics.md).
- **An application host**: derive from `ProsperoApp`, override `OnFrame`, call `Run`. See
  [Application](application.md).
- **An interface toolkit**: build screens out of controls driven by the controller, so you do not draw
  the interface by hand. See [Interface toolkit](ui.md).
- **System services**: save data, on-screen dialogs, trophies, screen capture, package install, and
  firmware compatibility. See [System services](system-services.md).
- **A toolchain that stands alone**: the linker supplies its own start object and its own module
  stubs, so a build needs no separate linker, start file, or stub library. See [Toolchain](toolchain.md).

## Requirements

- .NET 10 SDK.
- The compile step runs on Linux; on Windows the build uses WSL for it automatically. See the
  [build pipeline](build-pipeline.md).

## License

GPL-3.0-or-later. Copyright (C) SvenGDK 2026.
