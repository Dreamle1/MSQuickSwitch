[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$launcher = Join-Path $PSScriptRoot "Launch-WinQuickSwitchSandbox.ps1"
$guest = Join-Path $PSScriptRoot "Run-SandboxUiSuite.ps1"
$comparison = Join-Path $PSScriptRoot "Run-SandboxPerformanceComparison.ps1"
$coveragePath = Join-Path $PSScriptRoot "FeatureCoverage.json"
$runtime = Join-Path $PSScriptRoot "..\..\src\WinQuickSwitch\E2E\SandboxE2eRuntime.cs"
$contractTests = Join-Path $PSScriptRoot "..\..\tests\WinQuickSwitch.Tests\Program.cs"
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($file in @($launcher, $guest, $comparison)) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $file,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    foreach ($parseError in $parseErrors) {
        $failures.Add(("{0}: {1}" -f $file, $parseError.Message))
    }
}

$launcherText = Get-Content -Raw -LiteralPath $launcher
$guestText = Get-Content -Raw -LiteralPath $guest
$comparisonText = Get-Content -Raw -LiteralPath $comparison
$runtimeText = Get-Content -Raw -LiteralPath $runtime
$contractTestText = Get-Content -Raw -LiteralPath $contractTests
$coverage = Get-Content -Raw -LiteralPath $coveragePath | ConvertFrom-Json

function Assert-RequiredText(
    [string]$Label,
    [string]$Text,
    [string]$Requirement,
    [string[]]$AnyOf) {
    $matched = $false
    foreach ($candidate in $AnyOf) {
        if ($Text.IndexOf($candidate, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $failures.Add("$Label is missing the performance contract for $Requirement (expected one of: $($AnyOf -join ', ')).")
    }
}

$requiredLauncherText = @(
    "<Networking>Disable</Networking>",
    "<ClipboardRedirection>Disable</ClipboardRedirection>",
    "<ReadOnly>true</ReadOnly>",
    "Run-SandboxUiSuite.ps1",
    "FeatureCoverage.json",
    "WinQuickSwitchE2E=true",
    "results.json",
    "index.html",
    "StartGuard",
    "Stop-SandboxClientProcesses",
    "HiddenWindowCount",
    "RemainingVisibleWindowCount",
    "KeepSandboxOpen is only supported with ShowSandboxWindow"
)
foreach ($expected in $requiredLauncherText) {
    if ($launcherText.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) {
        $failures.Add("Launcher is missing required text: $expected")
    }
}

foreach ($forbidden in @("CloseMainWindow()")) {
    if ($launcherText.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        $failures.Add("Launcher contains a disruptive Sandbox teardown action: $forbidden")
    }
}

$requiredGuestText = @(
    "--attended-validation",
    "--sandbox-e2e",
    "service-journal.jsonl",
    "Validate-ServiceJournal",
    "overlay-dark-workspace",
    "main-dark-",
    "main-light-",
    "Use dark theme",
    "Edition-aware overlay navigation",
    "Main Ctrl+number navigation follows the edition",
    "Overlay arrow keys move through the launcher",
    "Overlay Ctrl+number and Escape navigation follows the edition",
    "Overlay supports a constrained responsive work area",
    "overlay-light-constrained-workspace",
    "WINQUICKSWITCH_E2E_HIGH_CONTRAST_MARKER",
    "High Contrast enabled for main window",
    "High Contrast enabled for overlay",
    "interactive controls have accessible names",
    "High Contrast label"
)
foreach ($expected in $requiredGuestText) {
    if ($guestText.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) {
        $failures.Add("Guest runner is missing required scenario text: $expected")
    }
}

# Performance mode is deliberately validated at the harness boundary.  This
# keeps performance runs opt-in and makes the report contract visible before a
# Sandbox is started.  Candidate spellings are accepted so the host and guest
# can use their natural PowerShell parameter/field names without weakening the
# required behavior.
Assert-RequiredText "Launcher" $launcherText "performance mode" @(
    "PerformanceMode", "-Performance", "-PerformanceMode")
Assert-RequiredText "Launcher" $launcherText "performance report" @(
    "performance.json")
Assert-RequiredText "Launcher" $launcherText "performance iteration scenario field" @(
    "PerformanceIterations", "IterationCount", "PerformanceIterationCount")
Assert-RequiredText "Launcher" $launcherText "performance warmup scenario field" @(
    "PerformanceWarmupIterations", "WarmupIterations", "WarmupCount")

Assert-RequiredText "Guest runner" $guestText "performance mode" @(
    "PerformanceMode", "-Performance", "-PerformanceMode")
Assert-RequiredText "Guest runner" $guestText "performance report" @(
    "performance.json")
Assert-RequiredText "Guest runner" $guestText "process sampling" @(
    "WorkingSet64", "PrivateMemorySize64", "HandleCount", "Get-ProcessSample", "ProcessSample")
Assert-RequiredText "Guest runner" $guestText "timing stages" @(
    "Measure-Stage", "Measure-PerformanceStage", "TimedStage", "Stopwatch")
Assert-RequiredText "Guest runner" $guestText "performance iteration scenario field" @(
    "PerformanceIterations", "IterationCount", "PerformanceIterationCount")
Assert-RequiredText "Guest runner" $guestText "performance warmup scenario field" @(
    "PerformanceWarmupIterations", "WarmupIterations", "WarmupCount")
Assert-RequiredText "Guest runner" $guestText "performance parity result" @(
    "MissingStages", "ExpectedStages")
Assert-RequiredText "Guest runner" $guestText "audio row readiness after volume refresh" @(
    "Wait-AudioSessionRowReady")
Assert-RequiredText "Guest runner" $guestText "settled performance boundary sampling" @(
    "Get-SettledPerformanceProcessSnapshot")
Assert-RequiredText "Guest runner" $guestText "iteration-aware resource samples" @(
    "SetContext", "ResourceWindowIterations")
Assert-RequiredText "Guest runner" $guestText "measured-window resource diagnostics" @(
    "PrivateMemoryTrendBytes", "HandleTrend", "ThreadTrend")
Assert-RequiredText "Comparison runner" $comparisonText "Paid and Free editions" @(
    'foreach ($edition in @("Paid", "Free"))', 'foreach ($edition in @(')
Assert-RequiredText "Comparison runner" $comparisonText "speed CSV" @(
    "performance-summary.csv")
Assert-RequiredText "Comparison runner" $comparisonText "memory CSV" @(
    "memory-summary.csv")
Assert-RequiredText "Comparison runner" $comparisonText "coverage CSV" @(
    "coverage-summary.csv")
Assert-RequiredText "Comparison runner" $comparisonText "peak working set" @(
    "PeakWorkingSetMB", "PeakWorkingSetBytes")
Assert-RequiredText "Comparison runner" $comparisonText "sample counts" @(
    "WorkloadSamples", "WorkloadSampleCount")
Assert-RequiredText "Comparison runner" $comparisonText "resource trend columns" @(
    "TrendMB", "HandleTrend", "ThreadTrend")
Assert-RequiredText "Comparison runner" $comparisonText "HTML report" @(
    "performance.html")

# Capture-Window is an evidence step, not a timed readiness/interaction step.
# Require an explicit exclusion marker in the guest runner so a future
# refactor cannot accidentally turn PrintWindow/CopyFromScreen latency into
# application performance latency.  Also reject a direct capture call inside
# the conventional timed-stage function when that function is present.
$screenshotExclusion = [regex]::IsMatch(
    $guestText,
    "(?is)(?:screenshot|capture-window).{0,100}(?:excluded|outside|not.{0,20}tim)|(?:excluded|outside|not.{0,20}tim).{0,100}(?:screenshot|capture-window)")
if (-not $screenshotExclusion -and
    $guestText.IndexOf("ExcludeFromTiming", [StringComparison]::OrdinalIgnoreCase) -lt 0 -and
    $guestText.IndexOf("ExcludeFromPerformance", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    $failures.Add("Guest runner must explicitly exclude screenshot capture from timed performance stages.")
}

$timedStageMatch = [regex]::Match(
    $guestText,
    "(?ism)function\s+(?:Measure-Stage|Measure-PerformanceStage|TimedStage)\b.*?(?=^\s*function\s|\z)")
if ($timedStageMatch.Success -and
    $timedStageMatch.Value.IndexOf("Capture-Window", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    $failures.Add("Guest timed-stage function must not call Capture-Window; screenshot capture must be outside measured stages.")
}

foreach ($feature in $coverage.features) {
    $hasInteractiveContract = @($feature.mainControls).Count -gt 0 -or
        @($feature.overlayControls).Count -gt 0 -or
        @($feature.journalActions).Count -gt 0
    $hasPerformanceStages =
        $null -ne $feature.PSObject.Properties["performanceStages"] -and
        @($feature.performanceStages).Count -gt 0
    $hasExplicitExclusion =
        $null -ne $feature.PSObject.Properties["performanceExclusions"] -and
        @($feature.performanceExclusions).Count -gt 0
    if ($hasInteractiveContract -and
        -not $hasPerformanceStages -and
        -not $hasExplicitExclusion) {
        $failures.Add(
            "Feature '$($feature.id)' has E2E controls/actions but no performance stages or explicit exclusion.")
    }
}

$forbiddenGuestText = @(
    "SetDisplayConfig",
    "SetDefaultAudioEndpoint",
    "pnputil",
    "netsh",
    "shutdown.exe"
)
foreach ($forbidden in $forbiddenGuestText) {
    if ($guestText.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        $failures.Add("Guest runner contains a hardware-changing action: $forbidden")
    }
}

$requiredRuntimeText = @(
    "SandboxE2eRuntime",
    "display.apply",
    "audio.session.volume",
    "audio.session.mute",
    "radios.set",
    "taskbar.autoHide",
    "startup.set",
    "profiles.save"
)
foreach ($expected in $requiredRuntimeText) {
    if ($runtimeText.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) {
        $failures.Add("E2E runtime is missing required action: $expected")
    }
}

if ($runtimeText.IndexOf("Platform.Windows", [StringComparison]::Ordinal) -ge 0) {
    $failures.Add("The deterministic E2E runtime must not reference Platform.Windows services.")
}

$featureIds = @($coverage.features | ForEach-Object { $_.id })
foreach ($requiredFeature in @(
    "shell", "display", "audio", "devices", "options", "favorites", "shortcuts", "tray", "persistence", "profiles", "about", "workspace")) {
    if ($featureIds -notcontains $requiredFeature) {
        $failures.Add("Coverage manifest is missing feature: $requiredFeature")
    }
}

$declaredContractTests = @($coverage.features | ForEach-Object {
    if ($_.PSObject.Properties.Name -contains "contractTests") {
        $_.contractTests
    }
})
foreach ($contractName in $declaredContractTests) {
    if ($contractTestText.IndexOf($contractName, [StringComparison]::Ordinal) -lt 0) {
        $failures.Add("Coverage references a missing contract test: $contractName")
    }
}

$profiles = $coverage.features | Where-Object { $_.id -eq "profiles" } | Select-Object -First 1
if ($null -eq $profiles -or @($profiles.editions).Count -ne 1 -or $profiles.editions[0] -ne "Paid") {
    $failures.Add("Profiles coverage must be Paid-only.")
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Windows Sandbox harness validation failed with $($failures.Count) error(s)."
}

Write-Host "Windows Sandbox E2E validation passed (feature manifest, deterministic runtime, safety configuration, and UI scenarios)."
& (Join-Path $PSScriptRoot "Test-PerformanceAnalysis.ps1")
