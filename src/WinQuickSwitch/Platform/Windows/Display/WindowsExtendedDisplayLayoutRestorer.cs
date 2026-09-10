using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Display;
using PathInfo = WinQuickSwitch.Platform.Windows.Display.WindowsDisplayTopologyService.DisplayConfigPathInfo;
using ModeInfo = WinQuickSwitch.Platform.Windows.Display.WindowsDisplayTopologyService.DisplayConfigModeInfo;

namespace WinQuickSwitch.Platform.Windows.Display;

internal interface IExtendedDisplayLayoutRestorer
{
    bool HasCapturedLayout { get; }

    bool TryCaptureCurrent();

    int? ApplyCaptured();
}

internal sealed class WindowsExtendedDisplayLayoutRestorer :
    IExtendedDisplayLayoutRestorer
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryAllPaths = 0x00000001;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private readonly Lock _snapshotLock = new();
    private CapturedDisplayLayout? _snapshot;

    public bool HasCapturedLayout
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot is not null;
            }
        }
    }

    public bool TryCaptureCurrent()
    {
        DisplayTopologySnapshot topology =
            new WindowsDisplayTopologyService().GetSnapshot();

        if (!DisplayTransitionMonitor.IsModeSatisfied(
                topology,
                DisplayMode.Extend))
        {
            return false;
        }

        DisplayConfigReadResult activeConfiguration =
            ReadDisplayConfig(QueryOnlyActivePaths);

        if (activeConfiguration.ErrorCode != ErrorSuccess ||
            activeConfiguration.Paths.Length < 2)
        {
            return false;
        }

        int activeSourceCount = activeConfiguration.Paths
            .Select(path =>
                (path.SourceInfo.AdapterId.ToInt64(), path.SourceInfo.Id))
            .Distinct()
            .Count();

        if (activeSourceCount != activeConfiguration.Paths.Length)
        {
            return false;
        }

        DisplayConfigReadResult allConfiguration =
            ReadDisplayConfig(QueryAllPaths);

        if (allConfiguration.ErrorCode != ErrorSuccess)
        {
            return false;
        }

        int availableTargetCount = allConfiguration.Paths
            .Where(path => path.TargetInfo.TargetAvailable)
            .Select(path =>
                (path.TargetInfo.AdapterId.ToInt64(), path.TargetInfo.Id))
            .Distinct()
            .Count();

        if (availableTargetCount != activeConfiguration.Paths.Length)
        {
            return false;
        }

        CapturedDisplayLayout captured = new(
            activeConfiguration.Paths.ToArray(),
            activeConfiguration.Modes.ToArray());

        lock (_snapshotLock)
        {
            _snapshot = captured;
        }

        return true;
    }

    public int? ApplyCaptured() =>
        UseCaptured(SdcApply | SdcUseSuppliedDisplayConfig);

    internal int? ValidateCaptured() =>
        UseCaptured(SdcValidate | SdcUseSuppliedDisplayConfig);

    private int? UseCaptured(uint flags)
    {
        CapturedDisplayLayout? snapshot;

        lock (_snapshotLock)
        {
            snapshot = _snapshot;
        }

        if (snapshot is null)
        {
            return null;
        }

        return SetDisplayConfig(
            checked((uint)snapshot.Paths.Length),
            snapshot.Paths,
            checked((uint)snapshot.Modes.Length),
            snapshot.Modes,
            flags);
    }

    private static DisplayConfigReadResult ReadDisplayConfig(uint flags)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(
                flags,
                out uint pathCount,
                out uint modeCount);

            if (result != ErrorSuccess)
            {
                return new(result, [], []);
            }

            PathInfo[] paths = new PathInfo[pathCount];
            ModeInfo[] modes = new ModeInfo[modeCount];

            result = QueryDisplayConfig(
                flags,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);

            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }

            return result == ErrorSuccess
                ? new(
                    result,
                    paths.Take(checked((int)pathCount)).ToArray(),
                    modes.Take(checked((int)modeCount)).ToArray())
                : new(result, [], []);
        }

        return new(ErrorInsufficientBuffer, [], []);
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] PathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] ModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint pathCount,
        [In] PathInfo[] paths,
        uint modeCount,
        [In] ModeInfo[] modes,
        uint flags);

    private sealed record CapturedDisplayLayout(
        PathInfo[] Paths,
        ModeInfo[] Modes);

    private sealed record DisplayConfigReadResult(
        int ErrorCode,
        PathInfo[] Paths,
        ModeInfo[] Modes);
}
