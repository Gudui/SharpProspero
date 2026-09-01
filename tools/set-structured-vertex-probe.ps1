# Offline CS vertex-body replacement, not a decoder. Full output must validate with LLVM gfx1030.
$ErrorActionPreference = 'Stop'
$shader = Join-Path $PSScriptRoot '../src/SharpProspero/Graphics/Agc/Shaders/mesh_vs.sb'
if ((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant() -ne
    'cc740ea480864fdd96925e89e4a623b1f42c1fea8f8e58a38f64f2063300a28c') {
    throw 'Expected exact CR vertex shader; refusing another input.'
}
$container = [IO.File]::ReadAllBytes($shader)
if ($container.Length -ne 992) { throw 'Unexpected CR vertex container size.' }

# Preserve the validated 68-byte NGG allocation/connectivity/vertex-lane prologue exactly.
$codeOffset = 64
$codeSize = 256
$prologueSize = 68
$bodyHex = @'
00 20 38 E0 05 08 02 80
70 3F 8C BF
F2 02 1A 7E
80 02 02 7E
80 02 0C 7E
0B 03 04 7E
F2 02 08 7E
80 02 18 7E
80 02 0E 7E
F2 02 1C 7E
80 02 06 7E
CF 08 00 F8 08 09 0A 0D
1F 02 00 F8 01 06 02 04
0F 02 00 F8 0C 07 0E 03
00 00 81 BF
'@
$body = [byte[]]@($bodyHex -split '\s+' | Where-Object { $_ } | ForEach-Object { [Convert]::ToByte($_, 16) })
if ($body.Length -ne 76) { throw 'Unexpected assembled CS body length.' }

$newCode = [byte[]]::new($codeSize)
[Array]::Copy($container, $codeOffset, $newCode, 0, $prologueSize)
[Array]::Copy($body, 0, $newCode, $prologueSize, $body.Length)
for ($at = $prologueSize + $body.Length; $at -lt $codeSize; $at += 4) {
    $newCode[$at + 0] = 0x00; $newCode[$at + 1] = 0x00
    $newCode[$at + 2] = 0x9F; $newCode[$at + 3] = 0xBF
}
[Array]::Copy($newCode, 0, $container, $codeOffset, $codeSize)
[IO.File]::WriteAllBytes($shader, $container)
Write-Output "CS_VS_GENERATED sha256=$((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant())"
