using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsTrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x8001;

    private const uint NotifyIconMessage = 0x0001;
    private const uint NotifyIconIcon = 0x0002;
    private const uint NotifyIconTip = 0x0004;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint MenuString = 0x00000000;
    private const uint TrackMenuReturnCommand = 0x0100;
    private const uint TrackMenuRightButton = 0x0002;
    private const int MouseLeftButtonUp = 0x0202;
    private const int MouseLeftButtonDoubleClick = 0x0203;
    private const int MouseRightButtonUp = 0x0205;
    private const int NullMessage = 0x0000;
    private const uint ApplicationIcon = 32512;

    private readonly IntPtr _windowHandle;
    private readonly uint _iconId;
    private NOTIFYICONDATA _iconData;
    private bool _isRegistered;
    private bool _isDisposed;

    public WindowsTrayIcon(IntPtr windowHandle, uint iconId = 1)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A window handle is required for a tray icon.",
                nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _iconId = iconId;
        _iconData = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = windowHandle,
            uID = iconId,
            uFlags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
            uCallbackMessage = CallbackMessage,
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)ApplicationIcon),
            szTip = "WinQuickSwitch",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        if (!Shell_NotifyIcon(NotifyIconAdd, ref _iconData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _isRegistered = true;
        _iconData.uTimeoutOrVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NotifyIconSetVersion, ref _iconData);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? QuitRequested;

    public bool HandleWindowMessage(int message, IntPtr parameter)
    {
        if (_isDisposed || message != CallbackMessage)
        {
            return false;
        }

        switch (parameter.ToInt32())
        {
            case MouseLeftButtonUp:
            case MouseLeftButtonDoubleClick:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MouseRightButtonUp:
                ShowContextMenu();
                break;
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_isRegistered)
        {
            Shell_NotifyIcon(NotifyIconDelete, ref _iconData);
            _isRegistered = false;
        }

        GC.SuppressFinalize(this);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(
                menu,
                MenuString,
                (UIntPtr)TrayMenuCommand.Open,
                "Open WinQuickSwitch");
            AppendMenu(
                menu,
                MenuString,
                (UIntPtr)TrayMenuCommand.Quit,
                "Exit WinQuickSwitch");

            SetForegroundWindow(_windowHandle);
            GetCursorPos(out POINT cursorPosition);
            uint command = TrackPopupMenu(
                menu,
                TrackMenuReturnCommand | TrackMenuRightButton,
                cursorPosition.X,
                cursorPosition.Y,
                0,
                _windowHandle,
                IntPtr.Zero);

            switch ((TrayMenuCommand)command)
            {
                case TrayMenuCommand.Open:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case TrayMenuCommand.Quit:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
            PostMessage(_windowHandle, NullMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private enum TrayMenuCommand : uint
    {
        Open = 1,
        Quit = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        // This field is the uTimeout/uVersion union in NOTIFYICONDATA.
        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(
        uint message,
        ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(
        IntPtr instance,
        IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(
        IntPtr menu,
        uint flags,
        UIntPtr newItem,
        string newItemText);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr windowHandle,
        IntPtr reservedRectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
