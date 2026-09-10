using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed record ToggleConfiguredSurfaceCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ToggleSurface;
}

internal sealed record OpenOverlayWorkspaceCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Navigate;
}

internal sealed record SetDefaultAudioEndpointCommand(
    string EndpointId,
    string EndpointName,
    AudioDefaultRoleSelection Role) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record AddAudioFavoriteCommand(
    string EndpointId,
    string EndpointName,
    AudioEndpointKind EndpointKind,
    FavoriteEndpointRole Role) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record RemoveAudioFavoriteCommand(
    AudioEndpointKind EndpointKind,
    int Slot) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record SetAudioSessionVolumeCommand(
    string SessionId,
    float Volume) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record SetAudioSessionMuteCommand(
    string SessionId,
    bool IsMuted) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ExecuteAudioQuickActionCommand(AudioQuickAction Action) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record CaptureCommandShortcutCommand(string CommandKey) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ClearCommandShortcutCommand(string CommandKey) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record SetAudioShortcutStepCommand(int Percent) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal enum AudioSettingsTarget
{
    VolumeMixer,
    Sound,
}

internal sealed record OpenAudioSettingsCommand(
    AudioSettingsTarget Target) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.OpenSystemSettings;
}

internal sealed record SetWirelessRadioStateCommand(
    WirelessRadioKind Radio,
    bool IsEnabled) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ToggleWirelessRadioCommand(
    WirelessRadioKind Radio) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record OpenDeviceSettingsCommand(
    OverlaySystemSettingsTarget Target) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.OpenSystemSettings;
}
