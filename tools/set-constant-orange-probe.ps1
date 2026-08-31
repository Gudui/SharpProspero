# Offline generation of the isolated GPU-CO diagnostic resource, not a shader decoder.
# Operand encodings are from LLVM 21.1.8 llvm-mc --mcpu=gfx1030 --show-encoding.
$ErrorActionPreference = 'Stop'
$shader = Join-Path $PSScriptRoot '../src/SharpProspero/Graphics/Agc/Shaders/mesh_ps.sb'
$expectedCn = 'ab3722ac8e950ead1a53adfa9c2ff0d85be68cae0eb11ea2bc40ffb85461490d'
if ((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant() -ne $expectedCn) {
    throw 'Expected the exact CN constant-white shader. Refusing to modify another resource.'
}
$bytes = [IO.File]::ReadAllBytes($shader)
if ($bytes.Length -ne 968 -or $bytes[0x48] -ne 0xF2 -or $bytes[0x4C] -ne 0xF2) {
    throw 'Unexpected CN operand locations.'
}
# v_mov_b32_e32 v3, 0.5 = F0 02 06 7E; v_mov_b32_e32 v4, 0 = 80 02 08 7E.
# Preserve every other byte: shader metadata, export, scheduling, padding and resource shape.
$bytes[0x48] = 0xF0
$bytes[0x4C] = 0x80
[IO.File]::WriteAllBytes($shader, $bytes)
Write-Output "CO_PS_GENERATED rgba=1,0.5,0,1 changed_offsets=0x48,0x4C sha256=$((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant())"
