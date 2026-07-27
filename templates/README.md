# Project templates

`dotnet new` templates that scaffold a ready-to-build project, one for each kind. Full documentation is
in [docs/templates.md](../docs/templates.md).

| Template | Creates |
|---|---|
| `prospero-app` | A frame-loop application that draws to the screen and reads the controller. |
| `prospero-game` | A real-time game paced by the frame time: a paddle and a ball, a score, and a frame-rate overlay. |
| `prospero-ui` | An application built from the interface toolkit (labels, buttons, sliders, pickers). |
| `prospero-launcher` | A carousel that starts the chosen title, for a homebrew launcher front end. |
| `prospero-filemanager` | A file browser: walks the file system with the controller, opening folders. |
| `prospero-tool` | A toolbox application that shows a checksum, the console name and the network status. |
| `prospero-media` | A media player that plays a bundled file, drawing the video and pacing to the audio. |
| `prospero-server` | A network service that serves an HTTP control panel and a JSON status endpoint. |
| `prospero-input` | An input tester that draws the live controller, keyboard and mouse state each frame. |
| `prospero-scene` | A scrolling 2D scene: a camera that follows a sprite, a tile map with collision, and a particle burst. |
| `prospero-synth` | An audio synthesizer that generates and mixes tones and streams them to the output. |
| `prospero-savedata` | A save browser that mounts a save, reads a counter, increments it, and writes it back. |
| `prospero-dialog` | A menu that opens the system message, on-screen keyboard and error dialogs and pumps them to completion. |
| `prospero-dashboard` | A tabbed read-out of system, user, network, memory and firmware facts. |
| `prospero-3d` | A spinning, lit cube rendered on the graphics processor with the built-in mesh shaders. |
| `prospero-payload` | A payload (`.elf` a loader maps into a running process): a headless service that echoes on a port. |
| `prospero-payload-httpd` | A payload web service that answers requests with a status page. |
| `prospero-payload-beacon` | A one-shot payload that connects out, sends a short report, and returns. |
| `prospero-prx` | A relocatable library module (`.prx`) that exports functions for another module. |

## Install

```
dotnet new install <sdk>/templates/prospero-app
dotnet new install <sdk>/templates/prospero-game
dotnet new install <sdk>/templates/prospero-ui
dotnet new install <sdk>/templates/prospero-launcher
dotnet new install <sdk>/templates/prospero-filemanager
dotnet new install <sdk>/templates/prospero-tool
dotnet new install <sdk>/templates/prospero-media
dotnet new install <sdk>/templates/prospero-server
dotnet new install <sdk>/templates/prospero-input
dotnet new install <sdk>/templates/prospero-scene
dotnet new install <sdk>/templates/prospero-synth
dotnet new install <sdk>/templates/prospero-savedata
dotnet new install <sdk>/templates/prospero-dialog
dotnet new install <sdk>/templates/prospero-dashboard
dotnet new install <sdk>/templates/prospero-3d
dotnet new install <sdk>/templates/prospero-payload
dotnet new install <sdk>/templates/prospero-payload-httpd
dotnet new install <sdk>/templates/prospero-payload-beacon
dotnet new install <sdk>/templates/prospero-prx
```

Replace `<sdk>` with the SharpProspero folder.

## Create a project

```
dotnet new prospero-app    -n MyApp      --title "My App"     --titleId PPSA99099
dotnet new prospero-game   -n MyGame     --title "My Game"    --titleId PPSA99098
dotnet new prospero-ui     -n MyMenu     --title "My Menu"    --titleId PPSA99097
dotnet new prospero-launcher -n MyLauncher --title "My Launcher" --titleId PPSA99093
dotnet new prospero-filemanager -n MyFiles --title "My Files"  --titleId PPSA99092
dotnet new prospero-tool   -n MyToolbox  --title "My Toolbox" --titleId PPSA99096
dotnet new prospero-media  -n MyPlayer   --title "My Player"  --titleId PPSA99095
dotnet new prospero-server -n MyPanel    --title "My Panel"   --titleId PPSA99094
dotnet new prospero-input  -n MyInputTest --title "My Input Test" --titleId PPSA99091
dotnet new prospero-scene  -n MyWorld    --title "My World"   --titleId PPSA99090
dotnet new prospero-synth  -n MySynth    --title "My Synth"   --titleId PPSA99089
dotnet new prospero-savedata -n MySaves  --title "My Saves"   --titleId PPSA99088
dotnet new prospero-dialog -n MyDialogs  --title "My Dialogs" --titleId PPSA99087
dotnet new prospero-dashboard -n MyStatus --title "My Status" --titleId PPSA99086
dotnet new prospero-3d     -n MyCube     --title "My Cube"    --titleId PPSA99085
dotnet new prospero-payload -n MyPayload
dotnet new prospero-payload-httpd -n MyPanelPayload
dotnet new prospero-payload-beacon -n MyBeacon
dotnet new prospero-prx    -n MyLibrary
```

The payload and library templates take no title or id options.

Options for the application templates:

| Option | Meaning | Default |
|---|---|---|
| `--title` | Display title. | My Application |
| `--titleId` | Nine-character title id. | PPSA99099 |
| `--conceptId` | Concept id. | 99099 |
| `--contentIdOverride` | A whole 36-character content id. Left empty, one is built from the title id. | (derived) |

## Build it

Point the project at the SDK once, then build:

```
setx SHARPPROSPERO_ROOT "<sdk>"
pwsh MyGame/build.ps1
```

`build.ps1` runs the SDK's shared pipeline: publish, link, and pack (an application) or leave the built
`.prx` in a folder (a library). Run the SDK's `doctor.ps1` first to confirm the toolchain and paths.

## Uninstall

Each install line put one template package on the machine, so removing them means naming each path.
`dotnet new uninstall` takes as many as you pass:

```
dotnet new uninstall <sdk>/templates/prospero-app <sdk>/templates/prospero-game
```

Run `dotnet new uninstall` with no arguments to list the installed packages, each with the command that
removes it.
