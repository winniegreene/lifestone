<#
.SYNOPSIS
    Renders the Lifestone hero video from a shot list: records every shot off-screen with a deterministic, frame-stepped
    camera, then edits it with ffmpeg (title and end cards, lower-third captions, crossfades) into an MP4.

.DESCRIPTION
    One command, in order (docs/hero-video.md):

      1. build      tools\godot.ps1 builds the game (Debug) once; every later run passes -NoBuild.
      2. record     one Godot run per shot, through tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds,
                    with the engine stepped at a fixed rate (--fixed-fps) and the shot's own arguments: world shots in
                    the main scene, spell shots in scenes/SpellViewer.tscn. The cinematic director (--cinematic) waits
                    under a black cover until the place is complete and the sky is built, runs the pre-roll, then
                    hands exactly the shot's frames to --cinematic-capture (raw RGBA at the window's size), so no
                    loading frame reaches the film and nothing needs trimming. A failed shot is retried once.
                    A clip shot (scene "clip", the M4 hero video's live takes) records nothing: it names a captured take
                    under cache\ (source) and the second it starts at (from), and its duration's frames are encoded.
      3. segments   each shot's raw frames are encoded to a near-lossless RGB intermediate and the raw file deleted.
      4. edit       ffmpeg: per segment the lower-third caption (drawtext over a soft dark halo, faded in and out)
                    and, over the title shot, the title card; a generated end card; crossfades (xfade) between all of
                    them; one encode to H.264 High, yuv420p (BT.709), CRF 18, +faststart, no audio.
      5. extras     a 720p version under 25 MB (two-pass), a poster (a clean frame of the list's poster shot with the
                    title) and a contact sheet; with -QaSheets also frames every 0.5 s in contact sheets for review.

    With -MovieMaker the shots are recorded by Godot's own Movie Maker instead (--write-movie, PNG frames): it writes
    every frame of the run at the project's viewport size (1280x720 in Godot 4.7, whatever -Resolution says), so each
    shot is trimmed to the frames its "shot recorded first_frame= last_frame=" log line names. Slower (about 230 ms a
    frame), 720p only; kept as the engine's reference path.

    Outputs (gitignored): <OutDir>\lifestone-hero.mp4, lifestone-hero-720p.mp4, lifestone-hero-poster.png,
    lifestone-hero-contact.png, and <WorkDir>\ (logs, segments, the edit's filter graph, QA sheets).

    Environment changes (DOTNET_ROOT, PATH, opt-outs) apply only to the processes tools\godot.ps1 starts. ffmpeg and
    ffprobe come from PATH and only read and write files below the work and output folders (and read the font). Nothing
    is downloaded; no system setting is changed.

.PARAMETER ShotList
    The shot list (JSON). Default: src\Lifestone.Game\cinematics\hero.json.

.PARAMETER OutDir
    Where the deliverables go. Default: docs\screenshots\hero. Must be inside docs\screenshots\ or cache\ (gitignored).

.PARAMETER WorkDir
    Logs, intermediate segments and the edit. Default: cache\hero. Must be inside cache\ (gitignored).

.PARAMETER Only
    Record only these shots (ids, comma-separated); the others reuse their existing segments. The edit always uses every
    shot of the list, so missing segments are an error.

.PARAMETER SkipRecord
    Do not record anything: re-edit the existing segments (captions, cards, crossfades, encodes).

.PARAMETER Stills
    Instead of the film, render framing stills of the world shots (--cinematic-stills) into <WorkDir>\stills and a contact
    sheet per three shots; nothing is encoded.

.PARAMETER Resolution
    Frame size, e.g. 1280x720 for a quick draft. Default: the shot list's width and height. Caption and card sizes scale.

.PARAMETER Fps
    Frame rate. Default: the shot list's fps.

.PARAMETER Crf
    CRF of the main encode (default 18).

.PARAMETER ShareMaxMB
    Size limit of the 720p version in MB of 1,000,000 bytes (default 25).

.PARAMETER TimeoutSeconds
    Per Godot run (default 420).

.PARAMETER QaSheets
    Also write frames every 0.5 s of the final video as contact sheets into <WorkDir>\qa.

.PARAMETER KeepRaw
    Keep the raw captured frames after encoding a segment (several GB per shot).

.PARAMETER MovieMaker
    Record with Godot's Movie Maker (--write-movie) and trim by the logged frame ranges, instead of --cinematic-capture.

.PARAMETER NoBuild
    Use the existing build.

.EXAMPLE
    pwsh -File tools\hero-video.ps1
    Builds, records all shots at 1920x1080 / 60 fps and writes docs\screenshots\hero\lifestone-hero.mp4 and friends.

.EXAMPLE
    pwsh -File tools\hero-video.ps1 -NoBuild -Only c-holtburg-street,k-holtburg-redoubt -QaSheets
    Re-records two shots, re-edits the film and writes review sheets.

.EXAMPLE
    pwsh -File tools\hero-video.ps1 -Stills -NoBuild
    Framing stills of every world shot (start, quarter, middle, three quarters, end).
#>
[CmdletBinding()]
param(
    [string] $ShotList = '',
    [string] $OutDir = '',
    [string] $WorkDir = '',
    [string[]] $Only = @(),
    [switch] $SkipRecord,
    [switch] $Stills,
    [ValidatePattern('^(\d+x\d+)?$')]
    [string] $Resolution = '',
    [ValidateRange(0, 240)]
    [int] $Fps = 0,
    [ValidateRange(0, 51)]
    [int] $Crf = 18,
    [ValidateRange(1, 1000)]
    [double] $ShareMaxMB = 25,
    [ValidateRange(30, 7200)]
    [int] $TimeoutSeconds = 420,
    [switch] $QaSheets,
    [switch] $KeepRaw,
    [switch] $MovieMaker,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$godotScript = Join-Path $PSScriptRoot 'godot.ps1'
$pwshExe = (Get-Process -Id $PID).Path
if (-not $ShotList) { $ShotList = Join-Path $repoRoot 'src\Lifestone.Game\cinematics\hero.json' }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'docs\screenshots\hero' }
if (-not $WorkDir) { $WorkDir = Join-Path $repoRoot 'cache\hero' }
$ShotList = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($ShotList)
$OutDir = [IO.Path]::GetFullPath($PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutDir))
$WorkDir = [IO.Path]::GetFullPath($PSCmdlet.GetUnresolvedProviderPathFromPSPath($WorkDir))
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

function Test-Inside([string] $path, [string[]] $roots) {
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\') + '\'
    foreach ($root in $roots) {
        $r = [IO.Path]::GetFullPath((Join-Path $repoRoot $root)).TrimEnd('\') + '\'
        if ($full.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# Renders of the user's retail data stay in gitignored folders.
if (-not (Test-Inside $OutDir @('docs\screenshots', 'cache'))) { throw "OutDir must be inside docs\screenshots\ or cache\ (gitignored): $OutDir" }
if (-not (Test-Inside $WorkDir @('cache'))) { throw "WorkDir must be inside cache\ (gitignored): $WorkDir" }
if (-not (Test-Path -LiteralPath $ShotList)) { throw "Shot list not found: $ShotList" }
foreach ($tool in 'ffmpeg', 'ffprobe') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool not found on PATH (the user's own ffmpeg encodes the renders)" }
}

# Godot splits its argument strings on whitespace (tools\godot.ps1), so the paths it gets are repository-relative.
function Get-RepoRelative([string] $path) {
    $relative = [IO.Path]::GetRelativePath($repoRoot, $path) -replace '\\', '/'
    if ($relative -match '\s' -or $relative.StartsWith('..')) { throw "Path must lie inside the repository and contain no spaces: $path" }
    return $relative
}

# ---------------------------------------------------------------------------------------------------------- shot list
function Get-Prop($object, [string] $name, $default = $null) {
    if ($null -ne $object -and $object.PSObject.Properties.Name -contains $name -and $null -ne $object.$name) { return $object.$name }
    return $default
}

$list = Get-Content -LiteralPath $ShotList -Raw | ConvertFrom-Json
$listFps = [int](Get-Prop $list 'fps' 60)
$width = [int](Get-Prop $list 'width' 1920)
$height = [int](Get-Prop $list 'height' 1080)
if ($Resolution) { $width, $height = $Resolution -split 'x' | ForEach-Object { [int]$_ } }
if ($Fps -le 0) { $Fps = $listFps }
$scale = $height / 1080.0
$crossfade = [double](Get-Prop $list 'crossfade' 0.7)
$listArgs = @(Get-Prop $list 'args' @())
$shots = @($list.shots)
foreach ($id in $Only) {
    if (-not ($shots | Where-Object { $_.id -eq $id })) { throw "No shot '$id' in $ShotList" }
}

$fontPath = [string](Get-Prop $list 'font' 'C:/Windows/Fonts/bahnschrift.ttf')
if (-not (Test-Path -LiteralPath $fontPath)) {
    $fontPath = @('C:/Windows/Fonts/segoeui.ttf', 'C:/Windows/Fonts/arial.ttf') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $fontPath) { throw 'No title font found (bahnschrift.ttf, segoeui.ttf or arial.ttf in C:\Windows\Fonts)' }
}

$logDir = Join-Path $WorkDir 'logs'
$rawDir = Join-Path $WorkDir 'raw'
$segDir = Join-Path $WorkDir 'segments'
$textDir = Join-Path $WorkDir 'text'
New-Item -ItemType Directory -Force -Path $OutDir, $WorkDir, $logDir, $rawDir, $segDir, $textDir | Out-Null
$listRel = Get-RepoRelative $ShotList
$rawRel = Get-RepoRelative $rawDir
$summary = [System.Collections.Generic.List[object]]::new()
$started = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Logged {
    <# Runs one child process with stdout and stderr captured into $LogPath; returns the exit code (124 on timeout). #>
    param([string] $FileName, [string[]] $Arguments, [string] $LogPath, [int] $Timeout)
    $psi = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $repoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    "> $FileName $($Arguments -join ' ')" | Set-Content -LiteralPath $LogPath -Encoding utf8
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($Timeout * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        Add-Content -LiteralPath $LogPath -Value $stdout.Result, $stderr.Result, "[hero-video] killed after $Timeout s" -Encoding utf8
        return 124
    }
    $process.WaitForExit()
    Add-Content -LiteralPath $LogPath -Value $stdout.Result, $stderr.Result -Encoding utf8
    return $process.ExitCode
}

function Invoke-FFmpeg {
    <# ffmpeg with its output in $LogPath; throws on failure. #>
    param([string[]] $Arguments, [string] $LogPath)
    $code = Invoke-Logged -FileName 'ffmpeg' -Arguments (@('-hide_banner', '-nostdin', '-y') + $Arguments) -LogPath $LogPath -Timeout 7200
    if ($code -ne 0) { throw "ffmpeg failed ($code); see $LogPath" }
}

function Get-Duration([string] $file) {
    $d = & ffprobe -v error -show_entries format=duration -of csv=p=0 $file
    return [double]::Parse(($d | Select-Object -First 1), [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ClipArguments {
    <#
    ffmpeg's arguments for a clip shot (scene "clip": a span of a captured take, the M4 hero video's live screen captures): $Frames frames of $Source from
    $From seconds, at $Fps and $Width x $Height, encoded as the recorded shots' near-lossless RGB segment. The seek is before the input, which ffmpeg makes
    frame-exact when it re-encodes.
    #>
    param([string] $Source, [double] $From, [int] $Frames, [int] $Fps, [int] $Width, [int] $Height, [string] $Segment)
    if ($From -lt 0) { throw "a clip's 'from' is a time in its source, 0 or more (got $From)" }
    if ($Frames -lt 1) { throw "a clip needs at least one frame (got $Frames)" }
    $at = $From.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
    return @('-ss', $at, '-i', $Source, '-frames:v', "$Frames", '-an', '-vf', "fps=$Fps,scale=${Width}:${Height}:flags=lanczos,format=rgb24",
        '-c:v', 'libx264rgb', '-preset', 'faster', '-crf', '8', $Segment)
}

$script:built = [bool]$NoBuild
function Invoke-Shot {
    <# One Godot run for one shot (or for stills). Returns @{ Code; Log; Text }. #>
    param($Shot, [string] $Suffix, [string[]] $ExtraGameArgs, [string[]] $ExtraEngineArgs = @())
    $isSpell = (Get-Prop $Shot 'scene' 'world') -eq 'spell'
    $log = Join-Path $logDir ("{0}{1}.log" -f $Shot.id, $Suffix)
    $game = @("--cinematic=$listRel", "--cinematic-shot=$($Shot.id)", '--vsync=off', '--max-fps=0') + $listArgs + @(Get-Prop $Shot 'args' @()) + $ExtraGameArgs
    $engine = @('--fixed-fps', "$Fps", '--disable-vsync') + $ExtraEngineArgs
    if ($isSpell) { $engine = @('res://scenes/SpellViewer.tscn') + $engine }
    $arguments = @('-NoProfile', '-File', $godotScript, '-Run', '-Offscreen', '-IsolateUserData', '-TimeoutSeconds', "$TimeoutSeconds",
        '-Resolution', "${width}x${height}", "-GodotArgs:$($engine -join ' ')", "-GameArgs:$($game -join ' ')")
    if ($script:built) { $arguments = @('-NoProfile', '-File', $godotScript, '-NoBuild') + $arguments[3..($arguments.Count - 1)] }
    $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath $log -Timeout ($TimeoutSeconds + 120)
    if ($code -eq 0) { $script:built = $true }
    return @{ Code = $code; Log = $log; Text = (Get-Content -LiteralPath $log -Raw) }
}

# ------------------------------------------------------------------------------------------------------------- stills
if ($Stills) {
    $stillsDir = Join-Path $WorkDir 'stills'
    if (Test-Path -LiteralPath $stillsDir) { Remove-Item -LiteralPath $stillsDir -Recurse -Force }
    $stillsRel = Get-RepoRelative $stillsDir
    foreach ($shot in $shots | Where-Object { (Get-Prop $_ 'scene' 'world') -eq 'world' -and ($Only.Count -eq 0 -or $Only -contains $_.id) }) {
        $run = Invoke-Shot -Shot $shot -Suffix '-stills' -ExtraGameArgs @("--cinematic-stills=$stillsRel", '--cinematic-stills-at=0,0.25,0.5,0.75,1')
        Write-Host ("[hero-video] stills {0,-24} exit {1}" -f $shot.id, $run.Code) -ForegroundColor ($(if ($run.Code -eq 0) { 'Green' } else { 'Red' }))
    }

    $files = Get-ChildItem -LiteralPath $stillsDir -Filter '*.png' -ErrorAction SilentlyContinue
    $sheetIndex = 1
    $ids = @($shots | ForEach-Object { $_.id } | Where-Object { $files.Name -match "^$([regex]::Escape($_))-" })
    for ($i = 0; $i -lt $ids.Count; $i += 3) {
        $rows = @()
        foreach ($id in $ids[$i..([Math]::Min($i + 2, $ids.Count - 1))]) {
            $pngs = @($files | Where-Object { $_.Name -match "^$([regex]::Escape($id))-\d" } | Sort-Object Name)
            $inputs = @(); $chain = @()
            for ($k = 0; $k -lt $pngs.Count; $k++) { $inputs += @('-i', $pngs[$k].FullName); $chain += "[$k]scale=384:216[s$k]" }
            $row = Join-Path $stillsDir "_row-$id.png"
            Invoke-FFmpeg -Arguments ($inputs + @('-filter_complex', (($chain -join ';') + ';' + ((0..($pngs.Count - 1) | ForEach-Object { "[s$_]" }) -join '') + "hstack=inputs=$($pngs.Count)"), '-frames:v', '1', $row)) -LogPath (Join-Path $logDir 'ffmpeg-stills.log')
            $rows += $row
        }
        $sheet = Join-Path $stillsDir ("sheet-{0}.png" -f $sheetIndex++)
        if ($rows.Count -eq 1) { Copy-Item -LiteralPath $rows[0] -Destination $sheet -Force }
        else {
            $inputs = @(); foreach ($r in $rows) { $inputs += @('-i', $r) }
            Invoke-FFmpeg -Arguments ($inputs + @('-filter_complex', ((0..($rows.Count - 1) | ForEach-Object { "[$_]" }) -join '') + "vstack=inputs=$($rows.Count)", '-frames:v', '1', $sheet)) -LogPath (Join-Path $logDir 'ffmpeg-stills.log')
        }
        Write-Host "[hero-video] $sheet"
    }
    Get-ChildItem -LiteralPath $stillsDir -Filter '_row-*' | Remove-Item -Force
    return
}

# ------------------------------------------------------------------------------------------------------------- record
foreach ($shot in $shots) {
    $id = $shot.id
    $segment = Join-Path $segDir "$id.mkv"
    $expected = [int][Math]::Max(1, [Math]::Round([double]$shot.duration * $Fps, [MidpointRounding]::AwayFromZero))
    $record = -not $SkipRecord -and ($Only.Count -eq 0 -or $Only -contains $id)
    if (-not $record) {
        if (-not (Test-Path -LiteralPath $segment)) { throw "No segment for '$id' ($segment); record it (drop -SkipRecord or add it to -Only)" }
        $summary.Add([pscustomobject]@{ Shot = $id; Frames = $expected; Seconds = [double]$shot.duration; Recorded = 'reused'; Wall = '' })
        continue
    }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    if ((Get-Prop $shot 'scene' 'world') -eq 'clip') {
        # A span of a captured take: trimmed and encoded, nothing is recorded. Takes are renders of the user's data, so they stay under cache\.
        $source = [IO.Path]::GetFullPath([IO.Path]::Combine($repoRoot, [string](Get-Prop $shot 'source' '')))
        if (-not (Test-Inside $source @('cache'))) { throw "Clip '$id': its source must be inside cache\ (gitignored): $source" }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Clip '$id': no source file $source" }
        $from = [double](Get-Prop $shot 'from' 0)
        Invoke-FFmpeg -LogPath (Join-Path $logDir "$id-clip.log") -Arguments (Get-ClipArguments -Source $source -From $from -Frames $expected -Fps $Fps -Width $width -Height $height -Segment $segment)
        $summary.Add([pscustomobject]@{ Shot = $id; Frames = $expected; Seconds = [double]$shot.duration; Recorded = ('clip {0:0.###} s' -f $from); Wall = ('{0:0}s' -f $watch.Elapsed.TotalSeconds) })
        Write-Host ("[hero-video] clipped  {0,-24} {1} frames from {2:0.###} s of {3}" -f $id, $expected, $from, (Split-Path -Leaf $source)) -ForegroundColor Green
        continue
    }

    $raw = Join-Path $rawDir "$id.rgba"
    $meta = Join-Path $rawDir "$id.json"
    $ok = $false
    if ($MovieMaker) {
        # Movie Maker writes every frame of the run; the shot's own are the ones its log line names.
        $movieDir = Join-Path $WorkDir "movie\$id"
        $moviePath = ($movieDir -replace '\\', '/') + '/f.png'
        if ($moviePath -match '\s') { throw "Movie Maker's path must contain no spaces: $moviePath" }
        for ($attempt = 1; $attempt -le 2 -and -not $ok; $attempt++) {
            if (Test-Path -LiteralPath $movieDir) { Remove-Item -LiteralPath $movieDir -Recurse -Force }
            New-Item -ItemType Directory -Force -Path $movieDir | Out-Null
            $run = Invoke-Shot -Shot $shot -Suffix $(if ($attempt -gt 1) { '-retry' } else { '' }) -ExtraGameArgs @() -ExtraEngineArgs @('--write-movie', $moviePath)
            $range = [regex]::Match($run.Text, "shot recorded id=$([regex]::Escape($id)) first_frame=(\d+) last_frame=(\d+) frames=(\d+)")
            $ok = $run.Code -eq 0 -and $range.Success -and [int]$range.Groups[3].Value -eq $expected
            if (-not $ok) { Write-Host "[hero-video] $id attempt $attempt failed (exit $($run.Code)); see $($run.Log)" -ForegroundColor Yellow }
        }
        if (-not $ok) { throw "Shot '$id' could not be recorded; see $logDir" }
        $first = [int]$range.Groups[1].Value
        Invoke-FFmpeg -LogPath (Join-Path $logDir "$id-encode.log") -Arguments @(
            '-framerate', "$Fps", '-start_number', "$first", '-i', (Join-Path $movieDir 'f%08d.png'), '-frames:v', "$expected",
            '-an', '-vf', 'format=rgb24', '-c:v', 'libx264rgb', '-preset', 'faster', '-crf', '8', $segment)
        $size = (& ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 $segment) -split ','
        if ([int]$size[0] -ne $width -or [int]$size[1] -ne $height) {
            Write-Host "[hero-video] Movie Maker recorded $($size -join 'x') (the project's viewport size); the edit uses it" -ForegroundColor Yellow
            $script:width = [int]$size[0]; $script:height = [int]$size[1]; $script:scale = $script:height / 1080.0
        }
        if (-not $KeepRaw) { Remove-Item -LiteralPath $movieDir -Recurse -Force }
        $summary.Add([pscustomobject]@{ Shot = $id; Frames = $expected; Seconds = [double]$shot.duration; Recorded = "movie maker $first-$($first + $expected - 1)"; Wall = ('{0:0}s' -f $watch.Elapsed.TotalSeconds) })
        Write-Host ("[hero-video] recorded {0,-24} frames {1}-{2} of the movie in {3:0} s" -f $id, $first, ($first + $expected - 1), $watch.Elapsed.TotalSeconds) -ForegroundColor Green
        continue
    }

    for ($attempt = 1; $attempt -le 2 -and -not $ok; $attempt++) {
        Remove-Item -LiteralPath $raw, $meta -ErrorAction SilentlyContinue
        $run = Invoke-Shot -Shot $shot -Suffix $(if ($attempt -gt 1) { "-retry" } else { '' }) -ExtraGameArgs @("--cinematic-capture=$rawRel")
        $info = if (Test-Path -LiteralPath $meta) { Get-Content -LiteralPath $meta -Raw | ConvertFrom-Json } else { $null }
        $ok = $run.Code -eq 0 -and $null -ne $info -and -not $info.failed -and [int]$info.frames -eq $expected -and [int]$info.width -eq $width -and [int]$info.height -eq $height
        if (-not $ok) {
            $why = if ($null -eq $info) { 'no frames' } else { "frames $($info.frames)/$expected, $($info.width)x$($info.height)" }
            Write-Host "[hero-video] $id attempt $attempt failed (exit $($run.Code), $why); see $($run.Log)" -ForegroundColor Yellow
        }
    }
    if (-not $ok) { throw "Shot '$id' could not be recorded; see $logDir" }

    # Near-lossless RGB intermediate (the edit converts to BT.709 YUV once, at the end).
    Invoke-FFmpeg -LogPath (Join-Path $logDir "$id-encode.log") -Arguments @(
        '-f', 'rawvideo', '-pix_fmt', 'rgba', '-s', "${width}x${height}", '-r', "$Fps", '-i', $raw,
        '-an', '-vf', 'format=rgb24', '-c:v', 'libx264rgb', '-preset', 'faster', '-crf', '8', $segment)
    if (-not $KeepRaw) { Remove-Item -LiteralPath $raw -Force }
    $summary.Add([pscustomobject]@{ Shot = $id; Frames = $expected; Seconds = [double]$shot.duration; Recorded = 'new'; Wall = ('{0:0}s' -f $watch.Elapsed.TotalSeconds) })
    Write-Host ("[hero-video] recorded {0,-24} {1} frames in {2:0} s" -f $id, $expected, $watch.Elapsed.TotalSeconds) -ForegroundColor Green
}

# A segment that opens on a black frame has a loading frame (the cover) in it: the trim is off.
foreach ($shot in $shots) {
    $segment = Join-Path $segDir "$($shot.id).mkv"
    $luma = & ffmpeg -hide_banner -nostdin -i $segment -vf 'trim=end_frame=1,format=gray,signalstats,metadata=print:key=lavfi.signalstats.YMAX:file=-' -f null - 2>$null |
        Select-String 'YMAX=(\d+)' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value } | Select-Object -First 1
    if ($null -ne $luma -and $luma -lt 8) { Write-Warning "[hero-video] $($shot.id) opens on a black frame (YMAX $luma): a loading frame may have been captured" }
}

# --------------------------------------------------------------------------------------------------------------- edit
function Write-Text([string] $name, [string] $text) {
    $path = Join-Path $textDir "$name.txt"
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    return $path
}

function Escape-FilterPath([string] $path) {
    # A path inside a filter argument: forward slashes, ':' and '\' escaped, single-quoted by the caller.
    return ($path -replace '\\', '/') -replace ':', '\:'
}

function Format-Number([double] $value) { return $value.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }

function Get-FilmFadeOut([double] $duration) {
    # The film's last second fading to black, when the list has no end card.
    return "fade=t=out:st=$(Format-Number ([Math]::Max(0.0, $duration - 1.0))):d=$(Format-Number ([Math]::Min(1.0, $duration)))"
}

function Get-Alpha([double] $in, [double] $out, [double] $fade) {
    # Opacity ramp: 0 before $in, fades up over $fade, holds, fades down to 0 at $out.
    $a = Format-Number $in; $b = Format-Number $out; $f = Format-Number $fade
    return "if(lt(t\,$a)\,0\,if(lt(t\,$a+$f)\,(t-$a)/$f\,if(lt(t\,$b-$f)\,1\,if(lt(t\,$b)\,($b-t)/$f\,0))))"
}

function Get-DrawText {
    param([string] $TextFile, [int] $Size, [string] $X, [string] $Y, [string] $Alpha, [string] $Color = 'white', [double] $Shadow = 0.55, [int] $ShadowOffset = 2)
    $font = Escape-FilterPath $fontPath
    $file = Escape-FilterPath $TextFile
    $s = [Math]::Max(1, [int][Math]::Round($ShadowOffset * $scale))
    return "drawtext=fontfile='$font':textfile='$file':expansion=none:fontsize=$([int][Math]::Round($Size * $scale)):fontcolor=$Color" +
        ":shadowcolor=black@$(Format-Number $Shadow):shadowx=${s}:shadowy=${s}:x=$X`:y=$Y`:alpha='$Alpha'"
}

function Get-HaloChains {
    <#
    A soft dark halo behind a caption, so white text reads over snow, sand and bright sky: the caption's glyphs, drawn
    fattened (border) into a grey mask with the caption's own position and fade and blurred, become the alpha of a black
    layer that is laid over the segment ($In) before the caption is drawn. Returns the chains; their output is [$Out].
    #>
    param([string] $In, [string] $Out, [string] $Tag, [double] $Duration, [string] $TextFile, [int] $Size, [string] $X, [string] $Y, [string] $Alpha)
    $font = Escape-FilterPath $fontPath
    $file = Escape-FilterPath $TextFile
    $source = "color=c=black:s=${width}x${height}:r=${Fps}:d=$(Format-Number $Duration)"
    $border = [Math]::Max(1, [int][Math]::Round(10 * $scale))
    $sigma = Format-Number (18 * $scale)
    $mask = "$source,format=gray,drawtext=fontfile='$font':textfile='$file':expansion=none:fontsize=$([int][Math]::Round($Size * $scale))" +
        ":fontcolor=white:borderw=${border}:bordercolor=white:x=$X`:y=$Y`:alpha='$Alpha',gblur=sigma=$sigma,lut=c0=val*0.62[hm$Tag]"
    return @(
        $mask,
        "$source,format=rgba[hk$Tag]",
        "[hk$Tag][hm$Tag]alphamerge[h$Tag]",
        "[$In][h$Tag]overlay=eof_action=pass:format=auto,format=rgb24[$Out]")
}

$titleCard = Get-Prop $list 'titleCard'
$endCard = Get-Prop $list 'endCard'
$inputs = [System.Collections.Generic.List[string]]::new()
$chains = [System.Collections.Generic.List[string]]::new()
$durations = [System.Collections.Generic.List[double]]::new()
$transitions = [System.Collections.Generic.List[string]]::new()
$fade = 0.5

for ($i = 0; $i -lt $shots.Count; $i++) {
    $shot = $shots[$i]
    $segment = Join-Path $segDir "$($shot.id).mkv"
    $duration = Get-Duration $segment
    $durations.Add($duration)
    $transitions.Add([string](Get-Prop $shot 'transition' 'fade'))
    $inputs.Add('-i'); $inputs.Add($segment)
    $base = "setpts=PTS-STARTPTS,fps=$Fps,format=rgb24"
    $head = "[${i}:v]$base"
    $filters = @()
    $caption = [string](Get-Prop $shot 'caption' '')
    if ($caption) {
        $file = Write-Text "caption-$($shot.id)" $caption
        $in = [double](Get-Prop $shot 'captionIn' ([Math]::Min(0.9, $duration / 4)))
        $out = [double](Get-Prop $shot 'captionOut' ($duration - [Math]::Max($crossfade, 0.6)))
        $y = if ((Get-Prop $shot 'captionPosition' 'bottom') -eq 'top') { [string][int](110 * $scale) } else { "h-$([int](150 * $scale))-text_h" }
        $x = [string][int](110 * $scale)
        $alpha = Get-Alpha $in $out $fade
        $chains.Add("[${i}:v]$base[b$i]")
        $chains.AddRange([string[]]@(Get-HaloChains -In "b$i" -Out "c$i" -Tag "$i" -Duration $duration -TextFile $file -Size 58 -X $x -Y $y -Alpha $alpha))
        $head = "[c$i]null"
        $filters += Get-DrawText -TextFile $file -Size 58 -X $x -Y $y -Alpha $alpha
    }
    if ($titleCard -and (Get-Prop $titleCard 'shot') -eq $shot.id) {
        $tIn = [double](Get-Prop $titleCard 'in' 1.0); $tOut = [double](Get-Prop $titleCard 'out' ($duration - 1.0))
        $title = Write-Text 'title' ([string]$titleCard.title)
        $subtitle = Write-Text 'subtitle' ([string](Get-Prop $titleCard 'subtitle' ''))
        $filters += Get-DrawText -TextFile $title -Size 168 -X '(w-text_w)/2' -Y "h*0.34-text_h/2" -Alpha (Get-Alpha $tIn $tOut 1.0) -Shadow 0.5 -ShadowOffset 4
        $filters += Get-DrawText -TextFile $subtitle -Size 44 -X '(w-text_w)/2' -Y "h*0.34+$([int](92 * $scale))" -Alpha (Get-Alpha ($tIn + 0.5) $tOut 1.0) -Shadow 0.6
    }
    if ($i -eq 0) { $filters += 'fade=t=in:st=0:d=1.0' }
    if ($i -eq $shots.Count - 1 -and -not $endCard) { $filters += Get-FilmFadeOut $duration } # no end card: the last shot fades to black
    $chains.Add("$head$(@($filters | ForEach-Object { ",$_" }) -join '')[v$i]")
}

# The end card: dark, the wordmark above the two lines of the list. A list with no endCard ends on its last shot's fade to black (the M4 hero
# video: the owner wanted no text on screen).
if ($endCard) {
$endIndex = $shots.Count
$endDuration = [double](Get-Prop $endCard 'duration' 5.0)
$durations.Add($endDuration)
$transitions.Add('fadeblack')
$inputs.Add('-f'); $inputs.Add('lavfi'); $inputs.Add('-i'); $inputs.Add("color=c=0x07080b:s=${width}x${height}:r=${Fps}:d=$(Format-Number $endDuration)")
$wordmark = Write-Text 'end-wordmark' 'LIFESTONE'
$endTitle = Write-Text 'end-title' ([string](Get-Prop $endCard 'title' ''))
$endSubtitle = Write-Text 'end-subtitle' ([string](Get-Prop $endCard 'subtitle' ''))
$endFilters = @("fps=$Fps", 'format=rgb24',
    (Get-DrawText -TextFile $wordmark -Size 92 -X '(w-text_w)/2' -Y "h*0.5-$([int](150 * $scale))" -Alpha (Get-Alpha 0.4 ($endDuration - 0.2) 0.8) -Color '0xE8D9B0'),
    (Get-DrawText -TextFile $endTitle -Size 46 -X '(w-text_w)/2' -Y "h*0.5" -Alpha (Get-Alpha 0.7 ($endDuration - 0.2) 0.8)),
    (Get-DrawText -TextFile $endSubtitle -Size 38 -X '(w-text_w)/2' -Y "h*0.5+$([int](78 * $scale))" -Alpha (Get-Alpha 1.2 ($endDuration - 0.2) 0.8) -Color '0xB8BCC6'),
    "fade=t=out:st=$(Format-Number ($endDuration - 0.8)):d=0.8")
$chains.Add("[${endIndex}:v]$($endFilters -join ',')[v$endIndex]")
}

# Crossfades: each join overlaps the streams by its transition's length; a shot's start in the film is its xfade offset.
$current = 'v0'
$length = $durations[0]
$starts = [System.Collections.Generic.List[double]]::new()
$starts.Add(0.0)
for ($k = 1; $k -lt $durations.Count; $k++) {
    $kind = $transitions[$k]
    $d = switch ($kind) { 'cut' { 1.0 / $Fps } 'fadeblack' { [Math]::Max($crossfade, 0.9) } default { $crossfade } }
    $xfade = if ($kind -eq 'fadeblack') { 'fadeblack' } else { 'fade' }
    $offset = $length - $d
    $starts.Add($offset)
    $label = "x$k"
    $chains.Add("[$current][v$k]xfade=transition=${xfade}:duration=$(Format-Number $d):offset=$(Format-Number $offset)[$label]")
    $current = $label
    $length = $offset + $durations[$k]
}
$chains.Add("[$current]scale=out_color_matrix=bt709:out_range=tv:flags=lanczos+accurate_rnd+full_chroma_int,format=yuv420p," +
    "setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv[out]")
$graph = Join-Path $WorkDir 'edit.filtergraph'
[IO.File]::WriteAllText($graph, ($chains -join ";`n"), [Text.UTF8Encoding]::new($false))

$final = Join-Path $OutDir 'lifestone-hero.mp4'
Write-Host ("[hero-video] editing {0} shots + end card, {1:0.0} s" -f $shots.Count, $length)
Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-edit.log') -Arguments (@($inputs) + @(
        '-/filter_complex', $graph, '-map', '[out]', '-an', '-r', "$Fps",
        '-c:v', 'libx264', '-profile:v', 'high', '-preset', 'slow', '-crf', "$Crf", '-pix_fmt', 'yuv420p',
        '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv',
        '-movflags', '+faststart', '-metadata', "title=$([string](Get-Prop $list 'title' 'Lifestone'))", $final))

# ------------------------------------------------------------------------------------------------------------- extras
$filmSeconds = Get-Duration $final
$share = Join-Path $OutDir 'lifestone-hero-720p.mp4'
$limit = [long]($ShareMaxMB * 1000000)
$targetBytes = $limit * 0.94
for ($attempt = 1; $attempt -le 3; $attempt++) {
    # The size limit's bitrate, but no more than 720p60 needs (a short film would otherwise get a huge file).
    $kbps = [int][Math]::Min(8000, [Math]::Floor($targetBytes * 8 / $filmSeconds / 1000))
    $passlog = Join-Path $WorkDir 'share-pass'
    $common = @('-i', $final, '-an', '-vf', 'scale=1280:720:flags=lanczos,setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv', '-c:v', 'libx264', '-profile:v', 'high', '-preset', 'slow',
        '-b:v', "${kbps}k", '-maxrate', "$([int]($kbps * 1.6))k", '-bufsize', "$([int]($kbps * 2))k", '-pix_fmt', 'yuv420p',
        '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv', '-passlogfile', $passlog)
    Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-share1.log') -Arguments ($common + @('-pass', '1', '-f', 'mp4', 'NUL'))
    Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-share2.log') -Arguments ($common + @('-pass', '2', '-movflags', '+faststart', $share))
    if ((Get-Item -LiteralPath $share).Length -le $limit) { break }
    $targetBytes *= 0.88
}
Get-ChildItem -LiteralPath $WorkDir -Filter 'share-pass*' | Remove-Item -Force

# Poster: a clean frame (no caption) of the list's poster shot at its moment, with the title card's wordmark and line.
$poster = Join-Path $OutDir 'lifestone-hero-poster.png'
$posterSpec = Get-Prop $list 'poster'
$posterShot = if ($posterSpec) { [string](Get-Prop $posterSpec 'shot' $shots[0].id) } else { $shots[0].id }
$posterT = if ($posterSpec) { [double](Get-Prop $posterSpec 't' 0) } else { 0.0 }
$posterFilters = @('format=rgb24')
if ($titleCard -and [bool](Get-Prop $posterSpec 'title' $true)) {
    $posterFilters += Get-DrawText -TextFile (Write-Text 'title' ([string]$titleCard.title)) -Size 168 -X '(w-text_w)/2' -Y "h*0.2-text_h/2" -Alpha '1' -Shadow 0.55 -ShadowOffset 4
    $posterFilters += Get-DrawText -TextFile (Write-Text 'subtitle' ([string](Get-Prop $titleCard 'subtitle' ''))) -Size 44 -X '(w-text_w)/2' -Y "h*0.2+$([int](92 * $scale))" -Alpha '1' -Shadow 0.65
}
Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-poster.log') -Arguments @('-ss', (Format-Number $posterT), '-i', (Join-Path $segDir "$posterShot.mkv"),
    '-vf', ($posterFilters -join ','), '-frames:v', '1', '-update', '1', $poster)

# Contact sheet: a frame every 2 s.
$contact = Join-Path $OutDir 'lifestone-hero-contact.png'
$rows = [int][Math]::Ceiling($filmSeconds / 2 / 8)
Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-contact.log') -Arguments @('-i', $final, '-vf',
    "fps=1/2,scale=384:216,drawtext=fontfile='$(Escape-FilterPath $fontPath)':text='%{pts\:hms}':x=6:y=6:fontsize=16:fontcolor=white:shadowcolor=black:shadowx=1:shadowy=1,tile=8x${rows}:padding=4:margin=4",
    '-frames:v', '1', '-update', '1', $contact)

if ($QaSheets) {
    $qa = Join-Path $WorkDir 'qa'
    if (Test-Path -LiteralPath $qa) { Remove-Item -LiteralPath $qa -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $qa | Out-Null
    Invoke-FFmpeg -LogPath (Join-Path $logDir 'ffmpeg-qa.log') -Arguments @('-i', $final, '-vf',
        "fps=2,scale=480:270,drawtext=fontfile='$(Escape-FilterPath $fontPath)':text='%{pts\:hms}':x=6:y=6:fontsize=18:fontcolor=yellow:shadowcolor=black:shadowx=1:shadowy=1,tile=4x5",
        (Join-Path $qa 'qa-%02d.png'))
}

# ------------------------------------------------------------------------------------------------------------ summary
$summary | Format-Table -AutoSize | Out-String | Write-Host
foreach ($file in $final, $share, $poster, $contact) {
    $bytes = (Get-Item -LiteralPath $file).Length
    Write-Host ("[hero-video] {0}  {1:N1} MB ({2:N0} bytes)" -f $file, ($bytes / 1e6), $bytes)  # MB of 1,000,000 bytes, as -ShareMaxMB
}
Write-Host ("[hero-video] film {0:0.00} s at {1}x{2}, {3} fps; shots start at {4}; total time {5:0} s" -f $filmSeconds, $width, $height, $Fps,
    (($starts | ForEach-Object { Format-Number $_ }) -join ', '), $started.Elapsed.TotalSeconds) -ForegroundColor Green
