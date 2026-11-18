<#
.SYNOPSIS
    The fight simulator: ACE's own combat code, run offline. Every number it prints is the server's (docs/fight-sim.md).

.DESCRIPTION
    Builds tools\fight-sim when its sources are newer than the built tool, then runs it with the arguments given. The
    build goes to cache\fight-sim\bin of this checkout (gitignored); the tool links the pinned ACE build under
    server\runtime\ace of the main checkout, so it runs from any worktree.

    It reads the local world and shard databases (queries only) and the retail dats, opens no socket and writes nothing
    but files under cache\fight-sim\. It runs beside the live server. Any agent may run it: it is not a server script
    and starts no backend (CLAUDE.md, "Numbers come from the corpus").

      pwsh -File tools\fight-sim.ps1 her                       her sheet as the server last saved her
      pwsh -File tools\fight-sim.ps1 matchup "Tusker Guard"    her spells on it, its attacks on her, the race
      pwsh -File tools\fight-sim.ps1 matchup 22593 --explain   the same, with ACE's working for its attacks
      pwsh -File tools\fight-sim.ps1 creature 23091            a creature as the server builds it
      pwsh -File tools\fight-sim.ps1 cast "Flame Bolt VI"      one of her casts, timed piece by piece
      pwsh -File tools\fight-sim.ps1 rank --min-level 60       every placed creature and ground, ranked for her
      pwsh -File tools\fight-sim.ps1 bestiary --check          the tracked creature tables, written again and compared
      pwsh -File tools\fight-sim.ps1 help                      every command and option

.PARAMETER DotnetRoot
    The .NET SDK root; found as tools\lib\Toolchain.psm1 finds it when omitted.

.PARAMETER Rebuild
    Build even when the built tool is newer than its sources.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$DotnetRoot,
    [switch]$Rebuild,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot

# This process only (children inherit it); nothing persistent.
$env:DOTNET_ROOT = $DotnetRoot
$env:PATH = "$DotnetRoot;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

$checkout = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'fight-sim\Lifestone.FightSim.csproj'
$bin = Join-Path $checkout 'cache\fight-sim\bin'
$tool = Join-Path $bin 'Lifestone.FightSim.dll'

$newestSource = Get-ChildItem (Join-Path $PSScriptRoot 'fight-sim') -File | Where-Object { $_.Extension -in '.cs', '.csproj' } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($Rebuild -or -not (Test-Path $tool) -or (Get-Item $tool).LastWriteTimeUtc -lt $newestSource.LastWriteTimeUtc) {
    $log = Join-Path $checkout 'cache\fight-sim\build.log'
    New-Item -ItemType Directory -Force (Split-Path -Parent $log) | Out-Null
    & (Join-Path $DotnetRoot 'dotnet.exe') build $project --disable-build-servers -c Release -o $bin *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content $log -Tail 20 | Write-Host
        throw "fight-sim did not build (the log: $log)"
    }
}

Push-Location $checkout
try {
    & (Join-Path $DotnetRoot 'dotnet.exe') $tool @Arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
