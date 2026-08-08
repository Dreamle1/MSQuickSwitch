using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Taskbar;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.Profiles;

internal sealed record ProfileEndpointTarget(
    string EndpointId,
    string Name)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(EndpointId) &&
        !string.IsNullOrWhiteSpace(Name);

    public ProfileEndpointTarget Normalize() =>
        new(EndpointId.Trim(), Name.Trim());
}

internal sealed record ProfileDefinition(
    string Id,
    string Name,
    bool IsPinned,
    WidgetShortcut? Shortcut,
    DisplayMode? DisplayMode = null,
    ProfileEndpointTarget? PlaybackGeneral = null,
    ProfileEndpointTarget? PlaybackCommunications = null,
    ProfileEndpointTarget? RecordingGeneral = null,
    ProfileEndpointTarget? RecordingCommunications = null,
    TaskbarState? TaskbarState = null,
    bool? MicrophoneMuted = null,
    float? MasterVolume = null)
{
    public const int MaximumNameLength = 80;

    public bool HasActions =>
        DisplayMode is not null ||
        PlaybackGeneral is not null ||
        PlaybackCommunications is not null ||
        RecordingGeneral is not null ||
        RecordingCommunications is not null ||
        TaskbarState is not null ||
        MicrophoneMuted is not null ||
        MasterVolume is not null;

    public ProfileDefinition Normalize()
    {
        string normalizedName = (Name ?? string.Empty).Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            normalizedName = normalizedName[..MaximumNameLength];
        }

        return this with
        {
            Id = (Id ?? string.Empty).Trim(),
            Name = normalizedName,
            Shortcut = Shortcut is { IsValid: true } ? Shortcut : null,
            PlaybackGeneral = NormalizeEndpoint(PlaybackGeneral),
            PlaybackCommunications = NormalizeEndpoint(PlaybackCommunications),
            RecordingGeneral = NormalizeEndpoint(RecordingGeneral),
            RecordingCommunications = NormalizeEndpoint(RecordingCommunications),
            TaskbarState = TaskbarState is Features.Taskbar.TaskbarState.Visible or
                Features.Taskbar.TaskbarState.AutoHidden
                ? TaskbarState
                : null,
            MasterVolume = MasterVolume is >= 0 and <= 1
                ? MasterVolume
                : null,
        };
    }

    private static ProfileEndpointTarget? NormalizeEndpoint(
        ProfileEndpointTarget? endpoint) =>
        endpoint is { IsValid: true }
            ? endpoint.Normalize()
            : null;
}

internal sealed record ProfileCatalog(
    int SchemaVersion,
    IReadOnlyList<ProfileDefinition> Profiles)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumPinnedProfiles = 4;

    public static ProfileCatalog Empty { get; } =
        new(CurrentSchemaVersion, []);

    public ProfileCatalog Normalize()
    {
        List<ProfileDefinition> normalized = [];
        HashSet<string> profileIds = new(StringComparer.Ordinal);

        foreach (ProfileDefinition profile in Profiles ?? [])
        {
            ProfileDefinition candidate = profile.Normalize();

            if (string.IsNullOrWhiteSpace(candidate.Id) ||
                string.IsNullOrWhiteSpace(candidate.Name) ||
                !profileIds.Add(candidate.Id))
            {
                continue;
            }

            normalized.Add(candidate);
        }

        int pinnedCount = 0;

        for (int index = 0; index < normalized.Count; index++)
        {
            ProfileDefinition profile = normalized[index];

            if (!profile.IsPinned)
            {
                continue;
            }

            pinnedCount++;

            if (pinnedCount > MaximumPinnedProfiles)
            {
                normalized[index] = profile with { IsPinned = false };
            }
        }

        return new(CurrentSchemaVersion, normalized);
    }
}
