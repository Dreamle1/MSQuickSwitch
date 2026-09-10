using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows;

namespace WinQuickSwitch.Tests;

internal static class CommandHotkeyTests
{
    internal static IReadOnlyList<(string Name, Func<Task> Run)> GetTests() =>
    [
        ("Command hotkeys register and resolve stable keys", RegistersAndResolvesCommand),
        ("Command hotkey IDs remain stable across reorder", CommandIdsRemainStableAcrossReorder),
        ("Command hotkeys report legacy and profile duplicate owners", CommandDuplicatesReportOwners),
        ("Invalid command shortcuts report a command failure", InvalidCommandShortcutsReportFailure),
        ("Failed command reapply restores the previous command", FailedCommandReapplyRestoresPrevious),
        ("Disposing command hotkeys unregisters and clears resolution", DisposingCommandHotkeysClearsResolution),
    ];

    internal static async Task Run()
    {
        foreach ((_, Func<Task> run) in GetTests())
        {
            await run();
        }
    }

    private static Task RegistersAndResolvesCommand()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        CommandHotkeyBinding binding = new(
            "audio.volume-up",
            "Volume up",
            Shortcut(0x55));

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(101),
            SettingsWithoutLegacy(),
            [],
            [binding]);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(1, native.ActiveRegistrations.Count);
        (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey) registration =
            native.ActiveRegistrations.Values.Single();
        int id = registration.Id;
        uint modifiers = registration.Modifiers;
        True(
            id is >= 0x4000 and <= 0x4FFF,
            "Command IDs must use the reserved command range.");
        True(
            (modifiers & 0x4000) != 0,
            "Command registrations must use MOD_NOREPEAT.");
        True(
            hotkeys.TryResolveCommandKey(id, out string commandKey),
            "The command ID should resolve.");
        Equal(binding.CommandKey, commandKey);
        True(
            !hotkeys.TryResolveAction(id, out _),
            "A command ID must not resolve as a legacy action.");
        return Task.CompletedTask;
    }

    private static Task CommandIdsRemainStableAcrossReorder()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        CommandHotkeyBinding first = new(
            "display.second-screen",
            "Second screen only",
            Shortcut(0x53));
        CommandHotkeyBinding second = new(
            "audio.volume-down",
            "Volume down",
            Shortcut(0x44));

        hotkeys.ApplyBindings(
            new IntPtr(102),
            SettingsWithoutLegacy(),
            [],
            [first, second]);
        Dictionary<string, int> initialIds = GetCommandIds(native);

        hotkeys.ApplyBindings(
            new IntPtr(102),
            SettingsWithoutLegacy(),
            [],
            [second, first]);
        Dictionary<string, int> reorderedIds = GetCommandIds(native);

        Equal(initialIds[first.CommandKey], reorderedIds[first.CommandKey]);
        Equal(initialIds[second.CommandKey], reorderedIds[second.CommandKey]);
        True(
            initialIds[first.CommandKey] != initialIds[second.CommandKey],
            "Distinct command keys must receive distinct IDs.");
        return Task.CompletedTask;
    }

    private static Task CommandDuplicatesReportOwners()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetShortcut duplicate = WidgetSettings.Default.ToggleWidget!;
        ProfileHotkeyBinding profile = new(
            "profile-work",
            duplicate,
            "Workday setup");
        CommandHotkeyBinding command = new(
            "widget.toggle",
            "Toggle widget",
            duplicate);

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(103),
            WidgetSettings.Default,
            [profile],
            [command]);

        True(result.HasFailures, "Duplicate owners should be reported.");
        True(!result.Succeeded, "Duplicate owners should not succeed.");
        True(
            result.ProfileFailures?.ContainsKey(profile.ProfileId) == true,
            "The profile duplicate should be associated with its profile.");
        True(
            result.CommandFailures?.ContainsKey(command.CommandKey) == true,
            "The command duplicate should be associated with its key.");
        Contains(
            "Show or hide",
            result.CommandFailures![command.CommandKey]);
        Equal(1, native.ActiveRegistrations.Count);
        True(
            !hotkeys.TryResolveCommandKey(
                WindowsGlobalHotkey.GetCommandId(
                    command.CommandKey,
                    new HashSet<int>()),
                out _),
            "A rejected command must not be resolvable.");
        using WindowsGlobalHotkey profileOnlyHotkeys = new(new FakeGlobalHotkeyNative());
        HotkeyRegistrationResult profileConflict = profileOnlyHotkeys.ApplyBindings(
            new IntPtr(107), SettingsWithoutLegacy(), [profile], [command]);
        Contains("profile Workday setup", profileConflict.CommandFailures![command.CommandKey]);
        return Task.CompletedTask;
    }

    private static Task FailedCommandReapplyRestoresPrevious()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        const string commandKey = "audio.volume-up";
        CommandHotkeyBinding working = new(
            commandKey,
            "Volume up",
            Shortcut(0x55));

        HotkeyRegistrationResult initial = hotkeys.ApplyBindings(
            new IntPtr(104),
            SettingsWithoutLegacy(),
            [],
            [working]);
        True(initial.Succeeded, initial.FirstFailure ?? "Initial registration failed.");
        int commandId = native.ActiveRegistrations.Keys.Single();

        native.FailingVirtualKey = 0x56;
        CommandHotkeyBinding rejected = working with
        {
            Shortcut = Shortcut(0x56),
        };
        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(104),
            SettingsWithoutLegacy(),
            [],
            [rejected]);

        True(result.HasFailures, "The replacement conflict should be reported.");
        True(
            result.RetainedPreviousBindings,
            "The previous command should be restored after a failed reapply.");
        Equal(
            0x55u,
            native.ActiveRegistrations[commandId].VirtualKey);
        True(
            hotkeys.TryResolveCommandKey(commandId, out string restoredKey),
            "The restored command should remain resolvable.");
        Equal(commandKey, restoredKey);
        return Task.CompletedTask;
    }

    private static Task InvalidCommandShortcutsReportFailure()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        const string commandKey = "audio.volume-up";
        CommandHotkeyBinding invalid = new(
            commandKey,
            "Volume up",
            new WidgetShortcut(WidgetHotkeyModifiers.None, 0x55));

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(106),
            SettingsWithoutLegacy(),
            [],
            [invalid]);

        True(!result.Succeeded, "An invalid command shortcut should fail.");
        True(
            result.CommandFailures?.ContainsKey(commandKey) == true,
            "The invalid shortcut failure should be tied to its command key.");
        Equal(0, native.ActiveRegistrations.Count);
        return Task.CompletedTask;
    }

    private static Task DisposingCommandHotkeysClearsResolution()
    {
        FakeGlobalHotkeyNative native = new();
        WindowsGlobalHotkey hotkeys = new(native);
        CommandHotkeyBinding binding = new(
            "audio.toggle-output-mute",
            "Mute output",
            Shortcut(0x4D));

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(105),
            SettingsWithoutLegacy(),
            [],
            [binding]);
        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        int commandId = native.ActiveRegistrations.Keys.Single();

        hotkeys.Dispose();

        True(
            native.Unregistrations.Any(item => item.Id == commandId),
            "Disposal must unregister command hotkeys.");
        True(
            !native.ActiveRegistrations.ContainsKey(commandId),
            "Disposed command hotkeys must not remain active.");
        True(
            !hotkeys.TryResolveCommandKey(commandId, out _),
            "Disposed command IDs must no longer resolve.");
        return Task.CompletedTask;
    }

    private static Dictionary<string, int> GetCommandIds(
        FakeGlobalHotkeyNative native)
    {
        Dictionary<string, int> result =
            new(StringComparer.Ordinal);
        foreach (KeyValuePair<
                     int,
                     (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)> entry
                 in native.ActiveRegistrations)
        {
            string key = entry.Value.VirtualKey switch
            {
                0x53 => "display.second-screen",
                0x44 => "audio.volume-down",
                _ => throw new InvalidOperationException(
                    $"Unexpected command virtual key 0x{entry.Value.VirtualKey:X}."),
            };
            result[key] = entry.Key;
        }

        return result;
    }

    private static WidgetSettings SettingsWithoutLegacy() =>
        WidgetSettings.Default with { ToggleWidget = null };

    private static WidgetShortcut Shortcut(int virtualKey) =>
        new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            virtualKey);

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"Expected {expected}, received {actual}."
                    : message);
        }
    }

    private static void Contains(string expected, string actual)
    {
        True(
            actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            $"Expected '{actual}' to contain '{expected}'.");
    }

    private sealed class FakeGlobalHotkeyNative : IGlobalHotkeyNative
    {
        public int? FailingVirtualKey { get; set; }

        public List<(IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)>
            Registrations { get; } = [];

        public List<(IntPtr Handle, int Id)> Unregistrations { get; } = [];

        public Dictionary<int, (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)>
            ActiveRegistrations { get; } = [];

        public bool Register(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey,
            out int errorCode)
        {
            if (virtualKey == FailingVirtualKey)
            {
                errorCode = 1409;
                return false;
            }

            (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey) registration =
                (windowHandle, id, modifiers, virtualKey);
            Registrations.Add(registration);
            ActiveRegistrations[id] = registration;
            errorCode = 0;
            return true;
        }

        public void Unregister(IntPtr windowHandle, int id)
        {
            Unregistrations.Add((windowHandle, id));
            ActiveRegistrations.Remove(id);
        }
    }
}
