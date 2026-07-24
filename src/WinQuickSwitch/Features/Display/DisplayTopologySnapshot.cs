namespace WinQuickSwitch.Features.Display;

public sealed record DisplayTopologySnapshot(
    DisplayMode? CurrentMode,
    int ActiveDisplayCount,
    int AvailableDisplayCount,
    bool IsReliable,
    string Status)
{
    public bool SupportsMultipleDisplays => IsReliable && AvailableDisplayCount > 1;
}
