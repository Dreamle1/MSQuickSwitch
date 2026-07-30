using System.ComponentModel;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsWidgetPlacementService
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public (ScreenPoint Pointer, ScreenRectangle WorkArea) GetPointerWorkArea()
    {
        if (!GetCursorPos(out NativePoint pointer))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not read the pointer position.");
        }

        IntPtr monitor = MonitorFromPoint(pointer, MonitorDefaultToNearest);
        return (
            new ScreenPoint(pointer.X, pointer.Y),
            GetMonitorWorkArea(monitor));
    }

    public ScreenRectangle GetWindowWorkArea(IntPtr windowHandle)
    {
        IntPtr monitor = MonitorFromWindow(
            windowHandle,
            MonitorDefaultToNearest);

        return GetMonitorWorkArea(monitor);
    }

    private static ScreenRectangle GetMonitorWorkArea(IntPtr monitor)
    {
        MonitorInfo monitorInfo = new()
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not read the current monitor work area.");
        }

        return new ScreenRectangle(
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Right,
            monitorInfo.WorkArea.Bottom);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
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
        IntPtr windowHandle,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);
}
