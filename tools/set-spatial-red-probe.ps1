# Offline CR selector patch, not a decoder. Full output must validate with LLVM gfx1030.
$ErrorActionPreference = 'Stop'
$shader = Join-Path $PSScriptRoot '../src/SharpProspero/Graphics/Agc/Shaders/mesh_ps.sb'
if ((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant() -ne
    '0fd6b4242b57bdfc07656294a3d9eb10474d55544b1fb4a3fa421bdcea158327') {
    throw 'Expected exact CQ shader; refusing another input.'
}
$bytes = [IO.File]::ReadAllBytes($shader)
if ($bytes.Length -ne 968 -or $bytes[0x49] -ne 7 -or $bytes[0x59] -ne 7) {
    throw 'Unexpected CQ selector layout.'
}
# LLVM: attr1.z pair = 00 06 08 C8 / 01 06 09 C8. Keep M0 and all other instructions.
$bytes[0x49] = 6
$bytes[0x59] = 6
[IO.File]::WriteAllBytes($shader, $bytes)
Write-Output "CR_PS_GENERATED sha256=$((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant())"
