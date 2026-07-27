---
title: Templates
nav_order: 4
---

# Templates

Ready-to-build starting points, one for each kind of project. Each is a `dotnet new` template under the
SDK's `templates/` folder. Install them once, then create a project from whichever fits.

## Install

```
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-app
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-game
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-ui
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-launcher
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-filemanager
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-tool
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-media
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-server
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-input
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-scene
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-synth
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-savedata
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-dialog
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-dashboard
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-3d
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-payload
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-payload-httpd
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-payload-beacon
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-prx
```

## The templates

| Template | Creates | Start here for |
|---|---|---|
| `prospero-app` | A frame-loop application that draws to the screen and reads the controller. | A demo, or any application that draws its own screen. |
| `prospero-game` | A real-time game paced by the frame time: a paddle and a ball, a score, and a frame-rate overlay. | A game or anything with delta-timed movement, physics, and a live score. |
| `prospero-ui` | An application built from the interface toolkit (labels, buttons, sliders, steppers, carousels, option pickers). | A menu-driven application, a settings screen, a launcher. |
| `prospero-launcher` | An app launcher: a carousel of entries that launches the chosen title id. | A homebrew launcher or a front-end that starts other applications. |
| `prospero-filemanager` | A file browser that walks the file system with the controller, opening folders. | A file explorer, a save-data browser, a content picker. |
| `prospero-tool` | A toolbox application that shows a checksum, the console name, and the network status. | A system utility or toolbox that uses the SDK's tool surfaces. |
| `prospero-media` | A media player that plays a bundled file, drawing the video and pacing the loop to the decoded audio. | A video or music player, or anything that plays a media file. |
| `prospero-server` | A network service that serves an HTTP control panel and a JSON status endpoint from the frame loop. | A control panel, a file browser, or any service reached over the network. |
| `prospero-input` | An input tester that draws the live controller, keyboard, and mouse state each frame. | A hardware test, a controller-heavy application, or learning the input API. |
| `prospero-scene` | A scrolling 2D scene: a camera that follows a sprite, a tile map with collision, and a particle burst. | A 2D game or a map viewer with a moving camera. |
| `prospero-synth` | An audio synthesizer that generates and mixes tones and streams them to the output. | A soundboard, an instrument, or anything that makes sound rather than plays a file. |
| `prospero-savedata` | A save browser that mounts a save, reads a counter, increments it, and writes it back. | A save manager or a save editor. |
| `prospero-dialog` | A menu that opens the system message, on-screen keyboard, and error dialogs and pumps them to completion. | Wiring the system overlays into an application. |
| `prospero-dashboard` | A tabbed read-out of system, user, network, memory, and firmware facts, built from the interface toolkit. | A diagnostics or monitoring application. |
| `prospero-3d` | A spinning, lit cube rendered on the graphics processor with the built-in mesh shaders. | A 3D application: a model viewer, a scene, or a game with real geometry. |
| `prospero-payload` | A headless network service built as a payload: an `.elf` a loader maps and runs in a process over the network, echoing what it receives. | A payload: a network service, a bridge, or a tool loaded at run time rather than installed. |
| `prospero-payload-httpd` | A payload web service that answers requests with a status page. | A control panel or status endpoint loaded into a running process. |
| `prospero-payload-beacon` | A one-shot payload that connects out to a machine you run, sends a short report, and returns. | A one-shot action: report home, trigger something, or a bring-up check. |
| `prospero-prx` | A relocatable library module (`.prx`) that exports functions for another module to load. | A shared library you load at run time from an application. |

## Create a project

The application templates take the package identity as options:

```
dotnet new prospero-app  -n MyGame   --title "My Game"      --titleId PPSA99099
dotnet new prospero-ui   -n MyMenu    --title "My Menu"      --titleId PPSA99098
dotnet new prospero-filemanager -n MyFiles --title "My Files" --titleId PPSA99098
dotnet new prospero-tool -n MyToolbox --title "My Toolbox"   --titleId PPSA99097
```

| Option | Meaning | Default |
|---|---|---|
| `--title` | The display title. | My Application |
| `--titleId` | The nine-character title id. | PPSA99099 |
| `--conceptId` | The concept id. | 99099 |
| `--contentIdOverride` | A whole 36-character content id. Left empty, one is built from the title id. | (derived) |

The content id carries the title id: the two have to agree or the build stops, so it is derived rather
than asked for, and the override is there for the case where a particular label is wanted.

The library template takes only a name:

```
dotnet new prospero-prx -n MyLibrary
```

## Build it

Each generated project has a `build.ps1` that runs the SDK's shared pipeline. Point it at the SDK once,
then build:

```
setx SHARPPROSPERO_ROOT "<sdk>"     # or export on Linux/macOS
pwsh MyGame/build.ps1
```

An application writes an installable `*.pkg` (or a folder with `-Output Folder`); a library writes its
`<name>.prx`, which you copy into an application's `sce_module` folder and load by name (see
[Modules and libraries](modules.md)).

## What each template contains

- **Applications** (`prospero-app`, `prospero-game`, `prospero-ui`, `prospero-launcher`, `prospero-filemanager`, `prospero-tool`,
  `prospero-media`, `prospero-server`, `prospero-input`, `prospero-scene`, `prospero-synth`, `prospero-savedata`,
  `prospero-dialog`, `prospero-dashboard`): `Program.cs`, the `sce_sys` package metadata (`param.json`, `icon0.png`), and
  `build.ps1`. Edit `Program.cs`, replace the icon, and set the title and ids in `param.json` (the
  `dotnet new` options fill these in).
- **Library** (`prospero-prx`): `Library.cs` with `[UnmanagedCallersOnly]` exported functions, the
  matching `<ProsperoExportSymbol>` entries in the project file, and `build.ps1`. Add a function and its
  export symbol for each entry point.
- **Payloads** (`prospero-payload`, `prospero-payload-httpd`, `prospero-payload-beacon`): `Program.cs`
  with a plain `Main` and `build.ps1`. Each builds to a single `<name>.elf` (not a package), which a
  loader maps and runs; `build.ps1` prints the command to send it. There is no `sce_sys` metadata and no
  title or id options, because a payload is not installed. `prospero-payload` echoes on a port,
  `prospero-payload-httpd` serves a web page, and `prospero-payload-beacon` runs once and reports out.
  [Modules and payloads](modules-and-payloads.md) explains when a payload is the right form.

## Uninstall

Each install line put one template package on the machine, so removing them means naming each path.
`dotnet new uninstall` takes as many as you pass:

```
dotnet new uninstall $SHARPPROSPERO_ROOT/templates/prospero-app $SHARPPROSPERO_ROOT/templates/prospero-game
```

Run `dotnet new uninstall` with no arguments to list the installed packages, each with the command that
removes it.
