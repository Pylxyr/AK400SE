<#
.SYNOPSIS
  Sanity-check a published deepcool-lite.exe WITHOUT running it (the exe demands elevation, so it cannot be
  launched from a normal terminal). Works in Windows PowerShell 5.1 and PowerShell 7.

  Checks: it is a Windows x64 executable, a plausible size for a framework-dependent single file, the
  version resource matches -ExpectedVersion, and the requireAdministrator manifest is embedded.
  Prints the SHA-256 too. Exit code 0 = all checks passed, 1 = at least one failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
function Ok([string]$m)   { Write-Host "  ok   $m" }
function Fail([string]$m) { Write-Host "  FAIL $m"; $failures.Add($m) }

if (-not (Test-Path -LiteralPath $Path)) { Write-Host "  FAIL file not found: $Path"; exit 1 }
$full = (Resolve-Path -LiteralPath $Path).Path
$bytes = [IO.File]::ReadAllBytes($full)
$mb = $bytes.Length / 1MB

Write-Host "File    : $full"
Write-Host ("Size    : {0:N0} bytes ({1:N2} MB)" -f $bytes.Length, $mb)
Write-Host ("SHA-256 : {0}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant())

# size: tiny means a stub; huge means a self-contained build slipped in (that one is ~70 MB and up)
if ($bytes.Length -lt 100KB) { Fail 'file is under 100 KB: not a real single-file publish' }
elseif ($mb -gt 60) { Fail ('file is {0:N0} MB: looks self-contained, expected a framework-dependent single file' -f $mb) }
else { Ok ('size plausible for a framework-dependent single file ({0:N2} MB)' -f $mb) }

# PE header: "MZ", then the PE header whose machine type must be x64 (0x8664)
$isPe = $false
if ($bytes.Length -gt 0x40 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($pe -gt 0 -and $pe + 6 -lt $bytes.Length -and $bytes[$pe] -eq 0x50 -and $bytes[$pe + 1] -eq 0x45) {
        $isPe = $true
        $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
        if ($machine -eq 0x8664) { Ok 'Windows x64 executable (PE machine 0x8664)' }
        else { Fail ('PE machine type is 0x{0:X4}, expected 0x8664 (x64)' -f $machine) }
    }
}
if (-not $isPe) { Fail 'not a Windows PE executable' }

# version resource
$vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($full)
Write-Host ("Version : FileVersion={0} ProductVersion={1}" -f $vi.FileVersion, $vi.ProductVersion)
if ($ExpectedVersion) {
    $want = $ExpectedVersion.Trim()
    if ($want.StartsWith('v')) { $want = $want.Substring(1) }
    if ($vi.ProductVersion -and $vi.ProductVersion.StartsWith($want)) { Ok "ProductVersion starts with $want" }
    else { Fail "ProductVersion is '$($vi.ProductVersion)', expected it to start with $want" }
    $numeric = ($want -replace '-.*$', '')
    if ($vi.FileVersion -and $vi.FileVersion.StartsWith($numeric + '.')) { Ok "FileVersion $($vi.FileVersion) matches $numeric" }
    else { Fail "FileVersion is '$($vi.FileVersion)', expected $numeric.x" }
}

# manifest: without it Windows would not ask for elevation and the sensors would not be readable
$text = [Text.Encoding]::ASCII.GetString($bytes)
if ($text.IndexOf('requireAdministrator', [StringComparison]::Ordinal) -ge 0) { Ok 'requireAdministrator manifest is embedded' }
else { Fail 'requireAdministrator manifest not found in the exe (app.manifest was not embedded)' }

if ($failures.Count -gt 0) { Write-Host "EXE CHECK: FAIL ($($failures.Count))"; exit 1 }
Write-Host 'EXE CHECK: PASS'
exit 0
