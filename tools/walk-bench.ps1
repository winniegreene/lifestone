<#
.SYNOPSIS
    The walk bench: a fixed set of offline walks on the retail dats, replayed exactly, so a steering or route change is judged walk for walk before and after (docs/milestones/world-walk.md, "The walk bench").

.DESCRIPTION
    Runs the bench test (tests/Lifestone.Client.Tests WalkBenchDatTests, Tier=Tool) with the label given, and compares it with the base label's run when one is named:

      hubs-ord, hubs-pred    the 40 hub legs (the Town Network's arrival to and from its eleven portal rooms, the Facility Hub's to and from nine), each steering
      hops                   the hop's front walk as the journeys run it live, from each hub's arrival to every portal's front there; facility-hub/hub-quarters is the
                             live crossing, 0x8A020212 to the Tusker Quarters portal's front
      dungeons-ord, -pred    every dungeon ground's whole tour from its arrival, leg after leg, each steering

    It writes cache\walk-bench\<label>.tsv (one row a walk) and prints each set's totals, and with -Base the totals before and after and every walk whose verdict
    (state, rungs, refusals, replans, wall frames) or counts changed. About five minutes on this machine.

    With -Rooms it also builds the world walk (Release, into cache\walk-bench\ww-<label>-bin) and runs its rooms verb on a fixed list of 77 landblocks (the first 60
    landblocks with rooms, the dungeon grounds and both hubs: 60,788 room legs, under a minute), into cache\walk-bench\rooms-<label>, and with -Base compares that run
    with rooms-<base> by the tool's own compare verb (exit code 1 when they differ).

    Everything runs at BelowNormal priority (the climber's live run shares the machine). Nothing is written outside cache\.

      pwsh -File tools\walk-bench.ps1 -Label before
      pwsh -File tools\walk-bench.ps1 -Label after -Base before -Rooms
      pwsh -File tools\walk-bench.ps1 -Label t -Sets hops -Trace hops/facility-hub/hub-quarters   (cache\walk-bench\trace-hops-facility-hub-hub-quarters.txt)

.PARAMETER Label
    This run's name.

.PARAMETER Base
    The run to compare with (its .tsv, and rooms-<base> with -Rooms).

.PARAMETER Sets
    The sets to run, comma-separated (default all).

.PARAMETER Trace
    One walk to trace, by its bench name (set/walk): its key changes, the steering that held them, the route and the runner's log.

.PARAMETER Rooms
    Also run and compare the world walk's rooms verb on the fixed landblocks.

.PARAMETER DatDir
    The retail dats for the world walk (default: the main checkout's client folder).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$Base,
    [string]$Sets,
    [string]$Trace,
    [switch]$Rooms,
    [string]$DatDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

(Get-Process -Id $PID).PriorityClass = 'BelowNormal' # children inherit it

Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot

# This process only (children inherit it); nothing persistent.
$env:DOTNET_ROOT = $DotnetRoot
$env:PATH = "$DotnetRoot;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnet = Join-Path $DotnetRoot 'dotnet.exe'

$checkout = Split-Path -Parent $PSScriptRoot
$out = Join-Path $checkout 'cache\walk-bench'
New-Item -ItemType Directory -Force $out | Out-Null

# The world walk's rooms landblocks: the first 60 landblocks with rooms (the rooms run's own order), the dungeon grounds' landblocks and the two hubs.
$roomsLandblocks = (@(1..0x3C | ForEach-Object { $_.ToString('X4') }) + @('0103', '0105', '0162', '019A', '5748', '5749', '5948', '5A48', '5B46', '5B47', '5B48', '5B4A', '5C43', '5C45', '5C4A', '5D43', '8A02')) -join ','

Push-Location $checkout
try {
    & $dotnet build tests\Lifestone.Client.Tests --disable-build-servers *> (Join-Path $out "$Label.build.log")
    if ($LASTEXITCODE -ne 0) { throw "the tests did not build (cache\walk-bench\$Label.build.log)" }

    $env:LIFESTONE_WALK_BENCH = if ($Base) { "$Label,$Base" } else { $Label }
    $env:LIFESTONE_WALK_BENCH_SETS = $Sets
    $env:LIFESTONE_WALK_BENCH_TRACE = $Trace
    $log = Join-Path $out "$Label.log"
    & $dotnet test tests\Lifestone.Client.Tests --no-build --disable-build-servers --filter 'FullyQualifiedName~WalkBenchDatTests' --logger 'console;verbosity=detailed' *> $log
    $benchExit = $LASTEXITCODE
    Get-Content $log | Where-Object { $_ -match '^\s*(\[bench\]|set \||(hubs|hops|dungeons)[^|]*\||VERDICT|counts|new )' } | ForEach-Object { $_.Trim() }
    if ($benchExit -ne 0) { throw "the bench failed (cache\walk-bench\$Label.log)" }

    if ($Rooms) {
        if (-not $DatDir) {
            $common = (& git rev-parse --path-format=absolute --git-common-dir).Trim()
            $DatDir = Join-Path (Split-Path -Parent $common) 'client'
        }

        $bin = Join-Path $out "ww-$Label-bin"
        & $dotnet build src\Lifestone.WorldWalk\Lifestone.WorldWalk.csproj -c Release --disable-build-servers -o $bin *> (Join-Path $out "ww-$Label.build.log")
        if ($LASTEXITCODE -ne 0) { throw "the world walk did not build (cache\walk-bench\ww-$Label.build.log)" }

        $run = Join-Path $out "rooms-$Label"
        if (Test-Path $run) { Remove-Item -Recurse -Force $run }
        & $dotnet (Join-Path $bin 'Lifestone.WorldWalk.dll') rooms "--out=$run" "--landblocks=$roomsLandblocks" "--dat-dir=$DatDir" '--ignore-climb=on' *> (Join-Path $out "rooms-$Label.log")
        if ($LASTEXITCODE -ne 0) { throw "the rooms run failed (cache\walk-bench\rooms-$Label.log)" }

        Get-Content (Join-Path $run 'results.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Measure-Object -Property legs, clean -Sum |
            ForEach-Object { "rooms $Label $($_.Property): $($_.Sum)" }
        if ($Base) {
            & $dotnet (Join-Path $bin 'Lifestone.WorldWalk.dll') compare "--out=$run" "--other=$(Join-Path $out "rooms-$Base")"
            exit $LASTEXITCODE
        }
    }
}
finally {
    Pop-Location
}
