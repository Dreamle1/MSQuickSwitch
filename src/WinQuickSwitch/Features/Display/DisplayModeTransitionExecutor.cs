namespace WinQuickSwitch.Features.Display;

internal sealed record DisplayModeTransitionOutcome(
    bool Succeeded,
    DisplayTopologySnapshot Snapshot,
    string Message,
    int ApplyCount);

internal static class DisplayModeTransitionExecutor
{
    public static async Task<DisplayModeTransitionOutcome> ExecuteAsync(
        IDisplayModeService displayModeService,
        IDisplayTopologyService topologyService,
        DisplayMode? currentMode,
        DisplayMode requestedMode,
        TimeSpan pollInterval,
        int bridgeMaximumAttempts,
        int finalMaximumAttempts,
        int requiredStableSnapshots,
        int recoveryApplyCount,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(displayModeService);
        ArgumentNullException.ThrowIfNull(topologyService);
        ArgumentOutOfRangeException.ThrowIfLessThan(bridgeMaximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(finalMaximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredStableSnapshots, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(recoveryApplyCount);

        int applyCount = 0;
        DisplayModeResult? lastApplyResult = null;

        foreach (DisplayMode transitionMode in DisplayModeTransitionPlan.Create(
                     currentMode,
                     requestedMode,
                     displayModeService.CanRestoreExtendedLayout))
        {
            lastApplyResult = await displayModeService.ApplyAsync(
                transitionMode,
                cancellationToken);
            applyCount++;

            if (!lastApplyResult.Succeeded)
            {
                return Failed(
                    topologyService.GetSnapshot(),
                    lastApplyResult.Message,
                    applyCount);
            }

            if (transitionMode == requestedMode)
            {
                continue;
            }

            DisplayTopologySnapshot bridgeSnapshot =
                await DisplayTransitionMonitor.WaitForModeAsync(
                    topologyService,
                    transitionMode,
                    pollInterval,
                    bridgeMaximumAttempts,
                    cancellationToken,
                    delay);

            if (!DisplayTransitionMonitor.IsModeSatisfied(
                    bridgeSnapshot,
                    transitionMode))
            {
                return Failed(
                    bridgeSnapshot,
                    $"Windows accepted the preparatory " +
                    $"{transitionMode.GetDisplayName()} request, but that " +
                    "topology did not become active. The requested display " +
                    "change was not applied.",
                    applyCount);
            }
        }

        DisplayTopologySnapshot snapshot = await WaitForRequestedModeAsync(
            topologyService,
            requestedMode,
            pollInterval,
            finalMaximumAttempts,
            requiredStableSnapshots,
            cancellationToken,
            delay);

        if (DisplayTransitionMonitor.IsModeSatisfied(snapshot, requestedMode))
        {
            return Succeeded(requestedMode, snapshot, applyCount);
        }

        for (int recoveryAttempt = 0;
             recoveryAttempt < recoveryApplyCount;
             recoveryAttempt++)
        {
            lastApplyResult = await displayModeService.ApplyAsync(
                requestedMode,
                cancellationToken);
            applyCount++;

            if (!lastApplyResult.Succeeded)
            {
                return Failed(
                    topologyService.GetSnapshot(),
                    lastApplyResult.Message,
                    applyCount);
            }

            snapshot = await WaitForRequestedModeAsync(
                topologyService,
                requestedMode,
                pollInterval,
                finalMaximumAttempts,
                requiredStableSnapshots,
                cancellationToken,
                delay);

            if (DisplayTransitionMonitor.IsModeSatisfied(snapshot, requestedMode))
            {
                return Succeeded(requestedMode, snapshot, applyCount);
            }
        }

        return Failed(
            snapshot,
            BuildUnsettledMessage(requestedMode, snapshot),
            applyCount);
    }

    private static Task<DisplayTopologySnapshot> WaitForRequestedModeAsync(
        IDisplayTopologyService topologyService,
        DisplayMode requestedMode,
        TimeSpan pollInterval,
        int maximumAttempts,
        int requiredStableSnapshots,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay) =>
        DisplayTransitionMonitor.WaitForModeAsync(
            topologyService,
            requestedMode,
            pollInterval,
            maximumAttempts,
            cancellationToken,
            delay,
            requiredStableSnapshots);

    private static DisplayModeTransitionOutcome Succeeded(
        DisplayMode requestedMode,
        DisplayTopologySnapshot snapshot,
        int applyCount) =>
        new(
            true,
            snapshot,
            $"{requestedMode.GetDisplayName()} applied.",
            applyCount);

    private static DisplayModeTransitionOutcome Failed(
        DisplayTopologySnapshot snapshot,
        string message,
        int applyCount) =>
        new(false, snapshot, message, applyCount);

    private static string BuildUnsettledMessage(
        DisplayMode requestedMode,
        DisplayTopologySnapshot snapshot)
    {
        if (requestedMode == DisplayMode.Extend &&
            snapshot.AvailableDisplayCount > snapshot.ActiveDisplayCount)
        {
            return $"Windows accepted Extend, but activated " +
                   $"{snapshot.ActiveDisplayCount} of " +
                   $"{snapshot.AvailableDisplayCount} displays. A full-screen " +
                   "app or graphics driver may be restoring another topology. " +
                   "Switch the game to borderless or windowed mode, or close " +
                   "it, then retry.";
        }

        string actualMode = snapshot.CurrentMode?.GetDisplayName() ??
            "an unknown display mode";

        return $"Windows accepted {requestedMode.GetDisplayName()}, but the " +
               $"active topology returned to {actualMode}. A full-screen app " +
               "or graphics driver may be overriding the change. Switch the " +
               "game to borderless or windowed mode, or close it, then retry.";
    }
}
