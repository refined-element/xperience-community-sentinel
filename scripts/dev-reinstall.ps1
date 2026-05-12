#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pack Sentinel from source, uninstall the global tool, and reinstall from the fresh package.
    Use this during iterative development to see local code changes reflected in the `sentinel` command.

.EXAMPLE
    ./scripts/dev-reinstall.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$csproj = Join-Path $repoRoot "src\XperienceCommunity.Sentinel\XperienceCommunity.Sentinel.csproj"
$packageOutputDir = Join-Path $repoRoot "src\XperienceCommunity.Sentinel\bin\Release"

Write-Host "==> Packing $csproj (Release)" -ForegroundColor Cyan
& dotnet pack $csproj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed." }

Write-Host "==> Uninstalling global tool (if present)" -ForegroundColor Cyan
& dotnet tool uninstall -g XperienceCommunity.Sentinel 2>$null | Out-Null

Write-Host "==> Installing global tool from $packageOutputDir" -ForegroundColor Cyan
# Resolve the latest packed version (needed because prerelease versions require explicit --version).
$nupkg = Get-ChildItem -Path $packageOutputDir -Filter 'XperienceCommunity.Sentinel.*.nupkg' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw "No .nupkg found in $packageOutputDir after pack." }
$version = [regex]::Match($nupkg.Name, 'XperienceCommunity\.Sentinel\.(.+)\.nupkg').Groups[1].Value
& dotnet tool install -g --add-source $packageOutputDir XperienceCommunity.Sentinel --version $version | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed." }

Write-Host "==> Installed. Run 'sentinel --version' to verify." -ForegroundColor Green
