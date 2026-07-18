#requires -Version 5.1
<#
.SYNOPSIS
    Builds a SharpProspero application and writes either an installable package or a plain folder.

.DESCRIPTION
    Compile the C# to an object, link it into the module, gather the module and its metadata, then
    either pack the result or leave the files as they are.

.PARAMETER ProjectPath
    The application project to build.

.PARAMETER Output
    Package writes an installable *.pkg (the default). Folder leaves every file in one folder, ready
    to copy to a console or inspect.

.PARAMETER OutputFolder
    Where the result is written. Defaults to an 'out' folder next to the project.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER SystemVersionPolicy
    How the system version the application requires is settled against the modules it ships.
    Match requires what the modules need and never lowers it (the default). Upgrade raises it to
    -SystemVersion. Downgrade lowers it to -SystemVersion and reports what stops loading. Keep
    leaves it alone.

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
    [string]$SdkRoot = ""
)

$ErrorActionPreference = "Stop"
$rid = "linux-x64"

if (-not $SdkRoot) { $SdkRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
if (-not (Test-Path (Join-Path $SdkRoot "build/Prospero.App.targets")) -and $env:SHARPPROSPERO_ROOT) {
    $SdkRoot = $env:SHARPPROSPERO_ROOT
}

$projectDir = Split-Path -Parent (Resolve-Path $ProjectPath)
if (-not $OutputFolder) { $OutputFolder = Join-Path $projectDir "out" }
$moduleFolder = Join-Path $OutputFolder "module"

$objectGuidance = @"
No compiled object was produced. The stock compiler emits an object only for the operating system it
runs on, so run this step on Linux (or WSL), or supply the object writer for the device through
PROSPERO_RUNTIME_PACK. See $SdkRoot/docs/build-pipeline.md.
"@

# 1. Compile the C# to an object for the device instruction set.
Write-Host "== Compile =="
& dotnet publish $ProjectPath -c $Configuration -r $rid --nologo
if ($LASTEXITCODE -ne 0) { throw $objectGuidance }

# Name the object the project actually produces. Picking a file out of the folder instead would link
# whichever object happened to be there, including one left behind by an earlier assembly name.
$targetName = (& dotnet msbuild $ProjectPath -getProperty:TargetName `
    -p:Configuration=$Configuration -p:RuntimeIdentifier=$rid --nologo | Out-String).Trim()
if (-not $targetName) { throw "Could not resolve the project's target name." }
$objectPath = Join-Path $projectDir "obj/$Configuration/net10.0/$rid/native/$targetName.o"
if (-not (Test-Path $objectPath)) { throw $objectGuidance }

# 2. Link the object into the module.
Write-Host "== Link =="
New-Item -ItemType Directory -Force -Path $moduleFolder | Out-Null
& dotnet msbuild $ProjectPath /t:ProsperoLink `
    /p:Configuration=$Configuration `
    /p:ProsperoObjectFile=$objectPath `
    /p:OutputPath=$moduleFolder/ `
    /nologo
if ($LASTEXITCODE -ne 0) { throw "Link failed." }

# 3. Gather the module's metadata and any modules it ships with. The destination is replaced rather
# than copied into, so a rebuild neither nests the folder inside itself nor keeps a deleted file.
foreach ($folder in @("sce_sys", "sce_module")) {
    $source = Join-Path $projectDir $folder
    if (-not (Test-Path $source)) { continue }
    $destination = Join-Path $moduleFolder $folder
    if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
    Copy-Item -Recurse -Force $source $destination
}

# 4. Settle the system version the application requires. A module records the system it was built
# against, and an application that ships it has to require at least as much, or the system installs
# the application and then fails to load the module.
Write-Host "== System version =="
$sysverArgs = @("sysver", "--folder", $moduleFolder, "--policy", $SystemVersionPolicy.ToLowerInvariant(), "--apply")
if ($SystemVersion) { $sysverArgs += @("--version", $SystemVersion) }
& dotnet run --project (Join-Path $SdkRoot "tools/SharpProspero.Bindings.Generator/SharpProspero.Bindings.Generator.csproj") `
    -c $Configuration -- @sysverArgs
# 4 reports a module that will not load under the result. That is a warning the user asked for by
# choosing the policy, not a build failure; anything else is.
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 4) { throw "Could not settle the system version." }

# 5. Either pack the result or leave the folder as the output.
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
