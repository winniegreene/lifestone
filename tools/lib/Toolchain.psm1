<#
    Finds what the scripts in tools\ launch or read: the .NET SDK, the Godot 4.7.2 .NET editor, the Lifestone checkouts
    around a repository root, and the local backend's runtime folder.

    Read-only: nothing here changes the environment, PATH, the registry or any file, and no git command runs. A location
    given explicitly (a parameter or LIFESTONE_* variable) must exist; it never falls through silently to another one.
    When nothing is found, the error lists every place that was tried.
#>
Set-StrictMode -Version Latest

$script:GodotFileName = 'Godot_v4.7.2-stable_mono_win64.exe'
$script:GodotFolderName = 'Godot_v4.7.2-stable_mono_win64'

function Get-LifestoneCheckouts {
    <#
    .SYNOPSIS
        The repository root and every enclosing folder that is also a Lifestone checkout (holds Lifestone.slnx), innermost
        first: a git worktree created inside a checkout shares the enclosing checkout's tools, as it shares its dats.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $dir = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($RepoRoot))
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Lifestone.slnx') -PathType Leaf) { $dir.FullName }
        $dir = $dir.Parent
    }
}

function Get-LifestoneMainCheckout {
    <#
      The main worktree of the git repository at $RepoRoot: the root itself when its '.git' is a folder; for a linked
      worktree, read from its '.git' file and the 'commondir' file that points to (no git command runs). $null when
      neither tells (no '.git', a bare repository or a separate git folder).
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $root = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/')
    $dotGit = Join-Path $root '.git'
    if (Test-Path -LiteralPath $dotGit -PathType Container) { return $root }
    if (-not (Test-Path -LiteralPath $dotGit -PathType Leaf)) { return $null }
    # A linked worktree's '.git' is one line, 'gitdir: <main>/.git/worktrees/<name>' (absolute, or relative to the root).
    $link = Get-Content -LiteralPath $dotGit -TotalCount 1
    if ($link -notmatch '^gitdir:\s*(.+?)\s*$') { return $null }
    $gitDir = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $Matches[1]))
    $commonFile = Join-Path $gitDir 'commondir'
    if (-not (Test-Path -LiteralPath $commonFile -PathType Leaf)) { return $null }
    $common = [IO.Path]::GetFullPath([IO.Path]::Combine($gitDir, (Get-Content -LiteralPath $commonFile -TotalCount 1).Trim()))
    $common = $common.TrimEnd('\', '/')
    if ([IO.Path]::GetFileName($common) -ne '.git') { return $null }
    return [IO.Path]::GetDirectoryName($common)
}

function Find-LifestoneServerRuntime {
    <#
    .SYNOPSIS
        The local backend's runtime folder (server\runtime: the binaries, databases, logs, pids.json and secrets.json that
        server\setup-server.ps1 creates) of the main checkout. The backend runs only from the main checkout, so every git
        worktree of it, nested or not, gets that one folder.
    .DESCRIPTION
        Tried in order, the first with a server\runtime folder wins: the git repository's main worktree (read from the
        '.git' file, no git command runs), then every enclosing Lifestone checkout from the outermost in, the repository
        root itself last. Returns the full path. When none has one (the backend was never set up) it throws, listing what
        was tried, or with -AllowMissing returns $null.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [switch] $AllowMissing
    )
    $checkouts = [Collections.Generic.List[string]]::new()
    $main = Get-LifestoneMainCheckout -RepoRoot $RepoRoot
    if ($main) { $checkouts.Add($main) }
    $enclosing = @(Get-LifestoneCheckouts -RepoRoot $RepoRoot)
    [array]::Reverse($enclosing)
    foreach ($checkout in @($enclosing) + @([IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/'))) {
        if (-not ($checkouts | Where-Object { $_ -eq $checkout })) { $checkouts.Add($checkout) }
    }

    $tried = [Collections.Generic.List[string]]::new()
    foreach ($checkout in $checkouts) {
        $runtime = Join-Path $checkout 'server\runtime'
        if (Test-Path -LiteralPath $runtime -PathType Container) { return $runtime }
        $tried.Add($runtime)
    }
    if ($AllowMissing) { return $null }
    throw ("The local backend's runtime folder (server\runtime) was not found. Tried:`n  " + ($tried -join "`n  ") +
        "`nSet the backend up once from the main checkout: pwsh -NoProfile -File server\setup-server.ps1 (server\README.md).")
}

function Find-LifestoneDotnetRoot {
    <#
    .SYNOPSIS
        The .NET SDK root (the folder holding dotnet.exe): -DotnetRoot, else $env:LIFESTONE_DOTNET_ROOT, else the per-user
        install %LOCALAPPDATA%\Microsoft\dotnet, else $env:DOTNET_ROOT, else the folder of dotnet.exe on PATH.
    #>
    param([string] $DotnetRoot = '')
    foreach ($explicit in @(@('-DotnetRoot', $DotnetRoot), @('LIFESTONE_DOTNET_ROOT', $env:LIFESTONE_DOTNET_ROOT))) {
        if (-not $explicit[1]) { continue }
        if (Test-Path -LiteralPath (Join-Path $explicit[1] 'dotnet.exe') -PathType Leaf) { return [IO.Path]::GetFullPath($explicit[1]) }
        throw "$($explicit[0]) is '$($explicit[1])', but it holds no dotnet.exe."
    }

    $tried = [Collections.Generic.List[string]]::new()
    $candidates = @()
    if ($env:LOCALAPPDATA) { $candidates += , @('per-user install', (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet')) }
    if ($env:DOTNET_ROOT) { $candidates += , @('DOTNET_ROOT', $env:DOTNET_ROOT) }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate[1] 'dotnet.exe') -PathType Leaf) { return [IO.Path]::GetFullPath($candidate[1]) }
        $tried.Add("$($candidate[0]): $($candidate[1])")
    }

    $onPath = Get-Command -Name 'dotnet.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return (Split-Path -Parent $onPath.Source) }
    $tried.Add('dotnet.exe on PATH')

    throw ("The .NET SDK (dotnet.exe) was not found. Tried:`n  -DotnetRoot and LIFESTONE_DOTNET_ROOT (not set)`n  " +
        ($tried -join "`n  ") + "`nInstall the .NET 10 SDK (global.json), a per-user install is enough, or pass -DotnetRoot <folder>.")
}

function Find-LifestoneGodot {
    <#
    .SYNOPSIS
        The Godot 4.7.2 .NET editor executable: -GodotExe, else $env:LIFESTONE_GODOT, else Godot_v4.7.2-stable_mono_win64.exe
        or godot.exe on PATH, else <checkout>\..\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
        (a Tools folder next to the checkout, or next to the enclosing checkout of a worktree).
    #>
    param(
        [string] $GodotExe = '',
        [Parameter(Mandatory)] [string] $RepoRoot
    )
    foreach ($explicit in @(@('-GodotExe', $GodotExe), @('LIFESTONE_GODOT', $env:LIFESTONE_GODOT))) {
        if (-not $explicit[1]) { continue }
        if (Test-Path -LiteralPath $explicit[1] -PathType Leaf) { return [IO.Path]::GetFullPath($explicit[1]) }
        throw "$($explicit[0]) is '$($explicit[1])', but that file does not exist."
    }

    $tried = [Collections.Generic.List[string]]::new()
    foreach ($name in @($script:GodotFileName, 'godot.exe')) {
        $onPath = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue |
            Where-Object { $_.Source -like '*.exe' } | Select-Object -First 1
        if (-not $onPath) { $tried.Add("$name on PATH"); continue }
        # Only the .NET build of 4.7.2 runs the C# project (its version resource reads 4.7.2.stable.mono.official).
        $version = (Get-Item -LiteralPath $onPath.Source).VersionInfo.ProductVersion
        if ($version -like '4.7.2.*mono*') { return $onPath.Source }
        $tried.Add("$name on PATH: $($onPath.Source) is version '$version', not 4.7.2 .NET (mono)")
    }

    $repo = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    foreach ($checkout in @(@($repo) + @(Get-LifestoneCheckouts -RepoRoot $repo) | Select-Object -Unique)) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $checkout "..\Tools\$script:GodotFolderName\$script:GodotFileName"))
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        $tried.Add($candidate)
    }

    throw ("Godot 4.7.2 .NET ($script:GodotFileName) was not found. Tried:`n  -GodotExe and LIFESTONE_GODOT (not set)`n  " +
        ($tried -join "`n  ") +
        "`nGet Godot_v4.7.2-stable_mono_win64.zip from the official godotengine/godot GitHub release, then pass -GodotExe <path>, " +
        "set LIFESTONE_GODOT, or unzip it into a Tools folder next to this checkout.")
}

function Split-LifestoneArguments {
    <#
    .SYNOPSIS
        Splits an argument string (godot.ps1's -GodotArgs and -GameArgs) into arguments: on whitespace, except inside double quotes.
    .DESCRIPTION
        --character="Lifestone Smoke" is one argument, --character=Lifestone Smoke (the quotes are dropped). A string with no quotes
        splits exactly as before. An odd number of double quotes is an error, never a silently merged or dropped argument.
    #>
    param([string] $Text)
    if (-not $Text) { return @() }
    if (([regex]::Matches($Text, '"')).Count % 2 -ne 0) { throw 'the argument string has an unbalanced double quote' }
    $arguments = @()
    foreach ($match in [regex]::Matches($Text, '(?:[^\s"]+|"[^"]*")+')) { $arguments += $match.Value.Replace('"', '') }
    return $arguments
}

Export-ModuleMember -Function Split-LifestoneArguments, Find-LifestoneDotnetRoot, Find-LifestoneGodot, Find-LifestoneServerRuntime, Get-LifestoneCheckouts
