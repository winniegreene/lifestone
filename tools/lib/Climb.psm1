# tools\climb.ps1's pure parts (M6 item 0 F5, review 0-R3): the verdict lines of the climb's three steps read from their logs, the rule that lets the third step start, and the summed verdict line.
# Nothing here starts a process, reads a setting or touches the backend; tools\climb.ps1 does that and tests call these directly.

Set-StrictMode -Version Latest

function Get-ClimbLine {
    <# The LAST line of $Text that holds $Pattern (log lines carry a time and a level before it), from the first match to the line's end; '' when there is none. #>
    param([string] $Text, [string] $Pattern)
    $found = [regex]::Matches([string]$Text, "(?m)$Pattern[^\r\n]*")
    if ($found.Count -eq 0) { return '' }
    return $found[$found.Count - 1].Value.TrimEnd()
}

function Get-ClimbFirstLine {
    param([string] $Text, [string] $Pattern)
    $m = [regex]::Match([string]$Text, "(?m)$Pattern[^\r\n]*")
    if ($m.Success) { return $m.Value.TrimEnd() }
    return ''
}

function ConvertFrom-ClimbFields {
    <# key=value tokens of a line as an ordered hashtable (the value runs to the next space). An absent key is simply absent: callers test with Contains, never assume. #>
    param([string] $Line)
    $fields = [ordered]@{}
    foreach ($m in [regex]::Matches([string]$Line, '(?<![\w.])(?<k>[A-Za-z_]\w*)=(?<v>\S*)')) { $fields[$m.Groups['k'].Value] = $m.Groups['v'].Value }
    return $fields
}

function Get-ClimbCount {
    <# An integer field, or $null when it is absent or not an integer (a count the log did not give is unread, never zero). #>
    param($Fields, [string] $Name)
    if ($Fields.Contains($Name) -and "$($Fields[$Name])" -match '^-?\d+$') { return [int]$Fields[$Name] }
    return $null
}

function Get-ClimbWord {
    param($Fields, [string] $Name)
    if ($Fields.Contains($Name)) { return [string]$Fields[$Name] }
    return $null
}

function Get-ClimbCreateVerdict {
    <#
      Step 1 (--scenario=climber-create): the headless summary line (ok=1) and the climber's verdict line. Returns @{ Ok; Granted; Level; Line; Why }.
      -OneWindow (M6 onscreen start, --login-check=climber-start): the window's one log instead: the run's own 'climber start: creation done ok=' line and the FIRST verdict line (the run logs one; the level up job that follows logs none).
      The process's exit code belongs to the whole run there, so it is not read for this part.
    #>
    param([string] $Text, [int] $ExitCode, [switch] $OneWindow)
    if ($OneWindow) {
        $done = Get-ClimbFirstLine -Text $Text -Pattern 'climber start: creation done ok='
        $line = Get-ClimbFirstLine -Text $Text -Pattern '(\[climber\]|climber:) verdict '
        $f = ConvertFrom-ClimbFields $line
        $why = @()
        if (-not $done) { $why += "no 'climber start: creation done' line" } elseif ($done -notmatch 'creation done ok=true\b') { $why += 'the creation done line is not ok=true' }
        if (-not $line) { $why += "no '[climber] verdict' line" }
        elseif ((Get-ClimbWord $f 'template') -ne 'ok') { $why += "template=$(Get-ClimbWord $f 'template')" }
        return @{ Ok = ($why.Count -eq 0); Granted = (Get-ClimbCount $f 'granted'); Level = (Get-ClimbCount $f 'level'); Line = $line; Why = ($why -join '; ') }
    }

    $done = Get-ClimbLine -Text $Text -Pattern 'headless done scenario=climber-create'
    $line = Get-ClimbLine -Text $Text -Pattern '(\[climber\]|climber:) verdict ' # the headless console writes 'climber: verdict', the loopback sink '[climber] verdict' (item 0 live probe 2)
    $f = ConvertFrom-ClimbFields $line
    $why = @()
    if ($ExitCode -ne 0) { $why += "exit code $ExitCode" }
    if (-not $done) { $why += "no 'headless done scenario=climber-create' line" } elseif ($done -notmatch '\bok=1\b') { $why += 'the headless line is not ok=1' }
    if (-not $line) { $why += "no '[climber] verdict' line" }
    elseif ((Get-ClimbWord $f 'template') -ne 'ok') { $why += "template=$(Get-ClimbWord $f 'template')" }
    return @{ Ok = ($why.Count -eq 0); Granted = (Get-ClimbCount $f 'granted'); Level = (Get-ClimbCount $f 'level'); Line = $line; Why = ($why -join '; ') }
}

function Get-ClimbJourneyVerdict {
    <#
      Step 2 (--scenario=journey --journey=climber): the journey's done line and the climber line. Returns @{ Ok; CheatFree; Granted; Place; RecoveryPlace; Level; Stop; Line; Why }.
      -OneWindow: the FIRST of each line in the window's one log (the start-up journey's come before anything the level up job logs), and no exit code (the whole run's).
    #>
    param([string] $Text, [int] $ExitCode, [switch] $OneWindow)
    if ($OneWindow) {
        $done = Get-ClimbFirstLine -Text $Text -Pattern 'journey done ok='
        $line = Get-ClimbFirstLine -Text $Text -Pattern 'journey climber cheat_free='
    }
    else {
        $done = Get-ClimbLine -Text $Text -Pattern 'journey done ok='
        $line = Get-ClimbLine -Text $Text -Pattern 'journey climber cheat_free='
    }
    $f = ConvertFrom-ClimbFields $line
    $why = @()
    if (-not $OneWindow -and $ExitCode -ne 0) { $why += "exit code $ExitCode" }
    if (-not $done) { $why += "no 'journey done' line" } elseif ($done -notmatch 'journey done ok=true\b') { $why += 'the journey done line is not ok=true' }
    if (-not $line) { $why += "no 'journey climber' line" }
    return @{
        Ok = ($why.Count -eq 0); CheatFree = (Get-ClimbWord $f 'cheat_free'); Granted = (Get-ClimbCount $f 'granted'); Place = (Get-ClimbCount $f 'place')
        RecoveryPlace = (Get-ClimbCount $f 'recovery_place'); Level = (Get-ClimbCount $f 'level'); Stop = (Get-ClimbWord $f 'stop'); Line = $line; Why = ($why -join '; ')
    }
}

function Test-ClimbMayLevelUp {
    <#
      The rule that lets step 3 start (0-R3): the creation read granted=0, and the climber journey read cheat_free=yes granted=0 place=0 recovery_place=0, in THIS run. A count the logs did not give is a refusal,
      not a zero. Returns @{ Allowed; Why }.
    #>
    param($Create, $Journey)
    $why = @()
    if ($null -eq $Create -or -not $Create.Ok) { $why += "step 1 (the creation) did not finish ok ($(if ($Create) { $Create.Why } else { 'not run' }))" }
    elseif ($Create.Granted -ne 0) { $why += "step 1 read granted=$(if ($null -eq $Create.Granted) { 'unread' } else { $Create.Granted }), not 0" }
    if ($null -eq $Journey -or -not $Journey.Ok) { $why += "step 2 (the climber journey) did not finish ok ($(if ($Journey) { $Journey.Why } else { 'not run' }))" }
    else {
        foreach ($pair in @(@('Granted', 'granted'), @('Place', 'place'), @('RecoveryPlace', 'recovery_place'))) {
            if ($Journey[$pair[0]] -ne 0) { $why += "step 2 read $($pair[1])=$(if ($null -eq $Journey[$pair[0]]) { 'unread' } else { $Journey[$pair[0]] }), not 0" }
        }
        if ($Journey.CheatFree -ne 'yes') { $why += "step 2 read cheat_free=$(if ($null -eq $Journey.CheatFree) { 'unread' } else { $Journey.CheatFree }), not yes" }
    }
    return @{ Allowed = ($why.Count -eq 0); Why = ($why -join '; ') }
}

function Get-ClimbGameGate {
    <#
      -OneWindow (M6 onscreen start): the game's own gate line, 'climber start: gate level_up=allowed|refused ...' (ClimberStartGate: the same rule as Test-ClimbMayLevelUp, enforced in the process). Returns @{ Word = 'allowed'|'refused'|$null; Line }.
      The script still applies its own rule to the same log's lines; when the two disagree that is a finding (the run says both).
    #>
    param([string] $Text)
    $line = Get-ClimbFirstLine -Text $Text -Pattern 'climber start: gate level_up='
    $word = if ($line -match 'level_up=(allowed|refused)\b') { $Matches[1] } else { $null }
    return @{ Word = $word; Line = $line }
}

function ConvertTo-ClimbOneWindowCommand {
    <#
      M6 onscreen start (owner ruling 2026-11-19: the whole run in one window): the one process's tools\play.ps1 arguments, made from the level up command as climb.ps1 finished it (every switch it added to -GameArgs: stays): no -Character (the climber is
      created in the window; the login check enters nobody by name), the one process's hard stop, and --login-check=climber-start --video-pace=<s> (and --climber-start-wait-file when a recording must begin first) in front of the game arguments.
    #>
    param([string[]] $LevelUp, [int] $TimeoutSeconds, [double] $Pace = 1.8, [string] $WaitFile = '')
    if ($WaitFile -match '\s') { throw "the wait file's path has a space, which -GameArgs cannot carry: $WaitFile (give -OutDir without spaces)" }
    $out = New-Object System.Collections.Generic.List[string]
    $found = $false
    for ($i = 0; $i -lt $LevelUp.Count; $i++) {
        $a = $LevelUp[$i]
        if ($a -eq '-Character') { $i++; continue }
        if ($a -eq '-TimeoutSeconds') { $out.Add($a); $out.Add("$TimeoutSeconds"); $i++; continue }
        if ($a -like '-GameArgs:*') {
            $front = '--login-check=climber-start --video-pace=' + $Pace.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
            if ($WaitFile) { $front += " --climber-start-wait-file=$WaitFile" }
            $out.Add('-GameArgs:' + $front + ' ' + $a.Substring('-GameArgs:'.Length))
            $found = $true
            continue
        }
        $out.Add($a)
    }
    if (-not $found) { throw 'the level up command has no -GameArgs: to add the climber start to' }
    return [string[]] $out.ToArray()
}

function Get-ClimbLevelUpVerdict {
    <# Step 3 (the play host's level-up job): the last [ladder] line (level, rung) and the autopilot's done line (ok, kills, deaths, granted, places). Returns @{ Ok; LevelFirst; Level; Rung; Kills; Deaths; Granted; Places; Stop; Line; Why }. #>
    param([string] $Text, [int] $ExitCode)
    $ladder = Get-ClimbLine -Text $Text -Pattern '\[ladder\] at='
    $first = Get-ClimbFirstLine -Text $Text -Pattern '\[ladder\] at='
    $done = Get-ClimbLine -Text $Text -Pattern 'autopilot done ok='
    $armor = Get-ClimbLine -Text $Text -Pattern 'buff: pass: casts='   # M6 7 AK (report-only, a new judge): the last pass's armor buffs and expected damage factor
    $cycle = Get-ClimbLine -Text $Text -Pattern 'autopilot cycle n='   # M6 2 e: the last cycle line's casters=<n> (a state read; '?' or absent: unread, $null)
    $l = ConvertFrom-ClimbFields $ladder
    $d = ConvertFrom-ClimbFields $done
    $why = @()
    if ($ExitCode -ne 0) { $why += "exit code $ExitCode" }
    if (-not $done) { $why += "no 'autopilot done' line" } elseif ((Get-ClimbWord $d 'ok') -ne 'yes') { $why += "autopilot done ok=$(Get-ClimbWord $d 'ok') stop=$(Get-ClimbWord $d 'stop')" }
    if (-not $ladder) { $why += "no '[ladder]' line" }
    return @{
        Ok = ($why.Count -eq 0); LevelFirst = (Get-ClimbCount (ConvertFrom-ClimbFields $first) 'level'); Level = (Get-ClimbCount $l 'level'); Rung = (Get-ClimbWord $l 'rung')
        Casters = (Get-ClimbCount (ConvertFrom-ClimbFields $cycle) 'casters'); ArmorBuffs = $(if ($armor -match 'armor_buffs=\S+ damage_factor=[0-9.]+') { $Matches[0] } else { $null }); HealthByLevel = (Get-ClimbHealthByLevel -Text $Text)
        RungChanges = ([regex]::Matches([string]$Text, 'autopilot travel: check after the rung change: present in')).Count   # M6 7 AC (report-only, D27): each rung change that arrived on a ground (the climb out, a corpse trip's, the move-on's), counted from the log
        Kills = (Get-ClimbCount $d 'kills'); Deaths = (Get-ClimbCount $d 'deaths'); Granted = (Get-ClimbCount $d 'granted'); Places = (Get-ClimbCount $d 'places'); Stop = (Get-ClimbWord $d 'stop')
        Line = $ladder; Why = ($why -join '; ')
    }
}

function Get-ClimbHealthByLevel {
    <#
      M6 7 R (report-only, D27): the maximum health at each level the level-up log reached, 'level:health' joined by commas ('6:17,7:24'); '' when the log gives none. The reads are the [ladder] lines' health= and the level-up spend's own line
      ('ladder: level-up spend: level 7: ... health=24'): the last read of a level wins (the spend's raises come before its line). A '?' is unread and never counted.
    #>
    param([string] $Text)
    $levels = [ordered]@{}
    foreach ($m in [regex]::Matches([string]$Text, '(?m)(?:\[ladder\] at=\S+ n=\d+ level=|ladder: level-up spend: level )(?<l>\d+)\b[^\r\n]*? health=(?<h>\d+)')) { $levels[$m.Groups['l'].Value] = $m.Groups['h'].Value }
    return (@($levels.Keys | Sort-Object { [int]$_ } | ForEach-Object { "${_}:$($levels[$_])" }) -join ',')
}

function Format-ClimbVerdict {
    <#
      The one summed line (report-only, D27): '[climb] verdict level_start= level_end= rung= kills= deaths= granted= place= recovery_place= minutes=' (then 'casters=' when the level-up log's last cycle line carries it, M6 2 e; 'health_by_level=' when the log gives health, M6 7 R). $Create and $Journey may be $null (-SkipCreate): their counts are then
      not in the sum, and the line says so with 'skipped=create,journey'. A count no log gave is 'n/a', never 0.
    #>
    param($Create, $Journey, $LevelUp, [double] $Minutes)
    $text = { param($v) if ($null -eq $v) { 'n/a' } else { "$v" } }
    $sum = {
        param($values)
        $all = @($values)
        $known = @($all | Where-Object { $null -ne $_ })
        if ($known.Count -ne $all.Count) { return 'n/a' }
        return "$([int]($known | Measure-Object -Sum).Sum)"
    }
    $levelStart = if ($Journey) { & $text $Journey.Level } elseif ($Create) { & $text $Create.Level } else { 'n/a' }
    $granted = & $sum @($(if ($Create) { $Create.Granted } else { 0 }), $(if ($Journey) { $Journey.Granted } else { 0 }), $(if ($LevelUp) { $LevelUp.Granted } else { 0 }))
    $place = & $sum @($(if ($Journey) { $Journey.Place } else { 0 }), $(if ($LevelUp) { $LevelUp.Places } else { 0 }))
    $recovery = & $sum @($(if ($Journey) { $Journey.RecoveryPlace } else { 0 }))   # the autopilot's done line carries no recovery_place field: steps 1-2 only
    $rung = if ($LevelUp -and $LevelUp.Rung) { $LevelUp.Rung } else { 'n/a' }
    $minutesText = [math]::Round($Minutes, 1).ToString([Globalization.CultureInfo]::InvariantCulture)
    $line = "[climb] verdict level_start=$levelStart level_end=$(if ($LevelUp) { & $text $LevelUp.Level } else { 'n/a' }) rung=$rung kills=$(if ($LevelUp) { & $text $LevelUp.Kills } else { 'n/a' }) deaths=$(if ($LevelUp) { & $text $LevelUp.Deaths } else { 'n/a' }) granted=$granted place=$place recovery_place=$recovery minutes=$minutesText"
    if ($LevelUp -and $LevelUp.ArmorBuffs) { $line += " $($LevelUp.ArmorBuffs)" }   # M6 7 AK (report-only)
    if ($LevelUp -and $null -ne $LevelUp.Casters) { $line += " casters=$($LevelUp.Casters)" }   # M6 2 e (report-only): the level-up log's last cycle line carries it
    if ($LevelUp -and $LevelUp.HealthByLevel) { $line += " health_by_level=$($LevelUp.HealthByLevel)" }   # M6 7 R (report-only)
    if (-not $Create -or -not $Journey) { $line += ' skipped=create,journey' }
    if ($LevelUp -and $null -ne $LevelUp.RungChanges) { $line += " rung_changes=$($LevelUp.RungChanges)" }   # M6 7 AC (report-only): the rung changes the level-up log shows arrived; no judge reads it (a gate that wants 'two' reads this number)
    return $line
}

function Get-ClimbCommands {
    <# The three command lines as argument arrays: Create and Journey are dotnet's; LevelUp is pwsh's (tools\play.ps1). $PlayTimeoutSeconds is play.ps1's hard stop. #>
    param([int] $Minutes, [int] $TargetLevel, [int] $PlayTimeoutSeconds, [switch] $NoLoot, [switch] $NoQuests, [string] $Quests = '', [switch] $NoBuffBot, [switch] $DenRun, [switch] $DenRunRehearse, [string] $Climber = 'Winnie', [string] $SteerRun = '', [switch] $NoOrders)
    $model = $Climber -eq 'Qwinnie'
    $account = if ($model) { 'qwinnie' } else { 'test' }
    $journey = if ($model) { 'qwinnie' } else { 'climber' }
    $headless = @('run', '--disable-build-servers', '--no-build', '--project', 'src/Lifestone.Headless', '--')
    $gameArgs = "--autopilot-job=level-up --autopilot-start --autopilot-minutes=$Minutes --autopilot-level=$TargetLevel --max-fps=60 --mute"
    # M6 item 2: the climber loots (the equip pass and the gear floor run on loot trips: the resupply's Gear follows --fight-loot); -NoLoot (tools\climb.ps1) leaves it at the option's default, off.
    if (-not $NoLoot) { $gameArgs += ' --fight-loot=on' }
    if ($model) { $gameArgs += " --autopilot-climber=$Climber" }
    # The watched run: the desk opens for looking (its controls refused), it takes the command orders (tools\order.ps1: trip, rebuff, quest <key>, ladder, ...; -NoOrders turns them off), and the screenshots are retail's native size.
    if ($SteerRun) { $gameArgs += " --autopilot-steer-dir=cache/avatar/$SteerRun --autopilot-steer-observe --autopilot-stop-file=cache/avatar/$SteerRun/stop" }
    if ($SteerRun -and -not $NoOrders) { $gameArgs += ' --autopilot-steer-orders' }
    $size = if ($SteerRun) { @('-Resolution', '1920x1080') } else { @() }
    # M6 item 14 S3: the sprint (-NoQuests: no quest errand from home, no trophy want; the Academy and the Pathwarden chest of step 2 stay) and the buff bot's option. The bot is on by default in both the creation
    # (its skill order: Life Magic, Item Enchantment, Mana Conversion, Creature Enchantment) and the level up job; -NoBuffBot passes --autopilot-buff-bot=off to both, so the character is created and trained in the order of before.
    if ($NoQuests) { $gameArgs += ' --autopilot-ladder-quests=off' }
    # M6 14 guard task: a selection (-Quests: only the quests it names, by the quest catalog's keys or the group tusks; a kill task it names is run). The game checks the keys; this checks only the form.
    if ($Quests) {
        if ($NoQuests) { throw '-Quests is a selection and -NoQuests the sprint: give one of them' }
        if ($Quests -notmatch '^[a-z0-9]+(-[a-z0-9]+)*(,[a-z0-9]+(-[a-z0-9]+)*)*$') { throw "-Quests: '$Quests' is not a list of quest keys (lower-case words joined by '-', the keys by ',', as tusks,tusker-guard-kill-task)" }
        $gameArgs += " --autopilot-ladder-quests=$Quests"
    }
    if ($NoBuffBot) { $gameArgs += ' --autopilot-buff-bot=off' }
    # M6 item 14 (the owner, 2026-11-12): the den run's order, off by default: the level up job does the trip home, the buff bot, her own pass and one run to the Black Spawn Den's entrance, ties there and stops
    # (den-run:tied, or the failure's name). The level stop rule is not read while the order stands.
    if ($DenRun) { $gameArgs += ' --autopilot-den-run' }
    # The den run's rehearsal: the order with its road cut short of the Desert March Portal at Danby's Outpost, then home (den-run:rehearsed).
    if ($DenRunRehearse) { $gameArgs += ' --autopilot-den-run-rehearse' }
    $createArgs = @('--scenario=climber-create', "--account=$account", '--fresh-character')
    if ($NoBuffBot) { $createArgs += '--autopilot-buff-bot=off' }
    return @{
        Create  = $headless + $createArgs
        Journey = $headless + @('--scenario=journey', "--journey=$journey", "--account=$account")
        LevelUp = @('-NoProfile', '-File', 'tools\play.ps1', '-Account', $account, '-Character', $Climber, '-Offscreen', '-IsolateUserData', '-KeepServer', '-NoBuild', '-TimeoutSeconds', "$PlayTimeoutSeconds") + $size + @("-GameArgs:$gameArgs")
    }
}

function Add-ClimbTrial {
    <#
    M6 item 14 (the owner, 2026-11-13): the trial switch. Appends --autopilot-trial-ground=<key> --autopilot-trial-minutes=<n> to the level up job's game arguments (the LevelUp command's -GameArgs:), so the job hunts
    that later rung's ground for n minutes of fight time from her first kill there, then returns to the ladder's rung. The game reads the trial's rules and refuses it by name; this checks only the key's form.
    #>
    param([hashtable] $Commands, [string] $TrialGround, [int] $TrialMinutes = 30)
    if ($TrialGround -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "-TrialGround: '$TrialGround' is not a ground key (lower-case words joined by '-', as tusker-quarters)" }
    if ($TrialMinutes -lt 1 -or $TrialMinutes -gt 1440) { throw "-TrialMinutes: $TrialMinutes is not 1 to 1440" }
    $levelUp = @($Commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { throw 'the level up command has no -GameArgs: to add the trial to' }
    $levelUp[$at] += " --autopilot-trial-ground=$TrialGround --autopilot-trial-minutes=$TrialMinutes"
    $Commands.LevelUp = $levelUp
    return $Commands
}

function Add-ClimbStartQuest {
    <#
    M6 14 (the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once)): appends --autopilot-start-quest=<key> to the level up job's game arguments (the LevelUp command's -GameArgs:), so the job runs
    that quest's errand from home at the run's start, after its first audit and buff pass, and then goes on with the ladder. The game checks the key against the quest catalog and the quest's level gate and refuses by name;
    this checks only the key's form.
    #>
    param([hashtable] $Commands, [string] $StartQuest)
    if ($StartQuest -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "-StartQuest: '$StartQuest' is not a quest key (lower-case words joined by '-', as atlan-wand)" }
    $levelUp = @($Commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { throw 'the level up command has no -GameArgs: to add the start quest to' }
    $levelUp[$at] += " --autopilot-start-quest=$StartQuest"
    $Commands.LevelUp = $levelUp
    return $Commands
}

function Add-ClimbHoldGrounds {
    <#
    M6 14 (live 2026-11-14 19:43: a death on the Tusker Armory's first visit): appends --autopilot-hold-grounds=<key,key> to the level up job's game arguments, so every way out holds those grounds for the session by name. The game
    checks each key; this checks only their form.
    #>
    param([hashtable] $Commands, [string] $HoldGrounds)
    foreach ($key in ($HoldGrounds -split ',')) {
        if ($key.Trim() -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "-HoldGrounds: '$key' is not a ground key (lower-case words joined by '-', as tusker-armory)" }
    }
    $levelUp = @($Commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { throw 'the level up command has no -GameArgs: to add the held grounds to' }
    $levelUp[$at] += " --autopilot-hold-grounds=$(($HoldGrounds -split ',' | ForEach-Object { $_.Trim() }) -join ',')"
    $Commands.LevelUp = $levelUp
    return $Commands
}

Export-ModuleMember -Function Get-ClimbLine, ConvertFrom-ClimbFields, Get-ClimbCount, Get-ClimbCreateVerdict, Get-ClimbJourneyVerdict, Test-ClimbMayLevelUp, Get-ClimbGameGate, ConvertTo-ClimbOneWindowCommand, Get-ClimbLevelUpVerdict, Format-ClimbVerdict, Get-ClimbCommands, Add-ClimbTrial, Add-ClimbHoldGrounds, Add-ClimbStartQuest
