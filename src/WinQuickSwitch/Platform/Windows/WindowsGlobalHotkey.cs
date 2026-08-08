using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsGlobalHotkey : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const int FirstHotkeyId = 0x5157;
    private const int FirstProfileHotkeyId = 0x5257;
    private const uint ModNoRepeat = 0x4000;

    private readonly IGlobalHotkeyNative _native;
    private readonly Dictionary<int, WidgetHotkeyAction> _actionsById = [];
    private readonly Dictionary<int, string> _profileIdsById = [];
    private IntPtr _windowHandle;

    public WindowsGlobalHotkey() : this(NativeGlobalHotkey.Instance)
    {
    }

    internal WindowsGlobalHotkey(IGlobalHotkeyNative native)
    {
        _native = native;
    }

    public HotkeyRegistrationResult ApplyBindings(
        IntPtr windowHandle,
        WidgetSettings settings) =>
        ApplyBindings(windowHandle, settings, []);

    public HotkeyRegistrationResult ApplyBindings(
        IntPtr windowHandle,
        WidgetSettings settings,
        IReadOnlyList<ProfileHotkeyBinding> profileBindings)
    {
        UnregisterAll();
        _windowHandle = windowHandle;
        Dictionary<WidgetHotkeyAction, string> failures = [];
        Dictionary<string, string> profileFailures = [];

        foreach (WidgetHotkeyAction action in Enum.GetValues<WidgetHotkeyAction>())
        {
            WidgetShortcut? shortcut = settings.GetShortcut(action);

            if (shortcut is null)
            {
                continue;
            }

            int id = GetId(action);
            uint modifiers = (uint)shortcut.Modifiers | ModNoRepeat;

            if (_native.Register(
                windowHandle,
                id,
                modifiers,
                (uint)shortcut.VirtualKey,
                out int errorCode))
            {
                _actionsById[id] = action;
            }
            else
            {
                failures[action] =
                    $"Could not register {shortcut.DisplayText} (Windows error {errorCode}).";
            }
        }

        int profileIndex = 0;

        foreach (ProfileHotkeyBinding binding in profileBindings)
        {
            if (!binding.Shortcut.IsValid)
            {
                continue;
            }

            int id = FirstProfileHotkeyId + profileIndex++;
            uint modifiers = (uint)binding.Shortcut.Modifiers | ModNoRepeat;

            if (_native.Register(
                windowHandle,
                id,
                modifiers,
                (uint)binding.Shortcut.VirtualKey,
                out int errorCode))
            {
                _profileIdsById[id] = binding.ProfileId;
            }
            else
            {
                profileFailures[binding.ProfileId] =
                    $"Could not register {binding.Shortcut.DisplayText} (Windows error {errorCode}).";
            }
        }

        return new HotkeyRegistrationResult(failures, profileFailures);
    }

    public bool TryResolveAction(int id, out WidgetHotkeyAction action) =>
        _actionsById.TryGetValue(id, out action);

    public bool TryResolveProfileId(int id, out string profileId) =>
        _profileIdsById.TryGetValue(id, out profileId!);

    public void Dispose()
    {
        UnregisterAll();
        _windowHandle = IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _actionsById.Clear();
            _profileIdsById.Clear();
            return;
        }

        foreach (int id in _actionsById.Keys)
        {
            _native.Unregister(_windowHandle, id);
        }

        _actionsById.Clear();
        _profileIdsById.Clear();
    }

    internal static int GetId(WidgetHotkeyAction action) =>
        FirstHotkeyId + (int)action;
}

internal sealed record HotkeyRegistrationResult(
    IReadOnlyDictionary<WidgetHotkeyAction, string> Failures,
    IReadOnlyDictionary<string, string>? ProfileFailures = null)
{
    public bool Succeeded =>
        Failures.Count == 0 &&
        (ProfileFailures?.Count ?? 0) == 0;

    public string? FirstFailure =>
        Failures.Values.FirstOrDefault() ??
        ProfileFailures?.Values.FirstOrDefault();
}

internal sealed record ProfileHotkeyBinding(
    string ProfileId,
    WidgetShortcut Shortcut);

internal interface IGlobalHotkeyNative
{
    bool Register(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey,
        out int errorCode);

    void Unregister(IntPtr windowHandle, int id);
}

internal sealed class NativeGlobalHotkey : IGlobalHotkeyNative
{
    public static NativeGlobalHotkey Instance { get; } = new();

    private NativeGlobalHotkey()
    {
    }

    public bool Register(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey,
        out int errorCode)
    {
        bool registered = RegisterHotKey(
            windowHandle,
            id,
            modifiers,
            virtualKey);
        errorCode = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    public void Unregister(IntPtr windowHandle, int id)
    {
        UnregisterHotKey(windowHandle, id);
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
