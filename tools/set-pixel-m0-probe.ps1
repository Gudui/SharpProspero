# Offline CQ generator, not an instruction decoder. LLVM gfx1030 validates the resulting program.
$ErrorActionPreference = 'Stop'
$shader = Join-Path $PSScriptRoot '../src/SharpProspero/Graphics/Agc/Shaders/mesh_ps.sb'
if ((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant() -ne
    'a8b8411104c3c54d3bc963a7ea3f2809a76451d9f7e868f50af046bab4c6fb08') {
    throw 'Expected exact CP shader; refusing another input.'
}
$bytes = [IO.File]::ReadAllBytes($shader)
if ($bytes.Length -ne 968) { throw 'Unexpected CP length.' }
# Move the interpolation/immediate sequence over one redundant NOP; keep export at 0x64.
[Array]::Copy($bytes, 0x44, $bytes, 0x48, 0x1C)
([byte[]](0x00,0x03,0xFC,0xBE)).CopyTo($bytes, 0x44) # s_mov_b32 m0, s0
[IO.File]::WriteAllBytes($shader, $bytes)
Write-Output "CQ_PS_GENERATED sha256=$((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant())"
