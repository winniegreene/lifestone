<#
.SYNOPSIS
    Per-test timings from the VSTest trx logger (M3 4.2 p5a, C6): what `dotnet test --logger trx` leaves in a results folder.

.DESCRIPTION
    tools\slice-check.ps1 and tools\smoke.ps1 run the default tests with `--logger "trx;LogFilePrefix=test" --results-directory <folder>`
    (one trx per test assembly; the logger ships with Microsoft.NET.Test.Sdk, no package is added). Get-TrxTimes reads them,
    Format-SlowestLines turns the slowest into the lines the two scripts print or write. Nothing here runs a test, reads a dat or touches
    anything outside the folder it is given.
#>

Set-StrictMode -Version Latest

function Get-TrxTimes {
    <# Every test result of every *.trx under $Directory as @{ Name; Seconds; Outcome }, slowest first. A missing folder gives an empty list. #>
    param([Parameter(Mandatory)] [string] $Directory)
    $rows = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Directory)) { return @() }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File -ErrorAction SilentlyContinue) {
        $document = [xml]::new()
        try { $document.Load($file.FullName) } catch { continue }
        foreach ($result in $document.GetElementsByTagName('UnitTestResult')) {
            $duration = $result.GetAttribute('duration')
            $seconds = 0.0
            $span = [TimeSpan]::Zero
            if ($duration -and [TimeSpan]::TryParse($duration, [Globalization.CultureInfo]::InvariantCulture, [ref] $span)) { $seconds = $span.TotalSeconds }
            $rows.Add([pscustomobject]@{ Name = $result.GetAttribute('testName'); Seconds = $seconds; Outcome = $result.GetAttribute('outcome') })
        }
    }
    return @($rows | Sort-Object -Property Seconds -Descending)
}

function Get-ShortTestName([string] $Name) {
    <# 'Lifestone.Data.Tests.Textures.RenderSurfaceRecordTests.Every_surface(x: 1)' -> 'RenderSurfaceRecordTests.Every_surface' #>
    $bare = ($Name -split '\(', 2)[0]
    $parts = $bare -split '\.'
    if ($parts.Count -ge 2) { return ($parts[-2] + '.' + $parts[-1]) }
    return $bare
}

function Format-SlowestLines {
    <# The $Count slowest tests, one 'seconds  ShortName' line each. #>
    param([object[]] $Times, [int] $Count = 20)
    return @($Times | Select-Object -First $Count | ForEach-Object { '{0,8:0.00}  {1}' -f $_.Seconds, (Get-ShortTestName $_.Name) })
}

function Format-SlowestSummary {
    <# One line: 'top 20 slowest (s): A 66.6, B 58.0, ...' (the first $Show named). #>
    param([object[]] $Times, [int] $Count = 20, [int] $Show = 3)
    if (@($Times).Count -eq 0) { return 'top 20 slowest: no trx results' }
    $named = @($Times | Select-Object -First $Show | ForEach-Object { '{0} {1:0.0}' -f (Get-ShortTestName $_.Name), $_.Seconds }) -join ', '
    $topSum = [math]::Round((@($Times | Select-Object -First $Count | Measure-Object -Property Seconds -Sum).Sum), 1)
    return "top $Count slowest (s): $named; top $Count sum $topSum s of $(@($Times).Count) tests"
}

Export-ModuleMember -Function Get-TrxTimes, Get-ShortTestName, Format-SlowestLines, Format-SlowestSummary
