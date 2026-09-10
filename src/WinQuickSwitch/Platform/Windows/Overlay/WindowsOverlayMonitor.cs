using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows.Overlay;

/// <summary>
/// Physical-pixel bounds for one Windows monitor.
/// </summary>
public readonly record struct OverlayMonitorBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Identity and usable work area for the monitor nearest the pointer. The
/// device name is used only to restore overlay card placement; it never
/// participates in display-topology mutations.
/// </summary>
public readonly record struct OverlayMonitorInfo(
    OverlayMonitorBounds Bounds,
    OverlayMonitorBounds WorkArea,
    string DeviceName);

/// <summary>
/// Minimal monitor lookup for the overlay. It intentionally exposes only the
/// monitor nearest the pointer and does not enumerate or synthesize topology.
/// </summary>
public static class WindowsOverlayMonitor
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static OverlayMonitorBounds GetPointerMonitorBounds() =>
        GetPointerMonitorInfo().Bounds;

    public static OverlayMonitorInfo GetPointerMonitorInfo()
    {
        if (!GetCursorPos(out NativePoint pointer))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not read the pointer position.");
        }

        IntPtr monitor = MonitorFromPoint(pointer, MonitorDefaultToNearest);
        return GetMonitorInfo(monitor);
    }

    /// <summary>
    /// Resolves the monitor nearest an owner window.  This is a safe fallback
    /// for sessions where Windows denies the pointer query (for example while
    /// a protected or elevated surface owns the cursor).
    /// </summary>
    public static OverlayMonitorInfo GetWindowMonitorInfo(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A window handle is required to resolve its monitor.",
                nameof(windowHandle));
        }

        IntPtr monitor = MonitorFromWindow(
            windowHandle,
            MonitorDefaultToNearest);
        return GetMonitorInfo(monitor);
    }

    /// <summary>
    /// Returns one safe fallback monitor without touching display topology.
    /// The overlay must never use the virtual-screen rectangle because that
    /// would span every connected monitor.
    /// </summary>
    public static OverlayMonitorInfo GetFallbackMonitorInfo()
    {
        IntPtr monitor = MonitorFromPoint(
            new NativePoint { X = 0, Y = 0 },
            MonitorDefaultToNearest);
        return GetMonitorInfo(monitor);
    }

    internal static int MonitorInfoStructureSize =>
        Marshal.SizeOf<MonitorInfo>();

    private static OverlayMonitorInfo GetMonitorInfo(IntPtr monitor)
    {
        MonitorInfo monitorInfo = new()
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };

        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not read the current monitor bounds.");
        }

        return new OverlayMonitorInfo(
            new OverlayMonitorBounds(
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Right,
                monitorInfo.Monitor.Bottom),
            new OverlayMonitorBounds(
                monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Top,
                monitorInfo.WorkArea.Right,
                monitorInfo.WorkArea.Bottom),
            monitorInfo.DeviceName.TrimEnd('\0'));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr window,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);
}
