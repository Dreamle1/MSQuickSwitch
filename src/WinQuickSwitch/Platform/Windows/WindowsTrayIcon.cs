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
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackMenuReturnCommand = 0x0100;
    private const uint TrackMenuRightButton = 0x0002;
    private const int MouseLeftButtonUp = 0x0202;
    private const int MouseLeftButtonDoubleClick = 0x0203;
    private const int MouseRightButtonUp = 0x0205;
    private const int ContextMenu = 0x007B;
    private const int NotificationSelect = 0x0400;
    private const int NotificationKeySelect = 0x0401;
    private const int NullMessage = 0x0000;
    private const uint ApplicationIcon = 32512;

    private readonly IntPtr _windowHandle;
    private readonly uint _iconId;
    private NOTIFYICONDATA _iconData;
    private IReadOnlyList<string> _favoriteLabels = [];
    private bool _isRegistered;
    private bool _isDisposed;

    public WindowsTrayIcon(
        IntPtr windowHandle,
        uint iconId = 1,
        string? tooltip = null)
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
            szTip = string.IsNullOrWhiteSpace(tooltip)
                ? "WinQuickSwitch"
                : tooltip.Trim(),
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

    public event EventHandler<TrayFavoriteRequestedEventArgs>? FavoriteRequested;

    public void SetFavoriteItems(IReadOnlyList<string> labels)
    {
        _favoriteLabels = labels.ToArray();
    }

    public bool HandleWindowMessage(
        int message,
        IntPtr wordParameter,
        IntPtr longParameter)
    {
        if (_isDisposed || message != CallbackMessage)
        {
            return false;
        }

        // NOTIFYICON_VERSION_4 packs the notification code into the low word
        // of lParam and the icon ID into the high word. Comparing the complete
        // value prevents every click from matching once version 4 is enabled.
        int notification = DecodeNotificationCode(longParameter);

        switch (notification)
        {
            case MouseLeftButtonUp:
            case MouseLeftButtonDoubleClick:
            case NotificationSelect:
            case NotificationKeySelect:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MouseRightButtonUp:
            case ContextMenu:
                ShowContextMenu(GetNotificationPoint(wordParameter));
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

    private void ShowContextMenu(POINT notificationPoint)
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
                "Open main window");

            if (_favoriteLabels.Count > 0)
            {
                AppendMenu(
                    menu,
                    MenuSeparator,
                    UIntPtr.Zero,
                    string.Empty);

                for (int index = 0; index < _favoriteLabels.Count; index++)
                {
                    AppendMenu(
                        menu,
                        MenuString,
                        (UIntPtr)((uint)TrayMenuCommand.FavoriteBase +
                            (uint)index),
                        _favoriteLabels[index]);
                }
            }

            AppendMenu(
                menu,
                MenuSeparator,
                UIntPtr.Zero,
                string.Empty);
            AppendMenu(
                menu,
                MenuString,
                (UIntPtr)TrayMenuCommand.Quit,
                "Exit WinQuickSwitch");

            SetForegroundWindow(_windowHandle);
            if (!GetCursorPos(out POINT cursorPosition))
            {
                cursorPosition = notificationPoint;
                if (cursorPosition.X == 0 && cursorPosition.Y == 0)
                {
                    cursorPosition = new POINT { X = 8, Y = 8 };
                }
            }
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
                default:
                    if (command >= (uint)TrayMenuCommand.FavoriteBase &&
                        command < (uint)TrayMenuCommand.FavoriteBase +
                            (uint)_favoriteLabels.Count)
                    {
                        FavoriteRequested?.Invoke(
                            this,
                            new TrayFavoriteRequestedEventArgs(
                                (int)(command -
                                    (uint)TrayMenuCommand.FavoriteBase)));
                    }

                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
            PostMessage(_windowHandle, NullMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal static int DecodeNotificationCode(IntPtr longParameter) =>
        unchecked((ushort)longParameter.ToInt64());

    private static POINT GetNotificationPoint(IntPtr wordParameter)
    {
        long packed = wordParameter.ToInt64();
        return new POINT
        {
            X = unchecked((short)(packed & 0xFFFF)),
            Y = unchecked((short)((packed >> 16) & 0xFFFF)),
        };
    }

    private enum TrayMenuCommand : uint
    {
        Open = 1,
        Quit = 2,
        FavoriteBase = 100,
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

internal sealed class TrayFavoriteRequestedEventArgs(int index) : EventArgs
{
    public int Index { get; } = index;
}
