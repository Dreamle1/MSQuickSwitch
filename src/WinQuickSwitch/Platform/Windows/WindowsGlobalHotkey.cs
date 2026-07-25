using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsGlobalHotkey : IDisposable
{
    public const int ToggleWidgetId = 0x5157;
    public const int WmHotkey = 0x0312;
    private const uint ModWin = 0x0008;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyQ = 0x51;

    private IntPtr _windowHandle;

    public bool IsRegistered => _windowHandle != IntPtr.Zero;

    public void Register(IntPtr windowHandle)
    {
        if (IsRegistered)
        {
            return;
        }

        if (!RegisterHotKey(
            windowHandle,
            ToggleWidgetId,
            ModWin | ModShift | ModNoRepeat,
            VirtualKeyQ))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Win + Shift + Q is already in use.");
        }

        _windowHandle = windowHandle;
    }

    public void Dispose()
    {
        if (!IsRegistered)
        {
            return;
        }

        UnregisterHotKey(_windowHandle, ToggleWidgetId);
        _windowHandle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
