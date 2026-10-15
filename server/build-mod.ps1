<#
.SYNOPSIS
  Compiles Lifestone's ACE server mod (server\mod) into cache\ as a check. Never installs it and never starts anything.

.DESCRIPTION
  Builds server\mod\Lifestone.ServerMod against a set-up ACE build (server\runtime\ace, created by setup-server.ps1),
  then checks what came out: exactly Lifestone.ServerMod.dll, Lifestone.ServerMod.pdb and Meta.json (with HotReload
  false), and no ACE, Harmony, McMaster or log4net assembly beside them. A copy of ACE.Server.dll next to the mod would
  be the type-identity trap: ACE would load the mod's own ACE.Server and not recognise its IHarmonyMod.

  The ACE build is the one in -AceDir, else the main checkout's server\runtime\ace (a git worktree inside a checkout
  uses the enclosing checkout's: the backend runs from the main checkout only). Its build stamp lifestone-build.json
  must name the commit versions.json pins; the stamp's ACE.Server.dll hash is passed to the build, which refuses a DLL
  that does not match it.

  dotnet runs as the current user with the opt-outs CLAUDE.md requires (no telemetry, no first-run PATH or
  certificate changes) and without build servers or node reuse, so no process outlives the build. The project has no
  package references, so the build downloads nothing.

  Installing the mod into server\runtime\ace-mods is setup-server.ps1's job (its 'Lifestone server mod' step, or
  setup-server.ps1 -ModOnly), not this script's.

  Exit codes: 0 built and checked; 1 failed; 3 skipped, because no ACE build exists to compile against (nothing was
  built).

.PARAMETER OutDir
  Where the build output goes; emptied first, so it must be a folder inside <repo>\cache whose name starts with
  server-mod. MSBuild's restore and intermediate files go to the sibling folder <OutDir>.obj (emptied first too), not
  to server\mod\Lifestone.ServerMod\obj, so builds into different folders never share intermediates. Default:
  <repo>\cache\server-mod.

.PARAMETER AceDir
  The folder of a built ACE (holding ACE.Server.dll and lifestone-build.json). Default: the main checkout's
  server\runtime\ace.

.PARAMETER BuildKey
  Recorded in the DLL's LifestoneModBuildKey metadata (setup passes its build key). Default: none.
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [string]$AceDir = '',
    [string]$BuildKey = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Import-Module (Join-Path $PSScriptRoot 'lib\LifestoneServer.psm1') -Force

$repo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repo 'tools\lib\Toolchain.psm1') -Force
$cfg = Get-LsConfig
$project = Join-Path $PSScriptRoot 'mod\Lifestone.ServerMod\Lifestone.ServerMod.csproj'

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'Lifestone server mod: compile check'
$cache = [IO.Path]::GetFullPath((Join-Path $repo 'cache')).TrimEnd('\') + '\'
if (-not $OutDir) { $OutDir = Join-Path $cache 'server-mod' }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path (Get-Location).ProviderPath $OutDir }
$OutDir = [IO.Path]::GetFullPath($OutDir).TrimEnd('\')
# The folder and its intermediates sibling (<OutDir>.obj) are emptied before the build, so they must be this script's
# own: inside cache\, named server-mod*.
if (-not ($OutDir + '\').StartsWith($cache, [StringComparison]::OrdinalIgnoreCase) -or ($OutDir + '\') -eq $cache -or
    -not (Split-Path -Leaf $OutDir).StartsWith('server-mod', [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "build-mod: -OutDir must be a folder inside $cache whose name starts with server-mod (got $OutDir)." -ForegroundColor Red
    exit 1
}

if (-not $AceDir) {
    $runtime = Find-LifestoneServerRuntime -RepoRoot $repo -AllowMissing
    if ($runtime) { $AceDir = Join-Path $runtime 'ace' }
}
if ($AceDir -and -not [IO.Path]::IsPathRooted($AceDir)) { $AceDir = Join-Path (Get-Location).ProviderPath $AceDir }
if (-not $AceDir -or -not (Test-Path -LiteralPath (Join-Path $AceDir 'ACE.Server.dll') -PathType Leaf)) {
    $where = if ($AceDir) { $AceDir } else { 'server\runtime\ace (no checkout has a server\runtime)' }
    Write-Host "build-mod: SKIP - no ACE build in $where to compile against. Set the backend up once from the main checkout: pwsh -NoProfile -File server\setup-server.ps1" -ForegroundColor Yellow
    exit 3
}
$AceDir = [IO.Path]::GetFullPath($AceDir).TrimEnd('\')

$stampFile = Join-Path $AceDir 'lifestone-build.json'
if (-not (Test-Path -LiteralPath $stampFile -PathType Leaf)) {
    Write-Host "build-mod: $AceDir has no lifestone-build.json, so its commit is unknown; rebuild ACE with server\setup-server.ps1 -RebuildAce." -ForegroundColor Red
    exit 1
}
$stamp = Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json
$pinned = [string]$cfg.Versions.ace.commit
if ([string]$stamp.commit -ne $pinned) {
    Write-Host "build-mod: the ACE build in $AceDir is commit $($stamp.commit), but server\versions.json pins $pinned; run server\setup-server.ps1." -ForegroundColor Red
    exit 1
}
$aceSha = ([string]$stamp.aceServerDllSha256).ToLowerInvariant()
Write-LsInfo "ACE $($pinned.Substring(0, 8)) in $AceDir (ACE.Server.dll SHA-256 $aceSha)"

# ----------------------------------------------------------------------------------------------------------------
$dotnetRoot = Get-LsDotnetRoot
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
# No telemetry, and none of the SDK's first-run changes to the user PATH or certificates (CLAUDE.md, ".NET").
$dotnetEnv = Get-LsDotnetEnvironment $dotnetRoot
if (-not $BuildKey) { $BuildKey = 'none' }
$objDir = $OutDir + '.obj'
foreach ($dir in @($OutDir, $objDir)) { if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force } }
# BaseIntermediateOutputPath is a global property, so it holds from the SDK's first props on, restore included.
$arguments = @(
    'build', $project, '-c', 'Release', '-o', $OutDir,
    '--disable-build-servers', '-nodeReuse:false', '-p:UseSharedCompilation=false', "-p:BaseIntermediateOutputPath=$objDir\",
    "-p:AceDir=$AceDir", "-p:AceServerDllSha256=$aceSha", "-p:AceCommit=$pinned", "-p:LifestoneModBuildKey=$BuildKey"
)
Write-LsInfo "dotnet build -> $OutDir (intermediates in $objDir)"
$build = Invoke-LsTool -FilePath $dotnet -Arguments $arguments -Environment $dotnetEnv -WorkingDirectory $repo -TimeoutSec 900 -AllowFailure
if ($build.ExitCode -ne 0) {
    Write-Host ($build.StdOut + $build.StdErr)
    Write-Host "build-mod: dotnet build failed (exit $($build.ExitCode))." -ForegroundColor Red
    exit 1
}
$warnings = @([regex]::Matches($build.StdOut, 'warning [A-Z]+\d+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
if ($warnings.Count) {
    Write-Host $build.StdOut
    Write-Host "build-mod: the build reported warnings: $($warnings -join ', ')." -ForegroundColor Red
    exit 1
}

# ----------------------------------------------------------------------------------------------------------------
# Exactly the dll, its pdb and Meta.json (HotReload false), and none of ACE's own assemblies; setup's install checks
# the same (Get-LsServerModOutputProblems).
$problems = @(Get-LsServerModOutputProblems $OutDir)
if ($problems.Count) {
    Write-Host "build-mod: the output in $OutDir is wrong: $($problems -join '; ')." -ForegroundColor Red
    exit 1
}
$meta = [IO.File]::ReadAllText((Join-Path $OutDir 'Meta.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json

$dll = Join-Path $OutDir 'Lifestone.ServerMod.dll'
Write-LsInfo "OK: $dll (version $($meta.Version), SHA-256 $(Get-LsSha256 $dll))"
exit 0
