[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Iterations = 20,
    [ValidateRange(0, 10)]
    [int]$WarmupIterations = 2,
    [switch]$SkipBuild,
    [switch]$EnforcePerformanceBudgets,
    [switch]$ShowSandboxWindow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$launcher = Join-Path $PSScriptRoot "Launch-WinQuickSwitchSandbox.ps1"
$runs = [System.Collections.Generic.List[object]]::new()
foreach ($edition in @("Paid", "Free")) {
    $arguments = @{
        Edition = $edition
        Performance = $true
        PerformanceIterations = $Iterations
        PerformanceWarmupIterations = $WarmupIterations
        SkipBuild = [bool]$SkipBuild
        EnforcePerformanceBudgets = [bool]$EnforcePerformanceBudgets
        ShowSandboxWindow = [bool]$ShowSandboxWindow
        ReturnFailedResult = $true
    }
    $output = @(& $launcher @arguments)
    $run = $output | Where-Object {
        $null -ne $_.PSObject.Properties["Results"]
    } | Select-Object -Last 1
    if ($null -eq $run) {
        throw "$edition performance run did not return a result object."
    }
    $performance = Get-Content -Raw -LiteralPath $run.Performance |
        ConvertFrom-Json
    $runs.Add([pscustomobject]@{
        Edition = $edition
        Run = $run
        Performance = $performance
    })
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$version = $runs[0].Performance.Version
$comparisonRoot = Join-Path $repositoryRoot (
    "artifacts\sandbox-performance\comparison\{0}-v{1}" -f
        (Get-Date -Format "yyyyMMdd-HHmmss"),
        $version)
New-Item -ItemType Directory -Path $comparisonRoot -Force | Out-Null

$rows = foreach ($run in $runs) {
    foreach ($stage in $run.Performance.StageSummary) {
        [pscustomobject]@{
            Edition = $run.Edition
            Stage = $stage.Name
            Count = $stage.Count
            MedianMilliseconds = $stage.MedianMilliseconds
            P95Milliseconds = $stage.P95Milliseconds
            MaximumMilliseconds = $stage.MaximumMilliseconds
        }
    }
}
$csvPath = Join-Path $comparisonRoot "performance-summary.csv"
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

$memoryRows = foreach ($run in $runs) {
    foreach ($session in $run.Performance.Sessions) {
        [pscustomobject]@{
            Edition = $run.Edition
            Session = $session.Name
            Samples = $session.Summary.SampleCount
            WorkloadSamples = $session.Summary.WorkloadSampleCount
            InitialPrivateMB = [Math]::Round(
                $session.Summary.InitialPrivateMemoryBytes / 1MB, 2)
            PeakPrivateMB = [Math]::Round(
                $session.Summary.PeakPrivateMemoryBytes / 1MB, 2)
            FinalPrivateMB = [Math]::Round(
                $session.Summary.FinalPrivateMemoryBytes / 1MB, 2)
            DeltaMB = [Math]::Round(
                $session.Summary.PrivateMemoryDeltaBytes / 1MB, 2)
            TrendMB = if ($session.Summary.ResourceTrendAvailable) { [Math]::Round(
                $session.Summary.PrivateMemoryTrendBytes / 1MB, 2) } else { $null }
            PeakWorkingSetMB = [Math]::Round(
                $session.Summary.PeakWorkingSetBytes / 1MB, 2)
            HandleDelta = $session.Summary.HandleDelta
            HandleTrend = $session.Summary.HandleTrend
            ThreadDelta = $session.Summary.ThreadDelta
            ThreadTrend = $session.Summary.ThreadTrend
            ResourceWindowIterations = $session.Summary.ResourceWindowIterations
            ResourceTrendAvailable = $session.Summary.ResourceTrendAvailable
            CpuMilliseconds = [Math]::Round(
                $session.Summary.CpuTimeMilliseconds, 2)
        }
    }
}
$memoryCsvPath = Join-Path $comparisonRoot "memory-summary.csv"
$memoryRows | Export-Csv -LiteralPath $memoryCsvPath -NoTypeInformation -Encoding utf8

$coverageRows = foreach ($run in $runs) {
    [pscustomobject]@{
        Edition = $run.Edition
        ExpectedStages = @($run.Performance.Coverage.ExpectedStages).Count
        ObservedStages = @($run.Performance.Coverage.ObservedStages).Count
        MissingStages = @($run.Performance.Coverage.MissingStages).Count
        Passed = [bool]$run.Performance.Coverage.Passed
    }
}
$coverageCsvPath = Join-Path $comparisonRoot "coverage-summary.csv"
$coverageRows | Export-Csv -LiteralPath $coverageCsvPath -NoTypeInformation -Encoding utf8

function ConvertTo-HtmlRows([object[]]$Items, [string[]]$Properties) {
    foreach ($item in $Items) {
        $cells = foreach ($property in $Properties) {
            $value = [System.Net.WebUtility]::HtmlEncode(
                [string]$item.$property)
            "<td>$value</td>"
        }
        "<tr>$($cells -join '')</tr>"
    }
}

$speedProperties = @(
    "Edition", "Stage", "Count", "MedianMilliseconds",
    "P95Milliseconds", "MaximumMilliseconds")
$memoryProperties = @(
    "Edition", "Session", "Samples", "WorkloadSamples", "InitialPrivateMB",
    "PeakPrivateMB", "FinalPrivateMB", "DeltaMB", "TrendMB",
    "PeakWorkingSetMB", "HandleDelta", "HandleTrend", "ThreadDelta",
    "ThreadTrend", "ResourceWindowIterations", "ResourceTrendAvailable", "CpuMilliseconds")
$coverageProperties = @(
    "Edition", "ExpectedStages", "ObservedStages", "MissingStages", "Passed")
$speedHeader = ($speedProperties | ForEach-Object { "<th>$_</th>" }) -join ""
$memoryHeader = ($memoryProperties | ForEach-Object { "<th>$_</th>" }) -join ""
$coverageHeader = ($coverageProperties | ForEach-Object { "<th>$_</th>" }) -join ""
$speedBody = (ConvertTo-HtmlRows @($rows) $speedProperties) -join [Environment]::NewLine
$memoryBody = (ConvertTo-HtmlRows @($memoryRows) $memoryProperties) -join [Environment]::NewLine
$coverageBody = (ConvertTo-HtmlRows @($coverageRows) $coverageProperties) -join [Environment]::NewLine
$paidResults = [System.Net.WebUtility]::HtmlEncode(
    ([System.Uri]::new([string]$runs[0].Run.Gallery)).AbsoluteUri)
$freeResults = [System.Net.WebUtility]::HtmlEncode(
    ([System.Uri]::new([string]$runs[1].Run.Gallery)).AbsoluteUri)
$htmlPath = Join-Path $comparisonRoot "performance.html"
@"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>WinQuickSwitch performance comparison v$version</title>
  <style>
    :root { color-scheme: dark; font-family: "Segoe UI", sans-serif; }
    body { margin: 0; padding: 28px; background: #111; color: #f3f3f3; }
    section { margin: 24px 0; padding: 18px; background: #1d1d1d; border-radius: 10px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 8px; border-bottom: 1px solid #444; text-align: left; }
    a { color: #60a5fa; }
  </style>
</head>
<body>
  <h1>WinQuickSwitch Paid and Free performance</h1>
  <p>v$version &middot; $Iterations measured iterations &middot; $WarmupIterations warm-up iterations</p>
  <p><a href="$paidResults">Paid artifacts</a> &middot; <a href="$freeResults">Free artifacts</a> &middot; <a href="performance-summary.csv">speed CSV</a> &middot; <a href="memory-summary.csv">memory CSV</a> &middot; <a href="coverage-summary.csv">coverage CSV</a></p>
  <section><h2>Edition-aware coverage</h2><table><thead><tr>$coverageHeader</tr></thead><tbody>$coverageBody</tbody></table></section>
  <section><h2>Speed</h2><table><thead><tr>$speedHeader</tr></thead><tbody>$speedBody</tbody></table></section>
  <section><h2>Memory and resources</h2><p>Budgets enforce the stable post-warm-up to final resource delta. Trend is additional diagnostic evidence from disjoint early and late windows of up to three measured iterations. A single iteration has no independent trend window.</p><table><thead><tr>$memoryHeader</tr></thead><tbody>$memoryBody</tbody></table></section>
</body>
</html>
"@ | Set-Content -LiteralPath $htmlPath -Encoding utf8

$comparisonOutput = [pscustomobject]@{
    Version = $version
    PaidPassed = [bool]$runs[0].Run.Passed
    FreePassed = [bool]$runs[1].Run.Passed
    Report = $htmlPath
    SpeedCsv = $csvPath
    MemoryCsv = $memoryCsvPath
    CoverageCsv = $coverageCsvPath
    PaidGallery = $runs[0].Run.Gallery
    FreeGallery = $runs[1].Run.Gallery
}
$comparisonOutput
if (-not $comparisonOutput.PaidPassed -or -not $comparisonOutput.FreePassed) {
    throw "One or more performance suites failed. Review $htmlPath."
}
