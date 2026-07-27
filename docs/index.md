---
title: Home
nav_order: 1
---

# SharpProspero

A C# SDK for building application modules that compile ahead of time to a standalone ELF and either
pack into an installable package or land in a plain folder. Write the application in C#; the toolchain
produces an `eboot.bin` with no separate runtime to deploy alongside it. The same toolchain builds a
`.prx` library and a payload `.elf` a loader maps over the network — see
[Modules and payloads](modules-and-payloads.md).

## Start here

1. [Setup](setup.md) installs the toolchain for Windows, Linux, and macOS.
2. [Getting started](getting-started.md) takes an empty project to a running module in a few steps.
3. [Templates](templates.md) are ready-to-build starting points for each kind of project.
4. [Guides and tips](guides.md) collect everyday recipes and troubleshooting.

## The documentation

| Section | What it covers |
|---|---|
| [Graphics](graphics.md) | The drawing surface, images, fonts, [2D scenes](graphics-scene.md), and the [GPU command layer](graphics-gpu.md). |
| [Input](input.md) | The controller, motion and touch, rumble and light bar, keyboard, and mouse. |
| [Audio](audio.md) | Output and microphone, decoding, encoding, filters, envelopes, synthesis, and mixing. |
| [Media](media.md) | Playing a media file, reading a track's tags, and decoding video. |
| [Networking](networking.md) | TCP and UDP sockets, a poller, an HTTP client and server, and downloads. |
| [Memory](memory.md) | Direct and flexible memory, the managed heap, pooling, and an asset cache. |
| [Application](application.md) | The app host, [timing](timing.md), [threading](threading.md), [diagnostics](diagnostics.md), and [animation](animation.md). |
| [Interface toolkit](ui.md) | Building screens from labels, buttons, lists, and other controls. |
| [Data and utilities](data.md) | [Files](storage.md), [numerics](numerics.md), [buffers](buffers.md), [text](text.md), [XML](xml.md), [compression](compression.md), and [hashing](hashing.md). |
| [System services](system-services.md) | [System info](system.md), [dialogs](dialogs.md), [save data](save-data.md), [trophies](trophies.md), [capture](content-capture.md), [packages and devices](packages-devices.md), and [firmware](firmware.md). |
| [Modules and libraries](modules.md) | Loading a module at run time and building a library. |
| [Toolchain](toolchain.md) | [Architecture](architecture.md), the [build pipeline](build-pipeline.md), [modules and payloads](modules-and-payloads.md), [bindings](bindings.md), the [signed and unsigned forms](signed-and-unsigned.md), [module offsets](offsets.md), [the param.json fields](param-json.md), and the [command reference](commands.md). |

Not sure where a type lives? Every page names the namespace it documents, and the search box (press
`s`) indexes the whole site.

## What it gives you

- **Interop bindings** for the device services a module uses: display output, controller input and
  output, audio output and microphone input, files and directories, image decode and encode, the
  real-time clock, the entropy source, and the user and system services. See [Bindings](bindings.md).
- **A drawing layer**: a framebuffer surface with fills, lines, circles, opaque and alpha-blended
  copies, in-place image effects, PNG, JPEG, BMP, TGA, and GIF decode with PNG, JPEG, BMP, and TGA
  encode, a bitmap font and a scalable TrueType font, over a double-buffered display. See
  [Graphics](graphics.md).
- **A graphics-processor layer**: the command interface, a command buffer, render-target, blend,
  depth-stencil and viewport state, image and sampler descriptors, surface tiling, and a lit 3D mesh
  renderer over it. See [GPU command layer](graphics-gpu.md).
- **An application host**: derive from `ProsperoApp`, override `OnFrame`, call `Run`. See
  [Application](application.md).
- **An interface toolkit**: build screens out of controls driven by the controller, so you do not draw
  the interface by hand. See [Interface toolkit](ui.md).
- **Audio, media and networking**: an output port paced to the audio clock, a mixer, synthesis and
  filters, a microphone port, compressed-audio and video decoding, and TCP, UDP and HTTP. See
  [Audio](audio.md), [Media](media.md), and [Networking](networking.md).
- **System services**: save data, on-screen dialogs, trophies, screen capture, package install, and
  firmware compatibility. See [System services](system-services.md).
- **A toolchain that stands alone**: the linker supplies its own start object and its own module
  stubs, so a build needs no separate linker, start file, or stub library. Its commands inspect, strip,
  retarget, convert and package a module. See [Toolchain](toolchain.md) and the
  [command reference](commands.md).

## Requirements

- .NET 10 SDK, on Windows, Linux or macOS (64-bit). Nothing else is set up: the runtime comes from the
  .NET SDK's own runtime pack, and the linker supplies its own start object, compatibility object and
  module stubs.
- The compile step runs on Linux; on Windows the build uses WSL for it automatically. See the
  [build pipeline](build-pipeline.md).

`pwsh doctor.ps1` in the SDK folder checks a machine and prints what to set for anything missing.

## License

GPL-3.0-or-later. Copyright (C) SvenGDK 2026.
