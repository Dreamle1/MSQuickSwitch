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

    public string ActiveRoleDescription => (IsDefault, IsCommunicationsDefault) switch
    {
        (true, true) => "Default audio and calls device",
        (true, false) => "Default audio device",
        (false, true) => "Calls device",
        _ => "Available audio device",
    };

    public string DisplayLabel => Name;
}
