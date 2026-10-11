<#
.SYNOPSIS
    Opens the Lifestone Godot project in the Godot 4.7.2 .NET editor, or runs the game.

.DESCRIPTION
    Builds src/Lifestone.Game with the per-user .NET SDK and launches Godot on it.

    Environment changes (DOTNET_ROOT, PATH, opt-outs, ...) are applied ONLY to the child processes this script
    starts (dotnet, Godot). Nothing is written to the user or machine environment, the registry, or this
    PowerShell session.

    Godot runs in self-contained mode when a file named '._sc_' sits next to the Godot executable (editor settings
    and caches then live in <godot dir>\editor_data instead of %APPDATA%\Godot).

.PARAMETER Run
    Run the game (main scene) instead of opening the editor. Waits for Godot to exit and returns its exit code.

.PARAMETER Wait
    Editor mode: wait for Godot to exit and return its exit code (implied by -Run). Useful with
    -GodotArgs "--headless --build-solutions --quit" to build the C# solution the way the editor does.

.PARAMETER NoBuild
    Skip 'dotnet build' of the Godot project before launching.

.PARAMETER Console
    Use the *_console.exe wrapper so Godot's log is printed in this terminal (default for -Run).

.PARAMETER Driver
    Rendering driver override: d3d12 (Godot 4.6+ default on Windows), vulkan or opengl3.

.PARAMETER Resolution
    Window size for -Run, e.g. 1280x720.

.PARAMETER DatDir
    Folder holding the retail dats (client_portal.dat, client_cell_1.dat). Passed as LIFESTONE_DAT_DIR.
    Default: the game uses LIFESTONE_DAT_DIR if set, else <repo>\client (the checkout's client folder; a git worktree
    inside a checkout uses the enclosing checkout's).

.PARAMETER IsolateUserData
    Point APPDATA/LOCALAPPDATA of the Godot process at <repo>\cache\godot\ so user:// data, logs and shader caches
    stay inside the repo (gitignored). Useful for automated runs.

.PARAMETER Offscreen
    With -Run: open the game window far off-screen (--position -20000,-20000) so automated runs (screenshots,
    fly-path measurements) render normally without popping up on the user's desktop. Use it for every automated run.

.PARAMETER TimeoutSeconds
    With -Run: kill Godot (whole process tree) if it has not exited after this many seconds. 0 = no timeout.

.PARAMETER WorkstationGC
    Run Godot with .NET's default workstation garbage collector. By default this script gives the Godot process (only
    it) DOTNET_gcServer=1: server GC with dynamic heap counts collects in parallel and cut the GC pauses by a third to a half in
    the performance measurements (docs/game-core.md section 7). The Godot editor binary ignores the project's runtimeconfig, so an
    environment variable is the only way to select it for editor and -Run launches.

.PARAMETER GodotExe
    Path to the Godot 4.7.2 .NET editor executable. Default, the first that exists (tools\lib\Toolchain.psm1):
      1. $env:LIFESTONE_GODOT;
      2. Godot_v4.7.2-stable_mono_win64.exe, then godot.exe, on PATH (only a 4.7.2 .NET build is accepted);
      3. <repo>\..\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe, i.e. a Tools folder next
         to the checkout (for a git worktree inside a checkout, next to the enclosing checkout too).
    A path given with -GodotExe or LIFESTONE_GODOT must exist. When nothing is found the error lists what was tried.

.PARAMETER DotnetRoot
    The .NET SDK root (the folder holding dotnet.exe). Default, the first that exists: $env:LIFESTONE_DOTNET_ROOT, the
    per-user install %LOCALAPPDATA%\Microsoft\dotnet, $env:DOTNET_ROOT, then the folder of dotnet.exe on PATH.

.PARAMETER GodotArgs
    Extra engine arguments, space separated (e.g. "--verbose").

.PARAMETER Screenshot
    Implies -Run. The main scene waits until the place is complete (streaming, objects, the interiors' cells), renders
    -Frames frames and at least 2 s (for the living statics), saves the viewport as PNG to this path and quits.
    Relative paths are resolved from this PowerShell location.

.PARAMETER Frames
    Frames to render before the screenshot (default 30).

.PARAMETER GameArgs
    Extra game arguments, space separated; passed after '--' (read in C# with OS.GetCmdlineUserArgs()).
    Use a single string; with 'pwsh -File' bind it with a colon so the leading dashes are not read as a
    parameter name, e.g. -GameArgs:'--dat-dir=D:/AC'. (Bare '-- --x' style arguments do not work with
    'pwsh -File', hence the explicit -Screenshot/-Frames/-DatDir parameters.)
    -GameArgs:'--help' prints every option the game knows (Lifestone.Client/Options/OptionCatalog.cs) and quits.
    Relative paths in game arguments (--screenshot, --golden-viewpoints, --interior-tour, ...) are taken from the
    repository root (the game makes it the working directory at start-up; Godot itself starts in src\Lifestone.Game).

.EXAMPLE
    pwsh -File tools\godot.ps1
    Builds and opens the editor.

.EXAMPLE
    pwsh -File tools\godot.ps1 -Screenshot docs\screenshots\m1\holtburg.png -Offscreen -IsolateUserData -TimeoutSeconds 120
    Builds, runs the main scene at Holtburg off-screen, saves a screenshot once streaming is idle and exits.

.EXAMPLE
    pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -NoBuild -TimeoutSeconds 60 -GameArgs:'--help'
    Prints every command-line option of every scene.

.EXAMPLE
    pwsh -File tools\smoke.ps1
    The M1 integration suite (build, tests, every Godot check harness) with a summary table.
#>
[CmdletBinding()]
param(
    [switch] $Run,
    [switch] $Wait,
    [switch] $NoBuild,
    [switch] $Console,
    [ValidateSet('', 'd3d12', 'vulkan', 'opengl3')]
    [string] $Driver = '',
    [ValidatePattern('^(\d+x\d+)?$')]
    [string] $Resolution = '',
    [string] $DatDir = '',
    [switch] $IsolateUserData,
    [switch] $Offscreen,
    [int] $TimeoutSeconds = 0,
    [switch] $WorkstationGC,
    [string] $GodotExe = '',
    [string] $DotnetRoot = '',
    [string] $GodotArgs = '',
    [string] $Screenshot = '',
    [ValidateRange(1, 100000)]
    [int] $Frames = 30,
    [string] $GameArgs = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src\Lifestone.Game'
$projectFile = Join-Path $projectDir 'Lifestone.Game.csproj'

Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
$GodotExe = Find-LifestoneGodot -GodotExe $GodotExe -RepoRoot $repoRoot
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot
$dotnetExe = Join-Path $DotnetRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $projectFile)) { throw "Not found: $projectFile" }

if ($Screenshot) { $Run = [switch]$true }
$useConsole = $Console -or $Run
$launchExe = $GodotExe
if ($useConsole) {
    $consoleExe = [IO.Path]::Combine([IO.Path]::GetDirectoryName($GodotExe),
        [IO.Path]::GetFileNameWithoutExtension($GodotExe) + '_console.exe')
    if (Test-Path -LiteralPath $consoleExe) { $launchExe = $consoleExe }
}

# Environment for child processes only.
$childEnv = [ordered]@{
    DOTNET_ROOT                        = $DotnetRoot
    PATH                               = "$DotnetRoot;$env:PATH"
    DOTNET_CLI_TELEMETRY_OPTOUT        = '1'
    DOTNET_NOLOGO                      = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE  = '1'
    # Never let the SDK edit the user PATH or create/trust an HTTPS dev certificate.
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH    = 'false'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    # No MSBuild nodes / compiler servers left running after builds.
    MSBUILDDISABLENODEREUSE            = '1'
    DOTNET_CLI_USE_MSBUILD_SERVER      = '0'
    UseSharedCompilation               = 'false'
}
if ($DatDir) { $childEnv['LIFESTONE_DAT_DIR'] = (Resolve-Path -LiteralPath $DatDir).Path }

function Start-Child {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [System.Collections.IDictionary] $Environment,
        [string] $WorkingDirectory
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false   # CreateProcess: no shell verbs, no elevation, inherits this console
    $psi.WorkingDirectory = $WorkingDirectory
    foreach ($key in $Environment.Keys) { $psi.Environment[$key] = [string]$Environment[$key] }
    return [System.Diagnostics.Process]::Start($psi)
}

if (-not $NoBuild) {
    Write-Host "[godot.ps1] dotnet build $projectFile"
    $build = Start-Child -FileName $dotnetExe -Environment $childEnv -WorkingDirectory $repoRoot `
        -Arguments @('build', $projectFile, '-c', 'Debug', '--disable-build-servers', '-nologo', '-v', 'minimal')
    $build.WaitForExit()
    if ($build.ExitCode -ne 0) { throw "dotnet build failed with exit code $($build.ExitCode)" }
}

$godotEnv = [ordered]@{}
foreach ($key in $childEnv.Keys) { $godotEnv[$key] = $childEnv[$key] }
# Performance: server GC for the Godot process only (see -WorkstationGC). Set either way, so a value in the
# calling shell cannot change a measurement silently.
$godotEnv['DOTNET_gcServer'] = if ($WorkstationGC) { '0' } else { '1' }
if ($IsolateUserData) {
    $roaming = Join-Path $repoRoot 'cache\godot\roaming'
    $local = Join-Path $repoRoot 'cache\godot\local'
    New-Item -ItemType Directory -Force -Path $roaming, $local | Out-Null
    $godotEnv['APPDATA'] = $roaming
    $godotEnv['LOCALAPPDATA'] = $local
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.Add('--path'); $arguments.Add($projectDir)
if (-not $Run) { $arguments.Add('--editor') }
if ($Driver) { $arguments.Add('--rendering-driver'); $arguments.Add($Driver) }
if ($Resolution) { $arguments.Add('--resolution'); $arguments.Add($Resolution) }
# A window that is merely off-screen (not minimised) is still drawn by Godot and DWM, so captures stay valid.
if ($Offscreen -and $Run) { $arguments.Add('--position'); $arguments.Add('-20000,-20000') }
if ($GodotArgs) { foreach ($a in ($GodotArgs -split '\s+' | Where-Object { $_ })) { $arguments.Add($a) } }
$userArgList = [System.Collections.Generic.List[string]]::new()
if ($Screenshot) {
    # Resolve relative to this PowerShell location; Godot prefers forward slashes.
    $screenshotPath = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Screenshot) -replace '\\', '/'
    $userArgList.Add("--screenshot=$screenshotPath")
    $userArgList.Add("--frames=$Frames")
}
if ($GameArgs) { foreach ($a in ($GameArgs -split '\s+' | Where-Object { $_ })) { $userArgList.Add($a) } }
if ($userArgList.Count -gt 0) {
    $arguments.Add('--')
    foreach ($a in $userArgList) { $arguments.Add($a) }
}

Write-Host "[godot.ps1] $launchExe $($arguments -join ' ')"
$godot = Start-Child -FileName $launchExe -Arguments $arguments -Environment $godotEnv -WorkingDirectory $projectDir
Write-Host "[godot.ps1] started Godot (pid $($godot.Id))"

if (-not ($Run -or $Wait)) { return }

if ($TimeoutSeconds -gt 0) {
    if (-not $godot.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Warning "[godot.ps1] Godot did not exit within $TimeoutSeconds s; killing process tree $($godot.Id)"
        $godot.Kill($true)
        $godot.WaitForExit()
        exit 124
    }
}
$godot.WaitForExit()
Write-Host "[godot.ps1] Godot exited with code $($godot.ExitCode)"
exit $godot.ExitCode
