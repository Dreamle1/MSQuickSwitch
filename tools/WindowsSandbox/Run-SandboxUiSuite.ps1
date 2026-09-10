Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$payloadRoot = "C:\WQS\Payload"
$resultsRoot = "C:\WQS\Results"
$scenario = Get-Content -Raw -LiteralPath (
    Join-Path $payloadRoot "scenario.json") | ConvertFrom-Json
$script:PerformanceMode = $null -ne $scenario.PSObject.Properties["PerformanceMode"] -and
    [bool]$scenario.PerformanceMode
$script:PerformanceIterations = if ($null -ne $scenario.PSObject.Properties["PerformanceIterations"]) {
    [Math]::Max(1, [int]$scenario.PerformanceIterations)
} else { 20 }
$script:PerformanceWarmupIterations = if ($null -ne $scenario.PSObject.Properties["PerformanceWarmupIterations"]) {
    [Math]::Max(0, [int]$scenario.PerformanceWarmupIterations)
} else { 2 }
$script:EnforcePerformanceBudgets =
    $null -ne $scenario.PSObject.Properties["EnforcePerformanceBudgets"] -and
    [bool]$scenario.EnforcePerformanceBudgets
$coverage = Get-Content -Raw -LiteralPath (
    Join-Path $payloadRoot "FeatureCoverage.json") | ConvertFrom-Json
$logPath = Join-Path $resultsRoot "sandbox.log"
$script:JournalPath = Join-Path $resultsRoot "service-journal.jsonl"
$script:HighContrastMarkerPath = Join-Path $resultsRoot "high-contrast.enabled"
$script:Screenshots = [System.Collections.Generic.List[object]]::new()
$script:Checks = [System.Collections.Generic.List[object]]::new()
$script:ApplicationProcess = $null
$script:CoverageSummary = $null
$script:PerformanceStages = [System.Collections.Generic.List[object]]::new()
$script:PerformanceSessions = [System.Collections.Generic.List[object]]::new()
$script:CurrentPerformanceIteration = 0
$script:CurrentPerformanceWarmup = $false
$script:PerformanceProcessId = 0
$script:PerformanceWorkloadBaseline = $null

function Write-SandboxLog([string]$Message) {
    $line = "{0:O} {1}" -f [DateTimeOffset]::Now, $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
}

function Add-Check(
    [string]$Name,
    [bool]$Passed,
    [string]$Details = "") {
    $script:Checks.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Details = $Details
    })
    if (-not $Passed) {
        throw "$Name failed. $Details"
    }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class WqsSandboxNative
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    public static void ClickAt(int x, int y)
    {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Could not position the Sandbox pointer.");
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    public static void SendCtrlAltKey(byte key)
    {
        // Inject into Windows' keyboard stream; SendKeys' journal playback may
        // reach a WPF control without generating registered global hotkeys.
        try
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
        }
        finally
        {
            keybd_event(key, 0, 2, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);
}

public sealed class WqsProcessSample
{
    public string Timestamp { get; set; }
    public string Stage { get; set; }
    public int Iteration { get; set; }
    public bool Warmup { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
    public long CpuTimeTicks { get; set; }
}

public static class WqsPerformanceSampler
{
    private static readonly object Sync = new object();
    private static readonly List<WqsProcessSample> Samples = new List<WqsProcessSample>();
    private static Thread worker;
    private static volatile bool running;
    private static volatile string stage = "idle";
    private static volatile int iteration;
    private static volatile bool warmup;
    private static int processId;
    private static int intervalMilliseconds;

    public static void Start(int pid, int intervalMs)
    {
        Stop();
        lock (Sync) { Samples.Clear(); }
        processId = pid;
        intervalMilliseconds = Math.Max(50, intervalMs);
        stage = "startup";
        iteration = 0;
        warmup = false;
        running = true;
        worker = new Thread(SampleLoop) { IsBackground = true, Name = "WQS E2E performance sampler" };
        worker.Start();
    }

    public static void SetStage(string value)
    {
        stage = String.IsNullOrWhiteSpace(value) ? "idle" : value;
    }

    public static void SetContext(int iterationValue, bool warmupValue)
    {
        lock (Sync) {
            iteration = iterationValue;
            warmup = warmupValue;
        }
    }

    public static WqsProcessSample[] Stop()
    {
        running = false;
        if (worker != null && worker.IsAlive) { worker.Join(2000); }
        worker = null;
        lock (Sync) { return Samples.ToArray(); }
    }

    private static void SampleLoop()
    {
        while (running)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.Refresh();
                    WqsProcessSample sample = new WqsProcessSample
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        Stage = stage,
                        WorkingSetBytes = process.WorkingSet64,
                        PrivateMemoryBytes = process.PrivateMemorySize64,
                        PeakWorkingSetBytes = process.PeakWorkingSet64,
                        HandleCount = process.HandleCount,
                        ThreadCount = process.Threads.Count,
                        CpuTimeTicks = process.TotalProcessorTime.Ticks
                    };
                    lock (Sync) {
                        sample.Iteration = iteration;
                        sample.Warmup = warmup;
                        Samples.Add(sample);
                    }
                }
            }
            catch { }
            Thread.Sleep(intervalMilliseconds);
        }
    }
}
"@

function Wait-TopLevelWindow(
    [int]$ProcessId,
    [string]$ExpectedTitle = "",
    [int]$TimeoutSeconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            try {
                $titleMatches = [string]::IsNullOrWhiteSpace($ExpectedTitle)
                if (-not $titleMatches -and $ExpectedTitle.Contains('*')) {
                    $titleMatches = $child.Current.Name -like $ExpectedTitle
                }
                elseif (-not $titleMatches) {
                    $titleMatches = $child.Current.Name -eq $ExpectedTitle
                }
                if ($child.Current.ProcessId -eq $ProcessId -and
                    $titleMatches) {
                    return $child
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for the WinQuickSwitch window '$ExpectedTitle'."
}

function Find-NamedElement(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [bool]$Required = $true) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $matches = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($match in $matches) {
        try {
            if (-not $match.Current.IsOffscreen -and $match.Current.IsEnabled) {
                return $match
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
    }
    if ($Required) {
        throw "The visible control '$Name' was not found."
    }
    $null
}

function Find-AnyNamedElement(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $matches = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($match in $matches) {
        try {
            [void]$match.Current.ControlType
            return $match
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
    }
    $null
}

function Wait-TopLevelWindowContainingControl(
    [int]$ProcessId,
    [string]$ControlName,
    [IntPtr]$ExcludedHandle = [IntPtr]::Zero,
    [int]$TimeoutSeconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            try {
                if ($child.Current.ProcessId -eq $ProcessId -and
                    ($ExcludedHandle -eq [IntPtr]::Zero -or
                        [IntPtr]$child.Current.NativeWindowHandle -ne $ExcludedHandle) -and
                    $null -ne (Find-AnyNamedElement $child $ControlName)) {
                    return $child
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for a WinQuickSwitch window containing '$ControlName'."
}

function Assert-FeatureControls(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$FeatureId,
    [ValidateSet("mainControls", "overlayControls")]
    [string]$Surface) {
    $feature = $coverage.features | Where-Object {
        $_.id -eq $FeatureId -and $_.editions -contains $scenario.Edition
    } | Select-Object -First 1
    if ($null -eq $feature) {
        return
    }
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($feature.$Surface)) {
        if ($null -eq (Find-AnyNamedElement $Root $name)) {
            $missing.Add($name)
        }
    }
    $details = if ($missing.Count -eq 0) {
        "Found $(@($feature.$Surface).Count) expected controls."
    }
    else {
        "Missing: $($missing -join ', ')"
    }
    Add-Check "$FeatureId $Surface controls" ($missing.Count -eq 0) $details
}

function Assert-NamedInteractiveControls(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Surface) {
    $interactiveTypes = @(
        [System.Windows.Automation.ControlType]::Button,
        [System.Windows.Automation.ControlType]::CheckBox,
        [System.Windows.Automation.ControlType]::ComboBox,
        [System.Windows.Automation.ControlType]::Edit,
        [System.Windows.Automation.ControlType]::Hyperlink,
        [System.Windows.Automation.ControlType]::ListItem,
        [System.Windows.Automation.ControlType]::MenuItem,
        [System.Windows.Automation.ControlType]::RadioButton,
        [System.Windows.Automation.ControlType]::Slider,
        [System.Windows.Automation.ControlType]::TabItem,
        [System.Windows.Automation.ControlType]::TreeItem)
    $unnamed = [System.Collections.Generic.List[string]]::new()
    $elements = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        try {
            if ($interactiveTypes -notcontains $element.Current.ControlType -or
                $element.Current.IsOffscreen -or
                -not $element.Current.IsEnabled -or
                -not $element.Current.IsKeyboardFocusable -or
                -not [string]::IsNullOrWhiteSpace($element.Current.Name)) {
                continue
            }
            $bounds = $element.Current.BoundingRectangle
            $unnamed.Add((
                "{0} at {1},{2} {3}x{4}" -f
                $element.Current.ControlType.ProgrammaticName,
                [Math]::Round($bounds.X),
                [Math]::Round($bounds.Y),
                [Math]::Round($bounds.Width),
                [Math]::Round($bounds.Height)))
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
    }
    $details = if ($unnamed.Count -eq 0) {
        "No unnamed visible keyboard controls were found."
    }
    else {
        "Unnamed controls: $($unnamed -join '; ')"
    }
    Add-Check "$Surface interactive controls have accessible names" (
        $unnamed.Count -eq 0) $details
}

function Set-SandboxHighContrast([bool]$Enabled) {
    if ($Enabled) {
        Set-Content -LiteralPath $script:HighContrastMarkerPath `
            -Value "enabled" -Encoding ascii
    }
    else {
        Remove-Item -LiteralPath $script:HighContrastMarkerPath `
            -Force -ErrorAction SilentlyContinue
    }
}

function Set-NamedRangeValue(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [double]$Value) {
    $element = Find-NamedElement $Root $Name
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.RangeValuePattern]::Pattern,
            [ref]$pattern)) {
        throw "The control '$Name' does not support RangeValuePattern."
    }
    ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue($Value)
    $element.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
    Start-Sleep -Milliseconds 900
}

function Close-TopLevelWindow(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$CloseControlName) {
    Invoke-NamedControl $Window $CloseControlName
    Start-Sleep -Milliseconds 500
}

function Invoke-NamedToggleWithKeyboard(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$TimeoutMilliseconds = 10000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    $lastError = $null
    do {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)
        $matches = $Root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        foreach ($candidate in $matches) {
            try {
                if (-not $candidate.Current.IsEnabled -or
                    -not $candidate.Current.IsKeyboardFocusable -or
                    $candidate.Current.IsOffscreen) {
                    continue
                }
                $candidate.SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait(" ")
                Start-Sleep -Milliseconds 900
                return
            }
            catch {
                $lastError = $_.Exception.Message
            }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTimeOffset]::Now -lt $deadline)

    $details = if ([string]::IsNullOrWhiteSpace($lastError)) {
        "No enabled, visible, keyboard-focusable instance was available."
    }
    else {
        $lastError
    }
    throw "The toggle '$Name' could not receive focus within $TimeoutMilliseconds ms. $details"
}

function Invoke-NamedControl(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$DelayMilliseconds = 900) {
    $element = Find-NamedElement $Root $Name
    $pattern = $null
    if ($element.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
        $expandCollapse = [System.Windows.Automation.ExpandCollapsePattern]$pattern
        if ($expandCollapse.Current.ExpandCollapseState -eq
            [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
            $expandCollapse.Expand()
        }
        else {
            $expandCollapse.Collapse()
        }
    }
    elseif ($element.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
    }
    elseif ($element.TryGetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern,
            [ref]$pattern)) {
        # UI Automation's TogglePattern changes IsChecked directly and does not
        # reliably raise WPF's Click event. Focus and press Space so tabs and
        # toggle controls follow the same routed-event path as real input.
        Invoke-NamedToggleWithKeyboard $Root $Name
    }
    elseif ($element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
    }
    else {
        throw "The control '$Name' has no supported activation pattern."
    }
    if ($DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

function Invoke-AnyNamedControl(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$DelayMilliseconds = 900) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $matches = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($element in $matches) {
        $pattern = $null
        try {
            if ($element.TryGetCurrentPattern(
                    [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
                    [ref]$pattern)) {
                $expandCollapse = [System.Windows.Automation.ExpandCollapsePattern]$pattern
                if ($expandCollapse.Current.ExpandCollapseState -eq
                    [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
                    $expandCollapse.Expand()
                }
                else {
                    $expandCollapse.Collapse()
                }
                if ($DelayMilliseconds -gt 0) {
                    Start-Sleep -Milliseconds $DelayMilliseconds
                }
                return
            }
            if ($element.TryGetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern,
                    [ref]$pattern)) {
                ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
                if ($DelayMilliseconds -gt 0) {
                    Start-Sleep -Milliseconds $DelayMilliseconds
                }
                return
            }
            if ($element.TryGetCurrentPattern(
                    [System.Windows.Automation.TogglePattern]::Pattern,
                    [ref]$pattern)) {
                Invoke-NamedToggleWithKeyboard $Root $Name
                return
            }
            if ($element.TryGetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern,
                    [ref]$pattern)) {
                ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
                if ($DelayMilliseconds -gt 0) {
                    Start-Sleep -Milliseconds $DelayMilliseconds
                }
                return
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
    }
    throw "The control '$Name' was not found with a supported activation pattern."
}

function Set-NamedExpanderState(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [System.Windows.Automation.ExpandCollapseState]$ExpectedState) {
    $element = Find-NamedElement $Root $Name
    $patternValue = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$patternValue)) {
        throw "The control '$Name' does not support ExpandCollapsePattern."
    }
    $pattern = [System.Windows.Automation.ExpandCollapsePattern]$patternValue
    if ($pattern.Current.ExpandCollapseState -ne $ExpectedState) {
        if ($ExpectedState -eq
            [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            $pattern.Expand()
        }
        else {
            $pattern.Collapse()
        }
    }
    $deadline = [DateTimeOffset]::Now.AddSeconds(5)
    do {
        if ($pattern.Current.ExpandCollapseState -eq $ExpectedState) {
            Start-Sleep -Milliseconds 1000
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The control '$Name' did not reach $ExpectedState within 5000 ms."
}

function Wait-VisibleNamedControl(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        $element = Find-NamedElement $Root $Name $false
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The control '$Name' did not become ready within $TimeoutMilliseconds ms."
}

function Wait-AudioSessionRowReady(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$ApplicationName,
    [int]$TimeoutMilliseconds = 5000) {
    # A successful volume mutation refreshes the shared Audio inventory. The
    # service journal records the mutation before that refresh has necessarily
    # republished and re-enabled the existing row. Keep this settling wait
    # outside timed stages so a fast journal callback cannot race the next
    # session action or contaminate its latency measurement.
    Wait-VisibleNamedControl $Root `
        "Volume for $ApplicationName" $TimeoutMilliseconds | Out-Null
    Wait-VisibleNamedControl $Root `
        "Mute $ApplicationName" $TimeoutMilliseconds | Out-Null
}

function Wait-FirstVisibleNamedControl(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$TimeoutMilliseconds = 5000) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        try {
            $element = $Root.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition)
            if ($null -ne $element -and
                -not $element.Current.IsOffscreen -and
                $element.Current.IsEnabled) {
                return $element
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The control '$Name' did not become ready within $TimeoutMilliseconds ms."
}

function Wait-NamedControlAbsent(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        if ($null -eq (Find-NamedElement $Root $Name $false)) {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The control '$Name' remained visible after $TimeoutMilliseconds ms."
}

function Wait-VisibleProcessControl(
    [int]$ProcessId,
    [string]$Name,
    [int]$TimeoutMilliseconds = 5000) {
    $condition = [System.Windows.Automation.AndCondition]::new(@(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)))
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        $matches = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        foreach ($match in $matches) {
            try {
                if (-not $match.Current.IsOffscreen -and $match.Current.IsEnabled) {
                    return $match
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The in-process control '$Name' did not become ready within $TimeoutMilliseconds ms."
}

function Wait-ProcessWindowContainingControl(
    [int]$ProcessId,
    [string]$ControlName,
    [int]$TimeoutMilliseconds = 5000) {
    $control = Wait-VisibleProcessControl `
        $ProcessId $ControlName $TimeoutMilliseconds
    $current = $control
    while ($null -ne $current) {
        try {
            if ($current.Current.ControlType -eq
                [System.Windows.Automation.ControlType]::Window) {
                return $current
            }
            $current = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent(
                $current)
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            break
        }
    }
    throw "The control '$ControlName' did not resolve to an in-process window."
}

function Wait-ProcessControlAbsent(
    [int]$ProcessId,
    [string]$Name,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        try {
            [void](Wait-VisibleProcessControl $ProcessId $Name 100)
        }
        catch {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The in-process control '$Name' remained visible after $TimeoutMilliseconds ms."
}

function Wait-AutomationElementDismissed(
    [System.Windows.Automation.AutomationElement]$Element,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        try {
            if ($Element.Current.IsOffscreen) { return }
            [void]$Element.Current.ProcessId
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The activated modal control remained visible after $TimeoutMilliseconds ms."
}

function Wait-WindowClosed(
    [IntPtr]$Handle,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        if (-not [WqsSandboxNative]::IsWindow($Handle) -or
            -not [WqsSandboxNative]::IsWindowVisible($Handle)) {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "The auxiliary window did not close within $TimeoutMilliseconds ms."
}

function Get-CachedInvokeControl(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name) {
    $element = Find-NamedElement $Root $Name
    foreach ($supportedPattern in @(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [System.Windows.Automation.TogglePattern]::Pattern,
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)) {
        $pattern = $null
        if ($element.TryGetCurrentPattern($supportedPattern, [ref]$pattern)) {
            return $element
        }
    }
    throw "The performance control '$Name' has no supported activation pattern."
}

function Invoke-CachedControl(
    [System.Windows.Automation.AutomationElement]$Element,
    [string]$Name) {
    $pattern = $null
    # Expanders may also advertise InvokePattern through their header. Prefer
    # their semantic pattern so the content is guaranteed to change state.
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
        $expandCollapse = [System.Windows.Automation.ExpandCollapsePattern]$pattern
        if ($expandCollapse.Current.ExpandCollapseState -eq
            [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
            $expandCollapse.Expand()
        }
        else {
            $expandCollapse.Collapse()
        }
        return
    }
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern,
            [ref]$pattern)) {
        $Element.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait(" ")
        return
    }
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        return
    }
    throw "The cached performance control '$Name' is no longer activatable."
}

function Measure-PerformanceStage(
    [string]$Name,
    [scriptblock]$Action,
    [int]$Iteration = $script:CurrentPerformanceIteration,
    [bool]$Warmup = $script:CurrentPerformanceWarmup) {
    [WqsPerformanceSampler]::SetStage($Name)
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    $errorText = ""
    try {
        & $Action | Out-Null
        $passed = $true
    }
    catch {
        $errorText = $_.Exception.Message
        throw
    }
    finally {
        $stopwatch.Stop()
        $script:PerformanceStages.Add([pscustomobject]@{
            Name = $Name
            Iteration = $Iteration
            Warmup = $Warmup
            DurationMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
            Passed = $passed
            Error = $errorText
        })
        [WqsPerformanceSampler]::SetStage("idle")
    }
}

function Start-PerformanceSession(
    [System.Diagnostics.Process]$Process,
    [string]$Name) {
    $script:PerformanceProcessId = $Process.Id
    $script:PerformanceWorkloadBaseline = $null
    [WqsPerformanceSampler]::Start($Process.Id, 100)
    [WqsPerformanceSampler]::SetStage("$Name.startup")
}

function Get-PerformanceProcessSnapshot {
    if ($script:PerformanceProcessId -le 0) {
        throw "No performance process is active."
    }
    $process = [System.Diagnostics.Process]::GetProcessById(
        $script:PerformanceProcessId)
    try {
        $process.Refresh()
        [pscustomobject]@{
            Timestamp = [DateTimeOffset]::UtcNow
            WorkingSetBytes = [long]$process.WorkingSet64
            PrivateMemoryBytes = [long]$process.PrivateMemorySize64
            PeakWorkingSetBytes = [long]$process.PeakWorkingSet64
            HandleCount = [int]$process.HandleCount
            ThreadCount = [int]$process.Threads.Count
            CpuTimeTicks = [long]$process.TotalProcessorTime.Ticks
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-SettledPerformanceProcessSnapshot {
    # Allow asynchronous cleanup outside timed stages, then retain the actual
    # latest reading. A median could conceal an increase near the window end.
    # This fixed delay is not proof of quiescence; raw samples remain available.
    Start-Sleep -Milliseconds 1400
    Get-PerformanceProcessSnapshot
}

function Complete-PerformanceIteration([string]$SessionName) {
    # Collect only the PowerShell automation client. Abandoned UIA proxies can
    # retain remote providers; forcing collection in the app would hide leaks.
    # All feature timings have ended before this diagnostic boundary.
    [WqsPerformanceSampler]::SetStage('diagnostic.client-cleanup')
    $before = Get-PerformanceProcessSnapshot
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
    $after = Get-SettledPerformanceProcessSnapshot
    [pscustomobject]@{
        Session = $SessionName
        Iteration = $script:CurrentPerformanceIteration
        Warmup = $script:CurrentPerformanceWarmup
        BeforeClientCleanup = $before
        AfterClientCleanup = $after
    } | ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath (
        Join-Path $resultsRoot 'resource-iterations.jsonl') -Encoding utf8
    Write-SandboxLog ("{0} iteration {1} (warmup={2}): handles {3}->{4}, private {5:N2}->{6:N2} MiB after client cleanup." -f
        $SessionName, $script:CurrentPerformanceIteration,
        $script:CurrentPerformanceWarmup, $before.HandleCount, $after.HandleCount,
        ($before.PrivateMemoryBytes / 1MB), ($after.PrivateMemoryBytes / 1MB))
    [WqsPerformanceSampler]::SetStage('idle')
}

function Set-PerformanceWorkloadBaseline {
    [WqsPerformanceSampler]::SetStage("workload.baseline")
    $script:PerformanceWorkloadBaseline = Get-SettledPerformanceProcessSnapshot
    [WqsPerformanceSampler]::SetStage("idle")
}

function Get-PerformanceResourceWindow(
    [object[]]$Samples,
    [int[]]$Iterations) {
    $windowSamples = @($Samples | Where-Object {
        -not $_.Warmup -and
        $_.Iteration -gt 0 -and
        $_.Stage -ne 'workload.baseline' -and
        $Iterations -contains [int]$_.Iteration
    })
    if ($windowSamples.Count -eq 0) {
        return $null
    }
    [pscustomobject]@{
        SampleCount = $windowSamples.Count
        PrivateMemoryBytes = [long](Get-Percentile `
            ([double[]]@($windowSamples.PrivateMemoryBytes)) 50)
        HandleCount = [int](Get-Percentile `
            ([double[]]@($windowSamples.HandleCount)) 50)
        ThreadCount = [int](Get-Percentile `
            ([double[]]@($windowSamples.ThreadCount)) 50)
    }
}

function Get-PerformanceResourceWindowSize([int]$IterationCount) {
    [int][Math]::Min(3, [Math]::Floor([Math]::Max(0, $IterationCount) / 2))
}

function Stop-PerformanceSession([string]$Name) {
    [WqsPerformanceSampler]::SetStage("workload.finalizing")
    $final = Get-SettledPerformanceProcessSnapshot
    [WqsPerformanceSampler]::SetStage("idle")
    Start-Sleep -Milliseconds 300
    $samples = @([WqsPerformanceSampler]::Stop())
    if ($samples.Count -eq 0) {
        throw "Performance session '$Name' did not collect process samples."
    }
    $launch = $samples[0]
    $baseline = $script:PerformanceWorkloadBaseline
    if ($null -eq $baseline) {
        $baseline = Get-PerformanceProcessSnapshot
    }
    $workloadSamples = @($samples | Where-Object {
        [DateTimeOffset]::Parse($_.Timestamp) -ge $baseline.Timestamp
    })
    if ($workloadSamples.Count -eq 0) {
        $workloadSamples = @($samples[-1])
    }
    [int[]]$measuredIterations = @($samples | Where-Object {
        -not $_.Warmup -and $_.Iteration -gt 0 -and $_.Stage -ne 'workload.baseline'
    } | Select-Object -ExpandProperty Iteration -Unique | Sort-Object)
    # Short runs still enforce boundary budgets. Only compare disjoint windows;
    # comparing an iteration with itself would manufacture zero growth.
    $resourceWindowSize = Get-PerformanceResourceWindowSize $measuredIterations.Count
    $firstResourceWindow = if ($resourceWindowSize -gt 0) {
        Get-PerformanceResourceWindow $samples `
            ([int[]]@($measuredIterations | Select-Object -First $resourceWindowSize))
    } else { $null }
    $lastResourceWindow = if ($resourceWindowSize -gt 0) {
        Get-PerformanceResourceWindow $samples `
            ([int[]]@($measuredIterations | Select-Object -Last $resourceWindowSize))
    } else { $null }
    if ($null -eq $firstResourceWindow) {
        $firstResourceWindow = [pscustomobject]@{
            SampleCount = 0
            PrivateMemoryBytes = $null
            HandleCount = $null
            ThreadCount = $null
        }
    }
    if ($null -eq $lastResourceWindow) {
        $lastResourceWindow = [pscustomobject]@{
            SampleCount = 0
            PrivateMemoryBytes = $null
            HandleCount = $null
            ThreadCount = $null
        }
    }
    $script:PerformanceSessions.Add([pscustomobject]@{
        Name = $Name
        Samples = $samples
        ResourceCoverage = [pscustomobject]@{
            ExpectedIterations = $script:PerformanceIterations
            ObservedIterations = $measuredIterations
            Passed = @(1..$script:PerformanceIterations | Where-Object {
                $measuredIterations -notcontains $_
            }).Count -eq 0
        }
        Summary = [pscustomobject]@{
            SampleCount = $samples.Count
            WorkloadSampleCount = $workloadSamples.Count
            StartupPrivateMemoryBytes = [long]$launch.PrivateMemoryBytes
            InitialPrivateMemoryBytes = [long]$baseline.PrivateMemoryBytes
            FinalPrivateMemoryBytes = [long]$final.PrivateMemoryBytes
            PrivateMemoryDeltaBytes = [long](
                $final.PrivateMemoryBytes - $baseline.PrivateMemoryBytes)
            PeakPrivateMemoryBytes = [long](($workloadSamples |
                Measure-Object -Property PrivateMemoryBytes -Maximum).Maximum)
            PeakWorkingSetBytes = [long](($workloadSamples |
                Measure-Object -Property WorkingSetBytes -Maximum).Maximum)
            InitialHandleCount = [int]$baseline.HandleCount
            FinalHandleCount = [int]$final.HandleCount
            HandleDelta = [int]($final.HandleCount - $baseline.HandleCount)
            InitialThreadCount = [int]$baseline.ThreadCount
            FinalThreadCount = [int]$final.ThreadCount
            ThreadDelta = [int]($final.ThreadCount - $baseline.ThreadCount)
            ResourceWindowIterations = [int]$resourceWindowSize
            ResourceTrendAvailable = $resourceWindowSize -gt 0
            FirstWindowSampleCount = [int]$firstResourceWindow.SampleCount
            LastWindowSampleCount = [int]$lastResourceWindow.SampleCount
            FirstWindowPrivateMemoryBytes = $firstResourceWindow.PrivateMemoryBytes
            LastWindowPrivateMemoryBytes = $lastResourceWindow.PrivateMemoryBytes
            PrivateMemoryTrendBytes = if ($resourceWindowSize -gt 0) { [long](
                $lastResourceWindow.PrivateMemoryBytes -
                    $firstResourceWindow.PrivateMemoryBytes) } else { $null }
            FirstWindowHandleCount = $firstResourceWindow.HandleCount
            LastWindowHandleCount = $lastResourceWindow.HandleCount
            HandleTrend = if ($resourceWindowSize -gt 0) { [int](
                $lastResourceWindow.HandleCount - $firstResourceWindow.HandleCount) } else { $null }
            FirstWindowThreadCount = $firstResourceWindow.ThreadCount
            LastWindowThreadCount = $lastResourceWindow.ThreadCount
            ThreadTrend = if ($resourceWindowSize -gt 0) { [int](
                $lastResourceWindow.ThreadCount - $firstResourceWindow.ThreadCount) } else { $null }
            CpuTimeMilliseconds = [Math]::Round(
                ($final.CpuTimeTicks - $baseline.CpuTimeTicks) /
                    [TimeSpan]::TicksPerMillisecond,
                3)
        }
    })
    $script:PerformanceProcessId = 0
    $script:PerformanceWorkloadBaseline = $null
}

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) { return 0.0 }
    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100.0) * $ordered.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, $index))
    [double]$ordered[$index]
}

function Get-JournalActionCount([string]$Action) {
    if (-not (Test-Path -LiteralPath $script:JournalPath)) { return 0 }
    @(
        Get-Content -LiteralPath $script:JournalPath -ErrorAction SilentlyContinue |
            Where-Object { $_ -match ('"Action"\s*:\s*"{0}"' -f [Regex]::Escape($Action)) }
    ).Count
}

function Wait-JournalActionCount(
    [string]$Action,
    [int]$MinimumCount,
    [int]$TimeoutMilliseconds = 5000) {
    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMilliseconds)
    do {
        if ((Get-JournalActionCount $Action) -ge $MinimumCount) { return }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw "Journal action '$Action' did not reach count $MinimumCount."
}

function Set-ToggleState(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name,
    [bool]$Enabled) {
    $element = Find-AnyNamedElement $Root $Name
    if ($null -eq $element) {
        throw "The toggle '$Name' was not found."
    }
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern,
            [ref]$pattern)) {
        throw "The control '$Name' does not support TogglePattern."
    }
    $toggle = [System.Windows.Automation.TogglePattern]$pattern
    $isEnabled = $toggle.Current.ToggleState -eq
        [System.Windows.Automation.ToggleState]::On
    if ($isEnabled -ne $Enabled) {
        # Use the keyboard activation path so WPF commands and routed click
        # handlers execute; TogglePattern alone can mutate IsChecked without
        # invoking the product behavior being tested.
        Invoke-NamedToggleWithKeyboard $Root $Name
        Start-Sleep -Milliseconds 300
    }
}

function Capture-Window(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name) {
    $handle = [IntPtr]$Root.Current.NativeWindowHandle
    $rect = [WqsSandboxNative+Rect]::new()
    if (-not [WqsSandboxNative]::GetWindowRect($handle, [ref]$rect)) {
        throw "Could not read the bounds for screenshot '$Name'."
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Screenshot '$Name' has invalid bounds $width x $height."
    }
    [WqsSandboxNative]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 300
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $rendered = $false
        $deviceContext = $graphics.GetHdc()
        try {
            # PW_RENDERFULLCONTENT captures the WPF window itself instead of
            # depending on the minimized Sandbox remote desktop framebuffer.
            $rendered = [WqsSandboxNative]::PrintWindow(
                $handle,
                $deviceContext,
                2)
        }
        finally {
            $graphics.ReleaseHdc($deviceContext)
        }
        if (-not $rendered) {
            $lastCaptureError = $null
            for ($captureAttempt = 0; $captureAttempt -lt 5; $captureAttempt++) {
                try {
                    $graphics.CopyFromScreen(
                        $rect.Left,
                        $rect.Top,
                        0,
                        0,
                        [System.Drawing.Size]::new($width, $height))
                    $rendered = $true
                    break
                }
                catch {
                    $lastCaptureError = $_
                    Start-Sleep -Milliseconds 400
                }
            }
            if (-not $rendered) {
                throw $lastCaptureError
            }
        }
        $fileName = "$Name.png"
        $path = Join-Path $resultsRoot $fileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $sampleColors = [System.Collections.Generic.HashSet[int]]::new()
        $stepX = [Math]::Max(1, [int]($width / 20))
        $stepY = [Math]::Max(1, [int]($height / 20))
        for ($x = 0; $x -lt $width; $x += $stepX) {
            for ($y = 0; $y -lt $height; $y += $stepY) {
                [void]$sampleColors.Add($bitmap.GetPixel($x, $y).ToArgb())
            }
        }
        if ($sampleColors.Count -lt 4) {
            throw "Screenshot '$Name' is blank or was not rendered while Sandbox was minimized."
        }
        $script:Screenshots.Add([pscustomobject]@{
            Name = $Name
            File = $fileName
            Width = $width
            Height = $height
        })
        Write-SandboxLog "Captured $fileName ($width x $height)."
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Assert-ControlHasDarkForegroundInScreenshot(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$ScreenshotName,
    [string]$ControlName) {
    $control = Find-NamedElement $Window $ControlName
    $windowRect = [WqsSandboxNative+Rect]::new()
    [WqsSandboxNative]::GetWindowRect(
        [IntPtr]$Window.Current.NativeWindowHandle,
        [ref]$windowRect) | Out-Null
    $bounds = $control.Current.BoundingRectangle
    $path = Join-Path $resultsRoot "$ScreenshotName.png"
    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        # Skip the checkbox glyph and inspect only its text area. In the
        # isolated High Contrast palette this area is white and the label must
        # contain a meaningful number of dark foreground pixels.
        $left = [Math]::Max(0, [int]($bounds.X - $windowRect.Left + 24))
        $right = [Math]::Min(
            $bitmap.Width - 1,
            [int]($bounds.Right - $windowRect.Left))
        $top = [Math]::Max(0, [int]($bounds.Y - $windowRect.Top))
        $bottom = [Math]::Min(
            $bitmap.Height - 1,
            [int]($bounds.Bottom - $windowRect.Top))
        $darkPixels = 0
        for ($x = $left; $x -le $right; $x++) {
            for ($y = $top; $y -le $bottom; $y++) {
                $color = $bitmap.GetPixel($x, $y)
                $luminance = (
                    299 * $color.R +
                    587 * $color.G +
                    114 * $color.B) / 1000
                if ($luminance -lt 128) {
                    $darkPixels++
                }
            }
        }
        Add-Check "High Contrast label '$ControlName' remains visible" (
            $darkPixels -ge 10) (
            "Found $darkPixels dark text pixels in $ScreenshotName.")
    }
    finally {
        $bitmap.Dispose()
    }
}

function Set-SandboxSettings(
    [bool]$UseDarkTheme,
    [bool]$OverlayEnabled) {
    $directoryName = if ($scenario.Edition -eq "Free") {
        "WinQuickSwitch Free"
    }
    else {
        "WinQuickSwitch"
    }
    $settingsDirectory = Join-Path $env:LOCALAPPDATA $directoryName
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    @{
        UseDarkTheme = $UseDarkTheme
        ToggleWidget = $null
        Display = $null
        Audio = $null
        Devices = $null
        AudioVolumeStepPercent = 5
        CommandShortcuts = @{
            "audio.volume-up" = @{ Modifiers = 3; VirtualKey = 85 }
            "audio.volume-down" = @{ Modifiers = 3; VirtualKey = 68 }
            "audio.toggle-output-mute" = @{ Modifiers = 3; VirtualKey = 77 }
            "audio.toggle-microphone-mute" = @{ Modifiers = 3; VirtualKey = 73 }
        }
        OverlayEnabled = $OverlayEnabled
        OverlayDimmingOpacity = 0.35
        WindowPinned = $true
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (
        Join-Path $settingsDirectory "settings.json") -Encoding utf8
}

function Start-TestApplication([string[]]$Arguments) {
    [Environment]::SetEnvironmentVariable(
        "WINQUICKSWITCH_INSTANCE_ID",
        "SandboxUi$([Guid]::NewGuid().ToString('N'))",
        "Process")
    [Environment]::SetEnvironmentVariable(
        "WINQUICKSWITCH_E2E_JOURNAL",
        $script:JournalPath,
        "Process")
    [Environment]::SetEnvironmentVariable(
        "WINQUICKSWITCH_E2E_HIGH_CONTRAST_MARKER",
        $script:HighContrastMarkerPath,
        "Process")
    $effectiveArguments = @($Arguments) + "--sandbox-e2e"
    $startArguments = @{
        FilePath = $script:ApplicationPath
        PassThru = $true
    }
    if ($effectiveArguments.Count -gt 0) {
        $startArguments.ArgumentList = $effectiveArguments
    }
    $process = Start-Process @startArguments
    $script:ApplicationProcess = $process
    $process
}

function Stop-TestApplication {
    if ($null -ne $script:ApplicationProcess -and
        -not $script:ApplicationProcess.HasExited) {
        Stop-Process -Id $script:ApplicationProcess.Id -Force
        $script:ApplicationProcess.WaitForExit()
    }
    $script:ApplicationProcess = $null
}

function Exercise-DisplayFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    Assert-FeatureControls $Window "display" "mainControls"
    Invoke-AnyNamedControl $Window "Refresh display status"
    foreach ($mode in @(
        "PC screen only display mode",
        "Duplicate display mode",
        "Extend display mode",
        "Second screen only display mode")) {
        Invoke-AnyNamedControl $Window $mode
    }
    Invoke-AnyNamedControl $Window "Open Windows display settings"
    Invoke-AnyNamedControl $Window "Open floating Display widget"
    $floating = Wait-TopLevelWindow -ProcessId $script:ApplicationProcess.Id `
        -ExpectedTitle "WinQuickSwitch*Display"
    Capture-Window $floating "floating-display"
    Close-TopLevelWindow $floating "Close floating display widget"
}

function Exercise-AudioFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    Assert-FeatureControls $Window "audio" "mainControls"
    Invoke-AnyNamedControl $Window "Refresh"
    Invoke-AnyNamedControl $Window "Volume mixer"
    Invoke-AnyNamedControl $Window "Sound settings"
    Invoke-AnyNamedControl $Window "Studio Speakers"
    foreach ($role in @("Default", "Calls", "Both")) {
        Invoke-AnyNamedControl $Window $role
    }
    Invoke-AnyNamedControl $Window "Add favorite"
    $removeFavoriteName = "Remove favorite Studio Speakers"
    Add-Check "Audio favorite can be removed" (
        $null -ne (Find-AnyNamedElement $Window $removeFavoriteName))
    Invoke-AnyNamedControl $Window $removeFavoriteName
    Set-NamedRangeValue $Window "Volume for Sample Browser" 38
    Wait-AudioSessionRowReady $Window "Sample Browser"
    Invoke-AnyNamedControl $Window "Mute Sample Browser"
    Invoke-AnyNamedControl $Window "Open floating Audio widget"
    $floating = Wait-TopLevelWindow -ProcessId $script:ApplicationProcess.Id `
        -ExpectedTitle "WinQuickSwitch quick controls"
    Capture-Window $floating "floating-audio"
    Close-TopLevelWindow $floating "Close floating widget"
}

function Exercise-DevicesFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    Assert-FeatureControls $Window "devices" "mainControls"
    Invoke-AnyNamedControl $Window "Refresh devices"
    Invoke-AnyNamedControl $Window "Toggle Wi-Fi"
    Invoke-AnyNamedControl $Window "Toggle Bluetooth"
    foreach ($settings in @(
        "Wi-Fi settings",
        "Bluetooth settings",
        "Airplane mode",
        "Network settings",
        "Connected-device settings")) {
        Invoke-AnyNamedControl $Window $settings
    }
    Invoke-AnyNamedControl $Window "Open floating Devices widget"
    $floating = Wait-TopLevelWindow -ProcessId $script:ApplicationProcess.Id `
        -ExpectedTitle "WinQuickSwitch quick controls"
    Capture-Window $floating "floating-devices"
    Close-TopLevelWindow $floating "Close floating widget"
}

function Exercise-OptionsFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    Assert-FeatureControls $Window "options" "mainControls"
    Assert-FeatureControls $Window "favorites" "mainControls"
    Set-NamedExpanderState $Window `
        "Show or hide keyboard shortcuts" `
        ([System.Windows.Automation.ExpandCollapseState]::Collapsed)
    Set-NamedExpanderState $Window `
        "Show or hide keyboard shortcuts" `
        ([System.Windows.Automation.ExpandCollapseState]::Expanded)
    Assert-FeatureControls $Window "shortcuts" "mainControls"
    $shortcutSearch = Find-NamedElement $Window "Search command shortcuts"
    $searchValue = $shortcutSearch.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $searchValue.SetValue("bluetooth")
    Start-Sleep -Seconds 1
    Capture-Window $Window "main-dark-shortcuts-search"
    $searchAutomation = @($Window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Current.Name
                Type = $_.Current.ControlType.ProgrammaticName
                Offscreen = $_.Current.IsOffscreen
                Enabled = $_.Current.IsEnabled
            }
        })
    $searchAutomation | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (
        Join-Path $resultsRoot "shortcut-search-automation.json") -Encoding utf8
    Add-Check "Shortcut search finds matching command" (
        $null -ne (Find-NamedElement $Window "Set shortcut for Toggle Bluetooth" $false))
    Add-Check "Shortcut search hides unrelated commands" (
        $null -eq (Find-NamedElement $Window "Set shortcut for Open Display" $false))
    $searchValue.SetValue("audio")
    Start-Sleep -Milliseconds 500
    foreach ($audioShortcut in @("Increase output volume", "Decrease output volume", "Toggle output mute", "Toggle microphone mute")) {
        Add-Check "Audio shortcut editor exposes $audioShortcut" (
            $null -ne (Find-NamedElement $Window "Set shortcut for $audioShortcut" $false))
    }
    Add-Check "Audio shortcut step selector is accessible" (
        $null -ne (Find-NamedElement $Window "Volume shortcut step percent" $false))
    Capture-Window $Window "main-dark-audio-shortcuts"
    # This script runs only inside Sandbox with deterministic services. These
    # native hotkeys must reach the simulated endpoint journal, never host audio.
    foreach ($audioAction in @(
        @{ Keys = "^%u"; Action = "audio.endpoint.volume"; Label = "Volume up"; Endpoint = "playback-speakers" },
        @{ Keys = "^%d"; Action = "audio.endpoint.volume"; Label = "Volume down"; Endpoint = "playback-speakers" },
        @{ Keys = "^%m"; Action = "audio.endpoint.mute"; Label = "Output mute"; Endpoint = "playback-speakers" },
        @{ Keys = "^%i"; Action = "audio.endpoint.mute"; Label = "Microphone mute"; Endpoint = "recording-mic" })) {
        $beforeAction = Get-JournalActionCount $audioAction.Action
        $shortcutSearch.SetFocus()
        [WqsSandboxNative]::SendCtrlAltKey([byte][char]$audioAction.Keys.Substring(2).ToUpperInvariant())
        Wait-JournalActionCount $audioAction.Action ($beforeAction + 1)
        $lastAudioAction = Get-Content -LiteralPath $script:JournalPath |
            ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object { $_.Action -eq $audioAction.Action } | Select-Object -Last 1
        Add-Check "Native audio shortcut executes: $($audioAction.Label)" (
            $lastAudioAction.Data.EndpointId -eq $audioAction.Endpoint)
        Start-Sleep -Milliseconds 200
    }
    $searchValue.SetValue("")
    Start-Sleep -Milliseconds 300
    Set-NamedExpanderState $Window `
        "Show or hide keyboard shortcuts" `
        ([System.Windows.Automation.ExpandCollapseState]::Collapsed)
    Invoke-AnyNamedControl $Window "Auto-hide taskbar"
    Invoke-AnyNamedControl $Window "Show taskbar"
    foreach ($settings in @(
        "Taskbar settings",
        "Display settings",
        "Notifications")) {
        Invoke-AnyNamedControl $Window $settings
    }
    Set-ToggleState $Window "Start WinQuickSwitch when I sign in" $true
    Set-ToggleState $Window "Pin the main window open" $true
}

function Exercise-ProfilesFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    if ($scenario.Edition -ne "Paid") {
        return
    }
    Assert-FeatureControls $Window "profiles" "mainControls"
    Add-Check "Populated Profiles does not show an empty inventory message" (
        $null -eq (Find-NamedElement $Window "No profiles saved yet." $false))
    Invoke-AnyNamedControl $Window "Edit profile Workday setup"
    Add-Check "Profile edit can be cancelled" (
        $null -ne (Find-AnyNamedElement $Window "Cancel edit"))
    Invoke-AnyNamedControl $Window "Cancel edit"
    Invoke-AnyNamedControl $Window "Apply profile Workday setup"
    Add-Check "Profile apply exposes Undo" (
        $null -ne (Find-AnyNamedElement $Window "Undo last profile"))
    Invoke-AnyNamedControl $Window "Copy profile Workday setup"
    Invoke-AnyNamedControl $Window "Undo last profile"
}

function Exercise-AboutFeature(
    [System.Windows.Automation.AutomationElement]$Window) {
    Assert-FeatureControls $Window "about" "mainControls"
    $documents = [ordered]@{
        eula = "Open End User License Agreement"
        privacy = "Open privacy policy"
    }
    foreach ($entry in $documents.GetEnumerator()) {
        Invoke-AnyNamedControl $Window $entry.Value
        $legal = Wait-TopLevelWindowContainingControl `
            -ProcessId $script:ApplicationProcess.Id `
            -ControlName "Legal document text"
        Add-Check "$($entry.Key) legal text rendered" (
            $null -ne (Find-AnyNamedElement $legal "Legal document text"))
        Capture-Window $legal "legal-$($entry.Key)"
        Close-TopLevelWindow $legal "Close"
    }
}

function Validate-ServiceJournal {
    $entries = @()
    if (Test-Path -LiteralPath $script:JournalPath) {
        $entries = @(Get-Content -LiteralPath $script:JournalPath | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        } | ForEach-Object { $_ | ConvertFrom-Json })
    }
    $actions = @($entries | ForEach-Object { $_.Action } | Sort-Object -Unique)
    $required = @($coverage.features | Where-Object {
        $_.editions -contains $scenario.Edition
    } | ForEach-Object { $_.journalActions } | Sort-Object -Unique)
    $missing = @($required | Where-Object { $actions -notcontains $_ })
    $forbidden = @()
    if ($scenario.Edition -eq "Free") {
        $forbidden = @($actions | Where-Object { $_ -like "profiles.*" })
    }
    $script:CoverageSummary = [pscustomobject]@{
        FeatureCount = @($coverage.features | Where-Object {
            $_.editions -contains $scenario.Edition
        }).Count
        RequiredJournalActions = $required
        ObservedJournalActions = $actions
        JournalEntryCount = $entries.Count
        MissingJournalActions = $missing
        ForbiddenJournalActions = $forbidden
    }
    Add-Check "Deterministic service action coverage" (
        $missing.Count -eq 0 -and $forbidden.Count -eq 0) (
        "Missing=$($missing -join ', ') Forbidden=$($forbidden -join ', ') Entries=$($entries.Count)")
}

function Capture-MainWindowSuite {
    Set-SandboxSettings -UseDarkTheme $true -OverlayEnabled $false
    $process = Start-TestApplication @("--attended-validation")
    $window = Wait-TopLevelWindow -ProcessId $process.Id
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [WqsSandboxNative]::SetWindowPos(
        $handle,
        [IntPtr]::Zero,
        48,
        36,
        1280,
        900,
        0x0040) | Out-Null
    [WqsSandboxNative]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Seconds 2

    $panels = [ordered]@{
        display = "Open display controls"
        audio = "Open audio controls"
        devices = "Open connected devices"
        options = "Open widget options"
        about = "Open About and disclaimer"
    }
    if ($scenario.Edition -eq "Paid") {
        $panels.Insert(4, "profiles", "Open saved profiles")
    }

    Add-Check "Main window opened" ($window.Current.Name -like "WinQuickSwitch*") (
        "Title was '$($window.Current.Name)'.")
    Assert-FeatureControls $window "shell" "mainControls"
    Assert-FeatureControls $window "workspace" "mainControls"

    foreach ($entry in $panels.GetEnumerator()) {
        Invoke-NamedControl $window $entry.Value
        Capture-Window $window "main-dark-$($entry.Key)"
        switch ($entry.Key) {
            "display" { Exercise-DisplayFeature $window }
            "audio" { Exercise-AudioFeature $window }
            "devices" { Exercise-DevicesFeature $window }
            "options" { Exercise-OptionsFeature $window }
            "profiles" { Exercise-ProfilesFeature $window }
            "about" { Exercise-AboutFeature $window }
        }
        Assert-NamedInteractiveControls $window "Main $($entry.Key)"
    }

    $keyboardPanels = [ordered]@{
        1 = "Refresh display status"
        2 = "Volume mixer"
        3 = "Refresh devices"
        4 = "Show taskbar"
    }
    if ($scenario.Edition -eq "Paid") {
        $keyboardPanels.Add(5, "Apply profile Workday setup")
        $keyboardPanels.Add(6, "Open privacy policy")
    }
    else {
        $keyboardPanels.Add(5, "Open privacy policy")
    }
    foreach ($entry in $keyboardPanels.GetEnumerator()) {
        [WqsSandboxNative]::SetForegroundWindow($handle) | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait("^$($entry.Key)")
        Wait-VisibleNamedControl $window $entry.Value | Out-Null
    }
    Add-Check "Main Ctrl+number navigation follows the edition" $true (
        "Verified $($keyboardPanels.Count) keyboard routes for $($scenario.Edition).")

    Invoke-NamedControl $window "Open widget options"
    Set-ToggleState $window "Use dark theme" $false
    foreach ($entry in $panels.GetEnumerator()) {
        Invoke-NamedControl $window $entry.Value
        Capture-Window $window "main-light-$($entry.Key)"
    }

    Invoke-NamedControl $window "Open widget options"
    Set-SandboxHighContrast $true
    Set-ToggleState $window "Use dark theme" $true
    Add-Check "High Contrast enabled for main window" (
        Test-Path -LiteralPath $script:HighContrastMarkerPath)
    Assert-NamedInteractiveControls $window "Main High Contrast Options"
    Capture-Window $window "main-high-contrast-options"
    # The conventional window's scroll position shows only the first
    # Application checkbox. The overlay screenshot below verifies all three
    # checked labels while fully inside its viewport.
    Assert-ControlHasDarkForegroundInScreenshot `
        $window "main-high-contrast-options" "Use dark theme"
    Set-SandboxHighContrast $false
    Set-ToggleState $window "Use dark theme" $false

    $profileButton = Find-NamedElement $window "Open saved profiles" $false
    Add-Check "Edition-aware main navigation" (
        ($scenario.Edition -eq "Paid" -and $null -ne $profileButton) -or
        ($scenario.Edition -eq "Free" -and $null -eq $profileButton)) (
        "Edition was $($scenario.Edition).")
    Stop-TestApplication
}

function Capture-OverlaySuite {
    Set-SandboxSettings -UseDarkTheme $true -OverlayEnabled $true
    $process = Start-TestApplication @()
    $window = Wait-TopLevelWindow -ProcessId $process.Id -ExpectedTitle (
        "WinQuickSwitch overlay")
    Start-Sleep -Seconds 3
    Add-Check "Overlay opened" ($window.Current.Name -eq "WinQuickSwitch overlay")
    Add-Check "Old workspace heading removed" (
        $null -eq (Find-NamedElement $window "Quick workspace" $false))
    Add-Check "Redundant workspace close button removed" (
        $null -eq (Find-AnyNamedElement $window "Close workspace"))
    Assert-FeatureControls $window "shell" "overlayControls"
    $missingDefaultWidgets = @(@(
        "Remove Display quick widget",
        "Remove Audio quick widget",
        "Remove Devices quick widget") | Where-Object {
            $null -eq (Find-NamedElement $window $_ $false)
        })
    Add-Check "Default workspace widgets visible" (
        $missingDefaultWidgets.Count -eq 0) (
        "Missing visible cards: $($missingDefaultWidgets -join ', ')")
    Capture-Window $window "overlay-dark-workspace"

    foreach ($feature in @("Display", "Audio", "Devices")) {
        $refreshAction = "$($feature.ToLowerInvariant()).refresh"
        $expectedRefreshCount = (Get-JournalActionCount $refreshAction) + 1
        Invoke-NamedControl $window "Refresh $feature"
        Wait-JournalActionCount $refreshAction $expectedRefreshCount
        Add-Check "Workspace $feature header refresh reaches the shared service" $true
    }

    $launchers = [ordered]@{
        display = "Open Display controls"
        audio = "Open Audio controls"
        devices = "Open Devices controls"
        options = "Open Options"
        about = "Open About"
    }
    if ($scenario.Edition -eq "Paid") {
        $launchers.Insert(4, "profiles", "Open Profiles")
    }

    $firstLauncher = Find-NamedElement $window "Open Display controls"
    $firstLauncher.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("{DOWN}")
    Start-Sleep -Milliseconds 300
    $focusedLauncher = [System.Windows.Automation.AutomationElement]::FocusedElement
    Add-Check "Overlay arrow keys move through the launcher" (
        $null -ne $focusedLauncher -and
        $focusedLauncher.Current.Name -eq "Open Audio controls") (
        "Focused control was '$($focusedLauncher.Current.Name)'.")

    $keyboardInspectors = [ordered]@{
        1 = "Refresh display status"
        2 = "Volume mixer"
        3 = "Refresh devices"
        4 = "Show taskbar"
    }
    if ($scenario.Edition -eq "Paid") {
        $keyboardInspectors.Add(5, "Apply profile Workday setup")
        $keyboardInspectors.Add(6, "Open privacy policy")
    }
    else {
        $keyboardInspectors.Add(5, "Open privacy policy")
    }
    foreach ($entry in $keyboardInspectors.GetEnumerator()) {
        [System.Windows.Forms.SendKeys]::SendWait("^$($entry.Key)")
        Wait-VisibleNamedControl $window "Back to workspace" | Out-Null
        Wait-VisibleNamedControl $window $entry.Value | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
        Wait-NamedControlAbsent $window "Back to workspace"
    }
    Add-Check "Overlay Ctrl+number and Escape navigation follows the edition" $true (
        "Verified $($keyboardInspectors.Count) keyboard routes for $($scenario.Edition).")

    foreach ($entry in $launchers.GetEnumerator()) {
        Assert-FeatureControls $window $entry.Key "overlayControls"
        Invoke-NamedControl $window $entry.Value
        Assert-FeatureControls $window "workspace" "overlayControls"
        Assert-FeatureControls $window $entry.Key "mainControls"
        Capture-Window $window "overlay-dark-$($entry.Key)-inspector"
        Assert-NamedInteractiveControls $window "Overlay $($entry.Key) inspector"
        if ($entry.Key -eq "display") {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            Add-Check "Inspector receives keyboard focus" (
                $null -ne $focused -and
                $focused.Current.IsKeyboardFocusable -and
                -not [string]::IsNullOrWhiteSpace($focused.Current.Name) -and
                $focused.Current.Name -ne $entry.Value) (
                "Focused control was '$($focused.Current.Name)'.")
            [System.Windows.Forms.SendKeys]::SendWait("^0")
            Start-Sleep -Milliseconds 900
            $backControl = Find-NamedElement $window "Back to workspace" $false
            Add-Check "Ctrl+0 returns to workspace" (
                $null -eq $backControl -or $backControl.Current.IsOffscreen)
        }
    }

    Invoke-NamedControl $window "Open Options"
    Set-ToggleState $window "Use dark theme" $false
    Capture-Window $window "overlay-light-options-inspector"
    Invoke-NamedControl $window "Back to workspace"
    Capture-Window $window "overlay-light-workspace"

    $overlayHandle = [IntPtr]$window.Current.NativeWindowHandle
    [WqsSandboxNative]::SetWindowPos(
        $overlayHandle,
        [IntPtr]::Zero,
        24,
        24,
        920,
        700,
        0x0040) | Out-Null
    Start-Sleep -Milliseconds 900
    $narrowRect = [WqsSandboxNative+Rect]::new()
    [WqsSandboxNative]::GetWindowRect($overlayHandle, [ref]$narrowRect) | Out-Null
    Add-Check "Overlay supports a constrained responsive work area" (
        ($narrowRect.Right - $narrowRect.Left) -le 960 -and
        ($narrowRect.Bottom - $narrowRect.Top) -le 740) (
        "Bounds were $($narrowRect.Right - $narrowRect.Left)x$($narrowRect.Bottom - $narrowRect.Top).")
    Capture-Window $window "overlay-light-constrained-workspace"

    Invoke-NamedControl $window "Open Options"
    Set-SandboxHighContrast $true
    Set-ToggleState $window "Use dark theme" $true
    Add-Check "High Contrast enabled for overlay" (
        Test-Path -LiteralPath $script:HighContrastMarkerPath)
    Assert-NamedInteractiveControls $window "Overlay High Contrast Options"
    Capture-Window $window "overlay-high-contrast-options"
    foreach ($controlName in @(
        "Use dark theme",
        "Use the dimmed workspace overlay",
        "Pin the main window open")) {
        Assert-ControlHasDarkForegroundInScreenshot `
            $window "overlay-high-contrast-options" $controlName
    }
    Set-SandboxHighContrast $false
    Set-ToggleState $window "Use dark theme" $false

    $profileButton = Find-NamedElement $window "Open Profiles" $false
    Add-Check "Edition-aware overlay navigation" (
        ($scenario.Edition -eq "Paid" -and $null -ne $profileButton) -or
        ($scenario.Edition -eq "Free" -and $null -eq $profileButton)) (
        "Edition was $($scenario.Edition).")
    # The five-DIP outer margin is bare OverlayRoot, even with an inspector
    # open. Do not click through into host apps: this helper runs inside Sandbox.
    $dismissRect = [WqsSandboxNative+Rect]::new()
    [WqsSandboxNative]::GetWindowRect($overlayHandle, [ref]$dismissRect) | Out-Null
    [WqsSandboxNative]::ClickAt($dismissRect.Left + 5, $dismissRect.Top + 5)
    Wait-WindowClosed $overlayHandle
    Add-Check "Clicking empty overlay background dismisses the workspace" $true
    Stop-TestApplication
}

function Add-StartupPerformanceStage(
    [string]$Name,
    [System.Diagnostics.Stopwatch]$Stopwatch) {
    $Stopwatch.Stop()
    $script:PerformanceStages.Add([pscustomobject]@{
        Name = $Name
        Iteration = 0
        Warmup = $false
        DurationMilliseconds = [Math]::Round($Stopwatch.Elapsed.TotalMilliseconds, 3)
        Passed = $true
        Error = ""
    })
}

function Invoke-PerformanceNavigation(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StageName,
    [string]$NavigationControl,
    [string]$ReadyControl) {
    $navigationElement = Get-CachedInvokeControl $Window $NavigationControl
    Measure-PerformanceStage $StageName {
        Invoke-CachedControl $navigationElement $NavigationControl
        Wait-FirstVisibleNamedControl $Window $ReadyControl | Out-Null
    }
}

function Invoke-PerformanceRefresh(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StageName,
    [string]$ControlName,
    [string]$JournalAction) {
    $expectedCount = (Get-JournalActionCount $JournalAction) + 1
    $refreshElement = Get-CachedInvokeControl $Window $ControlName
    Measure-PerformanceStage $StageName {
        Invoke-CachedControl $refreshElement $ControlName
        Wait-JournalActionCount $JournalAction $expectedCount
        Wait-FirstVisibleNamedControl $Window $ControlName | Out-Null
    }
}

function Invoke-PerformanceAction(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StageName,
    [string]$ControlName,
    [string]$JournalAction = "",
    [string]$ReadyControl = "") {
    $expectedCount = if ([string]::IsNullOrWhiteSpace($JournalAction)) {
        0
    }
    else {
        (Get-JournalActionCount $JournalAction) + 1
    }
    $element = Get-CachedInvokeControl $Window $ControlName
    Measure-PerformanceStage $StageName {
        Invoke-CachedControl $element $ControlName
        if (-not [string]::IsNullOrWhiteSpace($JournalAction)) {
            Wait-JournalActionCount $JournalAction $expectedCount
        }
        if (-not [string]::IsNullOrWhiteSpace($ReadyControl)) {
            Wait-FirstVisibleNamedControl $Window $ReadyControl | Out-Null
        }
    }
}

function Set-PerformanceRangeValue(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StageName,
    [string]$ControlName,
    [double]$Value,
    [string]$JournalAction) {
    $expectedCount = (Get-JournalActionCount $JournalAction) + 1
    $element = Find-NamedElement $Window $ControlName
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.RangeValuePattern]::Pattern,
            [ref]$pattern)) {
        throw "The performance control '$ControlName' does not support RangeValuePattern."
    }
    Measure-PerformanceStage $StageName {
        ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue($Value)
        $element.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
        Wait-JournalActionCount $JournalAction $expectedCount
    }
}

function Measure-PerformanceExpanderState(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StageName,
    [string]$ControlName,
    [System.Windows.Automation.ExpandCollapseState]$ExpectedState) {
    $element = Find-NamedElement $Window $ControlName
    $patternValue = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$patternValue)) {
        throw "The performance control '$ControlName' does not support ExpandCollapsePattern."
    }
    $pattern = [System.Windows.Automation.ExpandCollapsePattern]$patternValue
    Measure-PerformanceStage $StageName {
        if ($ExpectedState -eq
            [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            $pattern.Expand()
        }
        else {
            $pattern.Collapse()
        }
        $deadline = [DateTimeOffset]::Now.AddSeconds(5)
        do {
            if ($pattern.Current.ExpandCollapseState -eq $ExpectedState) {
                return
            }
            Start-Sleep -Milliseconds 25
        } while ([DateTimeOffset]::Now -lt $deadline)
        throw "The control '$ControlName' did not reach $ExpectedState within 5000 ms."
    }
}

function Measure-PerformanceAuxiliaryWindow(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StagePrefix,
    [string]$OpenControl,
    [string]$ExpectedTitle,
    [string]$ContainingControl,
    [string]$CloseControl) {
    $openElement = Get-CachedInvokeControl $Window $OpenControl
    $script:PerformanceAuxiliaryWindow = $null
    Measure-PerformanceStage "$StagePrefix.open" {
        Invoke-CachedControl $openElement $OpenControl
        $script:PerformanceAuxiliaryWindow = if (
            -not [string]::IsNullOrWhiteSpace($ContainingControl)) {
            $ownerHandle = [IntPtr]$Window.Current.NativeWindowHandle
            Wait-TopLevelWindowContainingControl `
                -ProcessId $script:ApplicationProcess.Id `
                -ControlName $ContainingControl `
                -ExcludedHandle $ownerHandle
        }
        else {
            Wait-TopLevelWindow `
                -ProcessId $script:ApplicationProcess.Id `
                -ExpectedTitle $ExpectedTitle
        }
    }
    $auxiliary = $script:PerformanceAuxiliaryWindow
    $handle = [IntPtr]$auxiliary.Current.NativeWindowHandle
    $closeElement = Get-CachedInvokeControl $auxiliary $CloseControl
    $windowPatternValue = $null
    [void]$auxiliary.TryGetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern,
        [ref]$windowPatternValue)
    Measure-PerformanceStage "$StagePrefix.close" {
        Invoke-CachedControl $closeElement $CloseControl
        try {
            Wait-WindowClosed $handle 750
        }
        catch {
            if ($null -eq $windowPatternValue) { throw }
            ([System.Windows.Automation.WindowPattern]$windowPatternValue).Close()
            Wait-WindowClosed $handle
        }
    }
    $script:PerformanceAuxiliaryWindow = $null
}

function Measure-PerformanceModalControl(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$StagePrefix,
    [string]$OpenControl,
    [string]$ContentControl,
    [string]$CloseControl) {
    $openElement = Get-CachedInvokeControl $Window $OpenControl
    $script:PerformanceModalWindow = $null
    Measure-PerformanceStage "$StagePrefix.open" {
        Invoke-CachedControl $openElement $OpenControl
        $script:PerformanceModalWindow = Wait-ProcessWindowContainingControl `
            $script:ApplicationProcess.Id $ContentControl
    }
    $modalWindow = $script:PerformanceModalWindow
    $modalHandle = [IntPtr]$modalWindow.Current.NativeWindowHandle
    $windowPatternValue = $null
    if (-not $modalWindow.TryGetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern,
            [ref]$windowPatternValue)) {
        throw "The modal opened by '$OpenControl' does not support WindowPattern."
    }
    Measure-PerformanceStage "$StagePrefix.close" {
        ([System.Windows.Automation.WindowPattern]$windowPatternValue).Close()
        Wait-WindowClosed $modalHandle
    }
    $script:PerformanceModalWindow = $null
}

function Measure-DisplayFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    $modes = [ordered]@{
        "PC screen only display mode" = "pc-screen-only"
        "Duplicate display mode" = "duplicate"
        "Extend display mode" = "extend"
        "Second screen only display mode" = "second-screen-only"
    }
    foreach ($mode in $modes.GetEnumerator()) {
        Invoke-PerformanceAction $Window `
            "main.action.display.$($mode.Value)" `
            $mode.Key `
            "display.apply"
    }
    Invoke-PerformanceAction $Window `
        "main.action.display.open-settings" `
        "Open Windows display settings" `
        "settings.windows"
    Measure-PerformanceAuxiliaryWindow $Window `
        "main.action.display.floating" `
        "Open floating Display widget" `
        "WinQuickSwitch*Display" `
        "" `
        "Close floating display widget"
}

function Measure-AudioFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    Invoke-PerformanceAction $Window `
        "main.action.audio.select-output" `
        "Studio Speakers"
    foreach ($role in @("Default", "Calls", "Both")) {
        Invoke-PerformanceAction $Window `
            "main.action.audio.role.$($role.ToLowerInvariant())" `
            $role `
            "audio.default"
    }

    $removeFavoriteName = "Remove favorite Studio Speakers"
    Invoke-PerformanceAction $Window `
        "main.action.audio.favorite.add" `
        "Add favorite" `
        "" `
        $removeFavoriteName
    $removeFavorite = Get-CachedInvokeControl $Window $removeFavoriteName
    Measure-PerformanceStage "main.action.audio.favorite.remove" {
        Invoke-CachedControl $removeFavorite $removeFavoriteName
        Wait-NamedControlAbsent $Window $removeFavoriteName
    }

    # Alternate the value on every warm-up/measured cycle. Setting a WPF
    # RangeValue control to its current value correctly produces no change
    # notification, which would make the journal wait measure a timeout rather
    # than an actual volume mutation.
    $volumeValue = if ($script:CurrentPerformanceWarmup) {
        if (($script:CurrentPerformanceIteration % 2) -eq 0) { 30 } else { 40 }
    }
    else {
        if (($script:CurrentPerformanceIteration % 2) -eq 0) { 45 } else { 55 }
    }
    Set-PerformanceRangeValue $Window `
        "main.action.audio.session.volume" `
        "Volume for Sample Browser" `
        $volumeValue `
        "audio.session.volume"
    Wait-AudioSessionRowReady $Window "Sample Browser"
    Invoke-PerformanceAction $Window `
        "main.action.audio.session.mute" `
        "Mute Sample Browser" `
        "audio.session.mute"
    Invoke-PerformanceAction $Window `
        "main.action.audio.open-volume-mixer" `
        "Volume mixer" `
        "settings.volumeMixer"
    Invoke-PerformanceAction $Window `
        "main.action.audio.open-sound-settings" `
        "Sound settings" `
        "settings.sound"
    Measure-PerformanceAuxiliaryWindow $Window `
        "main.action.audio.floating" `
        "Open floating Audio widget" `
        "WinQuickSwitch quick controls" `
        "" `
        "Close floating widget"
}

function Measure-DevicesFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    Invoke-PerformanceAction $Window `
        "main.action.devices.wifi.off" `
        "Toggle Wi-Fi" `
        "radios.set"
    Invoke-PerformanceAction $Window `
        "main.action.devices.wifi.on" `
        "Toggle Wi-Fi" `
        "radios.set"
    Invoke-PerformanceAction $Window `
        "main.action.devices.bluetooth.off" `
        "Toggle Bluetooth" `
        "radios.set"
    Invoke-PerformanceAction $Window `
        "main.action.devices.bluetooth.on" `
        "Toggle Bluetooth" `
        "radios.set"
    $settings = [ordered]@{
        "Wi-Fi settings" = "wifi-settings"
        "Bluetooth settings" = "bluetooth-settings"
        "Airplane mode" = "airplane-mode"
        "Network settings" = "network-settings"
        "Connected-device settings" = "connected-device-settings"
    }
    foreach ($entry in $settings.GetEnumerator()) {
        Invoke-PerformanceAction $Window `
            "main.action.devices.$($entry.Value)" `
            $entry.Key `
            "settings.devices"
    }
    Measure-PerformanceAuxiliaryWindow $Window `
        "main.action.devices.floating" `
        "Open floating Devices widget" `
        "WinQuickSwitch quick controls" `
        "" `
        "Close floating widget"
}

function Measure-OptionsFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    Measure-PerformanceExpanderState $Window `
        "main.action.options.shortcuts.expand" `
        "Show or hide keyboard shortcuts" `
        ([System.Windows.Automation.ExpandCollapseState]::Expanded)
    Measure-PerformanceExpanderState $Window `
        "main.action.options.shortcuts.collapse" `
        "Show or hide keyboard shortcuts" `
        ([System.Windows.Automation.ExpandCollapseState]::Collapsed)

    Invoke-PerformanceAction $Window `
        "main.action.options.taskbar.auto-hide" `
        "Auto-hide taskbar" `
        "taskbar.autoHide"
    Invoke-PerformanceAction $Window `
        "main.action.options.taskbar.show" `
        "Show taskbar" `
        "taskbar.autoHide"
    $settings = [ordered]@{
        "Taskbar settings" = "taskbar-settings"
        "Display settings" = "display-settings"
        "Notifications" = "notifications"
    }
    foreach ($entry in $settings.GetEnumerator()) {
        Invoke-PerformanceAction $Window `
            "main.action.options.$($entry.Value)" `
            $entry.Key `
            "settings.windows"
    }
    Invoke-PerformanceAction $Window `
        "main.action.options.startup.enable" `
        "Start WinQuickSwitch when I sign in" `
        "startup.set"
    Invoke-PerformanceAction $Window `
        "main.action.options.startup.disable" `
        "Start WinQuickSwitch when I sign in" `
        "startup.set"
    Invoke-PerformanceAction $Window `
        "main.action.options.pin.unpin" `
        "Pin the main window open"
    Invoke-PerformanceAction $Window `
        "main.action.options.pin.pin" `
        "Pin the main window open"
    Invoke-PerformanceAction $Window `
        "main.action.options.theme.light" `
        "Use dark theme"
    Invoke-PerformanceAction $Window `
        "main.action.options.theme.dark" `
        "Use dark theme"
    Invoke-PerformanceAction $Window `
        "main.action.options.overlay.enable" `
        "Use the dimmed workspace overlay"
    Invoke-PerformanceAction $Window `
        "main.action.options.overlay.disable" `
        "Use the dimmed workspace overlay"
}

function Measure-ProfilesFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    if ($scenario.Edition -ne "Paid") { return }
    Invoke-PerformanceAction $Window `
        "main.action.profiles.edit" `
        "Edit profile Workday setup" `
        "" `
        "Cancel edit"
    Invoke-PerformanceAction $Window `
        "main.action.profiles.cancel-edit" `
        "Cancel edit" `
        "" `
        "Edit profile Workday setup"
    Invoke-PerformanceAction $Window `
        "main.action.profiles.apply" `
        "Apply profile Workday setup" `
        "display.apply" `
        "Undo last profile"
    Invoke-PerformanceAction $Window `
        "main.action.profiles.undo" `
        "Undo last profile"
    if (-not $script:CurrentPerformanceWarmup -and
        $script:CurrentPerformanceIteration -eq 1) {
        Invoke-PerformanceAction $Window `
            "main.action.profiles.copy" `
            "Copy profile Workday setup" `
            "profiles.save" `
            "Apply profile Workday setup copy"
    }
}

function Measure-AboutFeatureActions(
    [System.Windows.Automation.AutomationElement]$Window) {
    Measure-PerformanceModalControl $Window `
        "main.action.about.eula" `
        "Open End User License Agreement" `
        "Legal document text" `
        "Close"
    Measure-PerformanceModalControl $Window `
        "main.action.about.privacy" `
        "Open privacy policy" `
        "Legal document text" `
        "Close"
}

function Capture-PerformanceDiagnostic(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$Name) {
    [WqsPerformanceSampler]::SetStage("diagnostic.screenshot")
    Capture-Window $Window $Name
    [WqsPerformanceSampler]::SetStage("idle")
}

function Complete-PerformanceProcess(
    [System.Windows.Automation.AutomationElement]$Window,
    [string]$SessionName,
    [string]$ExitControl) {
    $exitElement = Get-CachedInvokeControl $Window $ExitControl
    Measure-PerformanceStage "$SessionName.exit" {
        Invoke-CachedControl $exitElement $ExitControl
        if (-not $script:ApplicationProcess.WaitForExit(5000)) {
            throw "$SessionName did not exit gracefully."
        }
    }
    $script:ApplicationProcess = $null
}

function Measure-MainWindowPerformance {
    Set-SandboxSettings -UseDarkTheme $true -OverlayEnabled $false
    $startup = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-TestApplication @("--attended-validation")
    Start-PerformanceSession $process "main"
    $window = Wait-TopLevelWindow -ProcessId $process.Id
    Wait-VisibleNamedControl $window "Refresh display status" | Out-Null
    Add-StartupPerformanceStage "main.startup" $startup
    Capture-PerformanceDiagnostic $window "performance-main-initial"

    $panels = [ordered]@{
        display = @("Open display controls", "Refresh display status")
        audio = @("Open audio controls", "Volume mixer")
        devices = @("Open connected devices", "Refresh devices")
        options = @("Open widget options", "Show taskbar")
        about = @("Open About and disclaimer", "Open privacy policy")
    }
    if ($scenario.Edition -eq "Paid") {
        $panels.Insert(4, "profiles", @("Open saved profiles", "Apply profile Workday setup"))
    }

    $totalIterations = $script:PerformanceWarmupIterations +
        $script:PerformanceIterations
    for ($index = 0; $index -lt $totalIterations; $index++) {
        $script:CurrentPerformanceWarmup =
            $index -lt $script:PerformanceWarmupIterations
        $script:CurrentPerformanceIteration = if ($script:CurrentPerformanceWarmup) {
            $index + 1
        } else {
            $index - $script:PerformanceWarmupIterations + 1
        }
        [WqsPerformanceSampler]::SetContext(
            $script:CurrentPerformanceIteration,
            $script:CurrentPerformanceWarmup)
        if ($index -eq $script:PerformanceWarmupIterations) {
            Set-PerformanceWorkloadBaseline
        }

        foreach ($entry in $panels.GetEnumerator()) {
            Invoke-PerformanceNavigation $window `
                "main.navigate.$($entry.Key)" `
                $entry.Value[0] `
                $entry.Value[1]
        }

        Invoke-PerformanceNavigation $window "main.navigate.display-refresh" `
            "Open display controls" "Refresh display status"
        Invoke-PerformanceRefresh $window "main.refresh.display" `
            "Refresh display status" "display.refresh"
        Invoke-PerformanceNavigation $window "main.navigate.audio-refresh" `
            "Open audio controls" "Refresh"
        Invoke-PerformanceRefresh $window "main.refresh.audio" `
            "Refresh" "audio.refresh"
        Invoke-PerformanceNavigation $window "main.navigate.devices-refresh" `
            "Open connected devices" "Refresh devices"
        Invoke-PerformanceRefresh $window "main.refresh.devices" `
            "Refresh devices" "devices.refresh"

        Invoke-PerformanceNavigation $window "main.navigate.display" `
            "Open display controls" "Refresh display status"
        Measure-DisplayFeatureActions $window
        Invoke-PerformanceNavigation $window "main.navigate.audio" `
            "Open audio controls" "Volume mixer"
        Measure-AudioFeatureActions $window
        Invoke-PerformanceNavigation $window "main.navigate.devices" `
            "Open connected devices" "Refresh devices"
        Measure-DevicesFeatureActions $window
        Invoke-PerformanceNavigation $window "main.navigate.options" `
            "Open widget options" "Show taskbar"
        Measure-OptionsFeatureActions $window
        if ($scenario.Edition -eq "Paid") {
            Invoke-PerformanceNavigation $window "main.navigate.profiles" `
                "Open saved profiles" "Apply profile Workday setup"
            Measure-ProfilesFeatureActions $window
        }
        Invoke-PerformanceNavigation $window "main.navigate.about" `
            "Open About and disclaimer" "Open privacy policy"
        Measure-AboutFeatureActions $window
        Start-Sleep -Milliseconds 75
        Complete-PerformanceIteration 'main'
    }

    $script:CurrentPerformanceWarmup = $false
    $script:CurrentPerformanceIteration = 0
    [WqsPerformanceSampler]::SetContext(0, $false)
    Stop-PerformanceSession "main"
    Capture-PerformanceDiagnostic $window "performance-main-final"
    Complete-PerformanceProcess $window "main" "Quit"
}

function Measure-OverlayPerformance {
    Set-SandboxSettings -UseDarkTheme $true -OverlayEnabled $true
    $startup = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-TestApplication @()
    Start-PerformanceSession $process "overlay"
    $window = Wait-TopLevelWindow -ProcessId $process.Id `
        -ExpectedTitle "WinQuickSwitch overlay"
    Wait-VisibleNamedControl $window "Open Display controls" | Out-Null
    Add-StartupPerformanceStage "overlay.startup" $startup
    Capture-PerformanceDiagnostic $window "performance-overlay-initial"

    $panels = [ordered]@{
        display = @("Open Display controls", "Refresh display status")
        audio = @("Open Audio controls", "Volume mixer")
        devices = @("Open Devices controls", "Refresh devices")
        options = @("Open Options", "Show taskbar")
        about = @("Open About", "Open privacy policy")
    }
    if ($scenario.Edition -eq "Paid") {
        $panels.Insert(4, "profiles", @("Open Profiles", "Apply profile Workday setup"))
    }

    $totalIterations = $script:PerformanceWarmupIterations +
        $script:PerformanceIterations
    for ($index = 0; $index -lt $totalIterations; $index++) {
        $script:CurrentPerformanceWarmup =
            $index -lt $script:PerformanceWarmupIterations
        $script:CurrentPerformanceIteration = if ($script:CurrentPerformanceWarmup) {
            $index + 1
        } else {
            $index - $script:PerformanceWarmupIterations + 1
        }
        [WqsPerformanceSampler]::SetContext(
            $script:CurrentPerformanceIteration,
            $script:CurrentPerformanceWarmup)
        if ($index -eq $script:PerformanceWarmupIterations) {
            Set-PerformanceWorkloadBaseline
        }

        foreach ($entry in $panels.GetEnumerator()) {
            Invoke-PerformanceNavigation $window `
                "overlay.inspect.$($entry.Key)" `
                $entry.Value[0] `
                $entry.Value[1]
            Measure-PerformanceStage "overlay.back-to-workspace" {
                Invoke-NamedControl $window "Back to workspace" 0
                Wait-VisibleNamedControl $window "Open Display controls" | Out-Null
            }
        }
        Start-Sleep -Milliseconds 75
        Complete-PerformanceIteration 'overlay'
    }

    $script:CurrentPerformanceWarmup = $false
    $script:CurrentPerformanceIteration = 0
    [WqsPerformanceSampler]::SetContext(0, $false)
    Stop-PerformanceSession "overlay"
    Capture-PerformanceDiagnostic $window "performance-overlay-final"
    Complete-PerformanceProcess $window "overlay" "Exit WinQuickSwitch"
}

function New-PerformanceResult {
    $measuredStages = @($script:PerformanceStages | Where-Object { -not $_.Warmup })
    $stageSummary = foreach ($group in ($measuredStages | Group-Object Name)) {
        [double[]]$values = @($group.Group | ForEach-Object {
            [double]$_.DurationMilliseconds
        })
        [pscustomobject]@{
            Name = $group.Name
            Count = $values.Count
            MinimumMilliseconds = [Math]::Round(($values | Measure-Object -Minimum).Minimum, 3)
            MedianMilliseconds = [Math]::Round((Get-Percentile $values 50), 3)
            P95Milliseconds = [Math]::Round((Get-Percentile $values 95), 3)
            MaximumMilliseconds = [Math]::Round(($values | Measure-Object -Maximum).Maximum, 3)
            AverageMilliseconds = [Math]::Round(($values | Measure-Object -Average).Average, 3)
        }
    }

    $expectedStages = @($coverage.features | Where-Object {
        $_.editions -contains $scenario.Edition -and
        $null -ne $_.PSObject.Properties["performanceStages"]
    } | ForEach-Object { $_.performanceStages } | Sort-Object -Unique)
    $observedStages = @($stageSummary | ForEach-Object { $_.Name } | Sort-Object -Unique)
    $missingStages = @($expectedStages | Where-Object {
        $observedStages -notcontains $_
    })

    $budgetFailures = [System.Collections.Generic.List[string]]::new()
    foreach ($stage in $stageSummary) {
        $limit = if ($stage.Name -eq "main.startup") { 3000 }
            elseif ($stage.Name -eq "overlay.startup") { 2500 }
            else { 1500 }
        if ($stage.P95Milliseconds -gt $limit) {
            $budgetFailures.Add(
                "$($stage.Name) P95 $($stage.P95Milliseconds) ms exceeded $limit ms.")
        }
    }
    foreach ($session in $script:PerformanceSessions) {
        $summary = $session.Summary
        if (-not $session.ResourceCoverage.Passed) {
            $budgetFailures.Add("$($session.Name) resource samples are missing measured iterations.")
        }
        if ($summary.PrivateMemoryDeltaBytes -gt 15MB) {
            $budgetFailures.Add(
                "$($session.Name) private-memory delta exceeded 15 MB.")
        }
        if ($summary.HandleDelta -gt 10) {
            $budgetFailures.Add("$($session.Name) handle delta exceeded 10.")
        }
        if ($summary.ThreadDelta -gt 2) {
            $budgetFailures.Add("$($session.Name) thread delta exceeded 2.")
        }
    }

    [pscustomobject]@{
        SchemaVersion = 2
        Edition = $scenario.Edition
        Version = $scenario.Version
        CompletedAt = [DateTimeOffset]::Now.ToString("O")
        WarmupIterations = $script:PerformanceWarmupIterations
        MeasuredIterations = $script:PerformanceIterations
        SampleIntervalMilliseconds = 100
        ScreenshotTimingExcluded = $true
        Stages = @($script:PerformanceStages)
        StageSummary = @($stageSummary)
        Sessions = @($script:PerformanceSessions)
        Coverage = [pscustomobject]@{
            ExpectedStages = $expectedStages
            ObservedStages = $observedStages
            MissingStages = $missingStages
            ResourceSamplesComplete = @($script:PerformanceSessions | Where-Object {
                -not $_.ResourceCoverage.Passed
            }).Count -eq 0
            Passed = $missingStages.Count -eq 0 -and @($script:PerformanceSessions | Where-Object {
                -not $_.ResourceCoverage.Passed
            }).Count -eq 0
        }
        Budgets = [pscustomobject]@{
            Enforced = $script:EnforcePerformanceBudgets
            Passed = $budgetFailures.Count -eq 0
            Failures = @($budgetFailures)
        }
    }
}

function Run-PerformanceSuite {
    Measure-MainWindowPerformance
    Measure-OverlayPerformance
}

New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $payloadRoot "FeatureCoverage.json") `
    -Destination (Join-Path $resultsRoot "FeatureCoverage.json") -Force
Set-Content -LiteralPath $logPath -Value "" -Encoding utf8
$localRoot = "C:\WQS\Runner"
$localApp = Join-Path $localRoot "App"
New-Item -ItemType Directory -Path $localApp -Force | Out-Null
Copy-Item -Path (Join-Path $payloadRoot "App\*") -Destination $localApp -Recurse -Force
$script:ApplicationPath = Join-Path $localApp "WinQuickSwitch.exe"
Unblock-File -LiteralPath $script:ApplicationPath -ErrorAction SilentlyContinue

$runError = $null
try {
    if ($script:PerformanceMode) {
        Write-SandboxLog "Starting $($scenario.Edition) v$($scenario.Version) performance suite."
        Run-PerformanceSuite
    }
    else {
        Write-SandboxLog "Starting $($scenario.Edition) v$($scenario.Version) UI suite."
        Capture-MainWindowSuite
        Capture-OverlaySuite
        Validate-ServiceJournal
    }
}
catch {
    $runError = $_.Exception.ToString()
    $script:Checks.Add([pscustomobject]@{
        Name = "Sandbox UI suite"
        Passed = $false
        Details = $runError
    })
    Write-SandboxLog $runError
}
finally {
    [void][WqsPerformanceSampler]::Stop()
    Stop-TestApplication
    Set-SandboxHighContrast $false
    $performance = if ($script:PerformanceMode) {
        New-PerformanceResult
    } else { $null }
    if ($null -ne $performance) {
        $performance | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (
            Join-Path $resultsRoot "performance.json") -Encoding utf8
    }
    $passed = $null -eq $runError -and
        -not ($script:Checks | Where-Object { -not $_.Passed })
    if ($script:PerformanceMode -and
        $null -ne $performance -and
        -not $performance.Coverage.Passed) {
        $passed = $false
    }
    if ($script:PerformanceMode -and
        $script:EnforcePerformanceBudgets -and
        $null -ne $performance -and
        -not $performance.Budgets.Passed) {
        $passed = $false
    }
    @{
        Edition = $scenario.Edition
        Version = $scenario.Version
        Passed = $passed
        CompletedAt = [DateTimeOffset]::Now.ToString("O")
        Screenshots = @($script:Screenshots)
        Checks = @($script:Checks)
        Coverage = $script:CoverageSummary
        Performance = if ($null -ne $performance) {
            [pscustomobject]@{
                StageSummary = $performance.StageSummary
                SessionSummary = @($performance.Sessions | ForEach-Object {
                    [pscustomobject]@{ Name = $_.Name; Summary = $_.Summary }
                })
                Budgets = $performance.Budgets
                Coverage = $performance.Coverage
            }
        } else { $null }
        Error = $runError
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (
        Join-Path $resultsRoot "results.json") -Encoding utf8
    Write-SandboxLog "Suite complete. Passed=$passed Screenshots=$($script:Screenshots.Count)."

    # The host launcher observes results.json and closes Windows Sandbox using
    # its normal window-close path. Do not force an in-guest shutdown here: it
    # is surfaced by the Sandbox client as a lost remote connection.
}
