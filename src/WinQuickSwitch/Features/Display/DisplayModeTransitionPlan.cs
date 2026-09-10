namespace WinQuickSwitch.Features.Display;

internal static class DisplayModeTransitionPlan
{
    public static IReadOnlyList<DisplayMode> Create(
        DisplayMode? currentMode,
        DisplayMode requestedMode,
        bool canRestoreExtendedLayout) =>
        requestedMode == DisplayMode.Extend &&
        !canRestoreExtendedLayout &&
        currentMode is DisplayMode.Duplicate or DisplayMode.SecondScreenOnly
            ? [DisplayMode.PcScreenOnly, DisplayMode.Extend]
            : [requestedMode];
}
