---
title: Getting started
nav_order: 2
---

# Getting started

Write a homebrew application in C#, build it into an installable package, and run it on the console.
This page gets you from nothing to a running module; [Setup](setup.md) has the full per-operating-system
install, and [Templates](templates.md) covers the other kinds of project you can start from.

## What you need

- The **.NET 10 SDK** (`dotnet --version` reports `10.x`).
- On Windows, **WSL** — the build runs the compile step there automatically. The runtime itself comes
  from the .NET SDK, so there is nothing else to set up. See [Setup](setup.md).
- This SDK checked out, with `SHARPPROSPERO_ROOT` pointing at the `SharpProspero` folder.

Check the machine is ready:

```
pwsh doctor.ps1
```

It reports the .NET SDK, the SDK root, and (on Windows) the WSL compile host, and prints what to set for
anything missing. A plain build and the tests need only .NET; building a module runs the compile step on
Linux (via WSL on Windows).

## 1. Create a project

The quickest start is the project template. Install it once, then create an application. On Windows
(PowerShell):

```powershell
dotnet new install $env:SHARPPROSPERO_ROOT/templates/prospero-app
dotnet new prospero-app -n MyGame --title "My Game" --titleId PPSA99099
```

On Linux or macOS (bash), the root is `$SHARPPROSPERO_ROOT` instead:

```bash
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-app
dotnet new prospero-app -n MyGame --title "My Game" --titleId PPSA99099
```

That writes a `MyGame` folder with `Program.cs`, the `sce_sys` package metadata, and a `build.ps1`.
Other project kinds — an interface app, a library, a toolbox — are in [Templates](templates.md).

## 2. Write the application

The template's `Program.cs` derives from `ProsperoApp` and draws each frame:

```csharp
using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Interop.Pad;

internal sealed class Game : ProsperoApp
{
    protected override void OnFrame(FrameContext context)
    {
        Surface surface = context.Surface;
        surface.Clear(Color.FromRgb(0x10, 0x14, 0x1A));
        surface.DrawTextCentered("My Game", 480, 6, Color.White);

        if (context.Input.IsPressed(ScePadButton.Options))
            context.RequestExit();
    }
}

internal static class Program
{
    private static void Main()
    {
        using var app = new Game();
        app.Run();
    }
}
```

`ProsperoApp` opens the display and controller, runs a paced loop that calls `OnFrame`, and tears
everything down on exit. From here, [Graphics and memory](graphics-and-memory.md) covers drawing and the
[Interface toolkit](ui.md) builds screens out of widgets instead of drawing by hand.

## 3. Build the package

One command compiles, links and packs:

```
pwsh MyGame/build.ps1
```

It produces an installable `*.pkg` under `MyGame/out`. To get the loose files instead of a package
(handy while iterating), pass `-Output Folder`, which leaves `eboot.bin`, `sce_sys` and any `sce_module`
libraries together in one folder.

## 4. Run it on the console

Install the `*.pkg` on a console in the appropriate mode for unsigned packages, then launch it from the
home screen. The [folder output](build-pipeline.md) is useful for inspecting the module or copying files
directly.

## Where to go next

- [Setup](setup.md) — the full install for Windows, Linux and macOS.
- [Templates](templates.md) — starting points for each kind of project.
- [Guides and tips](guides.md) — everyday recipes and troubleshooting.
- [Architecture](architecture.md) — how the layers fit together.
- [Build pipeline](build-pipeline.md) — what compile, link and pack each do.
