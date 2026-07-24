namespace WinQuickSwitch.Features.Audio;

public sealed record AudioSessionInfo(
    string Id,
    string ApplicationName,
    string EndpointName,
    float Volume,
    bool IsMuted)
{
    public int VolumePercent => (int)Math.Round(
        Math.Clamp(Volume, 0, 1) * 100,
        MidpointRounding.AwayFromZero);

    public string VolumeLabel => $"{VolumePercent}%";

    public string MuteLabel => IsMuted ? "Yes" : "No";
}
