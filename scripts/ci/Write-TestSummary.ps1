<#
.SYNOPSIS
  Summarizes the test results (.trx files): totals per test project and every
  failed test with the first line of its message.

.DESCRIPTION
  Reads every .trx under -ResultsDirectory (dotnet test --logger trx). Writes a
  Markdown summary to $env:GITHUB_STEP_SUMMARY when it is set (GitHub Actions)
  and a plain summary to the console always.

  Always exits with 0: failing tests already make `dotnet test` fail, and this
  script runs after it (if: always()) only to report. Works in PowerShell 7 and
  Windows PowerShell 5.1.

.EXAMPLE
  ./scripts/ci/Write-TestSummary.ps1 -ResultsDirectory TestResults
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $ResultsDirectory,

  # Longer lists are cut so the summary stays readable; every result stays in the .trx artifact.
  [int] $MaxFailuresListed = 50
)

$ErrorActionPreference = 'Stop'
$ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }

function Get-FirstLine([string] $text, [int] $max = 300) {
  if ([string]::IsNullOrWhiteSpace($text)) { return '' }
  $line = ($text -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1).Trim()
  if ($line.Length -gt $max) { $line = $line.Substring(0, $max) + '...' }
  return $line
}

# Markdown table cells: no pipes, no raw HTML, no line breaks.
function ConvertTo-Cell([string] $text) {
  return ($text -replace '\|', '\|' -replace '<', '&lt;' -replace '>', '&gt;' -replace "`r?`n", ' ')
}

function Get-Count($counters, [string] $name) {
  if ($null -eq $counters) { return 0 }
  $value = $counters.GetAttribute($name)
  if ([string]::IsNullOrEmpty($value)) { return 0 }
  return [int] $value
}

$files = @()
if (Test-Path -LiteralPath $ResultsDirectory) {
  $files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter *.trx | Sort-Object FullName)
}

$projects = @()
$failures = @()

foreach ($file in $files) {
  [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw

  # The test project is the assembly the tests were loaded from (codeBase keeps its casing; storage is lower case).
  $method = Select-Xml -Xml $trx -XPath '//t:TestDefinitions/t:UnitTest/t:TestMethod' -Namespace $ns | Select-Object -First 1
  $project = $file.BaseName
  if ($method -and $method.Node.GetAttribute('codeBase')) {
    $project = [IO.Path]::GetFileNameWithoutExtension($method.Node.GetAttribute('codeBase'))
  }

  $countersNode = Select-Xml -Xml $trx -XPath '//t:ResultSummary/t:Counters' -Namespace $ns | Select-Object -First 1
  $counters = $null
  if ($countersNode) { $counters = $countersNode.Node }

  $total = Get-Count $counters 'total'
  $passed = Get-Count $counters 'passed'
  $failed = (Get-Count $counters 'failed') + (Get-Count $counters 'error') + (Get-Count $counters 'timeout') + (Get-Count $counters 'aborted')
  $skipped = (Get-Count $counters 'notExecuted') + (Get-Count $counters 'inconclusive')

  $projects += [pscustomobject]@{ Project = $project; Total = $total; Passed = $passed; Failed = $failed; Skipped = $skipped }

  foreach ($result in Select-Xml -Xml $trx -XPath '//t:Results/t:UnitTestResult' -Namespace $ns) {
    $node = $result.Node
    if (@('Failed', 'Error', 'Timeout', 'Aborted') -notcontains $node.GetAttribute('outcome')) { continue }

    $message = Select-Xml -Xml $node -XPath './t:Output/t:ErrorInfo/t:Message' -Namespace $ns | Select-Object -First 1
    $text = ''
    if ($message) { $text = $message.Node.InnerText }
    $failures += [pscustomobject]@{ Project = $project; Test = $node.GetAttribute('testName'); Message = (Get-FirstLine $text) }
  }
}

$sum = [pscustomobject]@{
  Total   = ($projects | Measure-Object -Property Total -Sum).Sum
  Passed  = ($projects | Measure-Object -Property Passed -Sum).Sum
  Failed  = ($projects | Measure-Object -Property Failed -Sum).Sum
  Skipped = ($projects | Measure-Object -Property Skipped -Sum).Sum
}
if ($null -eq $sum.Total) { $sum.Total = 0; $sum.Passed = 0; $sum.Failed = 0; $sum.Skipped = 0 }

# --- Console ---
if ($files.Count -eq 0) {
  Write-Host "No .trx files under '$ResultsDirectory': the tests did not run (see the build and test steps)."
}
else {
  $projects | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
  Write-Host "Total: $($sum.Total)  Passed: $($sum.Passed)  Failed: $($sum.Failed)  Skipped: $($sum.Skipped)"
  foreach ($failure in $failures) {
    Write-Host "FAILED [$($failure.Project)] $($failure.Test)" -ForegroundColor Red
    if ($failure.Message) { Write-Host "    $($failure.Message)" }
  }
}

# --- GitHub job summary ---
if ($env:GITHUB_STEP_SUMMARY) {
  # Leading blank line: other steps append their own sections to the same summary.
  $lines = @('', '### Tests', '')
  if ($files.Count -eq 0) {
    $lines += 'No test results were produced: the tests did not run. Check the build and test steps.'
  }
  else {
    $status = $(if ($sum.Failed -gt 0) { "**$($sum.Failed) failed**" } else { 'all passed' })
    $lines += "$($sum.Total) tests, $status, $($sum.Skipped) skipped."
    $lines += ''
    $lines += '| Project | Total | Passed | Failed | Skipped |'
    $lines += '|---|---:|---:|---:|---:|'
    foreach ($p in $projects) {
      $lines += "| $(ConvertTo-Cell $p.Project) | $($p.Total) | $($p.Passed) | $($p.Failed) | $($p.Skipped) |"
    }

    if ($failures.Count -gt 0) {
      $lines += ''
      $lines += '#### Failed tests'
      $lines += ''
      $lines += '| Project | Test | Message |'
      $lines += '|---|---|---|'
      foreach ($failure in $failures | Select-Object -First $MaxFailuresListed) {
        $lines += "| $(ConvertTo-Cell $failure.Project) | $(ConvertTo-Cell $failure.Test) | $(ConvertTo-Cell $failure.Message) |"
      }
      if ($failures.Count -gt $MaxFailuresListed) {
        $lines += ''
        $lines += "... and $($failures.Count - $MaxFailuresListed) more. Every result is in the test-results artifact."
      }
    }
  }

  Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n") -Encoding utf8
}

exit 0
