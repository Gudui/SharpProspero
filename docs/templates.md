---
title: Templates
nav_order: 11
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
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-tool
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-media
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-server
dotnet new install $SHARPPROSPERO_ROOT/templates/prospero-prx
```

## The templates

| Template | Creates | Start here for |
|---|---|---|
| `prospero-app` | A frame-loop application that draws to the screen and reads the controller. | A demo, or any application that draws its own screen. |
| `prospero-game` | A real-time game paced by the frame time: a paddle and a ball, a score, and a frame-rate overlay. | A game or anything with delta-timed movement, physics, and a live score. |
| `prospero-ui` | An application built from the interface toolkit (labels, buttons, sliders, steppers, carousels, option pickers). | A menu-driven application, a settings screen, a launcher. |
| `prospero-launcher` | An app launcher: a carousel of entries that launches the chosen title id. | A homebrew launcher or a front-end that starts other applications. |
| `prospero-tool` | A toolbox application that shows a checksum, the console name, and the network status. | A system utility or toolbox that uses the SDK's tool surfaces. |
| `prospero-media` | A media player that plays a bundled file, drawing the video and pacing the loop to the decoded audio. | A video or music player, or anything that plays a media file. |
| `prospero-server` | A network service that serves an HTTP control panel and a JSON status endpoint from the frame loop. | A control panel, a file browser, or any service reached over the network. |
| `prospero-prx` | A relocatable library module (`.prx`) that exports functions for another module to load. | A shared library you load at run time from an application. |

## Create a project

The application templates take the package identity as options:

```
dotnet new prospero-app  -n MyGame   --title "My Game"      --titleId PPSA99099
dotnet new prospero-ui   -n MyMenu    --title "My Menu"      --titleId PPSA99098
dotnet new prospero-tool -n MyToolbox --title "My Toolbox"   --titleId PPSA99097
```

| Option | Meaning | Default |
|---|---|---|
| `--title` | The display title. | My Application |
| `--titleId` | The nine-character title id. | PPSA99099 |
| `--conceptId` | The concept id. | 99099 |
| `--contentId` | The 36-character content id. | UP9000-PPSA99099_00-PROSPERO00000000 |

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

- **Applications** (`prospero-app`, `prospero-game`, `prospero-ui`, `prospero-launcher`, `prospero-tool`,
  `prospero-media`, `prospero-server`): `Program.cs`, the `sce_sys` package metadata (`param.json`, `icon0.png`), and
  `build.ps1`. Edit `Program.cs`, replace the icon, and set the title and ids in `param.json` (the
  `dotnet new` options fill these in).
- **Library** (`prospero-prx`): `Library.cs` with `[UnmanagedCallersOnly]` exported functions, the
  matching `<ProsperoExportSymbol>` entries in the project file, and `build.ps1`. Add a function and its
  export symbol for each entry point.

## Uninstall

```
dotnet new uninstall $SHARPPROSPERO_ROOT/templates/prospero-app
```
