---
title: Toolchain
nav_order: 17
has_children: true
---

# Toolchain

The toolchain turns C# into a module the console runs, without a separate linker, start file, or stub
library. These pages cover how the layers fit together, the compile-link-output pipeline, how bindings
reach the device services, and the module and package formats.

| Page | What it covers |
|---|---|
| [Architecture](architecture.md) | How the layers of the SDK fit together, from the interop bindings up to the application host. |
| [Build pipeline](build-pipeline.md) | The three steps from C# to a module: compile ahead of time, link, and write the output. |
| [Modules and payloads](modules-and-payloads.md) | The two execution forms the toolchain builds, why they differ, and which one to build. |
| [Bindings](bindings.md) | How a device-service binding is shaped, the built-in bindings, and how result codes are read. |
| [Signed and unsigned](signed-and-unsigned.md) | The unsigned and signed forms of a program and a library, and converting between them. |
| [Working with module offsets](offsets.md) | Reading a module's offsets, retargeting the version it records, and contributing a firmware's facts. |
| [The param.json fields](param-json.md) | The package metadata a title carries, and the packaging step that assembles it. |

The reader tool, inspector, and packager are command-line programs; each page above shows the command
for its task. The same command-line program builds texture files from images with its `gnf` command,
covered in [Building a texture file](graphics-gpu.md#building-a-texture-file).
