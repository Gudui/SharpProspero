#requires -Version 5.1
<#
.SYNOPSIS
    Checks that the machine is ready to build SharpProspero applications.
.DESCRIPTION
    Reports the .NET SDK and the runtime pack, and prints what to set for anything missing. A plain
    build and the tests need only .NET; producing a runnable module also needs the runtime pack. The
    SDK's own linker links the module and supplies its own start object and stubs, so no separate
    linker, start file, or stub library is required.
#>

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ok = $true

function Report([string]$name, [bool]$present, [string]$detail, [string]$fix, [bool]$required) {
    $mark = if ($present) { "[ ok ]" } elseif ($required) { "[fail]" } else { "[warn]" }
    Write-Host "$mark $name"
    if ($detail) { Write-Host "        $detail" }
    if (-not $present) {
        if ($fix) { Write-Host "        -> $fix" }
        if ($required) { $script:ok = $false }
    }
}

Write-Host "SharpProspero environment check"
Write-Host "-------------------------------"

# .NET SDK
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetVersion = if ($dotnet) { (& dotnet --version) } else { "" }
$dotnetOk = $dotnet -and ($dotnetVersion -like "10.*")
Report ".NET 10 SDK" $dotnetOk $dotnetVersion "Install the .NET 10 SDK from https://dotnet.microsoft.com/download" $true

# Runtime pack
$pack = $env:PROSPERO_RUNTIME_PACK
$packOk = $pack -and (Test-Path $pack) -and ((Get-ChildItem -Path $pack -Filter *.a -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
Report "Runtime pack (PROSPERO_RUNTIME_PACK)" $packOk $pack "Assemble the pack; see $here/runtime/README.md." $false

# SDK root for templates
$root = $env:SHARPPROSPERO_ROOT
$rootOk = $root -and (Test-Path (Join-Path $root "build/build-app.ps1"))
Report "SDK root (SHARPPROSPERO_ROOT)" $rootOk $root "Set SHARPPROSPERO_ROOT to $here so templates and app builds find the SDK." $false

Write-Host "-------------------------------"
if ($ok) {
    Write-Host "Ready to build and test. Producing a runnable module also needs the runtime pack above."
    exit 0
} else {
    Write-Host "A required item is missing. Fix the [fail] lines above."
    exit 1
}
