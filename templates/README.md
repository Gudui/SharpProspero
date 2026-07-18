# Project templates

`dotnet new` templates that scaffold a ready-to-build project, one for each kind. Full documentation is
in [docs/templates.md](../docs/templates.md).

| Template | Creates |
|---|---|
| `prospero-app` | A frame-loop application that draws to the screen and reads the controller. |
| `prospero-ui` | An application built from the interface toolkit (labels, buttons, sliders, pickers). |
| `prospero-tool` | A toolbox application that shows a checksum, the console name and the network status. |
| `prospero-prx` | A relocatable library module (`.prx`) that exports functions for another module. |

## Install

```
dotnet new install <sdk>/templates/prospero-app
dotnet new install <sdk>/templates/prospero-ui
dotnet new install <sdk>/templates/prospero-tool
dotnet new install <sdk>/templates/prospero-prx
```

Replace `<sdk>` with the SharpProspero folder.

## Create a project

```
dotnet new prospero-app  -n MyGame    --title "My Game"    --titleId PPSA99099
dotnet new prospero-ui   -n MyMenu     --title "My Menu"    --titleId PPSA99098
dotnet new prospero-tool -n MyToolbox  --title "My Toolbox" --titleId PPSA99097
dotnet new prospero-prx  -n MyLibrary
```

Options for the application templates:

| Option | Meaning | Default |
|---|---|---|
| `--title` | Display title. | My Application |
| `--titleId` | Nine-character title id. | PPSA99099 |
| `--conceptId` | Concept id. | 99099 |
| `--contentId` | 36-character content id. | UP9000-PPSA99099_00-PROSPERO00000000 |

## Build it

Point the project at the SDK once, then build:

```
setx SHARPPROSPERO_ROOT "<sdk>"
pwsh MyGame/build.ps1
```

`build.ps1` runs the SDK's shared pipeline: publish, link, and pack (an application) or leave the built
`.prx` in a folder (a library). Run the SDK's `doctor.ps1` first to confirm the toolchain and paths.

## Uninstall

```
dotnet new uninstall <sdk>/templates/prospero-app
```
