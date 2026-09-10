using System.ComponentModel;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Display;

namespace WinQuickSwitch.Platform.Windows.Display;

public sealed class WindowsDisplayTopologyService : IDisplayTopologyService
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryAllPaths = 0x00000001;
    private const uint QueryDatabaseCurrent = 0x00000004;
    private const uint PathActive = 0x00000001;
    private const uint TopologyInternal = 0x00000001;
    private const uint TopologyClone = 0x00000002;
    private const uint TopologyExtend = 0x00000004;
    private const uint TopologyExternal = 0x00000008;

    public DisplayTopologySnapshot GetSnapshot()
    {
        try
        {
            IReadOnlyCollection<DisplayPathDescriptor> paths = ReadDisplayPaths();
            DisplayMode? reportedMode = TryReadWindowsProjectionMode();
            return DisplayTopologyClassifier.Classify(paths, reportedMode);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            InvalidOperationException or
            OverflowException)
        {
            return DisplayTopologyClassifier.Unavailable(
                $"Current display mode is unavailable: {exception.Message}");
        }
    }

    internal static DisplayMode? MapTopologyId(uint topologyId) => topologyId switch
    {
        TopologyInternal => DisplayMode.PcScreenOnly,
        TopologyClone => DisplayMode.Duplicate,
        TopologyExtend => DisplayMode.Extend,
        TopologyExternal => DisplayMode.SecondScreenOnly,
        _ => null,
    };

    private static DisplayMode? TryReadWindowsProjectionMode()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(
                QueryDatabaseCurrent,
                out uint pathCount,
                out uint modeCount);

            if (result != ErrorSuccess)
            {
                return null;
            }

            DisplayConfigPathInfo[] paths = new DisplayConfigPathInfo[pathCount];
            DisplayConfigModeInfo[] modes = new DisplayConfigModeInfo[modeCount];

            result = QueryDisplayConfigWithTopology(
                QueryDatabaseCurrent,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                out uint topologyId);

            if (result == ErrorSuccess)
            {
                return MapTopologyId(topologyId);
            }

            if (result != ErrorInsufficientBuffer)
            {
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyCollection<DisplayPathDescriptor> ReadDisplayPaths()
    {
        int result = GetDisplayConfigBufferSizes(
            QueryAllPaths,
            out uint pathCount,
            out uint modeCount);

        ThrowIfFailed(result, "Windows could not size the display configuration.");

        DisplayConfigPathInfo[] paths = new DisplayConfigPathInfo[pathCount];
        DisplayConfigModeInfo[] modes = new DisplayConfigModeInfo[modeCount];

        result = QueryDisplayConfig(
            QueryAllPaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);

        ThrowIfFailed(result, "Windows could not read the display configuration.");

        return paths
            .Take(checked((int)pathCount))
            .Select(path => new DisplayPathDescriptor(
                path.SourceInfo.AdapterId.ToInt64(),
                path.SourceInfo.Id,
                path.TargetInfo.Id,
                path.TargetInfo.OutputTechnology,
                (path.Flags & PathActive) != 0,
                path.TargetInfo.TargetAvailable))
            .ToArray();
    }

    private static void ThrowIfFailed(int result, string message)
    {
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, message);
        }
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
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    private static extern int QueryDisplayConfigWithTopology(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        out uint currentTopologyId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public DisplayOutputTechnology OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly long ToInt64() =>
            ((long)HighPart << 32) | LowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(0)]
        public DisplayConfigSourceMode SourceMode;

        [FieldOffset(0)]
        public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HorizontalSyncFrequency;
        public DisplayConfigRational VerticalSyncFrequency;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfig2DRegion
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
