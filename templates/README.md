# Project template

A `dotnet new` template that scaffolds a ready-to-build application.

## Install

```
dotnet new install <sdk>/templates/prospero-app
```

Replace `<sdk>` with the SharpProspero folder.

## Create an application

```
dotnet new prospero-app -n MyGame --title "My Game" --titleId PPSA99099
```

Options:

| Option | Meaning | Default |
|---|---|---|
| `--title` | Display title. | My Application |
| `--titleId` | Nine-character title id. | PPSA99099 |
| `--contentId` | 36-character content id. | UP9000-PPSA99099_00-PROSPERO00000000 |

The generated project holds `Program.cs`, `sce_sys` metadata, and a `build.ps1`.

## Build it

Point the project at the SDK once, then build:

```
setx SHARPPROSPERO_ROOT "<sdk>"
pwsh MyGame/build.ps1
```

`build.ps1` runs the SDK's shared pipeline: publish, link, and pack. Run the SDK's `doctor.ps1` first
to confirm the toolchain and paths.

## Uninstall

```
dotnet new uninstall <sdk>/templates/prospero-app
```
