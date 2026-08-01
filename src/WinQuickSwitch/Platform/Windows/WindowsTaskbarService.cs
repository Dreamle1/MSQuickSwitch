using System.Diagnostics;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Taskbar;

namespace WinQuickSwitch.Platform.Windows;

public sealed class WindowsTaskbarService : ITaskbarService
{
    private const uint AbmSetState = 0x0000000A;
    private const uint AbmGetState = 0x00000004;
    private const uint AbsAutoHide = 0x00000001;

    private readonly ITaskbarBackend _backend;
    private readonly ITaskbarSettingsLauncher _settingsLauncher;

    public WindowsTaskbarService() : this(
        new ShellTaskbarBackend(),
        new ShellTaskbarSettingsLauncher())
    {
    }

    internal WindowsTaskbarService(
        ITaskbarBackend backend,
        ITaskbarSettingsLauncher settingsLauncher)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settingsLauncher);
        _backend = backend;
        _settingsLauncher = settingsLauncher;
    }

    public TaskbarSnapshot GetSnapshot()
    {
        try
        {
            uint state = _backend.GetState();
            return new TaskbarSnapshot(MapState(state));
        }
        catch
        {
            return new TaskbarSnapshot(TaskbarState.Unavailable);
        }
    }

    public TaskbarActionResult SetAutoHide(bool enabled)
    {
        try
        {
            bool changed = _backend.SetState(enabled ? AbsAutoHide : 0);
            return changed
                ? new TaskbarActionResult(
                    true,
                    enabled
                        ? "Taskbar is now set to auto-hide."
                        : "Taskbar is now showing.")
                : new TaskbarActionResult(
                    false,
                    "Windows could not change the taskbar visibility.");
        }
        catch (Exception exception)
        {
            return new TaskbarActionResult(
                false,
                $"Taskbar visibility could not be changed: {exception.Message}");
        }
    }

    public TaskbarActionResult OpenTaskbarSettings() => OpenSettings(
        "ms-settings:taskbar",
        "Taskbar settings");

    public TaskbarActionResult OpenDisplaySettings() => OpenSettings(
        "ms-settings:display",
        "Display settings");

    public TaskbarActionResult OpenNotificationSettings() => OpenSettings(
        "ms-settings:notifications",
        "Notification settings");

    private TaskbarActionResult OpenSettings(string uri, string description)
    {
        try
        {
            _settingsLauncher.Open(uri);
            return new TaskbarActionResult(true, $"{description} opened.");
        }
        catch (Exception exception)
        {
            return new TaskbarActionResult(
                false,
                $"{description} could not be opened: {exception.Message}");
        }
    }

    internal static TaskbarState MapState(uint state) =>
        (state & AbsAutoHide) != 0
            ? TaskbarState.AutoHidden
            : TaskbarState.Visible;

    internal const uint SetStateMessage = AbmSetState;
    internal const uint GetStateMessage = AbmGetState;
    internal const uint AutoHideState = AbsAutoHide;
}

internal interface ITaskbarBackend
{
    uint GetState();

    bool SetState(uint state);
}

internal sealed class ShellTaskbarBackend : ITaskbarBackend
{
    public uint GetState()
    {
        APPBARDATA data = new()
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        };

        return unchecked((uint)SHAppBarMessage(
            WindowsTaskbarService.GetStateMessage,
            ref data));
    }

    public bool SetState(uint state)
    {
        APPBARDATA data = new()
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = GetTaskbarWindow(),
            lParam = (IntPtr)state,
        };

        return SHAppBarMessage(
            WindowsTaskbarService.SetStateMessage,
            ref data) != UIntPtr.Zero;
    }

    private static IntPtr GetTaskbarWindow() =>
        FindWindow("Shell_TrayWnd", null);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern UIntPtr SHAppBarMessage(
        uint message,
        ref APPBARDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(
        string? className,
        string? windowName);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal interface ITaskbarSettingsLauncher
{
    void Open(string uri);
}

internal sealed class ShellTaskbarSettingsLauncher : ITaskbarSettingsLauncher
{
    public void Open(string uri)
    {
        Process.Start(new ProcessStartInfo(uri)
        {
            UseShellExecute = true,
        });
    }
}
