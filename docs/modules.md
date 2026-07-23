---
title: Modules and libraries
nav_order: 16
---

# Modules and libraries

An application interacts with a library you supply as a `.prx`. You need only the module itself: the
SDK reads its exports, generates a wrapper for it, loads it at run time, and packs it with the
application. No other library or development kit is involved.

A library also comes signed, as a `.sprx`. The reader and the inspector take either form: a signed
module is unwrapped to its ELF first, so a `.sprx` reads the same way as a `.prx`. See
[Signed and unsigned](signed-and-unsigned.md) for what the forms are and how to convert them.

## Load a module at run time

Drop the `.prx` in the package's `sce_module` folder and load it by name. Resolve the exports you
call, then dispose the handle to unload.

```csharp
using SharpProspero.Modules;

using PrxModule lib = PrxModule.LoadFromPackage("mylib.prx");

// Resolve an export to an unmanaged function pointer and call it.
var doThing = (delegate* unmanaged<int, int>)lib.GetFunctionPointer("sceMyLibDoThing");
int result = doThing(42);
```

`LoadFromPackage("mylib.prx")` loads `/app0/sce_module/mylib.prx`. `GetExport` returns an address, or
`IntPtr.Zero` when the symbol is absent; `GetFunctionPointer` throws when it is missing.

## Inspect a module

List the exports of a module with the generator's `prx` command:

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- prx --module mylib.prx --inspect
```

Each line reports the export's identifier and its library and module numbers. The identifier is
derived from the plain name, so a name you know maps to an export you can confirm.

For the module's structure, the `elf` command prints the header, the program headers, the modules it
depends on, and its export count; add `--exports` to list every export:

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- elf --file mylib.prx --exports
```

This reads the module directly and needs no external inspector.

## Generate a wrapper for a module

Write the exports you use, one per line, with an optional call signature, then generate a wrapper:

`names.txt`:

```
# name = returnType(parameterTypes)

sceMyLibDoThing = int(int)
sceMyLibReset = void()
sceMyLibTable
```

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- prx \
  --module mylib.prx --names names.txt --class MyLib --namespace My.App --out MyLib.g.cs
```

The generated `MyLib` loads the module and exposes each named export: a signed entry becomes a
callable function pointer, an unsigned one exposes its address. The generator verifies each name is
present in the module and warns about any that are not.

```csharp
using My.App;

using MyLib lib = MyLib.Load();
int result = lib.sceMyLibDoThing(42);
```

## Build your own library as a PRX

An application project builds an `eboot.bin`. To build a library module instead, set the module kind:

```xml
<PropertyGroup>
  <ProsperoModuleKind>Prx</ProsperoModuleKind>
</PropertyGroup>
```

The link step then produces `<name>.prx` as a shared module rather than an executable. Place the
result in another application's `sce_module` folder and load it as above.

For the module to expose functions, name the symbols it exports through `ProsperoExportSymbol`. These
are the unmanaged entry points (methods marked `[UnmanagedCallersOnly]`) another module imports:

```xml
<ItemGroup>
  <ProsperoExportSymbol Include="myLibDoThing" />
  <ProsperoExportSymbol Include="myLibReset" />
</ItemGroup>
```

The linker records each as an export, so a consumer resolves it by name. Confirm the exports on the
built module with `elf --file <name>.prx --exports`.

## Link against a module at build time

Run-time loading needs only the `.prx`. When you would rather bind a library at link time, generate
a stub for it and add it to the link. Point the stub at the module itself so it matches what the
module publishes:

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- \
  stub --module mylib.prx --names names.txt --out libs/libMyLib_stub.a
```

```xml
<ItemGroup>
  <ProsperoUserStubLibrary Include="libs\libMyLib_stub.a" />
</ItemGroup>
```

The stub is a small object that carries the plain names and their identifiers, so the linker resolves
calls to the module that provides them.

### Versions have to match

A module records the module and library **version** of everything it imports, and the loader binds an
import only when that version matches the one the providing module publishes. Get it wrong and the
symbol does not bind.

`--module` reads the library name and its version out of the module, so the stub and the imports the
linker writes agree with it by construction. `--lib` instead assumes the usual versions (module 1.1,
library 1), which is right for most libraries but is an assumption; use `--module` when you have the
file. Either way, `--module-version` and `--library-version` set them explicitly (hexadecimal, e.g.
`--library-version 0003`).

This only applies to linking against a library. Loading a `.prx` at run time resolves each export by
its identifier and records no versions, so it binds whatever the library declares.

## The system version a module needs

A module records the system it was built against. An application that ships the module has to require
at least that much, or the system installs the application and then fails to load the module: the
package looks fine and the application breaks at run time.

The build settles this for you. After it gathers `sce_module`, it reads what every module needs, and
raises the application's requirement to match the highest. Ship a library built against 11.20 in an
application that asked for 02.00 and the requirement becomes 11.20 on its own.

Read what a module needs at any time:

```
dotnet run --project tools/SharpProspero.Bindings.Generator -- sysver --folder out/module
```

```
  libSceMyLib.prx                  11.20
  eboot.bin                        02.00

Current:  02.00
Modules:  11.20
Result:   11.20  0x1120000000000000
  Raised to 11.20 for libSceMyLib.prx.
```

Without `--apply` it only reports. With `--apply` it writes the result to `sce_sys/param.json`.

### Choosing the version yourself

`-SystemVersionPolicy` on `build-app.ps1`, or `--policy` on the command, picks how it is settled:

| Policy | What it does |
|---|---|
| `match` | Require what the modules need. Never lowers. The default. |
| `upgrade` | Raise the requirement to `--version`. Refuses a version below the current one. |
| `downgrade` | Lower the requirement to `--version`, and name every module that stops loading. |
| `keep` | Leave the requirement alone, and still name a module that needs more. |

Require a newer system than any module asks for, for example because your own code calls something
newer:

```powershell
.\build\build-app.ps1 -ProjectPath .\MyApp.csproj -SystemVersionPolicy Upgrade -SystemVersion 11.20
```

Target an older system than a module was built against:

```powershell
.\build\build-app.ps1 -ProjectPath .\MyApp.csproj -SystemVersionPolicy Downgrade -SystemVersion 02.00
```

Lowering the requirement does not change what a module needs. Anything built against something newer
is named:

```
  libSceMyLib.prx needs 11.20 and will not load under 02.00.
```

The build continues, because you asked for it, but that module will not load on a 02.00 system.
Whenever a module is left in that state the command reports it, whichever policy put it there.

A file in `sce_module` that cannot be read is reported rather than passed over, because its
requirement is unknown rather than absent:

```
  vendor.prx                       unreadable
```

## Packaging

Any `.prx` under the application's `sce_module` folder is packed with the application. The packager
copies the folder into the build alongside `eboot.bin` and `sce_sys`.
