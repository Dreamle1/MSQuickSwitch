using WinQuickSwitch.Features.Display;

namespace WinQuickSwitch.Platform.Windows.Display;

internal static class DisplayTopologyClassifier
{
    public static DisplayTopologySnapshot Classify(
        IReadOnlyCollection<DisplayPathDescriptor> paths)
    {
        DisplayPathDescriptor[] activePaths = paths
            .Where(path => path.IsActive)
            .ToArray();

        int availableDisplayCount = paths
            .Where(path => path.IsAvailable)
            .Select(path => (path.AdapterId, path.TargetId))
            .Distinct()
            .Count();

        if (activePaths.Length == 0)
        {
            return new DisplayTopologySnapshot(
                null,
                0,
                availableDisplayCount,
                false,
                "Windows did not report an active display path.");
        }

        DisplayMode? currentMode = activePaths.Length == 1
            ? ClassifySinglePath(activePaths[0])
            : ClassifyMultiplePaths(activePaths);

        string displayWord = activePaths.Length == 1 ? "display" : "displays";
        string modeName = currentMode?.GetDisplayName() ?? "Custom or mixed topology";

        return new DisplayTopologySnapshot(
            currentMode,
            activePaths.Length,
            Math.Max(availableDisplayCount, activePaths.Length),
            true,
            $"{modeName} · {activePaths.Length} active {displayWord}");
    }

    public static DisplayTopologySnapshot Unavailable(string message) =>
        new(null, 0, 0, false, message);

    private static DisplayMode ClassifySinglePath(DisplayPathDescriptor path) =>
        IsInternal(path.OutputTechnology)
            ? DisplayMode.PcScreenOnly
            : DisplayMode.SecondScreenOnly;

    private static DisplayMode? ClassifyMultiplePaths(
        IReadOnlyCollection<DisplayPathDescriptor> activePaths)
    {
        int sourceCount = activePaths
            .Select(path => (path.AdapterId, path.SourceId))
            .Distinct()
            .Count();

        if (sourceCount == 1)
        {
            return DisplayMode.Duplicate;
        }

        return sourceCount == activePaths.Count
            ? DisplayMode.Extend
            : null;
    }

    private static bool IsInternal(DisplayOutputTechnology technology) =>
        technology is
            DisplayOutputTechnology.Internal or
            DisplayOutputTechnology.Lvds or
            DisplayOutputTechnology.DisplayPortEmbedded or
            DisplayOutputTechnology.UdiEmbedded;
}
