namespace WinQuickSwitch.Features.Audio;

public sealed record AudioInventory(
    IReadOnlyList<AudioEndpointInfo> PlaybackEndpoints,
    IReadOnlyList<AudioEndpointInfo> RecordingEndpoints,
    IReadOnlyList<AudioSessionInfo> Sessions,
    DateTimeOffset CapturedAt);
