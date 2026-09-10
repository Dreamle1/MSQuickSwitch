namespace WinQuickSwitch.Features.Display;

internal static class DisplayTransitionMonitor
{
    internal static bool IsModeSatisfied(
        DisplayTopologySnapshot snapshot,
        DisplayMode expectedMode) =>
        snapshot.CurrentMode == expectedMode &&
        (expectedMode != DisplayMode.Extend ||
         snapshot.ActiveDisplayCount >= snapshot.AvailableDisplayCount);

    public static async Task<DisplayTopologySnapshot> WaitForModeAsync(
        IDisplayTopologyService topologyService,
        DisplayMode expectedMode,
        TimeSpan interval,
        int maximumAttempts,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int requiredStableSnapshots = 1)
    {
        ArgumentNullException.ThrowIfNull(topologyService);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredStableSnapshots, 1);

        delay ??= Task.Delay;
        DisplayTopologySnapshot snapshot = topologyService.GetSnapshot();
        int stableSnapshotCount = 1;

        for (int attempt = 1;
             !IsModeSatisfied(snapshot, expectedMode) ||
             stableSnapshotCount < requiredStableSnapshots;
             attempt++)
        {
            if (attempt >= maximumAttempts)
            {
                break;
            }

            await delay(interval, cancellationToken);
            DisplayTopologySnapshot nextSnapshot = topologyService.GetSnapshot();
            stableSnapshotCount = snapshot == nextSnapshot
                ? stableSnapshotCount + 1
                : 1;
            snapshot = nextSnapshot;
        }

        return snapshot;
    }
}
