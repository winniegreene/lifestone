<#
.SYNOPSIS
    Looks up retail acclient functions by name or address in the pinned 11.6096 symbol table (read-only).

.DESCRIPTION
    Reads <main checkout>\cache\research\symbol_transplant_11.6096.tsv, the table that maps the 11.4186 PDB names onto
    the user's 11.6096 acclient.exe (columns va_11.6096, va_11.4186, name, module, confidence, candidates, alt_names).
    The file is fetched once by hand (owner decision D4; docs/research/retail-addresses-m2.md, "The pin") and is never
    executed: this script only hashes it and splits its lines.

    Before any lookup the file's SHA-256 is checked against the pinned value below. If it differs, the script refuses
    to answer. The default location is the cache of the outermost enclosing checkout (a folder holding Lifestone.slnx),
    so a git worktree nested inside the main checkout finds the main checkout's copy; inner checkouts are tried next.

    Each row comes back as an object (Name, Va6096, Va4186, Module, Confidence, Candidates, AltName). Confidence is the
    table's own: exact, verified or predicted. A function that is not in the table (one of the builds' 46 changed
    functions, or a body the 2015 linker folded away) gives a warning, or an error with -Strict.

.PARAMETER Name
    Function names, matched exactly (ordinal) against the name and alt_names columns, e.g. 'gmSmartBoxUI::UseTime'.
    With -Like, PowerShell wildcards ('CameraSet::*').

.PARAMETER Address
    Virtual addresses in hex ('0x004D7A90', '004D7A90' or '4D7A90'), matched against the column -Build names.

.PARAMETER Build
    Which address column -Address searches: '11.6096', '11.4186' or 'any' (the default).

.PARAMETER Like
    Treat -Name as wildcard patterns.

.PARAMETER Strict
    Throw when a query matches nothing, instead of warning (for scripted citation checks).

.PARAMETER TsvPath
    Use this copy of the table instead of the cache one. It must still have the pinned SHA-256.

.EXAMPLE
    pwsh -File tools\research\Find-RetailSymbol.ps1 -Name 'gmSmartBoxUI::UseTime','CameraSet::SetDefaultOffsets'

.EXAMPLE
    pwsh -File tools\research\Find-RetailSymbol.ps1 -Address 0x00458F80 -Build 11.4186

.EXAMPLE
    pwsh -File tools\research\Find-RetailSymbol.ps1 -Name 'CMotionInterp::*jump*' -Like | Format-Table
#>
[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
    [Parameter(ParameterSetName = 'ByName', Mandatory, Position = 0)] [string[]] $Name,
    [Parameter(ParameterSetName = 'ByAddress', Mandatory)] [string[]] $Address,
    [Parameter(ParameterSetName = 'ByAddress')] [ValidateSet('11.6096', '11.4186', 'any')] [string] $Build = 'any',
    [Parameter(ParameterSetName = 'ByName')] [switch] $Like,
    [switch] $Strict,
    [string] $TsvPath = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The pin: one read of Mag-nus/acclient-AI-RE@01ccd5be2d8fff40e25151d804fa97b3e8f27ec6
# '2015-10 11.6096/docs/symbol_transplant_11.6096.tsv' (git blob f0b2aae95c687ece822638208e7152821e2744a3,
# 3,036,979 bytes, fetched 2026-10-12 with gh api). Reference data only (MIT label; the names are Turbine's).
$PinnedSha256 = 'cc19490d457fb8906c3c2bde58bd210830a64d5b632a1d696b401a639ff654d7'
$TableFileName = 'symbol_transplant_11.6096.tsv'

function Find-Table {
    # Every enclosing Lifestone checkout (a folder holding Lifestone.slnx), outermost first: for a git worktree nested
    # inside the main checkout that is the main checkout (the walk Get-LifestoneCheckouts in tools\lib\Toolchain.psm1 does).
    $checkouts = [Collections.Generic.List[string]]::new()
    $dir = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')))
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Lifestone.slnx') -PathType Leaf) { $checkouts.Insert(0, $dir.FullName) }
        $dir = $dir.Parent
    }
    $tried = [Collections.Generic.List[string]]::new()
    foreach ($checkout in $checkouts) {
        $candidate = Join-Path $checkout "cache\research\$TableFileName"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        $tried.Add($candidate)
    }
    throw ("The pinned symbol table was not found. Tried:`n  " + ($tried -join "`n  ") +
        "`nFetch it once as docs/research/retail-addresses-m2.md ('The pin') describes, or pass -TsvPath.")
}

function Split-Query([string[]] $items) {
    # 'pwsh -File' passes 'a,b' as one string: split on commas outside template brackets ('X<a,b>::f' stays whole).
    foreach ($item in $items) {
        $depth = 0
        $start = 0
        for ($i = 0; $i -lt $item.Length; $i++) {
            switch ($item[$i]) {
                '<' { $depth++ }
                '>' { if ($depth -gt 0) { $depth-- } }
                ',' { if ($depth -eq 0) { $item.Substring($start, $i - $start).Trim(); $start = $i + 1 } }
            }
        }
        $item.Substring($start).Trim()
    }
}

function ConvertTo-Va([string] $text) {
    $t = $text.Trim()
    if ($t -match '^(0x)?([0-9A-Fa-f]{1,8})$') { return ('0x{0:X8}' -f [Convert]::ToUInt32($Matches[2], 16)) }
    throw "Not a hex address: '$text'"
}

$path = if ($TsvPath) {
    if (-not (Test-Path -LiteralPath $TsvPath -PathType Leaf)) { throw "-TsvPath '$TsvPath' does not exist." }
    (Resolve-Path -LiteralPath $TsvPath).Path
} else { Find-Table }

$actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $PinnedSha256) {
    throw "Refusing to read '$path': its SHA-256 is $actual, not the pinned $PinnedSha256. Re-fetch the pinned file."
}
Write-Verbose "Symbol table: $path (SHA-256 verified)"

$rows = foreach ($line in [IO.File]::ReadLines($path)) {
    if ($line.StartsWith('va_', [StringComparison]::Ordinal)) { continue }
    $f = $line.Split("`t")
    if ($f.Count -lt 7) { continue }
    [pscustomobject]@{
        Name = $f[2]; Va6096 = $f[0]; Va4186 = $f[1]; Module = $f[3]; Confidence = $f[4]; Candidates = $f[5]; AltName = $f[6]
    }
}

$missing = [Collections.Generic.List[string]]::new()
if ($PSCmdlet.ParameterSetName -eq 'ByName') {
    foreach ($query in @(Split-Query $Name | Where-Object { $_ })) {
        $hits = @(if ($Like) {
                $rows | Where-Object { ($_.Name -and $_.Name -clike $query) -or ($_.AltName -and $_.AltName -clike $query) }
            } else {
                $rows | Where-Object { $_.Name -ceq $query -or $_.AltName -ceq $query }
            })
        if ($hits.Count -eq 0) { $missing.Add($query) } else { $hits }
    }
} else {
    foreach ($query in @(Split-Query $Address | Where-Object { $_ })) {
        $va = ConvertTo-Va $query
        $hits = @($rows | Where-Object {
                ($Build -ne '11.4186' -and $_.Va6096 -eq $va) -or ($Build -ne '11.6096' -and $_.Va4186 -eq $va)
            })
        if ($hits.Count -eq 0) { $missing.Add("$va ($Build)") } else { $hits }
    }
}

if ($missing.Count -gt 0) {
    $message = 'No row in the pinned table for: ' + ($missing -join ', ')
    if ($Strict) { throw $message }
    Write-Warning $message
}
