[CmdletBinding()]
param(
    [ValidateSet("Paid", "Free")]
    [string]$Edition = "Paid",
    [switch]$SkipBuild,
    [switch]$KeepSandboxOpen,
    [switch]$ShowSandboxWindow,
    [switch]$ValidateOnly,
    [switch]$Performance,
    [ValidateRange(1, 100)]
    [int]$PerformanceIterations = 20,
    [ValidateRange(0, 10)]
    [int]$PerformanceWarmupIterations = 2,
    [switch]$EnforcePerformanceBudgets,
    [switch]$ReturnFailedResult
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class WqsSandboxHostWindows
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    private delegate void WinEventDelegate(
        IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private static readonly object Sync = new object();
    private static readonly List<IntPtr> Hooks = new List<IntPtr>();
    private static HashSet<int> baselineProcessIds = new HashSet<int>();
    private static WinEventDelegate eventCallback;
    private static int hiddenWindowCount;
    private static volatile bool guardRunning;
    private static System.Threading.Thread guardThread;

    private static bool IsTargetProcess(uint processId)
    {
        if (baselineProcessIds.Contains((int)processId))
        {
            return false;
        }
        try
        {
            string name = Process.GetProcessById((int)processId).ProcessName;
            return name.Equals("WindowsSandbox", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WindowsSandboxClient", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool HideIfTarget(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }
        uint processId;
        GetWindowThreadProcessId(window, out processId);
        if (!IsTargetProcess(processId) || !IsWindowVisible(window))
        {
            return false;
        }
        if (ShowWindow(window, 0))
        {
            System.Threading.Interlocked.Increment(ref hiddenWindowCount);
            return true;
        }
        return false;
    }

    private static void HideCurrentTargetWindows()
    {
        EnumWindows((window, parameter) =>
        {
            HideIfTarget(window);
            return true;
        }, IntPtr.Zero);
    }

    public static int HideTopLevelWindows(int[] processIds)
    {
        HashSet<int> targets = new HashSet<int>(processIds ?? Array.Empty<int>());
        int hidden = 0;
        EnumWindows((window, parameter) =>
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (targets.Contains((int)processId) && IsWindowVisible(window))
            {
                // SW_HIDE prevents the RDP-backed Sandbox viewer and any owned
                // connection dialog from entering the user's working desktop.
                if (ShowWindow(window, 0))
                {
                    System.Threading.Interlocked.Increment(ref hiddenWindowCount);
                    hidden++;
                }
            }
            return true;
        }, IntPtr.Zero);
        return hidden;
    }

    public static int CountVisibleTopLevelWindows(int[] processIds)
    {
        HashSet<int> targets = new HashSet<int>(processIds ?? Array.Empty<int>());
        int visible = 0;
        EnumWindows((window, parameter) =>
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (targets.Contains((int)processId) && IsWindowVisible(window))
            {
                visible++;
            }
            return true;
        }, IntPtr.Zero);
        return visible;
    }

    public static void StartGuard(int[] baselineIds)
    {
        lock (Sync)
        {
            StopGuard();
            baselineProcessIds = new HashSet<int>(baselineIds ?? Array.Empty<int>());
            hiddenWindowCount = 0;
            eventCallback = (hook, eventType, window, objectId, childId, eventThread, eventTime) =>
            {
                HideIfTarget(window);
            };

            const uint WINEVENT_OUTOFCONTEXT = 0;
            const uint WINEVENT_SKIPOWNPROCESS = 2;
            const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
            const uint EVENT_OBJECT_CREATE = 0x8000;
            const uint EVENT_OBJECT_SHOW = 0x8002;
            uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
            Hooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, eventCallback, 0, 0, flags));
            Hooks.Add(SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
                IntPtr.Zero, eventCallback, 0, 0, flags));
            Hooks.Add(SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
                IntPtr.Zero, eventCallback, 0, 0, flags));

            // WinEvent callbacks are an immediate first line of defense. The
            // dedicated watcher is the fallback for Windows builds that queue
            // out-of-context events until the caller pumps messages.
            guardRunning = true;
            guardThread = new System.Threading.Thread(() =>
            {
                while (guardRunning)
                {
                    HideCurrentTargetWindows();
                    System.Threading.Thread.Sleep(10);
                }
            });
            guardThread.IsBackground = true;
            guardThread.Name = "WinQuickSwitch Sandbox window guard";
            guardThread.Start();
        }
    }

    public static int StopGuard()
    {
        lock (Sync)
        {
            guardRunning = false;
            if (guardThread != null)
            {
                guardThread.Join(500);
                guardThread = null;
            }
            foreach (IntPtr hook in Hooks)
            {
                if (hook != IntPtr.Zero)
                {
                    UnhookWinEvent(hook);
                }
            }
            Hooks.Clear();
            eventCallback = null;
            return hiddenWindowCount;
        }
    }
}
"@

if ($KeepSandboxOpen -and -not $ShowSandboxWindow) {
    throw "KeepSandboxOpen is only supported with ShowSandboxWindow. Background runs must clean up their hidden Sandbox client."
}

function Get-SandboxClientProcesses([int[]]$BaselineProcessIds) {
    $baseline = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($processId in @($BaselineProcessIds)) {
        [void]$baseline.Add($processId)
    }
    @(Get-Process -Name WindowsSandbox,WindowsSandboxClient -ErrorAction SilentlyContinue |
        Where-Object { -not $baseline.Contains($_.Id) })
}

function Hide-SandboxClientWindows([int[]]$BaselineProcessIds) {
    $processIds = @(Get-SandboxClientProcesses $BaselineProcessIds |
        Select-Object -ExpandProperty Id)
    if ($processIds.Count -eq 0) {
        return 0
    }
    [WqsSandboxHostWindows]::HideTopLevelWindows($processIds)
}

function Stop-SandboxClientProcesses([int[]]$BaselineProcessIds) {
    $stopped = [System.Collections.Generic.HashSet[int]]::new()
    # Keep watching briefly because WindowsSandbox.exe can create the client
    # after the root process has begun exiting. Hide before terminating so an
    # error/feedback dialog cannot flash on the working desktop.
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $processes = @(Get-SandboxClientProcesses $BaselineProcessIds)
        if ($processes.Count -gt 0) {
            [void](Hide-SandboxClientWindows $BaselineProcessIds)
            foreach ($process in $processes) {
                [void]$stopped.Add($process.Id)
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Milliseconds 100
    }
    @($stopped)
}

function Get-RepositoryRoot {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}

function Get-ReleaseVersion([string]$RepositoryRoot) {
    [xml]$properties = Get-Content -Raw -LiteralPath (
        Join-Path $RepositoryRoot "Directory.Build.props")
    $versionNode = $properties.SelectSingleNode(
        "/Project/PropertyGroup/WinQuickSwitchReleaseVersion")
    $version = $versionNode.InnerText
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Directory.Build.props does not define WinQuickSwitchReleaseVersion."
    }
    $version
}

function New-SandboxConfiguration(
    [string]$PayloadDirectory,
    [string]$ResultsDirectory,
    [string]$ConfigurationPath) {
    $payload = [System.Security.SecurityElement]::Escape($PayloadDirectory)
    $results = [System.Security.SecurityElement]::Escape($ResultsDirectory)
    $content = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$payload</HostFolder>
      <SandboxFolder>C:\WQS\Payload</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$results</HostFolder>
      <SandboxFolder>C:\WQS\Results</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\WQS\Payload\Run-SandboxUiSuite.ps1</Command>
  </LogonCommand>
</Configuration>
"@
    Set-Content -LiteralPath $ConfigurationPath -Value $content -Encoding utf8
    [void][xml](Get-Content -Raw -LiteralPath $ConfigurationPath)
}

function New-ScreenshotGallery(
    [string]$ResultsDirectory,
    [string]$Edition,
    [string]$Version) {
    $screenshots = Get-ChildItem -LiteralPath $ResultsDirectory -Filter "*.png" |
        Sort-Object Name
    $cards = foreach ($screenshot in $screenshots) {
        $encodedName = [System.Net.WebUtility]::HtmlEncode($screenshot.Name)
        @"
<article>
  <h2>$encodedName</h2>
  <a href="$encodedName"><img src="$encodedName" alt="$encodedName"></a>
</article>
"@
    }
    $editionText = [System.Net.WebUtility]::HtmlEncode($Edition)
    $versionText = [System.Net.WebUtility]::HtmlEncode($Version)
    $body = $cards -join [Environment]::NewLine
    $performanceLink = if (Test-Path -LiteralPath (
        Join-Path $ResultsDirectory "performance.json")) {
        ' &middot; <a href="performance.json">performance results</a>'
    } else { "" }
    $gallery = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>WinQuickSwitch Sandbox UI - $editionText v$versionText</title>
  <style>
    :root { color-scheme: dark; font-family: "Segoe UI", sans-serif; }
    body { margin: 0; padding: 28px; background: #111; color: #f3f3f3; }
    header { max-width: 1100px; margin: 0 auto 28px; }
    main { display: grid; gap: 28px; }
    article { background: #1d1d1d; border: 1px solid #3a3a3a; border-radius: 12px; padding: 18px; }
    h1, h2 { margin-top: 0; }
    h2 { font-size: 16px; }
    img { display: block; max-width: 100%; height: auto; border: 1px solid #555; }
    a { color: #60a5fa; }
  </style>
</head>
<body>
  <header>
    <h1>WinQuickSwitch Sandbox UI</h1>
    <p>$editionText v$versionText. Captured inside a disposable Sandbox with networking, clipboard, cameras, microphone, and printers disabled.</p>
    <p>Results: <a href="results.json">results.json</a>$performanceLink &middot; <a href="FeatureCoverage.json">coverage contract</a> &middot; <a href="service-journal.jsonl">service journal</a> &middot; <a href="contract-tests.txt">contract tests</a></p>
  </header>
  <main>
    $body
  </main>
</body>
</html>
"@
    $galleryPath = Join-Path $ResultsDirectory "index.html"
    Set-Content -LiteralPath $galleryPath -Value $gallery -Encoding utf8
    $galleryPath
}

$repositoryRoot = Get-RepositoryRoot
$releaseVersion = Get-ReleaseVersion $repositoryRoot
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactFamily = if ($Performance) { "sandbox-performance" } else { "sandbox-ui" }
$runRoot = Join-Path $repositoryRoot (
    "artifacts\{0}\{1}\{2}-v{3}" -f
        $artifactFamily,
        $Edition.ToLowerInvariant(),
        $runId,
        $releaseVersion)
$payloadDirectory = Join-Path $runRoot "payload"
$appDirectory = Join-Path $payloadDirectory "App"
$resultsDirectory = Join-Path $runRoot "results"
$cacheDirectory = Join-Path $repositoryRoot (
    "artifacts\sandbox-e2e-cache\{0}\v{1}\App" -f
        $Edition.ToLowerInvariant(),
        $releaseVersion)

New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

$projectPath = Join-Path $repositoryRoot "src\WinQuickSwitch\WinQuickSwitch.csproj"
$dotnet = Join-Path $repositoryRoot ".dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$contractTestProject = Join-Path $repositoryRoot (
    "tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj")
$contractOutput = @(& $dotnet run --project $contractTestProject `
    --configuration Release --no-restore 2>&1)
$contractOutput | Set-Content -LiteralPath (
    Join-Path $resultsDirectory "contract-tests.txt") -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    throw "The prerequisite service, persistence, hotkey, tray, and geometry contract suite failed."
}

if ($SkipBuild) {
    $cachedApplication = Join-Path $cacheDirectory "WinQuickSwitch.exe"
    if (-not (Test-Path -LiteralPath $cachedApplication)) {
        throw "No cached $Edition E2E build v$releaseVersion was found at $cachedApplication. Run without -SkipBuild once."
    }
}
else {
    $publishArguments = @(
        "publish",
        $projectPath,
        "--configuration", "Release",
        "--runtime", "win-x64",
        "--self-contained", "true",
        "--output", $cacheDirectory,
        "-p:WinQuickSwitchEdition=$Edition",
        "-p:WinQuickSwitchE2E=true",
        "-p:WinQuickSwitchReleaseVersion=$releaseVersion",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:PublishReadyToRun=false",
        "-p:PublishTrimmed=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )
    & $dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The $Edition Sandbox application publish failed."
    }
}

Copy-Item -Path (Join-Path $cacheDirectory "*") -Destination $appDirectory -Recurse -Force

Copy-Item -LiteralPath (
    Join-Path $PSScriptRoot "Run-SandboxUiSuite.ps1") -Destination $payloadDirectory
Copy-Item -LiteralPath (
    Join-Path $PSScriptRoot "FeatureCoverage.json") -Destination $payloadDirectory
@{
    Edition = $Edition
    Version = $releaseVersion
    KeepSandboxOpen = [bool]$KeepSandboxOpen
    PerformanceMode = [bool]$Performance
    PerformanceIterations = $PerformanceIterations
    PerformanceWarmupIterations = $PerformanceWarmupIterations
    EnforcePerformanceBudgets = [bool]$EnforcePerformanceBudgets
    CreatedAt = [DateTimeOffset]::Now.ToString("O")
} | ConvertTo-Json | Set-Content -LiteralPath (
    Join-Path $payloadDirectory "scenario.json") -Encoding utf8

$configurationPath = Join-Path $runRoot (
    "WinQuickSwitch-{0}-v{1}.wsb" -f $Edition, $releaseVersion)
New-SandboxConfiguration $payloadDirectory $resultsDirectory $configurationPath

if ($ValidateOnly) {
    [pscustomobject]@{
        Edition = $Edition
        Version = $releaseVersion
        RunDirectory = $runRoot
        Configuration = $configurationPath
        Application = Join-Path $appDirectory "WinQuickSwitch.exe"
        CoverageManifest = Join-Path $payloadDirectory "FeatureCoverage.json"
        PerformanceMode = [bool]$Performance
        PerformanceIterations = $PerformanceIterations
        PerformanceWarmupIterations = $PerformanceWarmupIterations
        Status = "Validated"
    }
    return
}

$sandboxExecutable = Join-Path $env:WINDIR "System32\WindowsSandbox.exe"
if (-not (Test-Path -LiteralPath $sandboxExecutable)) {
    throw "Windows Sandbox is not installed or enabled. Enable the Windows feature named Containers-DisposableClientVM, restart Windows if requested, and run this command again."
}

Write-Host "Starting isolated $Edition v$releaseVersion UI validation..."
$resultPath = Join-Path $resultsDirectory "results.json"
$baselineSandboxProcessIds = @(Get-Process `
    -Name WindowsSandbox,WindowsSandboxClient `
    -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$startArguments = @{
    FilePath = $sandboxExecutable
    ArgumentList = ('"{0}"' -f $configurationPath)
    PassThru = $true
}
if (-not $ShowSandboxWindow) {
    $startArguments.WindowStyle = "Hidden"
    [WqsSandboxHostWindows]::StartGuard($baselineSandboxProcessIds)
}
try {
    $sandboxProcess = Start-Process @startArguments
}
catch {
    if (-not $ShowSandboxWindow) {
        [void][WqsSandboxHostWindows]::StopGuard()
    }
    throw
}

# The guest writes results before it exits. Poll that mapped file instead of
# shutting the guest down from inside the remote session; an in-guest forced
# shutdown makes the host client report a misleading 0x80072746 connection loss.
$result = $null
$cleanupRecorded = $false
try {
    $timeoutMinutes = if ($Performance) {
        [Math]::Max(30, [Math]::Ceiling(
            (($PerformanceIterations + $PerformanceWarmupIterations) * 1.2) + 10))
    }
    else { 30 }
    $deadline = [DateTimeOffset]::Now.AddMinutes($timeoutMinutes)
    while (-not (Test-Path -LiteralPath $resultPath)) {
        if (-not $ShowSandboxWindow) {
            [void](Hide-SandboxClientWindows $baselineSandboxProcessIds)
        }
        $sandboxProcess.Refresh()
        if ($sandboxProcess.HasExited) {
            throw "The Sandbox closed without producing results. Review $(Join-Path $resultsDirectory 'sandbox.log')."
        }
        if ([DateTimeOffset]::Now -ge $deadline) {
            throw "The Sandbox UI suite did not complete within $timeoutMinutes minutes."
        }
        Start-Sleep -Milliseconds 50
    }

    # Wait for the atomic JSON write to become readable before closing the client.
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $result; $attempt++) {
        try {
            $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if ($null -eq $result) {
        throw "The Sandbox produced an unreadable results file at $resultPath."
    }

    if (-not $KeepSandboxOpen) {
        $stoppedSandboxProcessIds = @(Stop-SandboxClientProcesses `
            $baselineSandboxProcessIds)
        $hiddenWindowCount = if ($ShowSandboxWindow) {
            0
        }
        else {
            [WqsSandboxHostWindows]::StopGuard()
        }
        $remainingSandboxProcesses = @(Get-SandboxClientProcesses `
            $baselineSandboxProcessIds)
        $remainingSandboxProcessIds = @($remainingSandboxProcesses |
            Select-Object -ExpandProperty Id)
        $remainingVisibleWindowCount = if ($remainingSandboxProcessIds.Count -eq 0) {
            0
        }
        else {
            [WqsSandboxHostWindows]::CountVisibleTopLevelWindows(
                $remainingSandboxProcessIds)
        }
        @{
            BackgroundMode = -not $ShowSandboxWindow
            HiddenWindowCount = $hiddenWindowCount
            StoppedProcessIds = $stoppedSandboxProcessIds
            RemainingProcessIds = $remainingSandboxProcessIds
            RemainingVisibleWindowCount = $remainingVisibleWindowCount
            FailureCleanup = $false
            CompletedAt = [DateTimeOffset]::Now.ToString("O")
        } | ConvertTo-Json | Set-Content -LiteralPath (
            Join-Path $resultsDirectory "host-lifecycle.json") -Encoding utf8
        $cleanupRecorded = $true
    }
}
finally {
    if (-not $KeepSandboxOpen -and -not $cleanupRecorded) {
        $stoppedSandboxProcessIds = @(Stop-SandboxClientProcesses `
            $baselineSandboxProcessIds)
        $hiddenWindowCount = if ($ShowSandboxWindow) {
            0
        }
        else {
            [WqsSandboxHostWindows]::StopGuard()
        }
        $remainingSandboxProcesses = @(Get-SandboxClientProcesses `
            $baselineSandboxProcessIds)
        $remainingSandboxProcessIds = @($remainingSandboxProcesses |
            Select-Object -ExpandProperty Id)
        $remainingVisibleWindowCount = if ($remainingSandboxProcessIds.Count -eq 0) {
            0
        }
        else {
            [WqsSandboxHostWindows]::CountVisibleTopLevelWindows(
                $remainingSandboxProcessIds)
        }
        @{
            BackgroundMode = -not $ShowSandboxWindow
            HiddenWindowCount = $hiddenWindowCount
            StoppedProcessIds = $stoppedSandboxProcessIds
            RemainingProcessIds = $remainingSandboxProcessIds
            RemainingVisibleWindowCount = $remainingVisibleWindowCount
            FailureCleanup = $true
            CompletedAt = [DateTimeOffset]::Now.ToString("O")
        } | ConvertTo-Json | Set-Content -LiteralPath (
            Join-Path $resultsDirectory "host-lifecycle.json") -Encoding utf8
    }
    elseif (-not $ShowSandboxWindow) {
        [void][WqsSandboxHostWindows]::StopGuard()
    }
}

$galleryPath = New-ScreenshotGallery $resultsDirectory $Edition $releaseVersion
$runOutput = [pscustomobject]@{
    Edition = $Edition
    Version = $releaseVersion
    Passed = [bool]$result.Passed
    ScreenshotCount = @($result.Screenshots).Count
    Gallery = $galleryPath
    Results = $resultPath
    Performance = if ($Performance) {
        Join-Path $resultsDirectory "performance.json"
    } else { $null }
    Log = Join-Path $resultsDirectory "sandbox.log"
}
$runOutput
if (-not $runOutput.Passed -and -not $ReturnFailedResult) {
    throw "The $Edition Sandbox suite failed. Review $($runOutput.Results)."
}
