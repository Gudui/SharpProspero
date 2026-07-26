#requires -Version 5.1
<#
.SYNOPSIS
    Builds a SharpProspero application and writes either an installable package or a plain folder.

.DESCRIPTION
    Compile the C# to an object, link it into the module with the ahead-of-time runtime, gather the
    module and its metadata, then either pack the result or leave the files as they are. Everything the
    link needs is produced by the toolchain and the .NET SDK; no runtime pack or outside tool is set up.

    The compile step (`dotnet publish -r linux-x64`) has to run on a Linux host, because that is where
    the ahead-of-time compiler emits an object for the device instruction set. On Windows this script
    runs that one step through WSL automatically, so a Windows user builds without switching hosts; the
    rest of the build is the toolchain itself and runs wherever the script is started.

.PARAMETER ProjectPath
    The application project to build.

.PARAMETER Output
    Package writes an installable *.pkg (the default). Folder leaves every file in one folder.

.PARAMETER OutputFolder
    Where the result is written. Defaults to an 'out' folder next to the project.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER SystemVersionPolicy
    How the system version the application requires is settled against the modules it ships.

.PARAMETER SystemVersion
    The version Upgrade and Downgrade move to, as NN.NN (for example 11.20).

.PARAMETER SdkRoot
    The SharpProspero SDK folder. Defaults to the folder above this script, then SHARPPROSPERO_ROOT.
#>
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet("Package", "Folder")][string]$Output = "Package",
    [string]$OutputFolder = "",
    [string]$Configuration = "Release",
    [ValidateSet("Match", "Upgrade", "Downgrade", "Keep")][string]$SystemVersionPolicy = "Match",
    [string]$SystemVersion = "",
    [string]$SdkRoot = "",
    [switch]$Payload
)

$ErrorActionPreference = "Stop"
$rid = "linux-x64"

if (-not $SdkRoot) { $SdkRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
if (-not (Test-Path (Join-Path $SdkRoot "build/Prospero.App.targets")) -and $env:SHARPPROSPERO_ROOT) {
    $SdkRoot = $env:SHARPPROSPERO_ROOT
}
# A project created from a template points its imports and its reference to the SDK through the
# SharpProsperoRoot property (defaulting to the SHARPPROSPERO_ROOT environment variable). The in-tree
# sample uses relative imports and does not need it, but a template project does, so the resolved SDK
# folder is passed to every compile and evaluate step below. Resolve it to a full path first.
$SdkRoot = (Resolve-Path $SdkRoot).Path

$projectDir = Split-Path -Parent (Resolve-Path $ProjectPath)
if (-not $OutputFolder) { $OutputFolder = Join-Path $projectDir "out" }
$moduleFolder = Join-Path $OutputFolder "module"

$onWindows = [System.Environment]::OSVersion.Platform -eq "Win32NT"
$haveWsl = $onWindows -and (Get-Command wsl.exe -ErrorAction SilentlyContinue)

# A Windows path as WSL sees it: C:\a\b -> /mnt/c/a/b.
function ConvertTo-WslPath([string]$p) {
    $full = [System.IO.Path]::GetFullPath($p)
    $drive = $full.Substring(0, 1).ToLowerInvariant()
    return "/mnt/$drive" + ($full.Substring(2) -replace '\\', '/')
}

# 1. Compile the C# to an object for the device instruction set. On Windows the ahead-of-time compiler
# cannot cross-compile, so the publish runs in WSL; the object still lands in the project's obj folder,
# which both sides share.
#
# The publish's own last step tries to link the object into a host executable and fails, because the
# device service functions the module imports (scePad*, sceVideoOut*, ...) are not present on the build
# host — the SDK's linker resolves those against the device modules. The object the compiler writes just
# before that link is exactly what is needed, so a non-zero publish result is expected; the object's
# presence is the real check.
Write-Host "== Compile =="
# Name the object the project produces, and clear any object a previous build left behind. The publish
# below ends in a host link that is expected to fail (the device modules are absent on the build host),
# so its exit code cannot signal success; the freshly written object is the real check. Removing the
# stale object first means a compile that fails before writing it (a source error) is caught here rather
# than silently linking the old one.
$targetName = (& dotnet msbuild $ProjectPath -getProperty:TargetName `
    -p:Configuration=$Configuration -p:RuntimeIdentifier=$rid "-p:SharpProsperoRoot=$SdkRoot" --nologo | Out-String).Trim()
if (-not $targetName) { throw "Could not resolve the project's target name." }
$objectPath = Join-Path $projectDir "obj/$Configuration/net10.0/$rid/native/$targetName.o"
if (Test-Path $objectPath) { Remove-Item $objectPath -Force }

# The publish always ends in that failed host link, so its output would report an error on every
# successful build. Keep it and show it only when the object is missing, which is the real failure.
if ($onWindows) {
    if (-not $haveWsl) {
        throw "The compile step needs a Linux host. Install WSL (wsl --install) so this runs automatically, or build on Linux."
    }
    $wslProject = ConvertTo-WslPath $ProjectPath
    $wslSdk = ConvertTo-WslPath $SdkRoot
    $publishLog = (& wsl.exe -e bash -lc "dotnet publish '$wslProject' -c $Configuration -r $rid -p:SharpProsperoRoot='$wslSdk' --nologo" 2>&1 | Out-String)
} else {
    $publishLog = (& dotnet publish $ProjectPath -c $Configuration -r $rid "-p:SharpProsperoRoot=$SdkRoot" --nologo 2>&1 | Out-String)
}

if (-not (Test-Path $objectPath)) {
    Write-Host $publishLog
    throw "No compiled object was produced at $objectPath. The ahead-of-time compile did not run to completion; the publish output above shows why."
}
Write-Host "  Compiled $targetName.o for $rid."

# 2. Gather the ahead-of-time runtime archives. They are restored by the publish above (the .NET SDK's
# NativeAOT runtime pack), so they are found in the package cache rather than assembled by hand. On
# Windows the cache lives in WSL, so the archives are copied out to the project's obj folder.
Write-Host "== Runtime support =="
$archiveNames = @(
    "libbootstrapper.o", "libRuntime.WorkstationGC.a", "libRuntime.VxsortDisabled.a",
    "libeventpipe-disabled.a", "libstandalonegc-disabled.a", "libaotminipal.a", "libstdc++compat.a",
    "libSystem.Native.a", "libz.a", "libbrotlienc.a", "libbrotlidec.a", "libbrotlicommon.a",
    "libSystem.IO.Compression.Native.a"
)
$supportDir = Join-Path $projectDir "obj/$Configuration/net10.0/$rid/runtime-support"
New-Item -ItemType Directory -Force -Path $supportDir | Out-Null

$packGlob = "microsoft.netcore.app.runtime.nativeaot.linux-x64/*/runtimes/linux-x64/native"
if ($onWindows) {
    $wslSupport = ConvertTo-WslPath $supportDir
    $copyScript = "set -e; NAT=`$(ls -d ~/.nuget/packages/$packGlob 2>/dev/null | sort -V | tail -1); " +
        "if [ -z `"`$NAT`" ]; then echo NONE; exit 0; fi; " +
        "for n in $($archiveNames -join ' '); do cp `"`$NAT/`$n`" '$wslSupport/' 2>/dev/null || true; done; echo `"`$NAT`""
    $nativeDir = (& wsl.exe -e bash -lc $copyScript | Out-String).Trim()
    if ($nativeDir -eq "NONE" -or -not $nativeDir) {
        throw "The NativeAOT runtime pack was not found in the WSL package cache. Run the compile step once so it is restored."
    }
} else {
    $userHome = [System.Environment]::GetFolderPath("UserProfile")
    $nativeDir = Get-ChildItem -Path (Join-Path $userHome ".nuget/packages/microsoft.netcore.app.runtime.nativeaot.linux-x64") `
        -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
    if (-not $nativeDir) { throw "The NativeAOT runtime pack was not found in the package cache." }
    $nativeDir = Join-Path $nativeDir.FullName "runtimes/linux-x64/native"
    foreach ($n in $archiveNames) {
        $src = Join-Path $nativeDir $n
        if (Test-Path $src) { Copy-Item -Force $src (Join-Path $supportDir $n) }
    }
}
if (-not (Get-ChildItem -Path $supportDir -File -ErrorAction SilentlyContinue)) {
    throw "No runtime archives were gathered into $supportDir."
}

# A payload is a position-independent executable a loader maps and runs in an existing process. It links
# to a single .elf with the payload output kind, resolves its outside references at run time, and is not
# packaged, so the module link, the metadata, and the packaging steps below are skipped.
if ($Payload) {
    Write-Host "== Link (payload) =="
    $name = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    $elf = Join-Path $OutputFolder "$name.elf"
    $linkArgs = @("link", "--kind", "payload", "--self-contained", "--obj", $objectPath)
    foreach ($o in Get-ChildItem -Path $supportDir -File -Filter *.o) { $linkArgs += @("--obj", $o.FullName) }
    foreach ($a in Get-ChildItem -Path $supportDir -File -Filter *.a) { $linkArgs += @("--lib", $a.FullName) }
    $linkArgs += @("--out", $elf)
    & dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
        -c $Configuration -- @linkArgs
    if ($LASTEXITCODE -ne 0) { throw "Payload link failed." }
    Write-Host "== Done =="
    Write-Host "Payload written to $elf"
    Write-Host "Send it to a listening loader with:"
    Write-Host "  dotnet run --project `"$SdkRoot/tools/SharpProspero.Bindings.Generator`" -- payload --send --host <address> --file `"$elf`""
    return
}

# 3. Link the object and the runtime archives into the module. The gathered archives live in one folder,
# so the folder is handed over and the link target picks up every archive in it — no list to pass.
Write-Host "== Link =="
New-Item -ItemType Directory -Force -Path $moduleFolder | Out-Null
& dotnet msbuild $ProjectPath /t:ProsperoLink `
    /p:Configuration=$Configuration `
    /p:ProsperoObjectFile=$objectPath `
    /p:ProsperoRuntimePack=$supportDir `
    /p:OutputPath=$moduleFolder/ `
    "/p:SharpProsperoRoot=$SdkRoot" `
    /nologo
if ($LASTEXITCODE -ne 0) { throw "Link failed." }

# 4. Gather the module's metadata and any modules it ships with.
foreach ($folder in @("sce_sys", "sce_module")) {
    $source = Join-Path $projectDir $folder
    if (-not (Test-Path $source)) { continue }
    $destination = Join-Path $moduleFolder $folder
    if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
    Copy-Item -Recurse -Force $source $destination
}

# 4b. Some of the modules an application imports from are not published by the system: the application
# carries its own copy, and one that names such a module without shipping it installs cleanly and then
# hangs the console at launch, with nothing written to the log. The step below gathers what is missing
# from a module folder and stops the build when it cannot, so that package is never produced.
Write-Host "== Modules =="
$sceModuleFolder = Join-Path $moduleFolder "sce_module"
$moduleSource = $env:PROSPERO_MODULES
if (-not $moduleSource) {
    $property = (& dotnet msbuild $ProjectPath -getProperty:ProsperoModuleFolder `
        -p:Configuration=$Configuration "-p:SharpProsperoRoot=$SdkRoot" --nologo | Out-String).Trim()
    if ($property) { $moduleSource = $property }
}
# The modules an application carries are supplied with the SDK, which is where they are taken from
# unless a folder is named. A copy lifted out of an installed application is the wrong thing to ship:
# it was built for whichever system that application shipped against, and the loader binds against it
# before any of the application's own code runs, so a mismatch hangs the console with nothing logged.
if (-not $moduleSource -and $env:SCE_PROSPERO_SDK_DIR) {
    $fromSdk = Join-Path $env:SCE_PROSPERO_SDK_DIR "target/sce_module"
    if (Test-Path $fromSdk) { $moduleSource = $fromSdk }
}
$linkedModule = @(Get-ChildItem -Path $moduleFolder -File |
    Where-Object { $_.Extension -in @(".bin", ".prx", ".elf", ".sprx", ".self") }) | Select-Object -First 1
if ($linkedModule) {
    $modulesArgs = @("modules", "--module", $linkedModule.FullName, "--folder", $sceModuleFolder)
    if ($moduleSource -and (Test-Path $moduleSource)) { $modulesArgs += @("--source", $moduleSource) }
    & dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
        -c $Configuration -- @modulesArgs
    if ($LASTEXITCODE -ne 0) { throw "The application is missing a module it has to carry. See the message above." }
}

# 4c. Check the metadata that describes the application to the system. A field carrying a value the
# system does not recognise stops the build; a field a finished title always carries but this one does
# not is filled in. Neither shows up as an error on the console - the home screen simply draws the
# title wrongly, or a service it expected to reach is never offered to it.
if (Test-Path (Join-Path $moduleFolder "sce_sys/param.json")) {
    Write-Host "== Metadata =="
    & dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
        -c $Configuration -- param --folder $moduleFolder --apply
    if ($LASTEXITCODE -ne 0) { throw "The application metadata describes the title wrongly. See the message above." }
}

# 5. Settle the system version the application requires. This lives in the application metadata; a
# library module has none (the application that bundles it settles its own version), so the step runs
# only when that metadata is present.
if (Test-Path (Join-Path $moduleFolder "sce_sys/param.json")) {
    Write-Host "== System version =="
    $sysverArgs = @("sysver", "--folder", $moduleFolder, "--policy", $SystemVersionPolicy.ToLowerInvariant(), "--apply")
    if ($SystemVersion) { $sysverArgs += @("--version", $SystemVersion) }
    & dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
        -c $Configuration -- @sysverArgs
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 4) { throw "Could not settle the system version." }
}

# 6. Wrap every module in the container the loader expects. A module left as a plain ELF is turned away
# before any of its code runs, so this is what makes the result launchable. The modules the application
# carries are wrapped alongside the one built here: a copy taken from a module folder is an unwrapped
# ELF and is turned away exactly as an unwrapped eboot.bin is. The step leaves an already-wrapped file
# untouched, so re-running a build is safe and a project that ships wrapped modules is handled too.
$builtModules = @(Get-ChildItem -Path $moduleFolder -File) +
    @(Get-ChildItem -Path (Join-Path $moduleFolder "sce_module") -File -ErrorAction SilentlyContinue)
$builtModules = @($builtModules | Where-Object { $_.Extension -in @(".bin", ".prx", ".elf", ".sprx", ".self") })
if ($builtModules.Count -gt 0) {
    Write-Host "== Sign =="
    foreach ($builtModule in $builtModules) {
        & dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
            -c $Configuration -- self --sign --in $builtModule.FullName --out $builtModule.FullName
        if ($LASTEXITCODE -ne 0) { throw "Could not wrap $($builtModule.Name) for the loader." }
    }
}

# 7. Either pack the result or leave the folder as the output.
if ($Output -eq "Folder") {
    Write-Host "== Done =="
    Write-Host "Files written to $moduleFolder"
    return
}

Write-Host "== Package =="
& dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Packager/SharpProspero.Packager.csproj") `
    -c $Configuration -- --in $moduleFolder --out $OutputFolder
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

Write-Host "Package written to $OutputFolder"
