---
title: Setup
nav_order: 3
---

# Setup

Everything needed to develop a homebrew application or a library, on Windows, Linux or macOS (64-bit).
Follow the section for your operating system, then run `doctor.ps1` to confirm the machine is ready.

## What the toolchain needs

Three things, whatever the host:

| Item | What it is | Needed for |
|---|---|---|
| **.NET 10 SDK** | The C# compiler and the ahead-of-time compiler. | Building and testing. |
| **Runtime pack** | The ahead-of-time runtime built for the console, plus the object writer for its ABI. Pointed at by `PROSPERO_RUNTIME_PACK`. | Producing a runnable module. |
| **SDK checkout** | This repository. Pointed at by `SHARPPROSPERO_ROOT`. | Templates and the shared build script. |

The linker, the start object and the import stubs are part of the SDK itself, so there is no separate
linker, start file, or stub library to install. See [Build pipeline](build-pipeline.md) for what each
step does.

## One thing to know about the compile step

The application is compiled ahead of time into an **ELF x86-64 object** (the console's ABI). The compiler
emits an object in the format of the host it runs on unless the runtime pack supplies the object writer
for the console ABI:

- **Linux (x64)** produces the console object natively — the simplest host.
- **Windows and macOS** run the link and pack steps natively (they are plain .NET), but the compile step
  needs either the object writer in the runtime pack, or a Linux environment (WSL2 on Windows, a container
  on macOS) to produce the object. Both routes are covered below.

## Windows (x64)

1. **Install the .NET 10 SDK.** Download it from <https://dotnet.microsoft.com/download>, or:

   ```
   winget install Microsoft.DotNet.SDK.10
   ```

   Confirm with `dotnet --version` (reports `10.x`). Install PowerShell 7 (`winget install Microsoft.PowerShell`) if you do not have `pwsh`.

2. **Point the environment at the runtime pack and the SDK** (persisted for new terminals):

   ```
   setx PROSPERO_RUNTIME_PACK "C:\path\to\runtime-pack"
   setx SHARPPROSPERO_ROOT "C:\path\to\SharpProspero"
   ```

   Assemble the runtime pack as described in `runtime/README.md`.

3. **The compile step.** If the runtime pack includes the object writer for a Windows host, the build
   works directly. Otherwise, install **WSL2** with Ubuntu and the .NET 10 SDK, and run the compile there:

   ```
   wsl --install -d Ubuntu
   # inside Ubuntu: install the .NET 10 SDK, then
   dotnet publish -c Release -r linux-x64 MyGame/MyGame.csproj
   ```

   The link and pack steps then run on Windows over the object WSL produced (`build.ps1` handles this when
   the object is present).

## Linux (x64)

1. **Install the .NET 10 SDK** — from the distribution feed or the install script:

   ```
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
   export PATH="$HOME/.dotnet:$PATH"
   ```

   Install PowerShell (`dotnet tool install --global PowerShell`) so `pwsh` is available.

2. **Point the environment at the runtime pack and the SDK** (add to `~/.bashrc` or `~/.profile`):

   ```
   export PROSPERO_RUNTIME_PACK="$HOME/runtime-pack"
   export SHARPPROSPERO_ROOT="$HOME/SharpProspero"
   ```

3. Nothing more — Linux x64 produces the console object natively, so the whole `publish → link → pack`
   pipeline runs on this host.

## macOS (x64 and Apple Silicon)

1. **Install the .NET 10 SDK** — from <https://dotnet.microsoft.com/download> or Homebrew, and PowerShell:

   ```
   brew install --cask dotnet-sdk
   brew install powershell/tap/powershell
   ```

2. **Point the environment at the runtime pack and the SDK** (add to `~/.zshrc`):

   ```
   export PROSPERO_RUNTIME_PACK="$HOME/runtime-pack"
   export SHARPPROSPERO_ROOT="$HOME/SharpProspero"
   ```

3. **The compile step.** As on Windows, the link and pack steps run natively. For the compile, use the
   object writer in the runtime pack, or run the compile in a Linux container (Docker Desktop, Colima or
   Lima):

   ```
   docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
     dotnet publish -c Release -r linux-x64 MyGame/MyGame.csproj
   ```

   Then run the link and pack on the host with `build.ps1`.

## Confirm the setup

```
pwsh doctor.ps1
```

Expect `[ ok ]` for the .NET SDK and, once assembled, the runtime pack and SDK root. Then build the
included sample end to end:

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
