namespace WinQuickSwitch.Platform.Windows.Display;

internal interface IDisplaySwitchProcess
{
    Task<int> RunAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken);
}
