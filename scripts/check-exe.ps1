<#
.SYNOPSIS
  Sanity-check a published AK400SE.exe and, optionally, the installer, WITHOUT running either. Works in
  Windows PowerShell 5.1 and PowerShell 7.

  App exe (-Path): a Windows x64 GUI executable whose folder holds the bundled .NET runtime (self-contained), version
  resource matching -ExpectedVersion, and a manifest that does NOT demand elevation (the app is started elevated through its
  scheduled task instead, so there is no UAC prompt at every start).
  Installer (-InstallerPath, optional): a Windows x64 executable of plausible size whose version matches.
  Prints SHA-256 and sizes. Exit code 0 = all checks passed, 1 = at least one failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$ExpectedVersion,
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
function Ok([string]$m)   { Write-Host "  ok   $m" }
function Fail([string]$m) { Write-Host "  FAIL $m"; $failures.Add($m) }

# PE header: "MZ", then the PE header. Returns machine type and subsystem, or $null when it is not a PE file.
function Get-PeInfo([byte[]]$bytes) {
    if ($bytes.Length -gt 0x40 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
        $pe = [BitConverter]::ToInt32($bytes, 0x3C)
        if ($pe -gt 0 -and $pe + 24 + 70 -lt $bytes.Length -and $bytes[$pe] -eq 0x50 -and $bytes[$pe + 1] -eq 0x45) {
            return [pscustomobject]@{
                Machine   = [BitConverter]::ToUInt16($bytes, $pe + 4)
                Subsystem = [BitConverter]::ToUInt16($bytes, $pe + 24 + 68)   # same offset in PE32 and PE32+
            }
        }
    }
    return $null
}

function Check-Version([string]$full, [string]$want) {
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($full)
    Write-Host ("Version : FileVersion={0} ProductVersion={1}" -f $vi.FileVersion, $vi.ProductVersion)
    if (-not $want) { return }
    $want = $want.Trim()
    if ($want.StartsWith('v')) { $want = $want.Substring(1) }
    if ($vi.ProductVersion -and $vi.ProductVersion.StartsWith($want)) { Ok "ProductVersion starts with $want" }
    else { Fail "ProductVersion is '$($vi.ProductVersion)', expected it to start with $want" }
    $numeric = ($want -replace '-.*$', '')
    if ($vi.FileVersion -and $vi.FileVersion.StartsWith($numeric + '.')) { Ok "FileVersion $($vi.FileVersion) matches $numeric" }
    else { Fail "FileVersion is '$($vi.FileVersion)', expected $numeric.x" }
}

# ---------- the app ----------
if (-not (Test-Path -LiteralPath $Path)) { Write-Host "  FAIL file not found: $Path"; exit 1 }
$full = (Resolve-Path -LiteralPath $Path).Path
$bytes = [IO.File]::ReadAllBytes($full)
Write-Host "File    : $full"
Write-Host ("Size    : {0:N0} bytes" -f $bytes.Length)
Write-Host ("SHA-256 : {0}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant())

# the exe itself is only the small launcher; the runtime sits next to it
if ($bytes.Length -lt 50KB) { Fail 'file is under 50 KB: not a real app launcher' }
elseif ($bytes.Length -gt 5MB) { Fail ('file is {0:N1} MB: expected a small launcher exe, not a single-file bundle' -f ($bytes.Length / 1MB)) }
else { Ok ('launcher size plausible ({0:N0} KB)' -f ($bytes.Length / 1KB)) }

$info = Get-PeInfo $bytes
if (-not $info) { Fail 'not a Windows PE executable' }
else {
    if ($info.Machine -eq 0x8664) { Ok 'Windows x64 executable (PE machine 0x8664)' }
    else { Fail ('PE machine type is 0x{0:X4}, expected 0x8664 (x64)' -f $info.Machine) }
    if ($info.Subsystem -eq 2) { Ok 'GUI application (no console window)' }
    else { Fail ('PE subsystem is {0}, expected 2 (Windows GUI)' -f $info.Subsystem) }
}

Check-Version $full $ExpectedVersion

# manifest: must not demand elevation (that would show a UAC prompt at every start)
$text = [Text.Encoding]::ASCII.GetString($bytes)
if ($text.IndexOf('requireAdministrator', [StringComparison]::Ordinal) -ge 0) { Fail 'manifest demands administrator (requireAdministrator): would show a UAC prompt at every start' }
elseif ($text.IndexOf('asInvoker', [StringComparison]::Ordinal) -ge 0) { Ok 'manifest present and does not demand elevation (asInvoker)' }
else { Fail 'manifest not found in the exe (app.manifest was not embedded)' }

# self-contained: the runtime and the libraries are in the folder
$dir = Split-Path -Parent $full
$must = @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll',
          'AK400SE.dll', 'AK400SE.runtimeconfig.json', 'LibreHardwareMonitorLib.dll', 'HidSharp.dll', 'ThirdParty/DSEG-LICENSE.txt')
$missing = @($must | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dir $_)) })
if ($missing.Count -eq 0) { Ok 'self-contained: .NET runtime and libraries are next to the exe' }
else { Fail ('missing next to the exe (not self-contained?): ' + ($missing -join ', ')) }
$total = (Get-ChildItem -LiteralPath $dir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ('Folder  : {0:N1} MB uncompressed' -f ($total / 1MB))

# ---------- the installer ----------
if ($InstallerPath) {
    Write-Host ''
    if (-not (Test-Path -LiteralPath $InstallerPath)) { Fail "installer not found: $InstallerPath" }
    else {
        $ifull = (Resolve-Path -LiteralPath $InstallerPath).Path
        $ib = [IO.File]::ReadAllBytes($ifull)
        Write-Host "Installer: $ifull"
        Write-Host ("Size     : {0:N0} bytes ({1:N1} MB)" -f $ib.Length, ($ib.Length / 1MB))
        Write-Host ("SHA-256  : {0}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $ifull).Hash.ToLowerInvariant())
        # compressed runtime + WinForms + libraries: well above 10 MB; far above 150 MB means something unwanted got bundled
        if ($ib.Length -lt 10MB) { Fail ('installer is only {0:N1} MB: the .NET runtime is probably not inside' -f ($ib.Length / 1MB)) }
        elseif ($ib.Length -gt 150MB) { Fail ('installer is {0:N0} MB: unexpectedly large' -f ($ib.Length / 1MB)) }
        else { Ok ('installer size plausible ({0:N1} MB)' -f ($ib.Length / 1MB)) }
        $ii = Get-PeInfo $ib
        if ($ii -and (($ii.Machine -eq 0x014C) -or ($ii.Machine -eq 0x8664))) { Ok 'Windows executable (Inno Setup stubs are 32-bit and run on x64)' }
        else { Fail 'installer is not a Windows PE executable' }
        Check-Version $ifull $ExpectedVersion
        if ($ExpectedVersion) {
            $want = $ExpectedVersion.Trim().TrimStart('v')
            if ((Split-Path -Leaf $ifull) -eq "AK400SE-setup-$want.exe") { Ok 'file name carries the version' }
            else { Fail "installer file name is '$(Split-Path -Leaf $ifull)', expected AK400SE-setup-$want.exe" }
        }
    }
}

if ($failures.Count -gt 0) { Write-Host "CHECK: FAIL ($($failures.Count))"; exit 1 }
Write-Host 'CHECK: PASS'
exit 0
