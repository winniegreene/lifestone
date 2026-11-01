# The live gate's view of the machine it runs on (M5 item 4 c2: m5-gate-slow-machine, m5-gate-foreign-godot).
# Display: a preflight that names a display that is off (Godot renders at about 7 fps then), and the gate's own display hold for its run.
# Foreign Godot: a Godot process that is not the gate's own is named, and a solo step waits for it a bounded time, then fails by name.
# Nothing here changes a setting: no power plan, registry key, scheduled task or service (CLAUDE.md, Installs). The hold is one child process
# that ends with the gate (or when the gate's process is gone).

Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------------------------- the display preflight

function ConvertFrom-NvidiaDisplayCsv {
    <# Reads the output of `nvidia-smi --query-gpu=display_active --format=csv` (a header line, then Enabled or Disabled per GPU). Returns 'Enabled' (any GPU has an active display), 'Disabled' (every GPU reports Disabled) or 'unknown'. #>
    param([string] $Text)
    $values = @(($Text -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne 'display_active' })
    if ($values.Count -eq 0) { return 'unknown' }
    if (@($values | Where-Object { $_ -eq 'Enabled' }).Count -gt 0) { return 'Enabled' }
    if (@($values | Where-Object { $_ -ne 'Disabled' }).Count -eq 0) { return 'Disabled' }
    return 'unknown'
}

function Get-GateDisplayState {
    <# The preflight: runs nvidia-smi when it exists. Returns @{ State = Enabled|Disabled|unknown|no-nvidia-smi; Detail }. Absent nvidia-smi it says so and the gate goes on. #>
    $exe = Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue
    if (-not $exe) { return @{ State = 'no-nvidia-smi'; Detail = 'nvidia-smi not found: the display cannot be read, the gate goes on' } }
    try {
        $text = (& $exe.Source --query-gpu=display_active --format=csv 2>&1 | Out-String)
        $state = ConvertFrom-NvidiaDisplayCsv -Text $text
        $detail = if ($state -eq 'Disabled') { 'display_active=Disabled: the display is OFF and Godot renders at about 7 fps' } elseif ($state -eq 'Enabled') { 'display_active=Enabled' } else { 'nvidia-smi gave no display_active value' }
        return @{ State = $state; Detail = $detail }
    }
    catch { return @{ State = 'unknown'; Detail = "nvidia-smi failed ($($_.Exception.GetType().Name)): the gate goes on" } }
}

# ---------------------------------------------------------------------------------------------- the display wake and hold

function Invoke-GateDisplayWake {
    <# A 1-pixel mouse move and back (user32 mouse_event, relative): real input, which wakes a sleeping display. Returns $true when both calls ran. #>
    try {
        if (-not ('GateHostNative.Wake' -as [type])) {
            Add-Type -Namespace GateHostNative -Name Wake -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, System.UIntPtr dwExtraInfo);'
        }
        [GateHostNative.Wake]::mouse_event(0x0001, 1, 0, 0, [UIntPtr]::Zero)    # MOUSEEVENTF_MOVE, one pixel right
        Start-Sleep -Milliseconds 150
        [GateHostNative.Wake]::mouse_event(0x0001, -1, 0, 0, [UIntPtr]::Zero)   # and back
        return $true
    }
    catch { return $false }
}

# The hold: a pwsh child whose thread asks Windows to keep the display and the system on (SetThreadExecutionState with
# ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED). The state belongs to the thread, so the child keeps its thread alive and re-asserts it;
# it ends with the gate (Stop-GateDisplayHold) or when the gate's own process is gone. No setting is changed: the state ends with the thread.
$script:DisplayHoldScript = @'
param([int] $ParentPid)
Add-Type -Namespace GateHostNative -Name Hold -MemberDefinition '[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);'
$flags = [uint32]2147483651   # ES_CONTINUOUS 0x80000000 | ES_DISPLAY_REQUIRED 0x2 | ES_SYSTEM_REQUIRED 0x1
while ($true) {
    [void][GateHostNative.Hold]::SetThreadExecutionState($flags)
    if (-not (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Seconds 5
}
'@

function Start-GateDisplayHold {
    <# Starts the hold child (hidden window); returns the process, or $null with a warning when it could not start. $Folder gets the child's script file. #>
    param([string] $Folder)
    try {
        New-Item -ItemType Directory -Force -Path $Folder | Out-Null
        $holdFile = Join-Path $Folder 'display-hold.ps1'
        Set-Content -LiteralPath $holdFile -Value $script:DisplayHoldScript -Encoding utf8
        $pwsh = (Get-Process -Id $PID).Path
        return Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-File', $holdFile, '-ParentPid', "$PID") -WindowStyle Hidden -PassThru
    }
    catch { Write-Warning "display hold did not start ($($_.Exception.GetType().Name))"; return $null }
}

function Stop-GateDisplayHold {
    param($Process)
    if ($Process -and -not $Process.HasExited) { try { $Process.Kill($true); $Process.WaitForExit(5000) | Out-Null } catch { } }
}

function Get-GateDisplayLine {
    <# The display= field of the gate's start line and summary. $Before and $After are Get-GateDisplayState results ($After $null when the display was not woken). #>
    param($Before, $After, [bool] $Held, [bool] $Skipped, [string] $Why = '')
    if ($Skipped) { return "$($Before.State) hold=off ($Why)" }
    $path = if ($After -and $After.State -ne $Before.State) { "$($Before.State)->$($After.State)" } else { $Before.State }
    return "$path hold=$(if ($Held) { 'on' } else { 'failed' })"
}

# ---------------------------------------------------------------------------------------------- the foreign Godot

function Format-GateGameArgs {
    <# The part of a Godot command line after the first ' -- ' (the game's own arguments), or the line itself, shortened; a value named like a password, secret, token or key is hidden. #>
    param([string] $CommandLine)
    if (-not $CommandLine) { return '(command line not readable)' }
    $m = [regex]::Match($CommandLine, '\s--\s(?<args>.*)$')
    $text = if ($m.Success) { $m.Groups['args'].Value } else { $CommandLine }
    $text = [regex]::Replace($text, '(?i)(--?\w*(?:pass|secret|token|key)\w*[= ])\S+', '$1<hidden>')
    if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
    return $text.Trim()
}

function Get-ForeignGodot {
    <#
      Godot game processes (the console wrappers excluded) that are not the gate's own: not a descendant of $OwnRoot. Returns objects with Pid, Started and Args.
      A process whose command line cannot be read says so. Reads Win32_Process, so it is read-only.
    #>
    param([int] $OwnRoot = $PID)
    $all = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)
    $parent = @{}
    foreach ($p in $all) { $parent[[int]$p.ProcessId] = [int]$p.ParentProcessId }
    $found = [System.Collections.Generic.List[object]]::new()
    foreach ($p in @($all | Where-Object { $_.Name -like 'Godot*' -and $_.Name -notlike '*_console*' })) {
        $own = $false; $cur = [int]$p.ProcessId; $guard = 0
        while ($parent.ContainsKey($cur) -and $guard++ -lt 64) { $cur = $parent[$cur]; if ($cur -eq $OwnRoot) { $own = $true; break } }
        if ($own) { continue }
        $started = if ($p.CreationDate) { ([datetime]$p.CreationDate).ToString('yyyy-MM-dd HH:mm:ss') } else { 'unknown' }
        $found.Add([pscustomobject]@{ Pid = [int]$p.ProcessId; Started = $started; Args = (Format-GateGameArgs $p.CommandLine) })
    }
    return @($found)
}

function Wait-GateForeignGodot {
    <#
      Before a solo step: names every foreign Godot process (pid, start time, game arguments) once, waits for them to end up to $Seconds (a line every minute), and then fails by name.
      Returns @{ Ok; Detail }. A failed detail starts with `foreign-godot:`. $Probe and $Say are injectable so a test needs no Godot. $PollSeconds is the poll interval.
    #>
    param([int] $Seconds = 120, [string] $Label = '', [scriptblock] $Say = { param($l) Write-Host $l }, [scriptblock] $Probe = { Get-ForeignGodot }, [double] $PollSeconds = 5)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $named = [System.Collections.Generic.HashSet[int]]::new()
    $minute = 0
    while ($true) {
        $others = @(& $Probe)
        if ($others.Count -eq 0) { return @{ Ok = $true; Detail = $(if ($named.Count -gt 0) { "foreign Godot ended after $([int]$clock.Elapsed.TotalSeconds) s" } else { '' }) } }
        foreach ($o in $others) {
            if ($named.Add([int]$o.Pid)) { [void](& $Say "[foreign-godot] ${Label}: a Godot process that is not the gate's is up: pid $($o.Pid) started $($o.Started) args: $($o.Args); waiting up to $Seconds s") }
        }
        if ($clock.Elapsed.TotalSeconds -ge $Seconds) {
            $who = ($others | ForEach-Object { "pid $($_.Pid) (started $($_.Started))" }) -join ', '
            return @{ Ok = $false; Detail = "foreign-godot: $who still up after $Seconds s; the step was not started (end that Godot run and rerun the step)" }
        }
        if ($clock.Elapsed.TotalSeconds -ge ($minute + 1) * 60) { $minute++; [void](& $Say "[foreign-godot] ${Label}: still waiting, minute $minute") }
        Start-Sleep -Milliseconds ([int]($PollSeconds * 1000))
    }
}

Export-ModuleMember -Function ConvertFrom-NvidiaDisplayCsv, Get-GateDisplayState, Invoke-GateDisplayWake, Start-GateDisplayHold, Stop-GateDisplayHold, Get-GateDisplayLine, Format-GateGameArgs, Get-ForeignGodot, Wait-GateForeignGodot
