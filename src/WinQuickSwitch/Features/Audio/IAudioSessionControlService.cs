namespace WinQuickSwitch.Features.Audio;

public interface IAudioSessionControlService
{
    Task<AudioControlResult> SetVolumeAsync(
        string sessionId,
        float volume,
        CancellationToken cancellationToken = default);

    Task<AudioControlResult> SetMuteAsync(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken = default);
}
