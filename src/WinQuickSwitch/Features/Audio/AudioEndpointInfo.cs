namespace WinQuickSwitch.Features.Audio;

public enum AudioEndpointKind
{
    Playback,
    Recording,
}

public sealed record AudioEndpointInfo(
    string Id,
    string Name,
    AudioEndpointKind Kind,
    bool IsConsoleDefault,
    bool IsMultimediaDefault,
    bool IsCommunicationsDefault)
{
    public bool IsDefault => IsConsoleDefault || IsMultimediaDefault;

    public string DisplayLabel => Name;
}
