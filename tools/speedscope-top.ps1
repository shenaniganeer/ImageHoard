<#.SYNOPSIS
  Summarize dotnet-trace Speedscope JSON exports: inclusive time per frame (O/C event pairing).

.DESCRIPTION
  Parses profiles[0].events (type O/C, frame index, at seconds) and ranks frames by inclusive duration.
  Totals are inclusive (ancestors double-count); use for comparing traces and ranking hot symbols, not exclusive wall time.

.PARAMETER Path
  One or more .speedscope.json files.

.PARAMETER Top
  Number of rows to print (default 25).

.PARAMETER Filter
  Case-insensitive substring filter on frame name; if omitted, all frames are ranked.

.EXAMPLE
  .\tools\speedscope-top.ps1 -Path .\ImageHoard.App.exe_20260513_201131.speedscope.json -Top 40 -Filter ImageHoard
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]] $Path,

    [Parameter()]
    [int] $Top = 25,

    [Parameter()]
    [string] $Filter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-InclusiveTotals {
    param(
        [object[]] $Events,
        [string[]] $FrameNames
    )

    $stack = [System.Collections.Generic.Stack[object]]::new()
    $totals = @{}

    foreach ($ev in $Events) {
        $t = $ev.type
        $frameIdx = [int]$ev.frame
        $at = [double]$ev.at

        if ($t -eq "O") {
            $stack.Push(@($frameIdx, $at))
        }
        elseif ($t -eq "C") {
            if ($stack.Count -eq 0) { continue }
            $top = $stack.Pop()
            $openFrame = [int]$top[0]
            $openAt = [double]$top[1]
            $dt = $at - $openAt
            if ($dt -lt 0) { $dt = 0 }

            $name = $FrameNames[$openFrame]
            if ($null -eq $name) { $name = "<unknown>" }

            if ($totals.ContainsKey($name)) {
                $totals[$name] += $dt
            }
            else {
                $totals[$name] = $dt
            }
        }
    }

    return $totals
}

$allFiles = @()
foreach ($p in $Path) {
    $r = Resolve-Path -LiteralPath $p
    $allFiles += $r
}

foreach ($file in $allFiles) {
    Write-Host ""
    Write-Host "=== $($file.Path)" -ForegroundColor Cyan

    $json = Get-Content -LiteralPath $file.Path -Raw | ConvertFrom-Json
    if ($null -eq $json.shared -or $null -eq $json.shared.frames) {
        Write-Warning "Missing shared.frames; not a recognized Speedscope export."
        continue
    }

    $frames = @($json.shared.frames | ForEach-Object { $_.name })
    $prof = $json.profiles[0]
    if ($null -eq $prof -or $null -eq $prof.events) {
        Write-Warning "Missing profiles[0].events."
        continue
    }

    $events = @($prof.events)
    $totals = Get-InclusiveTotals -Events $events -FrameNames $frames

    $rows = foreach ($kv in $totals.GetEnumerator()) {
        [pscustomobject]@{ Name = $kv.Key; InclusiveSeconds = [double]$kv.Value }
    }

    if ($Filter) {
        $fl = $Filter.ToLowerInvariant()
        $rows = $rows | Where-Object { $_.Name.ToLowerInvariant().Contains($fl) }
    }

    $rows = $rows | Sort-Object -Property InclusiveSeconds -Descending | Select-Object -First $Top
    $rows | Format-Table -AutoSize Name, @{ Label = "Inclusive(s)"; Expression = { "{0:N3}" -f $_.InclusiveSeconds } }
}
