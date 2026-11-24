<#
.SYNOPSIS
  Checks line coverage per assembly against the minimums of RNF-MAN-03.

.DESCRIPTION
  Reads the Summary.json that ReportGenerator writes (report type JsonSummary)
  after merging the coverage of every test project, prints the measured line
  coverage of each assembly next to its minimum, and exits with 1 when one is
  below its minimum or has no coverage data. The minimums live only here.

  Writes the table to $env:GITHUB_STEP_SUMMARY when it is set (GitHub Actions)
  and to the console always. Works in PowerShell 7 and Windows PowerShell 5.1.

.EXAMPLE
  ./scripts/ci/Test-Coverage.ps1 -SummaryJson coverage-report/Summary.json
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $SummaryJson,

  # RNF-MAN-03: Domain >= 90 %, Application >= 70 %.
  [double] $DomainMinimum = 90,
  [double] $ApplicationMinimum = 70
)

$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture

function Format-Percent([double] $value) {
  return [string]::Format($invariant, '{0:0.00} %', $value)
}

if (-not (Test-Path -LiteralPath $SummaryJson)) {
  Write-Error "Coverage summary not found: $SummaryJson. Run ReportGenerator with -reporttypes:JsonSummary first."
  exit 1
}

$summary = Get-Content -LiteralPath $SummaryJson -Raw | ConvertFrom-Json
$assemblies = @($summary.coverage.assemblies)

$minimums = [ordered]@{
  'MiSaldo.Domain'      = $DomainMinimum
  'MiSaldo.Application' = $ApplicationMinimum
}

$rows = @()
$failed = $false
foreach ($name in $minimums.Keys) {
  $minimum = [double] $minimums[$name]
  $assembly = $assemblies | Where-Object { $_.name -eq $name } | Select-Object -First 1

  if ($null -eq $assembly -or [double] $assembly.coverablelines -le 0) {
    $rows += [pscustomobject]@{ Assembly = $name; Covered = '-'; Measured = 'no data'; Minimum = (Format-Percent $minimum); Result = 'FAIL' }
    $failed = $true
    continue
  }

  # Computed from the line counts so the comparison does not depend on ReportGenerator's rounding.
  $measured = 100.0 * [double] $assembly.coveredlines / [double] $assembly.coverablelines
  $ok = $measured -ge $minimum
  if (-not $ok) { $failed = $true }

  $rows += [pscustomobject]@{
    Assembly = $name
    Covered  = "$($assembly.coveredlines)/$($assembly.coverablelines)"
    Measured = (Format-Percent $measured)
    Minimum  = (Format-Percent $minimum)
    Result   = $(if ($ok) { 'ok' } else { 'FAIL' })
  }
}

$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = @('### Line coverage (RNF-MAN-03)', '', '| Assembly | Covered lines | Measured | Minimum | Result |', '|---|---|---|---|---|')
  foreach ($row in $rows) {
    $result = $(if ($row.Result -eq 'ok') { 'ok' } else { '**FAIL**' })
    $lines += "| $($row.Assembly) | $($row.Covered) | $($row.Measured) | $($row.Minimum) | $result |"
  }
  Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n") -Encoding utf8
}

if ($failed) {
  foreach ($row in $rows | Where-Object { $_.Result -eq 'FAIL' }) {
    if ($row.Measured -eq 'no data') {
      Write-Host "No coverage data for $($row.Assembly): check that coverage.runsettings includes it and that its tests ran." -ForegroundColor Red
    }
    else {
      Write-Host "Coverage of $($row.Assembly) is $($row.Measured), below the minimum of $($row.Minimum)." -ForegroundColor Red
    }
  }
  exit 1
}

Write-Host 'Coverage meets every minimum.'
exit 0
