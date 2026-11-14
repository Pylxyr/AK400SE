<#
.SYNOPSIS
  Tests for scripts/version.ps1, run against a temporary copy of the csproj (the real one is never touched).
  Exit code 0 = all passed.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $here
$ver = Join-Path (Join-Path $root 'scripts') 'version.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('dcl-version-' + [guid]::NewGuid().ToString('N') + '.csproj')
Copy-Item -LiteralPath (Join-Path $root 'deepcool-lite.csproj') -Destination $tmp

$script:n = 0
$script:bad = 0
function Expect([string]$what, $actual, $expected) {
    $script:n++
    if ("$actual" -ceq "$expected") { Write-Host "  ok   $what = $actual" }
    else { $script:bad++; Write-Host "  FAIL $what : got '$actual', expected '$expected'" }
}
function ExpectThrow([string]$what, [scriptblock]$body) {
    $script:n++
    $threw = $false
    try { & $body | Out-Null } catch { $threw = $true }
    if ($threw) { Write-Host "  ok   $what (rejected)" } else { $script:bad++; Write-Host "  FAIL $what : was accepted" }
}

try {
    $start = & $ver -Get -Csproj $tmp
    Expect 'starts as a valid semver' ($start -match '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') 'True'

    Expect 'Set 1.2.3'                  (& $ver -Set 1.2.3 -Csproj $tmp) '1.2.3'
    Expect 'Get after Set'              (& $ver -Get -Csproj $tmp) '1.2.3'
    Expect 'Next patch'                 (& $ver -Next patch -Csproj $tmp) '1.2.4'
    Expect 'Next minor'                 (& $ver -Next minor -Csproj $tmp) '1.3.0'
    Expect 'Next major'                 (& $ver -Next major -Csproj $tmp) '2.0.0'
    Expect 'Next does not write'        (& $ver -Get -Csproj $tmp) '1.2.3'
    Expect 'Set v-prefixed'             (& $ver -Set v0.2.0-rc1 -Csproj $tmp) '0.2.0-rc1'
    Expect 'Next patch on rc releases'  (& $ver -Next patch -Csproj $tmp) '0.2.0'
    Expect 'Next minor on rc'           (& $ver -Next minor -Csproj $tmp) '0.3.0'
    Expect 'Check matching tag'         (& $ver -Check v0.2.0-rc1 -Csproj $tmp) '0.2.0-rc1'
    ExpectThrow 'Check wrong tag'       { & $ver -Check v9.9.9 -Csproj $tmp }
    ExpectThrow 'Set banana'            { & $ver -Set banana -Csproj $tmp }
    ExpectThrow 'Set 1.2'               { & $ver -Set 1.2 -Csproj $tmp }
    ExpectThrow 'Set 1.2.3.4'           { & $ver -Set 1.2.3.4 -Csproj $tmp }
    Expect 'rejected Set left file alone' (& $ver -Get -Csproj $tmp) '0.2.0-rc1'

    # only the one element changes: the rest of the file is byte-identical
    & $ver -Set $start -Csproj $tmp | Out-Null
    $same = ([IO.File]::ReadAllText($tmp) -ceq [IO.File]::ReadAllText((Join-Path $root 'deepcool-lite.csproj')))
    Expect 'round trip restores the file exactly' $same 'True'
}
finally {
    Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue
}

if ($script:bad -gt 0) { Write-Host "VERSION TESTS: FAIL ($script:bad of $script:n)"; exit 1 }
Write-Host "VERSION TESTS: PASS ($script:n checks)"
exit 0
