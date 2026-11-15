<#
.SYNOPSIS
  Builds the installer: publishes the app (self-contained win-x64), fetches the pinned PawnIO installer, and compiles
  installer/deepcool-lite.iss with Inno Setup. Output: artifacts/installer/deepcool-lite-setup-<version>.exe
  Needs Windows, the .NET 10 SDK and Inno Setup 6 (preinstalled on GitHub's windows-latest; installed with Chocolatey if missing).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $here
Set-Location $root

function Assert-Native([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" } }

if (Test-Path artifacts/publish) { Remove-Item -Recurse -Force artifacts/publish }
Write-Host '== publish (self-contained, win-x64, ReadyToRun)'
dotnet publish deepcool-lite.csproj -c Release -o artifacts/publish
Assert-Native 'dotnet publish'

Write-Host '== PawnIO installer (pinned, verified)'
& (Join-Path $here 'get-pawnio.ps1')

Write-Host '== Inno Setup'
$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host 'Inno Setup 6 not found, installing it with Chocolatey'
    choco install innosetup --version=6.7.1 -y --no-progress
    Assert-Native 'choco install innosetup'
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'ISCC.exe not found after installing Inno Setup' }
}
Write-Host "Using $iscc"

$env:DCL_VERSION = $Version
& $iscc (Join-Path $root 'installer/deepcool-lite.iss')
Assert-Native 'ISCC'

$out = Join-Path $root "artifacts/installer/deepcool-lite-setup-$Version.exe"
if (-not (Test-Path -LiteralPath $out)) { throw "Expected installer not found: $out" }
Write-Host "Built $out"
