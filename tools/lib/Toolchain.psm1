<#
    Finds the toolchain the scripts in tools\ launch: the .NET SDK and the Godot 4.7.2 .NET editor.

    Read-only: nothing here changes the environment, PATH, the registry or any file. A location given explicitly (a
    parameter or LIFESTONE_* variable) must exist; it never falls through silently to another one. When nothing is
    found, the error lists every place that was tried.
#>
Set-StrictMode -Version Latest

$script:GodotFileName = 'Godot_v4.7.2-stable_mono_win64.exe'
$script:GodotFolderName = 'Godot_v4.7.2-stable_mono_win64'

function Get-LifestoneCheckouts {
    <# The repository root and every enclosing folder that is also a Lifestone checkout (holds Lifestone.slnx): a git
       worktree created inside a checkout shares the enclosing checkout's tools, as it shares its dats. #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $dir = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($RepoRoot))
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Lifestone.slnx') -PathType Leaf) { $dir.FullName }
        $dir = $dir.Parent
    }
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

Export-ModuleMember -Function Find-LifestoneDotnetRoot, Find-LifestoneGodot
