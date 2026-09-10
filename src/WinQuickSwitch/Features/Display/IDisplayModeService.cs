namespace WinQuickSwitch.Features.Display;

public interface IDisplayModeService
{
    bool CanRestoreExtendedLayout { get; }

    Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default);
}
