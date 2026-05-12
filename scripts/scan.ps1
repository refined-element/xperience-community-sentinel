#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run Sentinel for Xperience by Kentico against an Xperience by Kentico project, auto-resolving the
    connection string from `dotnet user-secrets`.

.EXAMPLE
    ./scripts/scan.ps1 -Project F:\RefinedElement\re-xbk

.EXAMPLE
    ./scripts/scan.ps1 -Project F:\RefinedElement\re-xbk -StaleDays 365 -Output ./reports/re-xbk
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Project,
    [int]$StaleDays = 180,
    [string]$Output = "",
    [string]$SecretKey = "ConnectionStrings:CMSConnectionString",
    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'

$Project = (Resolve-Path $Project).Path
if (-not (Test-Path $Project)) {
    throw "Project path not found: $Project"
}

if (-not $Output) {
    $projectName = Split-Path $Project -Leaf
    $Output = Join-Path $PSScriptRoot "..\reports\$projectName-latest"
}

# Resolve connection string from user-secrets on the target project.
$secretsOutput = & dotnet user-secrets list --project $Project 2>$null
$connLine = $secretsOutput | Where-Object { $_ -match "^$([regex]::Escape($SecretKey))\s*=" } | Select-Object -First 1
if (-not $connLine) {
    throw "Could not find '$SecretKey' in user-secrets for $Project. Set it with:`n  dotnet user-secrets set '$SecretKey' 'Server=...' --project $Project"
}
$connectionString = ($connLine -split ' = ', 2)[1]

Write-Host "Scanning $Project" -ForegroundColor Cyan
& sentinel scan --path $Project --connection-string $connectionString --stale-days $StaleDays --output $Output
$exitCode = $LASTEXITCODE

if ($OpenReport) {
    $htmlPath = Join-Path $Output "report.html"
    if (Test-Path $htmlPath) {
        Start-Process $htmlPath
    }
}

exit $exitCode
