<#
    The decisions of tools\play.ps1 (m2-plan L18, owner decisions D13 and D10), kept apart from the script so a test can run
    them without a backend, a game or a window: what to do about the backend, what to stop afterwards, and which game
    arguments are allowed. Pure functions: nothing here starts or stops a process, reads secrets.json or writes a file.

    A password never appears in any of it: play.ps1 passes names only (an account key, a character name), godot.ps1 echoes its
    arguments, and ps1 files are read by anyone who clones the repository.
#>
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Toolchain.psm1') -DisableNameChecking

# An entry of server\runtime\secrets.json's accounts (play, test, or one setup-server.ps1 -AddAccount made): a name only.
$script:AccountPattern = '^[A-Za-z0-9][A-Za-z0-9_.-]{0,49}$'
# A character name as ACE accepts one: letters, spaces, apostrophes and hyphens (at most 32 characters; SessionOptions allows 64).
$script:CharacterPattern = "^[A-Za-z][A-Za-z' -]{0,63}$"
# The game options play.ps1 owns: -GameArgs may not carry them (an --account there would get round the rule for automated runs).
$script:OwnedOptions = @('--play', '--account', '--character', '--server')
# Automated runs (-Offscreen) use only this account (the owner's play account is never used by an agent or a test).
$script:AutomatedAccount = 'test'

function Get-PlayBackendPlan {
    <#
    .SYNOPSIS
        What play.ps1 does about the backend, from Get-LsBackendState's answer (server\lib\LifestoneServer.psm1).
    .OUTPUTS
        @{ Action = 'use' | 'start' | 'refuse'; Reason = <text>; MariaDbRunning = <bool> }.
        'use': ACE is up with its Lifestone mod ready; nothing is started and nothing will be stopped.
        'start': ACE is down; start-server.ps1 starts what is missing (mariadbd too when it is down).
        'refuse': ACE is up without a ready mod (pids.json ace.modReady is not true): the session layer would refuse the login,
        and ACE would make the first account an Admin. play.ps1 neither starts, stops nor uses it.
    #>
    param([Parameter(Mandatory)] $State)
    $ace = [bool]$State.Ace
    $db = [bool]$State.MariaDb
    if ($ace -and [bool]$State.ModReady) {
        return @{ Action = 'use'; MariaDbRunning = $db; Reason = "ACE (PID $($State.AcePid)) is running with the Lifestone mod ready; nothing will be started or stopped" }
    }
    if ($ace) {
        return @{ Action = 'refuse'; MariaDbRunning = $db; Reason = "ACE (PID $($State.AcePid)) is running without a confirmed Lifestone server mod (pids.json ace.modReady is not true), so ACE would promote the first account to Admin. Run server\stop-server.ps1, then tools\play.ps1 again." }
    }
    $reason = if ($db) { 'ACE is not running (mariadbd is): start-server.ps1 will start ACE' } else { 'the backend is not running: start-server.ps1 will start mariadbd and ACE' }
    return @{ Action = 'start'; MariaDbRunning = $db; Reason = $reason }
}

function Get-PlayStopPlan {
    <#
    .SYNOPSIS
        What play.ps1 stops when the game exits: only what it started (D13).
    .OUTPUTS
        @{ Action = 'none' | 'all' | 'aceonly' | 'leave-mariadb'; Note = <text> }.
        'all': it started both, so stop-server.ps1 stops both. 'aceonly': MariaDB was already up, so stop-server.ps1 -AceOnly
        stops only ACE. 'leave-mariadb': it started MariaDB under an ACE that was already running, which needs it, so nothing
        is stopped. -KeepServer keeps whatever it started.
    #>
    param([bool] $StartedMariaDb, [bool] $StartedAce, [switch] $KeepServer)
    if (-not ($StartedMariaDb -or $StartedAce)) {
        return @{ Action = 'none'; Note = 'play.ps1 started nothing, so it stops nothing' }
    }
    if ($KeepServer) {
        return @{ Action = 'none'; Note = '-KeepServer: the backend play.ps1 started keeps running (stop it with server\stop-server.ps1)' }
    }
    if ($StartedMariaDb -and $StartedAce) { return @{ Action = 'all'; Note = 'stopping the backend play.ps1 started (ACE, then mariadbd)' } }
    if ($StartedAce) { return @{ Action = 'aceonly'; Note = 'stopping the ACE play.ps1 started; mariadbd was running before and keeps running' } }
    return @{ Action = 'leave-mariadb'; Note = 'the mariadbd play.ps1 started stays up: the ACE that was already running needs it' }
}

function Get-PlayStartedByUs {
    <#
    .SYNOPSIS
        What this run started: what is up in the state read after the start and was not up in the state read before it. Also right when the start was cut short
        (Ctrl+C, a throw): mariadbd and ACE run detached, so whatever came up is ours to stop.
    .OUTPUTS
        @{ MariaDb = <bool>; Ace = <bool> }. A null $After (the state could not be read) gives false for both.
    #>
    param([Parameter(Mandatory)] $Before, $After)
    if ($null -eq $After) { return @{ MariaDb = $false; Ace = $false } }
    return @{
        MariaDb = [bool]($After.MariaDb -and -not $Before.MariaDb)
        Ace     = [bool]($After.Ace -and -not $Before.Ace)
    }
}

function Test-PlayAccountName {
    param([string] $Name)
    return [bool]($Name -and ($Name -match $script:AccountPattern))
}

function Test-PlayCharacterName {
    param([string] $Name)
    return [bool]($Name -and ($Name -match $script:CharacterPattern))
}

function Get-PlayGameOptions {
    <#
    .SYNOPSIS
        Checks play.ps1's -Account, -Character and -GameArgs and builds the game's arguments (names only, never a password).
    .DESCRIPTION
        The rules:
          - -Account names an entry of secrets.json's accounts; its value is never echoed back in a problem.
          - With -Offscreen (an automated run) the account is `test` and nothing else: the play account is the owner's.
          - -Character is a character name; a name with spaces is quoted so godot.ps1's argument splitting keeps it whole.
          - -Server (127.0.0.1:<acePort> from server\versions.json, Get-PlayServerEndpoint) becomes --server=; empty leaves the game's compiled default.
          - -GameArgs may not carry --play, --account, --character or --server (play.ps1 owns them) or anything named for a password.
    .OUTPUTS
        @{ Problems = @(...); Account = <effective account or ''>; Arguments = @('--play', ...); GameArgs = <string for godot.ps1> }.
    #>
    param([string] $Account = '', [string] $Character = '', [switch] $Offscreen, [string] $GameArgs = '', [string] $Server = '')
    $problems = New-Object System.Collections.Generic.List[string]
    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('--play')

    $effective = $Account
    if ($Account -and -not (Test-PlayAccountName $Account)) {
        $problems.Add("-Account must be the name of an entry of server\runtime\secrets.json's accounts (letters, digits, '_', '.' and '-', at most 50 characters). It is a name only: no option takes a password.")
        $effective = ''
    }
    elseif ($Offscreen) {
        if (-not $Account) { $effective = $script:AutomatedAccount }
        elseif ($Account -ne $script:AutomatedAccount) {
            $problems.Add("an automated run (-Offscreen) uses only the test account: leave -Account out or give -Account $script:AutomatedAccount. The play account is the owner's.")
            $effective = ''
        }
    }
    if ($effective) { $arguments.Add("--account=$effective") }
    if ($Server) {
        if ($Server -notmatch '^127\.0\.0\.1:[0-9]{1,5}$') { $problems.Add('-Server must be the loopback endpoint Get-PlayServerEndpoint returns (127.0.0.1:<port>).') }
        else { $arguments.Add("--server=$Server") }
    }

    if ($Character) {
        if (-not (Test-PlayCharacterName $Character)) {
            $problems.Add("-Character must be a character name (letters, spaces, apostrophes and hyphens, starting with a letter).")
        }
        elseif ($Character -match '\s') { $arguments.Add("--character=`"$Character`"") }
        else { $arguments.Add("--character=$Character") }
    }

    if ($GameArgs) {
        try { $extra = @(Split-LifestoneArguments $GameArgs) }
        catch { $problems.Add("-GameArgs: $($_.Exception.Message)"); $extra = @() }
        foreach ($token in $extra) {
            $name = ($token -split '=', 2)[0].ToLowerInvariant()
            if ($script:OwnedOptions -contains $name) {
                $problems.Add("-GameArgs carries $name, which tools\play.ps1 owns (use -Account and -Character; the server is the one in server\versions.json).")
            }
            elseif ($name -match 'password|passwd|secret|token') {
                $problems.Add("-GameArgs carries '$name': no game option takes a password, and play.ps1 never puts one on a command line.")
            }
            else { $arguments.Add($token) }
        }
    }

    $text = ($arguments | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '="') { "`"$_`"" } else { $_ } }) -join ' '
    return @{ Problems = @($problems); Account = $effective; Arguments = @($arguments); GameArgs = $text }
}

function Assert-PlayLoopback {
    <# Refuses unless server\versions.json names 127.0.0.1 as the network host (the whole backend is loopback-only). #>
    param([Parameter(Mandatory)] $Versions)
    $hostName = ''
    if ($Versions.PSObject.Properties['network'] -and $Versions.network.PSObject.Properties['host']) { $hostName = [string]$Versions.network.host }
    if ($hostName -ne '127.0.0.1') {
        throw "server\versions.json names network.host '$hostName', not 127.0.0.1: tools\play.ps1 only plays against a backend on this machine's loopback address."
    }
}

function Get-PlayServerEndpoint {
    <# The login endpoint the game is told to use: 127.0.0.1:<network.acePort of server\versions.json>, so the client and the backend can never disagree about the port. Call after Assert-PlayLoopback. #>
    param([Parameter(Mandatory)] $Versions)
    $port = 0
    if ($Versions.PSObject.Properties['network'] -and $Versions.network.PSObject.Properties['acePort']) { $port = [int]$Versions.network.acePort }
    if ($port -lt 1 -or $port -gt 65535) {
        throw "server\versions.json names network.acePort '$port', which is not a port: tools\play.ps1 passes the game --server=127.0.0.1:<acePort>."
    }
    return "127.0.0.1:$port"
}

Export-ModuleMember -Function Get-PlayServerEndpoint, Get-PlayBackendPlan, Get-PlayStopPlan, Get-PlayStartedByUs, Test-PlayAccountName, Test-PlayCharacterName, Get-PlayGameOptions, Assert-PlayLoopback
