---
title: Command reference
nav_order: 8
parent: Toolchain
---

# Command reference

Every command the toolchain offers, in one place. Run any of them with `--help` for its options.

```
sharpprospero-bindgen <command> [options]
sharpprospero-bindgen <command> --help
```

Building an application does not need any of these directly — `build/build-app.ps1` runs the ones it
needs. They are here for when you want to look at what was built, work with a module you were given, or
build a piece by hand.

## Building a module

| Command | What it does | More |
|---|---|---|
| `link` | Links objects into an `eboot.bin`, a `.prx` library, or a payload. | [Build pipeline](build-pipeline.md) |
| `crt` | Writes the start object that carries the program entry point. | [Build pipeline](build-pipeline.md) |
| `compat` | Writes the compatibility object that bridges the runtime's C calls to what the device publishes. | [Build pipeline](build-pipeline.md) |
| `stub` | Builds an import library for a module, either from the module itself or from a name list. | [Modules and libraries](modules.md) |
| `self` | Signs, extracts, or reports the form of a container. | [Signed and unsigned](signed-and-unsigned.md) |
| `payload` | Sends a payload built with `link --kind payload` to a listening loader. | [Modules and payloads](modules-and-payloads.md) |

`crt` and `compat` exist so the same objects this toolchain links can also be handed to another linker.
Producing both builds and comparing them is the sharpest check available on what a module carries:

```
sharpprospero-bindgen compat --out compat.o
sharpprospero-bindgen crt    --out crt.o
```

## Looking at a module

| Command | What it does | More |
|---|---|---|
| `elf` | Prints a module's header, program headers, dependencies and exports; also its loadable size, symbols, strings, or a stripped copy. | [Signed and unsigned](signed-and-unsigned.md) |
| `prx` | Lists a module's exports with their identifiers, or generates a C# wrapper for it. | [Modules and libraries](modules.md) |
| `diff` | Reports the exports added, removed and moved between two modules. | [Working with module offsets](offsets.md) |
| `nid` | Computes the export identifier for a name, so a name you know maps to an export you can confirm. | [Modules and libraries](modules.md) |
| `offsets` | Dumps a module's export identifiers and addresses, and how they cover the names the SDK needs. | [Working with module offsets](offsets.md) |
| `shader` | Reports a compiled shader binary: its kind, version, sizes and register writes. | [Graphics on the GPU](graphics-gpu.md) |

A module this toolchain writes names its regions in a section table, so the tools that read a module
built any other way read one of these too — listing sections or disassembling a range needs no extra
step.

## Packaging and metadata

| Command | What it does | More |
|---|---|---|
| `param` | Reads and repairs `sce_sys/param.json`, and lists the kinds of title. | [The param.json fields](param-json.md) |
| `modules` | Checks that every module an application has to carry travels with it, and gathers the missing ones. | [Modules and libraries](modules.md) |
| `sysver` | Settles the system version an application records against the modules it ships. | [Firmware](firmware.md) |
| `retarget` | Rewrites a module's recorded version so one built for a newer system loads on an older one. | [Firmware](firmware.md) |

## Content

| Command | What it does | More |
|---|---|---|
| `gnf` | Turns a PNG, TGA or BMP into a texture the graphics processor samples; also reports one. | [Graphics on the GPU](graphics-gpu.md) |
| `vag` | Converts a 16-bit PCM WAV to compact sound-effect audio, and back. | [Audio](audio.md) |

## Bindings

| Command | What it does | More |
|---|---|---|
| `modules` (with `--catalog`) and the generator's binding mode | Turns the SDK headers into more bindings from a small catalog. | [Bindings](bindings.md) |

Run the tool with no arguments to see the same list from the command line.
