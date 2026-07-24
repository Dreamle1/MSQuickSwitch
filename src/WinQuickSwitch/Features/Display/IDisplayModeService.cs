namespace WinQuickSwitch.Features.Display;

public interface IDisplayModeService
{
    Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default);
}
