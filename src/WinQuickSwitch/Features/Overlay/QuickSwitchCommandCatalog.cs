using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.SharedUi;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.Overlay;

internal enum QuickSwitchCommandCategory
{
    General,
    Navigation,
    Display,
    Audio,
    Devices,
    Taskbar,
    Favorites,
    Profiles,
}

internal sealed record QuickSwitchCommandDescriptor(
    string Key,
    string Label,
    QuickSwitchCommandCategory Category,
    Func<QuickSwitchCommand> CreateCommand,
    WidgetHotkeyAction? LegacyHotkeyAction = null,
    WidgetShortcut? DefaultShortcut = null,
    ProductFeature? RequiredFeature = null)
{
    public bool IsAvailable(ProductConfiguration configuration) =>
        RequiredFeature is not ProductFeature feature ||
        configuration.HasFeature(feature);

    public OverlayDisposition DefaultDisposition =>
        QuickSwitchCommandMetadata.GetDefaultDisposition(CreateCommand());
}

/// <summary>
/// One edition-aware source of command identity and typed execution for UI,
/// tray, overlay, and global-shortcut surfaces. Legacy hotkey actions remain
/// append-only persistence keys and resolve through this catalog.
/// </summary>
internal static class QuickSwitchCommandCatalog
{
    private static readonly IReadOnlyList<QuickSwitchCommandDescriptor> BuiltIns =
        BuildBuiltIns();

    public static QuickSwitchCommandDescriptor OpenMainWindow { get; } = new(
        "general.open-main-window",
        "Open main window",
        QuickSwitchCommandCategory.General,
        () => new OpenMainWindowCommand());

    public static QuickSwitchCommandDescriptor ExitApplication { get; } = new(
        "general.exit",
        "Exit WinQuickSwitch",
        QuickSwitchCommandCategory.General,
        () => new ExitApplicationCommand());

    public static QuickSwitchCommandDescriptor CreateOverlayNavigation(
        OverlayFeatureId feature)
    {
        ProductFeature? requiredFeature = feature == OverlayFeatureId.Profiles
            ? ProductFeature.Profiles
            : null;
        return new QuickSwitchCommandDescriptor(
            $"overlay.navigation.{feature.ToString().ToLowerInvariant()}",
            feature == OverlayFeatureId.Home ? "Quick widgets" : feature.ToString(),
            QuickSwitchCommandCategory.Navigation,
            () => new NavigateToOverlayFeatureCommand(feature),
            RequiredFeature: requiredFeature);
    }

    public static QuickSwitchCommandDescriptor CreateOverlayRefresh(
        OverlayFeatureId feature)
    {
        ProductFeature? requiredFeature = feature == OverlayFeatureId.Profiles
            ? ProductFeature.Profiles
            : null;
        return new QuickSwitchCommandDescriptor(
            $"overlay.refresh.{feature.ToString().ToLowerInvariant()}",
            $"Refresh {feature}",
            QuickSwitchCommandCategory.General,
            () => new RefreshOverlayFeatureCommand(feature),
            RequiredFeature: requiredFeature);
    }

    public static IReadOnlyList<QuickSwitchCommandDescriptor> GetBuiltIns(
        ProductConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return BuiltIns
            .Where(descriptor => descriptor.IsAvailable(configuration))
            .ToArray();
    }

    public static IReadOnlyList<QuickSwitchCommandDescriptor> GetKeyedCommands() =>
    [
        new("audio.volume-up", "Increase output volume", QuickSwitchCommandCategory.Audio,
            () => new ExecuteAudioQuickActionCommand(AudioQuickAction.VolumeUp)),
        new("audio.volume-down", "Decrease output volume", QuickSwitchCommandCategory.Audio,
            () => new ExecuteAudioQuickActionCommand(AudioQuickAction.VolumeDown)),
        new("audio.toggle-output-mute", "Toggle output mute", QuickSwitchCommandCategory.Audio,
            () => new ExecuteAudioQuickActionCommand(AudioQuickAction.ToggleOutputMute)),
        new("audio.toggle-microphone-mute", "Toggle microphone mute", QuickSwitchCommandCategory.Audio,
            () => new ExecuteAudioQuickActionCommand(AudioQuickAction.ToggleMicrophoneMute)),
    ];

    public static IEnumerable<QuickSwitchCommandDescriptor> GetShortcutCommands(
        ProductConfiguration configuration) =>
        GetBuiltIns(configuration).Concat(GetKeyedCommands().Where(item => item.IsAvailable(configuration)));

    public static bool TryGetLegacyHotkey(
        WidgetHotkeyAction action,
        ProductConfiguration configuration,
        out QuickSwitchCommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        descriptor = BuiltIns.FirstOrDefault(candidate =>
            candidate.LegacyHotkeyAction == action &&
            candidate.IsAvailable(configuration))!;
        return descriptor is not null;
    }

    public static QuickSwitchCommandDescriptor CreateProfile(
        ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new QuickSwitchCommandDescriptor(
            $"profile:{profile.Id}",
            profile.Name,
            QuickSwitchCommandCategory.Profiles,
            () => new ApplyProfileCommand(profile.Id),
            RequiredFeature: ProductFeature.Profiles);
    }

    private static IReadOnlyList<QuickSwitchCommandDescriptor> BuildBuiltIns()
    {
        List<QuickSwitchCommandDescriptor> descriptors =
        [
            BuiltIn(
                "general.toggle-configured-surface",
                "Show or hide WinQuickSwitch",
                QuickSwitchCommandCategory.General,
                WidgetHotkeyAction.ToggleWidget,
                () => new ToggleConfiguredSurfaceCommand()),
            BuiltIn(
                "general.toggle-overlay",
                "Toggle overlay",
                QuickSwitchCommandCategory.General,
                WidgetHotkeyAction.ToggleOverlay,
                () => new OpenOverlayWorkspaceCommand()),
            BuiltIn(
                "navigation.display",
                "Open Display",
                QuickSwitchCommandCategory.Navigation,
                WidgetHotkeyAction.Display,
                () => new OpenMainWindowCommand(OverlayFeatureId.Display)),
            BuiltIn(
                "navigation.audio",
                "Open Audio",
                QuickSwitchCommandCategory.Navigation,
                WidgetHotkeyAction.Audio,
                () => new OpenMainWindowCommand(OverlayFeatureId.Audio)),
            BuiltIn(
                "navigation.devices",
                "Open Devices",
                QuickSwitchCommandCategory.Navigation,
                WidgetHotkeyAction.Devices,
                () => new OpenMainWindowCommand(OverlayFeatureId.Devices)),
            BuiltIn(
                "navigation.options",
                "Open Options",
                QuickSwitchCommandCategory.Navigation,
                WidgetHotkeyAction.Options,
                () => new OpenMainWindowCommand(OverlayFeatureId.Options)),
            BuiltIn(
                "display.pc-screen-only",
                "PC screen only",
                QuickSwitchCommandCategory.Display,
                WidgetHotkeyAction.PcScreenOnly,
                () => new ApplyDisplayModeCommand(DisplayMode.PcScreenOnly)),
            BuiltIn(
                "display.duplicate",
                "Duplicate",
                QuickSwitchCommandCategory.Display,
                WidgetHotkeyAction.Duplicate,
                () => new ApplyDisplayModeCommand(DisplayMode.Duplicate)),
            BuiltIn(
                "display.extend",
                "Extend",
                QuickSwitchCommandCategory.Display,
                WidgetHotkeyAction.Extend,
                () => new ApplyDisplayModeCommand(DisplayMode.Extend)),
            BuiltIn(
                "display.second-screen-only",
                "Second screen only",
                QuickSwitchCommandCategory.Display,
                WidgetHotkeyAction.SecondScreenOnly,
                () => new ApplyDisplayModeCommand(DisplayMode.SecondScreenOnly)),
            BuiltIn(
                "devices.toggle-wifi",
                "Toggle Wi-Fi",
                QuickSwitchCommandCategory.Devices,
                WidgetHotkeyAction.ToggleWiFi,
                () => new ToggleWirelessRadioCommand(WirelessRadioKind.WiFi)),
            BuiltIn(
                "devices.toggle-bluetooth",
                "Toggle Bluetooth",
                QuickSwitchCommandCategory.Devices,
                WidgetHotkeyAction.ToggleBluetooth,
                () => new ToggleWirelessRadioCommand(WirelessRadioKind.Bluetooth)),
            BuiltIn(
                "taskbar.toggle-auto-hide",
                "Toggle taskbar auto-hide",
                QuickSwitchCommandCategory.Taskbar,
                WidgetHotkeyAction.ToggleTaskbarAutoHide,
                () => new ToggleTaskbarAutoHideCommand()),
            BuiltIn(
                "display.refresh",
                "Refresh Display",
                QuickSwitchCommandCategory.Display,
                WidgetHotkeyAction.RefreshDisplay,
                () => new RefreshOverlayFeatureCommand(OverlayFeatureId.Display)),
            BuiltIn(
                "audio.refresh",
                "Refresh Audio",
                QuickSwitchCommandCategory.Audio,
                WidgetHotkeyAction.RefreshAudio,
                () => new RefreshOverlayFeatureCommand(OverlayFeatureId.Audio)),
            BuiltIn(
                "devices.refresh",
                "Refresh Devices",
                QuickSwitchCommandCategory.Devices,
                WidgetHotkeyAction.RefreshDevices,
                () => new RefreshOverlayFeatureCommand(OverlayFeatureId.Devices)),
            BuiltIn(
                "general.refresh-all",
                "Refresh all",
                QuickSwitchCommandCategory.General,
                WidgetHotkeyAction.RefreshAll,
                () => new RefreshOverlayFeatureCommand(OverlayFeatureId.Home)),
        ];

        for (int slot = 0; slot < WidgetSettings.MaximumFavoriteOutputs; slot++)
        {
            int capturedSlot = slot;
            descriptors.Add(BuiltIn(
                $"favorites.output.{slot + 1}",
                $"Apply favorite output {slot + 1}",
                QuickSwitchCommandCategory.Favorites,
                WidgetSettings.GetFavoriteAction(slot),
                () => new ApplyAudioFavoriteCommand(
                    AudioEndpointKind.Playback,
                    capturedSlot)));
        }

        for (int slot = 0; slot < WidgetSettings.MaximumFavoriteInputs; slot++)
        {
            int capturedSlot = slot;
            descriptors.Add(BuiltIn(
                $"favorites.microphone.{slot + 1}",
                $"Apply favorite microphone {slot + 1}",
                QuickSwitchCommandCategory.Favorites,
                WidgetSettings.GetInputFavoriteAction(slot),
                () => new ApplyAudioFavoriteCommand(
                    AudioEndpointKind.Recording,
                    capturedSlot)));
        }

        return descriptors;
    }

    private static QuickSwitchCommandDescriptor BuiltIn(
        string key,
        string label,
        QuickSwitchCommandCategory category,
        WidgetHotkeyAction action,
        Func<QuickSwitchCommand> createCommand) =>
        new(
            key,
            label,
            category,
            createCommand,
            action,
            WidgetSettings.Default.GetShortcut(action));
}
