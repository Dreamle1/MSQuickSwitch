using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal enum QuickSwitchLoadState
{
    Loading,
    Ready,
    Empty,
    Error,
}

/// <summary>
/// Immutable application snapshot. Coordinators publish a new instance rather
/// than exposing mutable WPF collections or controls.
/// </summary>
internal sealed record QuickSwitchState(
    DisplayQuickSwitchState Display,
    AudioQuickSwitchState Audio,
    DevicesQuickSwitchState Devices,
    OptionsQuickSwitchState Options,
    ProfilesQuickSwitchState Profiles,
    AboutQuickSwitchState About,
    QuickSwitchFeedbackState Feedback)
{
    public static QuickSwitchState Empty { get; } = new(
        DisplayQuickSwitchState.Empty,
        AudioQuickSwitchState.Empty,
        DevicesQuickSwitchState.Empty,
        OptionsQuickSwitchState.Empty,
        ProfilesQuickSwitchState.Empty,
        AboutQuickSwitchState.Empty,
        QuickSwitchFeedbackState.Empty);
}

internal sealed record DisplayQuickSwitchState(
    QuickSwitchLoadState LoadState,
    DisplayMode? CurrentMode,
    int ActiveDisplayCount,
    int AvailableDisplayCount,
    bool IsReliable,
    bool IsBusy,
    string StatusMessage)
{
    public static DisplayQuickSwitchState Empty { get; } = new(
        QuickSwitchLoadState.Loading, null, 0, 0, false, false,
        "Reading display topology...");

    public bool SupportsMultipleDisplays => IsReliable && AvailableDisplayCount > 1;
}

internal sealed record AudioQuickSwitchState(
    QuickSwitchLoadState LoadState,
    IReadOnlyList<AudioEndpointInfo> PlaybackEndpoints,
    IReadOnlyList<AudioEndpointInfo> RecordingEndpoints,
    IReadOnlyList<AudioSessionInfo> Sessions,
    IReadOnlyList<FavoriteAudioEndpointState> FavoriteOutputs,
    IReadOnlyList<FavoriteAudioEndpointState> FavoriteInputs,
    bool IsBusy,
    string StatusMessage)
{
    public static AudioQuickSwitchState Empty { get; } = new(
        QuickSwitchLoadState.Loading,
        Array.Empty<AudioEndpointInfo>(),
        Array.Empty<AudioEndpointInfo>(),
        Array.Empty<AudioSessionInfo>(),
        Array.Empty<FavoriteAudioEndpointState>(),
        Array.Empty<FavoriteAudioEndpointState>(),
        false,
        "Loading audio devices...");
}

internal sealed record FavoriteAudioEndpointState(
    int Slot,
    string EndpointId,
    string DisplayName,
    AudioEndpointKind Kind,
    FavoriteEndpointRole Role,
    bool IsConnected,
    string ShortcutText);

internal sealed record DevicesQuickSwitchState(
    QuickSwitchLoadState LoadState,
    WirelessRadioSnapshot? Radios,
    IReadOnlyList<ConnectedDeviceInfo> Devices,
    bool IsBusy,
    string StatusMessage)
{
    public static DevicesQuickSwitchState Empty { get; } = new(
        QuickSwitchLoadState.Loading,
        null,
        Array.Empty<ConnectedDeviceInfo>(),
        false,
        "Loading connected devices...");
}

internal sealed record OptionsQuickSwitchState(
    bool UseDarkTheme,
    bool OverlayEnabled,
    bool StartWithWindows,
    bool MainWindowPinned,
    double OverlayDimmingOpacity,
    double InspectorWidth,
    IReadOnlyDictionary<WidgetHotkeyAction, string> ShortcutTexts,
    string StatusMessage,
    IReadOnlyDictionary<string, string>? CommandShortcutTexts = null,
    int AudioVolumeStepPercent = 5)
{
    public static OptionsQuickSwitchState Empty { get; } = new(
        true,
        true,
        false,
        false,
        WidgetSettings.DefaultOverlayDimmingOpacity,
        OverlayWorkspaceSettings.DefaultInspectorWidth,
        new Dictionary<WidgetHotkeyAction, string>(),
        string.Empty);
}

internal sealed record ProfilesQuickSwitchState(
    QuickSwitchLoadState LoadState,
    IReadOnlyList<ProfileDefinition> Profiles,
    bool IsBusy,
    string StatusMessage)
{
    public static ProfilesQuickSwitchState Empty { get; } = new(
        QuickSwitchLoadState.Empty,
        Array.Empty<ProfileDefinition>(),
        false,
        "Profiles are saved locally.");
}

internal sealed record AboutQuickSwitchState(
    string Edition,
    string Version,
    string Publisher,
    string PrivacySummary)
{
    public static AboutQuickSwitchState Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, string.Empty);
}

internal sealed record QuickSwitchFeedbackState(
    QuickSwitchResultSeverity Severity,
    string Message,
    long Revision)
{
    public static QuickSwitchFeedbackState Empty { get; } = new(
        QuickSwitchResultSeverity.Info, string.Empty, 0);
}
