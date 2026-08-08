namespace WinQuickSwitch.Features.Audio;

public sealed record AudioEndpointControlSnapshot(
    float MasterVolume,
    bool IsMuted);

public interface IAudioEndpointControlService
{
    Task<AudioEndpointControlSnapshot?> GetStateAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    Task<AudioControlResult> SetMasterVolumeAsync(
        string endpointId,
        string endpointName,
        float volume,
        CancellationToken cancellationToken = default);

    Task<AudioControlResult> SetMuteAsync(
        string endpointId,
        string endpointName,
        bool isMuted,
        CancellationToken cancellationToken = default);
}
