[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Load only pure analysis functions from the actual guest runner. Never execute
# its top-level Windows/Sandbox setup or any platform mutations in this test.
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Run-SandboxUiSuite.ps1'),
    [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Guest runner has syntax errors.' }
foreach ($name in @('Get-Percentile', 'Get-PerformanceResourceWindow',
    'Get-PerformanceResourceWindowSize', 'New-PerformanceResult')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $false)
    if ($null -eq $definition) { throw "Missing analysis function: $name" }
    Invoke-Expression $definition.Extent.Text
}

$script:PassedChecks = 0
function Assert-Analysis([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:PassedChecks++
}

foreach ($count in @(0, 1, 2, 3, 4, 5, 6, 20)) {
    $size = Get-PerformanceResourceWindowSize $count
    Assert-Analysis "Disjoint windows for $count iterations" (
        $size -ge 0 -and $size -le 3 -and 2 * $size -le $count)
}
Assert-Analysis 'One iteration cannot claim an independent trend' (
    (Get-PerformanceResourceWindowSize 1) -eq 0)

$samples = @(
    [pscustomobject]@{Iteration=1; Warmup=$true; PrivateMemoryBytes=999999; HandleCount=999; ThreadCount=99}
    [pscustomobject]@{Iteration=1; Warmup=$false; PrivateMemoryBytes=100; HandleCount=10; ThreadCount=2}
    [pscustomobject]@{Iteration=1; Warmup=$false; PrivateMemoryBytes=110; HandleCount=11; ThreadCount=3}
    [pscustomobject]@{Iteration=1; Warmup=$false; PrivateMemoryBytes=9000; HandleCount=900; ThreadCount=90}
    [pscustomobject]@{Iteration=2; Warmup=$false; PrivateMemoryBytes=200; HandleCount=20; ThreadCount=4}
)
$samples | ForEach-Object { $_ | Add-Member -NotePropertyName Stage -NotePropertyValue 'action' }
$window = Get-PerformanceResourceWindow $samples @(1)
Assert-Analysis 'Warm-up and other iterations excluded' ($window.SampleCount -eq 3)
Assert-Analysis 'Median resists transient peaks' (
    $window.PrivateMemoryBytes -eq 110 -and $window.HandleCount -eq 11 -and $window.ThreadCount -eq 3)
Assert-Analysis 'Missing samples return unavailable' (
    $null -eq (Get-PerformanceResourceWindow $samples @(3)))

$scenario = [pscustomobject]@{Edition='Free'; Version='test'}
$coverage = [pscustomobject]@{features=@(
    [pscustomobject]@{editions=@('Free'); performanceStages=@('main.refresh.audio')}
)}
$script:PerformanceWarmupIterations = 2
$script:PerformanceIterations = 1
$script:EnforcePerformanceBudgets = $true
$script:PerformanceStages = @(
    [pscustomobject]@{Name='main.refresh.audio'; Warmup=$false; DurationMilliseconds=10; Passed=$true}
    [pscustomobject]@{Name='main.refresh.audio'; Warmup=$true; DurationMilliseconds=99999; Passed=$true}
)
foreach ($case in @(
    @{Name='Exact limits pass'; Memory=15MB; Handles=10; Threads=2; Pass=$true}
    @{Name='Retained memory fails even with flat trend'; Memory=(15MB+1); Handles=0; Threads=0; Pass=$false}
    @{Name='Retained handles fail even with flat trend'; Memory=0; Handles=11; Threads=0; Pass=$false}
    @{Name='Retained threads fail even with flat trend'; Memory=0; Handles=0; Threads=3; Pass=$false}
)) {
    $script:PerformanceSessions = @([pscustomobject]@{Name='main'; ResourceCoverage=[pscustomobject]@{Passed=$true}; Summary=[pscustomobject]@{
        PrivateMemoryDeltaBytes=$case.Memory; HandleDelta=$case.Handles; ThreadDelta=$case.Threads
        PrivateMemoryTrendBytes=0; HandleTrend=0; ThreadTrend=0
    }})
    $result = New-PerformanceResult
    Assert-Analysis $case.Name ($result.Budgets.Passed -eq $case.Pass)
}
Assert-Analysis 'Warm-up timing excluded' ($result.StageSummary[0].P95Milliseconds -eq 10)
$script:PerformanceSessions[0].ResourceCoverage.Passed = $false
$result = New-PerformanceResult
Assert-Analysis 'Missing resource samples fail coverage' (-not $result.Coverage.Passed)
$script:PerformanceSessions[0].ResourceCoverage.Passed = $true
$samples[1].Stage = 'workload.baseline'
Assert-Analysis 'Boundary samples excluded from trend' ((Get-PerformanceResourceWindow $samples @(1)).SampleCount -eq 2)
$script:PerformanceStages = @()
$result = New-PerformanceResult
Assert-Analysis 'Missing stage still fails coverage' (-not $result.Coverage.Passed)
Write-Host "Performance analysis: $script:PassedChecks passed, 0 failed."
