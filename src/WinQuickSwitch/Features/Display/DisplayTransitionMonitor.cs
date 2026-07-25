namespace WinQuickSwitch.Features.Display;

internal static class DisplayTransitionMonitor
{
    public static async Task<DisplayTopologySnapshot> WaitForModeAsync(
        IDisplayTopologyService topologyService,
        DisplayMode expectedMode,
        TimeSpan interval,
        int maximumAttempts,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(topologyService);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        delay ??= Task.Delay;
        DisplayTopologySnapshot snapshot = topologyService.GetSnapshot();

        for (int attempt = 1;
             snapshot.CurrentMode != expectedMode && attempt < maximumAttempts;
             attempt++)
        {
            await delay(interval, cancellationToken);
            snapshot = topologyService.GetSnapshot();
        }

        return snapshot;
    }
}
