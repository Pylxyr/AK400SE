<#
.SYNOPSIS
  Read, bump or set the one version number of AK400SE: the <Version> element in AK400SE.csproj.
  Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  .\scripts\version.ps1 -Get              # prints 0.1.0
  .\scripts\version.ps1 -Next patch       # prints 0.1.1   (does not write anything)
  .\scripts\version.ps1 -Set 0.2.0        # rewrites <Version> in the csproj
  .\scripts\version.ps1 -Check v0.2.0     # fails unless the csproj says 0.2.0
#>
[CmdletBinding(DefaultParameterSetName = 'Get')]
param(
    [Parameter(ParameterSetName = 'Get')][switch]$Get,
    [Parameter(ParameterSetName = 'Next', Mandatory = $true)][ValidateSet('patch', 'minor', 'major')][string]$Next,
    [Parameter(ParameterSetName = 'Set', Mandatory = $true)][string]$Set,
    [Parameter(ParameterSetName = 'Check', Mandatory = $true)][string]$Check,
    [string]$Csproj
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is EMPTY in param() defaults under Windows PowerShell 5.1 with -File, so work it out in the body.
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Csproj) { $Csproj = Join-Path (Split-Path -Parent $here) 'AK400SE.csproj' }
$Csproj = (Resolve-Path -LiteralPath $Csproj).Path
$semver = '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'

function Read-Version {
    $text = [IO.File]::ReadAllText($Csproj)
    $m = [regex]::Match($text, '<Version>\s*([^<\s]+)\s*</Version>')
    if (-not $m.Success) { throw "No <Version> element in $Csproj" }
    $v = $m.Groups[1].Value
    if ($v -notmatch $semver) { throw "<Version> '$v' is not MAJOR.MINOR.PATCH[-prerelease]" }
    return $v
}

function Normalize([string]$s) {
    $s = $s.Trim()
    if ($s.StartsWith('v')) { $s = $s.Substring(1) }
    if ($s -notmatch $semver) { throw "'$s' is not MAJOR.MINOR.PATCH[-prerelease], e.g. 0.2.0 or 0.2.0-rc1" }
    return $s
}

switch ($PSCmdlet.ParameterSetName) {
    'Next' {
        $m = [regex]::Match((Read-Version), $semver)
        $maj = [int]$m.Groups[1].Value; $min = [int]$m.Groups[2].Value; $pat = [int]$m.Groups[3].Value
        $pre = $m.Groups[4].Value
        if ($Next -eq 'major') { Write-Output ('{0}.0.0' -f ($maj + 1)) }
        elseif ($Next -eq 'minor') { Write-Output ('{0}.{1}.0' -f $maj, ($min + 1)) }
        # patch on a pre-release (0.2.0-rc1) releases it (0.2.0); otherwise it adds one
        elseif ($pre) { Write-Output ('{0}.{1}.{2}' -f $maj, $min, $pat) }
        else { Write-Output ('{0}.{1}.{2}' -f $maj, $min, ($pat + 1)) }
    }
    'Set' {
        $v = Normalize $Set
        $text = [IO.File]::ReadAllText($Csproj)
        $re = New-Object System.Text.RegularExpressions.Regex('<Version>\s*[^<]*</Version>')
        if (-not $re.IsMatch($text)) { throw "No <Version> element in $Csproj" }
        $new = $re.Replace($text, "<Version>$v</Version>", 1)
        if ($new -ne $text) { [IO.File]::WriteAllText($Csproj, $new, (New-Object System.Text.UTF8Encoding($false))) }
        Write-Output $v
    }
    'Check' {
        $want = Normalize $Check
        $have = Read-Version
        if ($have -ne $want) { throw "AK400SE.csproj says $have, but $want was expected" }
        Write-Output $have
    }
    default { Write-Output (Read-Version) }
}
