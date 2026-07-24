using System.ComponentModel;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Display;

namespace WinQuickSwitch.Platform.Windows.Display;

public sealed class WindowsDisplayTopologyService : IDisplayTopologyService
{
    private const int ErrorSuccess = 0;
    private const uint QueryAllPaths = 0x00000001;
    private const uint PathActive = 0x00000001;

    public DisplayTopologySnapshot GetSnapshot()
    {
        try
        {
            return DisplayTopologyClassifier.Classify(ReadDisplayPaths());
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
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
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly long ToInt64() =>
            ((long)HighPart << 32) | LowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(0)]
        public DisplayConfigSourceMode SourceMode;

        [FieldOffset(0)]
        public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
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
    private struct DisplayConfig2DRegion
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
