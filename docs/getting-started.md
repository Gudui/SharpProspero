---
title: Getting started
nav_order: 2
---

# Getting started

This walks from an empty project to an installable package.

## 1. Prerequisites

- .NET 10 SDK (`dotnet --version` reports 10.x).
- The runtime pack, pointed at by the environment:

```
setx PROSPERO_RUNTIME_PACK "<folder with the runtime archives>"
```

.NET 10 alone is enough to build and test the SDK; the runtime pack is needed to link a module, which
the SDK's own linker does — it supplies its own start object and stubs, so nothing else is required.
See [build-pipeline.md](build-pipeline.md).

## 2. Try the SDK

```
dotnet build SharpProspero.slnx
dotnet test tests/SharpProspero.Tests/SharpProspero.Tests.csproj
```

## 3. Create an application project

An application is a small executable project that references the SDK and imports the build files.

`MyApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="path\to\build\Prospero.App.props" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <ProsperoModuleName>eboot.bin</ProsperoModuleName>
    <ProsperoHeapHardLimitBytes>134217728</ProsperoHeapHardLimitBytes>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="path\to\src\SharpProspero\SharpProspero.csproj" />
  </ItemGroup>
  <Import Project="path\to\build\Prospero.App.targets" />
</Project>
```

`Program.cs`:

```csharp
using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Interop.Pad;

internal sealed class MyApp : ProsperoApp
{
    protected override void OnFrame(FrameContext context)
    {
        Surface surface = context.Surface;
        surface.Clear(Color.FromRgb(0x10, 0x14, 0x1A));
        surface.DrawTextCentered("My first C# module", 480, 5, Color.White);

        // Leave on the frame Options is pressed.
        if (context.Pressed(ScePadButton.Options))
            context.RequestExit();
    }
}

internal static class Program
{
    private static void Main()
    {
        using var app = new MyApp();
        app.Run();
    }
}
```

## 4. Add package metadata

Create `sce_sys/param.json` with the content id, title and version, and `sce_sys/icon0.png`. Copy
the sample's `sce_sys` as a starting point and change the title and content id.

## 5. Build the package

Model the build on `src/SharpProspero.Sample/build.ps1`:

1. `dotnet publish -c Release -r linux-x64` to compile the object.
2. `dotnet msbuild MyApp.csproj /t:ProsperoLink /p:ProsperoObjectFile=<object> /p:OutputPath=<module>/`
   to link `eboot.bin`.
3. Copy `sce_sys` next to `eboot.bin`.
4. `dotnet run --project tools/SharpProspero.Packager -- --in <module> --out <out>`.

The finished `*.pkg` installs on a debug-mode console.

## Where to go next

- [architecture.md](architecture.md) for how the layers fit together.
- [bindings.md](bindings.md) to add services beyond the built-in set.
- [graphics-and-memory.md](graphics-and-memory.md) for the drawing surface and memory model.
