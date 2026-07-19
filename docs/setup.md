---
title: Setup
nav_order: 3
---

# Setup

Everything needed to develop a homebrew application or a library, on Windows, Linux or macOS (64-bit).
Follow the section for your operating system, then run `doctor.ps1` to confirm the machine is ready.

## What the toolchain needs

Two things, whatever the host:

| Item | What it is | Needed for |
|---|---|---|
| **.NET 10 SDK** | The C# compiler and the ahead-of-time compiler. | Building and testing. |
| **SDK checkout** | This repository. Pointed at by `SHARPPROSPERO_ROOT`. | Templates and the shared build script. |

The runtime is not a separate item: the compile step restores the .NET SDK's own ahead-of-time runtime
pack, and `build.ps1` gathers it from there. The linker, the start object, the compat object and the
import stubs are part of the SDK itself, so there is no runtime pack, separate linker, start file, or
stub library to install. See [Build pipeline](build-pipeline.md) for what each step does.

## One thing to know about the compile step

The application is compiled ahead of time into an **ELF x86-64 object** (the console's ABI). The
ahead-of-time compiler emits an object only for the operating system it runs on, so the compile step
runs on Linux:

- **Linux (x64)** produces the object natively — the simplest host, and the whole pipeline runs there.
- **Windows** runs the link and pack steps natively (they are plain .NET); for the compile step,
  `build.ps1` uses **WSL** automatically, so you build in place without switching hosts.
- **macOS** runs the compile in a Linux container; the link and pack steps run natively.

## Windows (x64)

1. **Install the .NET 10 SDK.** Download it from <https://dotnet.microsoft.com/download>, or:

   ```
   winget install Microsoft.DotNet.SDK.10
   ```

   Confirm with `dotnet --version` (reports `10.x`). Install PowerShell 7 (`winget install Microsoft.PowerShell`) if you do not have `pwsh`.

2. **Point the environment at the SDK** (persisted for new terminals):

   ```
   setx SHARPPROSPERO_ROOT "C:\path\to\SharpProspero"
   ```

3. **Install WSL for the compile step.** The build runs the compile inside WSL automatically; you do not
   run anything there by hand.

   ```
   wsl --install -d Ubuntu
   # inside Ubuntu, install the .NET 10 SDK (dotnet-install.sh --channel 10.0)
   ```

   With WSL and .NET present, `build.ps1` runs the compile in WSL and the link and pack on Windows, over
   the object WSL produced (both sides share the project's `obj` folder).

## Linux (x64)

1. **Install the .NET 10 SDK** — from the distribution feed or the install script:

   ```
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
   export PATH="$HOME/.dotnet:$PATH"
   ```

   Install PowerShell (`dotnet tool install --global PowerShell`) so `pwsh` is available.

2. **Point the environment at the SDK** (add to `~/.bashrc` or `~/.profile`):

   ```
   export SHARPPROSPERO_ROOT="$HOME/SharpProspero"
   ```

3. Nothing more — Linux x64 produces the console object natively, so the whole `publish → link → pack`
   pipeline runs on this host with no extra setup.

## macOS (x64 and Apple Silicon)

1. **Install the .NET 10 SDK** — from <https://dotnet.microsoft.com/download> or Homebrew, and PowerShell:

   ```
   brew install --cask dotnet-sdk
   brew install powershell/tap/powershell
   ```

2. **Point the environment at the SDK** (add to `~/.zshrc`):

   ```
   export SHARPPROSPERO_ROOT="$HOME/SharpProspero"
   ```

3. **The compile step.** The ahead-of-time compiler does not cross-compile to Linux from macOS, and
   there is no WSL, so run the compile in a Linux container (Docker Desktop, Colima or Lima), then run
   the link and pack on the host:

   ```
   docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
     dotnet publish -c Release -r linux-x64 MyGame/MyGame.csproj
   ```

   The simplest alternative is to run the whole build inside a Linux container or on a Linux host.

## Confirm the setup

```
pwsh doctor.ps1
```

Expect `[ ok ]` for the .NET SDK and the SDK root, and (on Windows) the WSL compile host. Then build
the included sample end to end:

```
pwsh build/build-app.ps1 -ProjectPath src/SharpProspero.Sample/SharpProspero.Sample.csproj
```

A `*.pkg` appears under the sample's `out` folder.

## Build a homebrew application vs a library

The same pipeline produces both; the project's `ProsperoModuleKind` decides which:

- **Application** (the default) → an `eboot.bin` the console launches. Templates: `prospero-app`,
  `prospero-ui`, `prospero-tool`.
- **Library** → a relocatable `.prx` another module loads at run time. Template: `prospero-prx`. Set
  `<ProsperoModuleKind>Prx</ProsperoModuleKind>`; the module is named `<AssemblyName>.prx`.

See [Templates](templates.md) for each, and [Modules and libraries](modules.md) for how a module loads a
`.prx` you supply.
