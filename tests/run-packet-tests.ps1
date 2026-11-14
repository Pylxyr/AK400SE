<#
.SYNOPSIS
  Packet identity test: the frames built by src/Packet.cs must be byte-identical to the Python reference
  (tests/ref_se.py) for temp 0-255 x usage 0-100 x both modes, plus the C# spot checks.
  Works in Windows PowerShell 5.1 and PowerShell 7. Needs the .NET SDK and Python 3.

  Exit code: 0 = identical, 1 = failed, 2 = skipped (only with -AllowNoPython and no Python found).
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [switch]$AllowNoPython
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is EMPTY in param() defaults under Windows PowerShell 5.1 with -File, so work it out in the body.
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $OutDir) { $OutDir = Join-Path $here 'out' }

# Windows PowerShell 5 turns anything a native program writes to stderr into a terminating error when
# $ErrorActionPreference is 'Stop' and the caller redirects streams (the error then has an EMPTY message).
# Every native call goes through this helper, which relaxes that for the call only; exit codes are checked explicitly.
function Invoke-Native([scriptblock]$Command) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command } finally { $ErrorActionPreference = $saved }
}

$ExpectedLines = 2 * 256 * 101 + 1      # {temp,usage} x 256 temps x 101 usages, plus the init packet
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$csOut = Join-Path $OutDir 'packets-cs.txt'
$pyOut = Join-Path $OutDir 'packets-py.txt'
Remove-Item -LiteralPath $csOut, $pyOut -ErrorAction SilentlyContinue

function Find-Python {
    # python.exe in WindowsApps can be a Store stub that is not a real Python: only accept one that answers.
    $candidates = @(
        @{ Name = 'python';  Prefix = @() },
        @{ Name = 'py';      Prefix = @('-3') },
        @{ Name = 'python3'; Prefix = @() }
    )
    foreach ($c in $candidates) {
        $cmd = Get-Command $c.Name -ErrorAction SilentlyContinue
        if (-not $cmd) { continue }
        try {
            $probe = @($c.Prefix) + @('-c', 'import sys; print(sys.version_info[0])')
            $out = Invoke-Native { & $cmd.Source @probe 2>$null }
            if ($LASTEXITCODE -eq 0 -and "$out".Trim() -eq '3') { return @{ Exe = $cmd.Source; Prefix = @($c.Prefix) } }
        } catch { }
    }
    return $null
}

# 1. C# side: writes the dump and runs the spot checks (exits non-zero on a failed check)
$project = Join-Path (Join-Path $here 'PacketTests') 'PacketTests.csproj'
$runArgs = @('run', '--project', $project, '-c', 'Release', '--', $csOut)
Write-Host "[1/3] C# frames (dotnet run PacketTests)"
Invoke-Native { & dotnet @runArgs }
if ($LASTEXITCODE -ne 0) { Write-Host "PACKET TEST: FAIL (C# test project failed, exit $LASTEXITCODE)"; exit 1 }
if (-not (Test-Path -LiteralPath $csOut)) { Write-Host "PACKET TEST: FAIL (no C# dump was written)"; exit 1 }

# 2. Python reference
Write-Host "[2/3] Python reference (tests/ref_se.py)"
$py = Find-Python
if (-not $py) {
    if ($AllowNoPython) { Write-Host "PACKET TEST: SKIPPED (no working Python 3 found; C# spot checks passed, identity not compared)"; exit 2 }
    Write-Host "PACKET TEST: FAIL (no working Python 3 found; install it or pass -AllowNoPython)"; exit 1
}
$ref = Join-Path $here 'ref_se.py'
$pyArgs = @($py.Prefix) + @($ref, $pyOut)
Invoke-Native { & $py.Exe @pyArgs }
if ($LASTEXITCODE -ne 0) { Write-Host "PACKET TEST: FAIL (ref_se.py exited $LASTEXITCODE)"; exit 1 }

# 3. Compare byte for byte
Write-Host "[3/3] Compare"
$hCs = (Get-FileHash -Algorithm SHA256 -LiteralPath $csOut).Hash
$hPy = (Get-FileHash -Algorithm SHA256 -LiteralPath $pyOut).Hash
$a = [IO.File]::ReadAllLines($csOut)
$b = [IO.File]::ReadAllLines($pyOut)
Write-Host ("  C#     : {0} lines, sha256 {1}" -f $a.Length, $hCs)
Write-Host ("  Python : {0} lines, sha256 {1}" -f $b.Length, $hPy)

if ($a.Length -ne $ExpectedLines -or $b.Length -ne $ExpectedLines) {
    Write-Host "PACKET TEST: FAIL (expected $ExpectedLines lines in each dump)"; exit 1
}
if ($hCs -ne $hPy) {
    for ($i = 0; $i -lt $a.Length; $i++) {
        if ($a[$i] -ne $b[$i]) {
            Write-Host ("  first difference at line {0}:`n    C#     {1}`n    Python {2}" -f ($i + 1), $a[$i], $b[$i])
            break
        }
    }
    Write-Host "PACKET TEST: FAIL (dumps differ)"; exit 1
}
Write-Host ("PACKET TEST: PASS ({0} frames identical, sha256 {1})" -f $ExpectedLines, $hCs)
exit 0
