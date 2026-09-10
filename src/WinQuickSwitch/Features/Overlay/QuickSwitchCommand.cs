using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.Overlay;

internal enum QuickSwitchCommandKind
{
    ToggleSurface,
    Navigate,
    Refresh,
    Expand,
    Hide,
    RestoreDefaults,
    ResetLayout,
    CloseOverlay,
    OpenMainWindow,
    OpenFloatingWidget,
    ApplyDisplayMode,
    ApplyAudioFavorite,
    ApplyProfile,
    SetOption,
    OpenSystemSettings,
    ExitApplication,
}

internal enum OverlayDisposition
{
    StayOpen,
    CloseOverlay,
    OpenInspector,
    OpenMainWindow,
    OpenFloatingWidget,
    OpenSystemSettings,
}

[Flags]
internal enum QuickSwitchRefreshScope
{
    None = 0,
    Display = 1 << 0,
    Audio = 1 << 1,
    Devices = 1 << 2,
    Options = 1 << 3,
    Favorites = 1 << 4,
    Profiles = 1 << 5,
    All = Display | Audio | Devices | Options | Favorites | Profiles,
}

internal enum QuickSwitchResultSeverity
{
    Success,
    Info,
    Warning,
    Error,
}

internal enum OverlayOption
{
    DarkTheme,
    OverlayEnabled,
    StartWithWindows,
    MainWindowPinned,
    TaskbarAutoHide,
}

internal enum OverlaySystemSettingsTarget
{
    Display,
    Sound,
    Devices,
    Wifi,
    Bluetooth,
    AirplaneMode,
    Network,
    Taskbar,
    Notifications,
}

/// <summary>
/// Base type for all overlay and quick-switch actions.  Consumers dispatch on
/// the concrete record rather than parsing string action IDs.
/// </summary>
internal abstract record QuickSwitchCommand
{
    public abstract QuickSwitchCommandKind Kind { get; }
}

internal sealed record NavigateToOverlayFeatureCommand(
    OverlayFeatureId Feature) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Navigate;
}

internal sealed record RefreshOverlayFeatureCommand(
    OverlayFeatureId Feature) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Refresh;
}

internal sealed record ExpandOverlayWidgetCommand(
    OverlayFeatureId Feature) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Expand;
}

internal sealed record HideOverlayWidgetCommand(
    OverlayFeatureId Feature) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.Hide;
}

internal sealed record RestoreOverlayDefaultsCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.RestoreDefaults;
}

internal sealed record ResetOverlayLayoutCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ResetLayout;
}

internal sealed record CloseOverlayCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.CloseOverlay;
}

internal sealed record OpenMainWindowCommand(
    OverlayFeatureId? Feature = null) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.OpenMainWindow;
}

internal sealed record OpenFloatingWidgetCommand(
    OverlayFeatureId Feature) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.OpenFloatingWidget;
}

internal sealed record ApplyDisplayModeCommand(
    DisplayMode Mode) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ApplyDisplayMode;
}

internal sealed record ApplyAudioFavoriteCommand(
    AudioEndpointKind EndpointKind,
    int Slot) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ApplyAudioFavorite;

    public bool IsValid => Slot is >= 0 and < 4;
}

internal sealed record ApplyProfileCommand(
    string ProfileId) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ApplyProfile;

    public bool IsValid => !string.IsNullOrWhiteSpace(ProfileId);
}

internal sealed record SetOverlayOptionCommand(
    OverlayOption Option,
    bool IsEnabled) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.SetOption;
}

internal sealed record OpenSystemSettingsCommand(
    OverlaySystemSettingsTarget Target) : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.OpenSystemSettings;
}

internal sealed record ExitApplicationCommand : QuickSwitchCommand
{
    public override QuickSwitchCommandKind Kind => QuickSwitchCommandKind.ExitApplication;
}

/// <summary>
/// The result contract lets every command report user-visible feedback and a
/// consistent smart-close decision to either surface.
/// </summary>
internal sealed record QuickSwitchResult
{
    public bool Succeeded { get; init; }

    public QuickSwitchResultSeverity Severity { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlySet<QuickSwitchRefreshScope> AffectedScopes { get; init; } =
        EmptyScopes;

    public OverlayDisposition Disposition { get; init; } =
        OverlayDisposition.StayOpen;

    public bool IsSuccessful => Succeeded && Severity == QuickSwitchResultSeverity.Success;

    public OverlayDisposition OverlayDisposition => Disposition;

    public static QuickSwitchResult Success(
        string message,
        OverlayDisposition disposition = OverlayDisposition.CloseOverlay,
        params QuickSwitchRefreshScope[] affectedScopes) =>
        Create(
            succeeded: true,
            QuickSwitchResultSeverity.Success,
            message,
            disposition,
            affectedScopes);

    public static QuickSwitchResult Info(
        string message,
        OverlayDisposition disposition = OverlayDisposition.StayOpen,
        params QuickSwitchRefreshScope[] affectedScopes) =>
        Create(
            succeeded: true,
            QuickSwitchResultSeverity.Info,
            message,
            disposition,
            affectedScopes);

    public static QuickSwitchResult Warning(
        string message,
        params QuickSwitchRefreshScope[] affectedScopes) =>
        Create(
            succeeded: false,
            QuickSwitchResultSeverity.Warning,
            message,
            OverlayDisposition.StayOpen,
            affectedScopes);

    public static QuickSwitchResult Failure(
        string message,
        params QuickSwitchRefreshScope[] affectedScopes) =>
        Create(
            succeeded: false,
            QuickSwitchResultSeverity.Error,
            message,
            OverlayDisposition.StayOpen,
            affectedScopes);

    public static QuickSwitchResult Cancelled(string message = "Action cancelled.") =>
        Create(
            succeeded: false,
            QuickSwitchResultSeverity.Info,
            message,
            OverlayDisposition.StayOpen,
            []);

    private static readonly IReadOnlySet<QuickSwitchRefreshScope> EmptyScopes =
        new HashSet<QuickSwitchRefreshScope>();

    private static QuickSwitchResult Create(
        bool succeeded,
        QuickSwitchResultSeverity severity,
        string message,
        OverlayDisposition disposition,
        IEnumerable<QuickSwitchRefreshScope> affectedScopes) =>
        new()
        {
            Succeeded = succeeded,
            Severity = severity,
            Message = message ?? string.Empty,
            Disposition = disposition,
            AffectedScopes = NormalizeScopes(affectedScopes),
        };

    private static IReadOnlySet<QuickSwitchRefreshScope> NormalizeScopes(
        IEnumerable<QuickSwitchRefreshScope> affectedScopes) =>
        new HashSet<QuickSwitchRefreshScope>(
            affectedScopes.Where(scope => scope != QuickSwitchRefreshScope.None));
}

internal static class QuickSwitchCommandMetadata
{
    public static OverlayDisposition GetDefaultDisposition(
        QuickSwitchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            ApplyDisplayModeCommand => OverlayDisposition.CloseOverlay,
            ApplyAudioFavoriteCommand => OverlayDisposition.CloseOverlay,
            ApplyProfileCommand => OverlayDisposition.CloseOverlay,
            ExpandOverlayWidgetCommand => OverlayDisposition.OpenInspector,
            OpenMainWindowCommand => OverlayDisposition.OpenMainWindow,
            OpenFloatingWidgetCommand => OverlayDisposition.OpenFloatingWidget,
            OpenSystemSettingsCommand => OverlayDisposition.OpenSystemSettings,
            CloseOverlayCommand => OverlayDisposition.CloseOverlay,
            _ => OverlayDisposition.StayOpen,
        };
    }

    public static QuickSwitchRefreshScope GetDefaultRefreshScope(
        QuickSwitchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            ApplyDisplayModeCommand => QuickSwitchRefreshScope.Display,
            WinQuickSwitch.Features.SharedUi.ExecuteAudioQuickActionCommand => QuickSwitchRefreshScope.Audio,
            WinQuickSwitch.Features.SharedUi.SetAudioShortcutStepCommand => QuickSwitchRefreshScope.Options,
            WinQuickSwitch.Features.SharedUi.CaptureCommandShortcutCommand => QuickSwitchRefreshScope.Options,
            WinQuickSwitch.Features.SharedUi.ClearCommandShortcutCommand => QuickSwitchRefreshScope.Options,
            ApplyAudioFavoriteCommand => QuickSwitchRefreshScope.Audio |
                QuickSwitchRefreshScope.Favorites,
            ApplyProfileCommand => QuickSwitchRefreshScope.All,
            SetOverlayOptionCommand => QuickSwitchRefreshScope.Options,
            RefreshOverlayFeatureCommand refresh =>
                ToRefreshScope(refresh.Feature),
            _ => QuickSwitchRefreshScope.None,
        };
    }

    public static OverlayFeatureId? GetFeature(QuickSwitchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            NavigateToOverlayFeatureCommand navigate => navigate.Feature,
            RefreshOverlayFeatureCommand refresh => refresh.Feature,
            ExpandOverlayWidgetCommand expand => expand.Feature,
            HideOverlayWidgetCommand hide => hide.Feature,
            OpenMainWindowCommand open => open.Feature,
            OpenFloatingWidgetCommand floating => floating.Feature,
            _ => null,
        };
    }

    private static QuickSwitchRefreshScope ToRefreshScope(
        OverlayFeatureId feature) =>
        feature switch
        {
            OverlayFeatureId.Display => QuickSwitchRefreshScope.Display,
            OverlayFeatureId.Audio => QuickSwitchRefreshScope.Audio,
            OverlayFeatureId.Devices => QuickSwitchRefreshScope.Devices,
            OverlayFeatureId.Favorites => QuickSwitchRefreshScope.Favorites |
                QuickSwitchRefreshScope.Audio,
            OverlayFeatureId.Options => QuickSwitchRefreshScope.Options,
            OverlayFeatureId.Profiles => QuickSwitchRefreshScope.Profiles,
            OverlayFeatureId.Home => QuickSwitchRefreshScope.All,
            OverlayFeatureId.About => QuickSwitchRefreshScope.None,
            _ => QuickSwitchRefreshScope.None,
        };
}
