#!/usr/bin/env pwsh
<#
.SYNOPSIS
Validates an audit-findings.json file against the sentinel-audit schema and
recomputes grades from the findings, failing on any mismatch.
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [string] $SchemaPath = (Join-Path $PSScriptRoot '..' 'plugins' 'sentinel-audit' 'skills' 'audit' 'references' 'findings-schema.json')
)
$ErrorActionPreference = 'Stop'

$json = Get-Content $Path -Raw
if (-not (Test-Json -Json $json -SchemaFile $SchemaPath -ErrorAction SilentlyContinue)) {
    # Re-run without suppression so the schema error reaches the console.
    try { Test-Json -Json $json -SchemaFile $SchemaPath | Out-Null } catch { Write-Error "Schema validation failed: $_" }
    exit 1
}

$doc = $json | ConvertFrom-Json
$deduction = @{ Critical = 25; High = 10; Medium = 4; Low = 1 }
$perRuleCap = @{ Medium = 12; Low = 5 }
function Get-Grade([int] $score) {
    if ($score -ge 90) { 'A' } elseif ($score -ge 80) { 'B' } elseif ($score -ge 70) { 'C' } elseif ($score -ge 60) { 'D' } else { 'F' }
}

$failed = $false
$scores = @{}
foreach ($dim in 'architecture', 'contentModel', 'security', 'performance') {
    $score = 100
    $dimFindings = $doc.findings | Where-Object { $_.dimension -eq $dim }

    # Critical and High: linear, uncapped — every finding counts in full.
    foreach ($f in $dimFindings | Where-Object { $_.severity -eq 'Critical' -or $_.severity -eq 'High' }) {
        $score -= $deduction[$f.severity]
    }

    # Medium and Low: grouped per rule ID (finding.id), capped per rule ID.
    foreach ($severity in 'Medium', 'Low') {
        $bySeverity = $dimFindings | Where-Object { $_.severity -eq $severity }
        $byRule = $bySeverity | Group-Object -Property id
        foreach ($ruleGroup in $byRule) {
            $ruleDeduction = [Math]::Min($ruleGroup.Count * $deduction[$severity], $perRuleCap[$severity])
            $score -= $ruleDeduction
        }
    }

    $score = [Math]::Max(0, $score)
    $scores[$dim] = $score
    $declared = $doc.grades.dimensions.$dim
    if ($declared.score -ne $score -or $declared.grade -ne (Get-Grade $score)) {
        Write-Error "Grade mismatch for '$dim': declared $($declared.score)/$($declared.grade), recomputed $score/$(Get-Grade $score)" -ErrorAction Continue
        $failed = $true
    }
}
$overall = [int][Math]::Round(($scores.Values | Measure-Object -Average).Average, 0, [MidpointRounding]::AwayFromZero)
if ($doc.grades.overall -ne $overall) {
    Write-Error "Overall mismatch: declared $($doc.grades.overall), recomputed $overall" -ErrorAction Continue
    $failed = $true
}
if ($failed) { exit 1 }
Write-Host "OK: $Path is schema-valid and grades recompute correctly." -ForegroundColor Green
exit 0
