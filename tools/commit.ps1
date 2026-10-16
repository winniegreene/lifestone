<#
.SYNOPSIS
    Commits on the current branch of this checkout with the repository's own identity and TZ=UTC. Never pushes.

.DESCRIPTION
    The one way agents commit (CLAUDE.md, "Commits and pushes").

      - TZ=UTC is set for the git process only (restored afterwards), so the author and committer dates carry +0000.
      - The identity is the repository-level (git config --local) user.name and user.email. When either is unset the script
        refuses (exit 1); a global or system identity is never used. git is run with -c user.name=<local> -c user.email=<local>,
        which beats a per-worktree config, and GIT_AUTHOR_NAME/EMAIL/DATE and GIT_COMMITTER_NAME/EMAIL/DATE are removed from the
        git process's environment (restored afterwards), so nothing outside the repository's own identity can reach the commit.
      - -Message is required and may span several lines (it goes to git through a temporary file, not the command line).
      - -Paths (optional): these paths are staged first (git add -- <paths>); under `pwsh -File` list them after -Paths
        separated by spaces (the first binds to -Paths, the rest are taken as further paths). Without it the script commits what is already
        staged; with nothing staged it refuses, unless a merge is in progress (see below).
      - There is no amend, no --no-verify and no push. The script refuses (exit 1, a message, nothing committed) when
        any extra argument, any -Paths entry or the first line of the message asks for a push: the word "push", or an
        argument such as --push or push.
      - Concluding a merge: git merge --no-ff --no-commit <branch>, then
        tools\commit.ps1 -Message "Merge <branch> ...". With a merge in progress the commit is made even when the index
        equals HEAD.
      - After committing it prints the new short hash and checks that the author date (%ad) and the committer date (%cd) both
        end in +0000, and that `git log -1 --format='%an <%ae>|%cn <%ce>'` is the local identity twice (author and committer);
        when either check fails, the script fails loudly (exit 2) after printing what git reported.

    Exit codes: 0 committed; 1 refused (nothing committed); 2 committed but the date check failed.

.EXAMPLE
    pwsh -File tools\commit.ps1 -Message "M3 1.1 s0: tools/commit.ps1" -Paths tools\commit.ps1

.EXAMPLE
    git merge --no-ff --no-commit m3/1-1-s0
    pwsh -File tools\commit.ps1 -Message "Merge m3/1-1-s0 (tools, cleanup, the dat pins)"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Message,
    [string[]] $Paths = @(),
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Rest = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string] $text, [int] $code = 1) {
    [Console]::Error.WriteLine("[commit] REFUSED: $text")
    exit $code
}

$repoRoot = Split-Path -Parent $PSScriptRoot

# No push, in any spelling: extra arguments, -Paths entries, the message's first line.
$firstLine = ($Message -split "`r?`n")[0]
$pushPattern = '(^|[\s=:/-])push(es|ed|ing)?($|[\s=:/,.;])'
foreach ($arg in @($Rest) + @($Paths)) {
    if ($arg -match $pushPattern) { Fail "argument '$arg' asks for a push; this script never pushes (the owner asks for pushes)." }
}
if ($firstLine -match $pushPattern) { Fail "the message's first line asks for a push; this script never pushes. Reword the first line." }
# With -File, "-Paths a b c" binds only a; the remaining plain words are more paths. Anything that looks like a switch is refused.
foreach ($arg in $Rest) {
    if ($arg.StartsWith('-')) { Fail "unexpected argument '$arg' (only -Message and -Paths are accepted)." }
}
$Paths = @($Paths) + @($Rest)
if ([string]::IsNullOrWhiteSpace($Message)) { Fail '-Message is empty.' }

# The repository-level identity, never a global one.
$name = (& git -C $repoRoot config --local user.name) 2>$null
$mail = (& git -C $repoRoot config --local user.email) 2>$null
if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($mail)) {
    Fail 'git config --local user.name and user.email must both be set in this repository; a global identity is never used.'
}

$merging = $false
& git -C $repoRoot rev-parse -q --verify MERGE_HEAD *> $null
if ($LASTEXITCODE -eq 0) { $merging = $true }

if ($Paths.Count -gt 0) {
    & git -C $repoRoot add -- @Paths
    if ($LASTEXITCODE -ne 0) { Fail "git add failed for: $($Paths -join ' ')" }
}

& git -C $repoRoot diff --cached --quiet
$hasStaged = ($LASTEXITCODE -ne 0)
if (-not $hasStaged -and -not $merging) { Fail 'nothing is staged (and no merge is in progress); pass -Paths or git add first.' }

$messageFile = [System.IO.Path]::GetTempFileName()
$oldTz = $env:TZ
# Environment that could put another identity or date on the commit: removed for the git process, restored afterwards.
$identityVars = 'GIT_AUTHOR_NAME', 'GIT_AUTHOR_EMAIL', 'GIT_AUTHOR_DATE', 'GIT_COMMITTER_NAME', 'GIT_COMMITTER_EMAIL', 'GIT_COMMITTER_DATE'
$savedVars = @{}
foreach ($v in $identityVars) {
    $item = Get-Item -LiteralPath "Env:\$v" -ErrorAction SilentlyContinue
    if ($null -ne $item) { $savedVars[$v] = $item.Value; Remove-Item -LiteralPath "Env:\$v" -ErrorAction SilentlyContinue }
}
try {
    [System.IO.File]::WriteAllText($messageFile, $Message.TrimEnd() + "`n", (New-Object System.Text.UTF8Encoding($false)))
    $env:TZ = 'UTC'
    & git -C $repoRoot -c "user.name=$name" -c "user.email=$mail" commit -F $messageFile
    $commitExit = $LASTEXITCODE
}
finally {
    if ($null -eq $oldTz) { Remove-Item Env:\TZ -ErrorAction SilentlyContinue } else { $env:TZ = $oldTz }
    foreach ($v in $savedVars.Keys) { Set-Item -LiteralPath "Env:\$v" -Value $savedVars[$v] }
    Remove-Item -LiteralPath $messageFile -Force -ErrorAction SilentlyContinue
}
if ($commitExit -ne 0) { Fail "git commit failed (exit $commitExit); see git's message above." }

$hash = (& git -C $repoRoot rev-parse --short HEAD).Trim()
$date = (& git -C $repoRoot log -1 --format=%ad --date=iso).Trim()
$committerDate = (& git -C $repoRoot log -1 --format=%cd --date=iso).Trim()
Write-Output "[commit] $hash $date"
if ($date -notmatch '\+0000$') {
    [Console]::Error.WriteLine("[commit] FAIL: the author date '$date' does not end in +0000 (TZ=UTC did not take effect).")
    exit 2
}
if ($committerDate -notmatch '\+0000$') {
    [Console]::Error.WriteLine("[commit] FAIL: the committer date '$committerDate' does not end in +0000 (TZ=UTC did not take effect).")
    exit 2
}
$expectedIdentity = "$name <$mail>|$name <$mail>"
$actualIdentity = (& git -C $repoRoot log -1 --format='%an <%ae>|%cn <%ce>').Trim()
if ($actualIdentity -ne $expectedIdentity) {
    [Console]::Error.WriteLine("[commit] FAIL: the commit's author and committer are '$actualIdentity', not the repository identity '$expectedIdentity'.")
    exit 2
}
exit 0
