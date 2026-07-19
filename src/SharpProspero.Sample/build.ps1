#requires -Version 5.1
<#
.SYNOPSIS
    Builds the sample into an ELF module (eboot.bin) and packs it into a package.
.DESCRIPTION
    Delegates to the SDK's shared build pipeline (publish, link, pack). The pipeline gathers the
    ahead-of-time runtime from the .NET SDK and links through the SDK's own linker, so no runtime pack
    or outside tool is set up; on Windows it runs the compile step through WSL. Run ../../doctor.ps1 to
    check the setup.
#>
param(
    [string]$Configuration = "Release",
    [ValidateSet("Package", "Folder")][string]$Output = "Package",
    [string]$OutputFolder = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sdkRoot = Split-Path -Parent (Split-Path -Parent $here)

& (Join-Path $sdkRoot "build/build-app.ps1") `
    -ProjectPath (Join-Path $here "SharpProspero.Sample.csproj") `
    -Output $Output `
    -OutputFolder $OutputFolder `
    -Configuration $Configuration `
    -SdkRoot $sdkRoot
