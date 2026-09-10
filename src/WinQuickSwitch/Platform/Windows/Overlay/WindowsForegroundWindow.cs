using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows.Overlay;

/// <summary>
/// Captures and restores the foreground window around the normal topmost WPF
/// overlay. It never shows, minimizes, or otherwise changes the target window.
/// </summary>
internal static class WindowsForegroundWindow
{
    public static IntPtr Capture() => GetForegroundWindow();

    public static bool TryRestore(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle) ||
            !IsWindowVisible(windowHandle))
        {
            return false;
        }

        return SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
