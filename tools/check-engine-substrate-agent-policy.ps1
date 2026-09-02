[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repository 'AGENTS.md'

if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) {
    throw "Missing SharpProspero agent policy: $policyPath"
}

$lines = @(Get-Content -LiteralPath $policyPath)
$text = $lines -join "`n"
$failures = [System.Collections.Generic.List[string]]::new()

if ($lines.Count -gt 180) {
    $failures.Add("AGENTS.md is $($lines.Count) lines; the compact-policy limit is 180.")
}

$required = [ordered]@{
    'engine-neutral boundary' = 'engine-neutral PS5 platform and runtime substrate'
    'progressive routing' = 'Read only what the task needs'
    'no AGC imports' = 'SharpProspero.Graphics.Agc'
    'no direct-memory exposure' = 'DirectMemoryRegion'
    'draw does not present' = 'A draw records work only'
    'single presentation owner' = 'Exactly one component owns presentation'
    'flip encoding owner' = 'Only the graphics frame/device backend may encode that flip'
    'checked suspension' = 'AgcDevice.SuspendPoint()'
    'bounded waits' = 'Every wait is bounded'
    'in-flight lifetime' = 'remains alive and unmodified'
    'whole shader contract' = 'one indivisible contract'
    'metadata-derived binding' = 'Offsets come from serialized metadata'
    'separate evidence claims' = 'Artifact integrity, successful execution and correct rendering are separate claims'
    'correct-rendering oracle' = 'Correct rendering requires a prewritten visible oracle'
    'architecture document update' = 'Update `docs/engine-substrate-design.md` in the same commit'
    'negative fixture requirement' = 'negative or near-miss fixture'
    'host is not target proof' = 'host build or unit test never qualifies new target behavior'
    'evidence graph routing' = '../GuitarHeroPs5/docs/evidence-graph/'
    'renderer skill routing' = 'prospero-renderer-probe-method'
    'risk-based skill routing' = 'triggered by the risk of the work'
    'no implicit target authority' = 'No instruction here authorizes console'
}

foreach ($entry in $required.GetEnumerator()) {
    if (-not $text.Contains($entry.Value, [System.StringComparison]::Ordinal)) {
        $failures.Add("Missing policy coverage: $($entry.Key) ('$($entry.Value)').")
    }
}

$forbidden = [ordered]@{
    'mutable current commit instruction' = 'current commit is'
    'mutable deployed title instruction' = 'current deployed artifact is'
    'blanket full-design load' = 'always read the entire engine-substrate design'
}

foreach ($entry in $forbidden.GetEnumerator()) {
    if ($text.Contains($entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("Forbidden context-heavy or mutable policy: $($entry.Key) ('$($entry.Value)').")
    }
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "ENGINE_SUBSTRATE_AGENT_POLICY_VALID lines=$($lines.Count) required=$($required.Count) forbidden=$($forbidden.Count)"
