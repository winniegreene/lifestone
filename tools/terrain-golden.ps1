<#
.SYNOPSIS
    Golden test of the terrain shader: renders landblocks top-down in Godot and compares them with the CPU reference.

.DESCRIPTION
    Runs scenes/TerrainPreview.tscn with --golden through tools\godot.ps1 (off-screen window, user data isolated under
    <repo>\cache\godot, killed after -TimeoutSeconds). For each landblock the shader is rendered with an orthographic
    camera into an off-screen 1000x1000 SubViewport in two debug views (raw merged colour and lit colour, nearest
    sampling at mip 0, no fog) and every comparable pixel is checked against Lifestone.Data's TerrainCompositor
    (docs/terrain-renderer.md, "Golden test").

    Writes terrain-golden-<block>-{raw,lit}-{gpu,cpu,diff}.png and terrain-golden-report.txt to -OutDir and exits with
    0 (pass), 2 (outside tolerance) or another non-zero code (error / timeout).

    Nothing outside the repository is written; no environment, registry or system setting is changed.

.PARAMETER Blocks
    Comma-separated landblock ids (hex). Default: Holtburg A9B4 (roads), the desert south-east of Yaraq 7E62, snowy
    mountains 7AC2, and 1FB1 + 1EB1 (40.1N 76.8W / 77.6W: adjacent blocks that between them have a three-overlay cell,
    all-road cells and cells with two road layers).

.PARAMETER OutDir
    Output folder. Default: <repo>\docs\screenshots\m1 (gitignored). Relative paths are taken from the current
    PowerShell location; spaces are fine.

.PARAMETER Size
    Render size in pixels per landblock edge (default 1000, i.e. 0.192 m per pixel).

.PARAMETER TimeoutSeconds
    Kill Godot after this many seconds (default 300).

.PARAMETER NoBuild
    Skip the dotnet build that tools\godot.ps1 does first.

.EXAMPLE
    pwsh -File tools\terrain-golden.ps1
#>
[CmdletBinding()]
param(
    [string] $Blocks = 'A9B4,7E62,7AC2,1FB1,1EB1',
    [string] $OutDir = '',
    [ValidateRange(64, 4096)]
    [int] $Size = 1000,
    [ValidateRange(10, 3600)]
    [int] $TimeoutSeconds = 300,
    [switch] $NoBuild,
    [ValidatePattern('^[a-z0-9]{0,8}$')]
    [string] $UserDataSlot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'docs\screenshots\m1' }
$OutDir = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if ($Blocks -notmatch '^[0-9A-Fa-fxX,]+$') { throw "Blocks must be comma-separated hex landblock ids, got '$Blocks'." }

# tools\godot.ps1 splits -GameArgs on whitespace, so the folder travels percent-encoded ('%' first, then spaces);
# TerrainPreviewOptions decodes it (Uri.UnescapeDataString).
$encodedOutDir = ($OutDir -replace '\\', '/') -replace '%', '%25' -replace ' ', '%20'
$gameArgs = "--golden=$encodedOutDir --golden-blocks=$Blocks --golden-size=$Size"
$godotScript = Join-Path $PSScriptRoot 'godot.ps1'
$arguments = @('-NoProfile', '-File', $godotScript, '-Run', '-Offscreen', '-IsolateUserData',
    '-TimeoutSeconds', "$TimeoutSeconds", '-GodotArgs', 'res://scenes/TerrainPreview.tscn', "-GameArgs:$gameArgs")
if ($NoBuild) { $arguments += '-NoBuild' }
if ($UserDataSlot) { $arguments += @('-UserDataSlot', $UserDataSlot) }

$pwshExe = (Get-Process -Id $PID).Path
& $pwshExe @arguments
$code = $LASTEXITCODE

$report = Join-Path $OutDir 'terrain-golden-report.txt'
if (Test-Path -LiteralPath $report) {
    Write-Host ''
    Get-Content -LiteralPath $report | Write-Host
}

switch ($code) {
    0 { Write-Host "[terrain-golden] PASS" }
    2 { Write-Warning "[terrain-golden] FAIL: outside tolerance (see $report and the *-diff.png images)" }
    default { Write-Warning "[terrain-golden] error: Godot exited with code $code" }
}
exit $code
