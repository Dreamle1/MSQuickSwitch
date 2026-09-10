using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsGlobalHotkey : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const int FirstHotkeyId = 0x5157;
    private const int FirstCommandHotkeyId = 0x4000;
    private const int LastCommandHotkeyId = 0x4FFF;
    private const int FirstProfileHotkeyId = 0x6000;
    private const int LastProfileHotkeyId = 0xBFFF;
    private const uint ModNoRepeat = 0x4000;

    private readonly IGlobalHotkeyNative _native;
    private readonly Dictionary<int, WidgetHotkeyAction> _actionsById = [];
    private readonly Dictionary<int, string> _profileIdsById = [];
    private readonly Dictionary<int, string> _commandKeysById = [];
    private readonly Dictionary<int, RegisteredHotkey> _registrationsById = [];
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
        ApplyBindings(windowHandle, settings, [], []);

    public HotkeyRegistrationResult ApplyBindings(
        IntPtr windowHandle,
        WidgetSettings settings,
        IReadOnlyList<ProfileHotkeyBinding> profileBindings)
        => ApplyBindings(windowHandle, settings, profileBindings, []);

    public HotkeyRegistrationResult ApplyBindings(
        IntPtr windowHandle,
        WidgetSettings settings,
        IReadOnlyList<ProfileHotkeyBinding> profileBindings,
        IReadOnlyList<CommandHotkeyBinding> commandBindings)
    {
        RegisteredHotkey[] previousRegistrations =
            _registrationsById.Values.ToArray();
        UnregisterAll();
        _windowHandle = windowHandle;
        Dictionary<WidgetHotkeyAction, string> failures = [];
        Dictionary<string, string> profileFailures = [];
        Dictionary<string, string> commandFailures = [];
        Dictionary<WidgetShortcut, string> shortcutOwners = [];

        foreach (WidgetHotkeyAction action in Enum.GetValues<WidgetHotkeyAction>())
        {
            WidgetShortcut? shortcut = settings.GetShortcut(action);

            if (shortcut is null)
            {
                continue;
            }

            shortcutOwners[shortcut] = GetActionLabel(action);

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
                _registrationsById[id] = new RegisteredHotkey(
                    id,
                    shortcut,
                    action,
                    null,
                    null);
            }
            else
            {
                failures[action] =
                    $"Could not register {shortcut.DisplayText} (Windows error {errorCode}).";
            }
        }

        HashSet<int> assignedProfileIds = [];
        foreach (ProfileHotkeyBinding binding in profileBindings
                     .OrderBy(binding => binding.ProfileId, StringComparer.Ordinal))
        {
            if (!binding.Shortcut.IsValid)
            {
                continue;
            }

            string profileName = string.IsNullOrWhiteSpace(binding.DisplayName)
                ? binding.ProfileId
                : binding.DisplayName;
            if (shortcutOwners.TryGetValue(
                    binding.Shortcut,
                    out string? existingOwner))
            {
                profileFailures[binding.ProfileId] =
                    $"{binding.Shortcut.DisplayText} is already assigned to " +
                    $"{existingOwner}. {profileName} needs a different shortcut.";
                continue;
            }

            shortcutOwners[binding.Shortcut] = $"profile {profileName}";

            int id = GetProfileId(binding.ProfileId, assignedProfileIds);
            uint modifiers = (uint)binding.Shortcut.Modifiers | ModNoRepeat;

            if (_native.Register(
                windowHandle,
                id,
                modifiers,
                (uint)binding.Shortcut.VirtualKey,
                out int errorCode))
            {
                _profileIdsById[id] = binding.ProfileId;
                _registrationsById[id] = new RegisteredHotkey(
                    id,
                    binding.Shortcut,
                    null,
                    binding.ProfileId,
                    null);
            }
            else
            {
                profileFailures[binding.ProfileId] =
                    $"Could not register {binding.Shortcut.DisplayText} (Windows error {errorCode}).";
            }
        }

        HashSet<int> assignedCommandIds = [];
        HashSet<string> registeredCommandKeys =
            new(StringComparer.Ordinal);
        foreach (CommandHotkeyBinding binding in commandBindings
                     .OrderBy(binding => binding.CommandKey,
                              StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(binding.CommandKey))
            {
                commandFailures[string.Empty] =
                    "A command hotkey needs a stable command key.";
                continue;
            }

            if (!registeredCommandKeys.Add(binding.CommandKey))
            {
                commandFailures[binding.CommandKey] =
                    "The command key is registered more than once.";
                continue;
            }

            if (!binding.Shortcut.IsValid)
            {
                commandFailures[binding.CommandKey] =
                    $"{binding.CommandKey} has an invalid shortcut.";
                continue;
            }

            string commandName = string.IsNullOrWhiteSpace(binding.Label)
                ? binding.CommandKey
                : binding.Label;
            if (shortcutOwners.TryGetValue(
                    binding.Shortcut,
                    out string? existingOwner))
            {
                commandFailures[binding.CommandKey] =
                    $"{binding.Shortcut.DisplayText} is already assigned to " +
                    $"{existingOwner}. {commandName} needs a different shortcut.";
                continue;
            }

            shortcutOwners[binding.Shortcut] = $"command {commandName}";

            int id = GetCommandId(
                binding.CommandKey,
                assignedCommandIds);
            uint modifiers = (uint)binding.Shortcut.Modifiers | ModNoRepeat;
            if (_native.Register(
                    windowHandle,
                    id,
                    modifiers,
                    (uint)binding.Shortcut.VirtualKey,
                    out int errorCode))
            {
                _commandKeysById[id] = binding.CommandKey;
                _registrationsById[id] = new RegisteredHotkey(
                    id,
                    binding.Shortcut,
                    null,
                    null,
                    binding.CommandKey);
            }
            else
            {
                commandFailures[binding.CommandKey] =
                    $"Could not register {binding.Shortcut.DisplayText} " +
                    $"(Windows error {errorCode}).";
            }
        }

        bool retainedPreviousBindings = false;
        string? recoveryFailure = null;

        if ((failures.Count > 0 ||
             profileFailures.Count > 0 ||
             commandFailures.Count > 0) &&
            previousRegistrations.Length > 0)
        {
            UnregisterAll();
            retainedPreviousBindings = Restore(previousRegistrations);
            if (!retainedPreviousBindings)
            {
                recoveryFailure =
                    "The previous shortcut set could not be fully restored.";
            }
        }

        return new HotkeyRegistrationResult(
            failures,
            profileFailures,
            retainedPreviousBindings,
            recoveryFailure,
            commandFailures);
    }

    public bool TryResolveAction(int id, out WidgetHotkeyAction action) =>
        _actionsById.TryGetValue(id, out action);

    public bool TryResolveProfileId(int id, out string profileId) =>
        _profileIdsById.TryGetValue(id, out profileId!);

    public bool TryResolveCommandKey(int id, out string commandKey) =>
        _commandKeysById.TryGetValue(id, out commandKey!);

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
            _commandKeysById.Clear();
            _registrationsById.Clear();
            return;
        }

        foreach (int id in _registrationsById.Keys)
        {
            _native.Unregister(_windowHandle, id);
        }

        _actionsById.Clear();
        _profileIdsById.Clear();
        _commandKeysById.Clear();
        _registrationsById.Clear();
    }

    private bool Restore(IEnumerable<RegisteredHotkey> registrations)
    {
        bool restoredAll = true;

        foreach (RegisteredHotkey registration in registrations)
        {
            uint modifiers =
                (uint)registration.Shortcut.Modifiers | ModNoRepeat;
            if (!_native.Register(
                    _windowHandle,
                    registration.Id,
                    modifiers,
                    (uint)registration.Shortcut.VirtualKey,
                    out _))
            {
                restoredAll = false;
                continue;
            }

            _registrationsById[registration.Id] = registration;
            if (registration.Action is WidgetHotkeyAction action)
            {
                _actionsById[registration.Id] = action;
            }
            else if (registration.ProfileId is string profileId)
            {
                _profileIdsById[registration.Id] = profileId;
            }
            else if (registration.CommandKey is string commandKey)
            {
                _commandKeysById[registration.Id] = commandKey;
            }
        }

        return restoredAll;
    }

    // WidgetHotkeyAction values are append-only: the overlay action is added
    // after the existing values so every previously assigned ID stays stable.
    internal static int GetId(WidgetHotkeyAction action) =>
        FirstHotkeyId + (int)action;

    internal static int GetProfileId(string profileId, ISet<int> assignedIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(assignedIds);

        uint hash = 2166136261;
        foreach (char character in profileId)
        {
            hash ^= character;
            hash *= 16777619;
        }

        int range = LastProfileHotkeyId - FirstProfileHotkeyId + 1;
        int id = FirstProfileHotkeyId + (int)(hash % (uint)range);
        for (int attempt = 0; attempt < range; attempt++)
        {
            if (assignedIds.Add(id))
            {
                return id;
            }

            id = id == LastProfileHotkeyId
                ? FirstProfileHotkeyId
                : id + 1;
        }

        throw new InvalidOperationException("No profile hotkey IDs are available.");
    }

    internal static int GetCommandId(string commandKey, ISet<int> assignedIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKey);
        ArgumentNullException.ThrowIfNull(assignedIds);

        uint hash = 2166136261;
        foreach (char character in commandKey)
        {
            hash ^= character;
            hash *= 16777619;
        }

        int range = LastCommandHotkeyId - FirstCommandHotkeyId + 1;
        int id = FirstCommandHotkeyId + (int)(hash % (uint)range);
        for (int attempt = 0; attempt < range; attempt++)
        {
            if (assignedIds.Add(id))
            {
                return id;
            }

            id = id == LastCommandHotkeyId
                ? FirstCommandHotkeyId
                : id + 1;
        }

        throw new InvalidOperationException("No command hotkey IDs are available.");
    }

    private static string GetActionLabel(WidgetHotkeyAction action) =>
        action switch
        {
            WidgetHotkeyAction.ToggleWidget => "Show or hide",
            WidgetHotkeyAction.ToggleOverlay => "Toggle overlay",
            WidgetHotkeyAction.Options => "Open Options",
            WidgetHotkeyAction.ToggleWiFi => "Toggle Wi-Fi",
            WidgetHotkeyAction.ToggleBluetooth => "Toggle Bluetooth",
            WidgetHotkeyAction.ToggleTaskbarAutoHide => "Toggle taskbar auto-hide",
            WidgetHotkeyAction.RefreshDisplay => "Refresh Display",
            WidgetHotkeyAction.RefreshAudio => "Refresh Audio",
            WidgetHotkeyAction.RefreshDevices => "Refresh Devices",
            WidgetHotkeyAction.RefreshAll => "Refresh all",
            WidgetHotkeyAction.PcScreenOnly => "PC screen only",
            WidgetHotkeyAction.SecondScreenOnly => "Second screen only",
            _ => action.ToString(),
        };

    private sealed record RegisteredHotkey(
        int Id,
        WidgetShortcut Shortcut,
        WidgetHotkeyAction? Action,
        string? ProfileId,
        string? CommandKey);
}

internal sealed record CommandHotkeyBinding(
    string CommandKey,
    string Label,
    WidgetShortcut Shortcut);

internal sealed record HotkeyRegistrationResult(
    IReadOnlyDictionary<WidgetHotkeyAction, string> Failures,
    IReadOnlyDictionary<string, string>? ProfileFailures = null,
    bool RetainedPreviousBindings = false,
    string? RecoveryFailure = null,
    IReadOnlyDictionary<string, string>? CommandFailures = null)
{
    public bool HasFailures =>
        Failures.Count > 0 ||
        (ProfileFailures?.Count ?? 0) > 0 ||
        (CommandFailures?.Count ?? 0) > 0 ||
        RecoveryFailure is not null;

    public bool Succeeded =>
        !HasFailures;

    public string? FirstFailure =>
        Failures.Values.FirstOrDefault() ??
        ProfileFailures?.Values.FirstOrDefault() ??
        CommandFailures?.Values.FirstOrDefault() ??
        RecoveryFailure;
}

internal sealed record ProfileHotkeyBinding(
    string ProfileId,
    WidgetShortcut Shortcut,
    string? DisplayName = null);

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
