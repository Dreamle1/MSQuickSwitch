using WinQuickSwitch.Features.Legal;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed record SetTaskbarAutoHideCommand(bool IsEnabled) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ToggleTaskbarAutoHideCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record SetOverlayDimmingOpacityCommand(double Opacity) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record SetOverlayInspectorWidthCommand(double Width) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record CaptureWidgetShortcutCommand(WidgetHotkeyAction Action) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ClearWidgetShortcutCommand(WidgetHotkeyAction Action) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ResetAllShortcutsCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record RestoreOptionsDefaultsCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.RestoreDefaults;
}

internal sealed record UpdateFavoriteAliasCommand(
    AudioEndpointKind EndpointKind,
    int Slot,
    string Alias) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record MoveFavoriteCommand(
    AudioEndpointKind EndpointKind,
    int Slot,
    int Direction) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record CaptureFavoriteShortcutCommand(
    AudioEndpointKind EndpointKind,
    int Slot) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ProfileDraft(
    string? ExistingProfileId,
    string Name,
    bool IncludeDisplay,
    bool IncludeTaskbar,
    bool IncludePlayback,
    bool IncludeRecording,
    bool IncludeMasterVolume,
    bool IncludeMicrophoneMute,
    string? PlaybackFavoriteId,
    string? RecordingFavoriteId,
    bool IsPinned);

internal sealed record SaveProfileDraftCommand(ProfileDraft Draft) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record UndoLastProfileCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record EditProfileCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record DuplicateProfileCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ToggleProfilePinCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record DeleteProfileCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record CaptureProfileShortcutCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record ClearProfileShortcutCommand(string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record OpenLegalDocumentCommand(LegalDocumentKind Document) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Navigate;
}
