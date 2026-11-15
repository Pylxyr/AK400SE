<#
.SYNOPSIS
  Downloads the official PawnIO installer (the exact release pinned in installer/deepcool-lite.iss) from
  github.com/namazso/PawnIO.Setup and stops if its SHA-256 differs from the pinned value. The file is bundled
  unmodified into the DeepCool Lite installer. Works in Windows PowerShell 5.1 and PowerShell 7.

  The version and hash live in ONE place, the two "#define PawnIo..." lines of the .iss file. To move to a newer
  PawnIO: read its release page, update both lines, and check the redistribution terms again.
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $here
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts/pawnio' }

$iss = Get-Content -Raw -LiteralPath (Join-Path $root 'installer/deepcool-lite.iss')
$verMatch = [regex]::Match($iss, '(?m)^#define PawnIoVersion "(?<v>[0-9]+\.[0-9]+\.[0-9]+)"')
$shaMatch = [regex]::Match($iss, '(?m)^#define PawnIoSha256 "(?<h>[0-9a-f]{64})"')
if (-not $verMatch.Success -or -not $shaMatch.Success) { throw 'Could not read PawnIoVersion / PawnIoSha256 from installer/deepcool-lite.iss' }
$version = $verMatch.Groups['v'].Value
$expected = $shaMatch.Groups['h'].Value

$url = "https://github.com/namazso/PawnIO.Setup/releases/download/$version/PawnIO_setup.exe"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$file = Join-Path $OutDir 'PawnIO_setup.exe'
if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }

Write-Host "Downloading PawnIO $version from $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
Write-Host "SHA-256 : $actual"
if ($actual -ne $expected) {
    Remove-Item -LiteralPath $file -Force
    throw "SHA-256 mismatch for PawnIO $version. Expected $expected but got $actual. The file was deleted and NOT bundled."
}
Write-Host "ok   matches the pinned SHA-256"
