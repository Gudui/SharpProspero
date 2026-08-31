# Offline GPU-CP generator, not a decoder. Encodings validated by LLVM 21.1.8 / gfx1030.
$ErrorActionPreference = 'Stop'
$shader = Join-Path $PSScriptRoot '../src/SharpProspero/Graphics/Agc/Shaders/mesh_ps.sb'
if ((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant() -ne
    'ff49d8756e41d443954a99463b1a050d47b7d25587ee9cb0ccc357c2e5f75b8f') {
    throw 'Expected exact CO shader; refusing another input.'
}
$bytes = [IO.File]::ReadAllBytes($shader)
if ($bytes.Length -ne 968) { throw 'Unexpected CO resource length.' }
# Replace red immediate and one NOP; green/blue/alpha, header, export and VS remain unchanged.
[byte[]]$p1 = 0x00,0x07,0x08,0xC8 # v_interp_p1_f32_e32 v2, v0, attr1.w
[byte[]]$p2 = 0x01,0x07,0x09,0xC8 # v_interp_p2_f32_e32 v2, v1, attr1.w
$p1.CopyTo($bytes, 0x44)
$p2.CopyTo($bytes, 0x54)
[IO.File]::WriteAllBytes($shader, $bytes)
Write-Output "CP_PS_GENERATED source=attr1.w channel=red sha256=$((Get-FileHash -LiteralPath $shader).Hash.ToLowerInvariant())"
