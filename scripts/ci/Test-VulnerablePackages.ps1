<#
.SYNOPSIS
  Lists the NuGet packages with known vulnerabilities, direct and transitive,
  and fails on high or critical severity.

.DESCRIPTION
  Runs `dotnet list package --vulnerable --include-transitive --format json`
  on the solution (restore it first) and prints one row per package, version
  and advisory, with the projects that use it. Exits with 1 when any advisory
  is High or Critical, or when the command itself fails; Low and Moderate are
  reported without failing (RNF-SEG-11, spec 05 R3.2).

  Writes the table to $env:GITHUB_STEP_SUMMARY when it is set (GitHub Actions)
  and to the console always. Works in PowerShell 7 and Windows PowerShell 5.1.

.EXAMPLE
  dotnet restore MiSaldo.slnx
  ./scripts/ci/Test-VulnerablePackages.ps1

.EXAMPLE
  # Check a saved report instead of running dotnet (used to test this script).
  ./scripts/ci/Test-VulnerablePackages.ps1 -InputJson report.json
#>
[CmdletBinding()]
param(
  [string] $Solution = 'MiSaldo.slnx',
  [string] $InputJson
)

$ErrorActionPreference = 'Stop'
$failingSeverities = @('High', 'Critical')

function Write-Summary([string[]] $lines) {
  if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n") -Encoding utf8
  }
}

if ($InputJson) {
  $raw = Get-Content -LiteralPath $InputJson -Raw
}
else {
  $output = & dotnet list $Solution package --vulnerable --include-transitive --format json 2>&1
  $exit = $LASTEXITCODE
  $raw = ($output | ForEach-Object { "$_" }) -join "`n"
  if ($exit -ne 0) {
    Write-Host $raw
    Write-Host "dotnet list package failed (exit $exit). Restore the solution first; a restore error NU1903/NU1904 already names the vulnerable package." -ForegroundColor Red
    Write-Summary @('', '### Vulnerable packages', '', "**The check could not run** (``dotnet list package`` exit $exit). See the job log.")
    exit 1
  }
}

# The JSON report starts at the first '{'; anything before it is tool noise.
$start = $raw.IndexOf('{')
if ($start -lt 0) {
  Write-Host $raw
  Write-Host 'The vulnerability report is not JSON.' -ForegroundColor Red
  exit 1
}
$report = $raw.Substring($start) | ConvertFrom-Json

$problems = @($report.problems | Where-Object { $_ -and $_.level -eq 'error' })
foreach ($problem in $problems) {
  Write-Host "ERROR $($problem.project): $($problem.text)" -ForegroundColor Red
}

# One entry per package, version and advisory, with every project that uses it.
$found = @{}
foreach ($project in @($report.projects)) {
  $projectName = [IO.Path]::GetFileNameWithoutExtension([string] $project.path)
  foreach ($framework in @($project.frameworks)) {
    if ($null -eq $framework) { continue }
    $packages = @()
    $packages += @($framework.topLevelPackages | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Package = $_; Kind = 'direct' } })
    $packages += @($framework.transitivePackages | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Package = $_; Kind = 'transitive' } })
    foreach ($entry in $packages) {
      foreach ($vulnerability in @($entry.Package.vulnerabilities | Where-Object { $_ })) {
        $key = "$($entry.Package.id)|$($entry.Package.resolvedVersion)|$($vulnerability.advisoryurl)"
        if (-not $found.ContainsKey($key)) {
          $found[$key] = [pscustomobject]@{
            Package  = $entry.Package.id
            Version  = $entry.Package.resolvedVersion
            Severity = $vulnerability.severity
            Kind     = $entry.Kind
            Advisory = $vulnerability.advisoryurl
            Projects = New-Object System.Collections.Generic.List[string]
          }
        }
        if (-not $found[$key].Projects.Contains($projectName)) { $found[$key].Projects.Add($projectName) }
      }
    }
  }
}

$order = @{ Critical = 0; High = 1; Moderate = 2; Low = 3 }
$rows = @($found.Values | Sort-Object @{ Expression = { $s = $order[$_.Severity]; if ($null -eq $s) { 9 } else { $s } } }, Package)
$blocking = @($rows | Where-Object { $failingSeverities -contains $_.Severity })

if ($rows.Count -eq 0) {
  Write-Host 'No vulnerable packages found (direct or transitive).'
}
else {
  $rows | Select-Object Severity, Package, Version, Kind, Advisory, @{ n = 'Projects'; e = { $_.Projects -join ', ' } } |
    Format-Table -AutoSize -Wrap | Out-String -Width 250 | Write-Host
}

$lines = @('', '### Vulnerable packages', '')
if ($rows.Count -eq 0) {
  $lines += 'No vulnerable packages found (direct or transitive).'
}
else {
  $lines += '| Severity | Package | Version | Kind | Advisory | Projects |'
  $lines += '|---|---|---|---|---|---|'
  foreach ($row in $rows) {
    $severity = $(if ($failingSeverities -contains $row.Severity) { "**$($row.Severity)**" } else { $row.Severity })
    $lines += "| $severity | $($row.Package) | $($row.Version) | $($row.Kind) | $($row.Advisory) | $($row.Projects -join ', ') |"
  }
  $lines += ''
  $lines += 'High and Critical fail the pipeline; Low and Moderate are reported only.'
}
Write-Summary $lines

if ($blocking.Count -gt 0 -or $problems.Count -gt 0) {
  foreach ($row in $blocking) {
    Write-Host "$($row.Severity): $($row.Package) $($row.Version) ($($row.Advisory))" -ForegroundColor Red
  }
  exit 1
}

if ($rows.Count -gt 0) {
  Write-Host "$($rows.Count) low or moderate advisories reported; none blocks the pipeline." -ForegroundColor Yellow
}
exit 0
