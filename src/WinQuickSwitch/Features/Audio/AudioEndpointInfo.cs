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

    public string DisplayLabel
    {
        get
        {
            List<string> roles = [];

            if (IsConsoleDefault && IsMultimediaDefault)
            {
                roles.Add("default");
            }
            else
            {
                if (IsConsoleDefault)
                {
                    roles.Add("console");
                }

                if (IsMultimediaDefault)
                {
                    roles.Add("multimedia");
                }
            }

            if (IsCommunicationsDefault)
            {
                roles.Add("communications");
            }

            return roles.Count == 0
                ? Name
                : $"{Name} ({string.Join(", ", roles)})";
        }
    }
}
