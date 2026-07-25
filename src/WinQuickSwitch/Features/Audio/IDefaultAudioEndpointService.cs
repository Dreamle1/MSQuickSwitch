namespace WinQuickSwitch.Features.Audio;

public interface IDefaultAudioEndpointService
{
    Task<AudioControlResult> SetDefaultAsync(
        string endpointId,
        string endpointName,
        AudioDefaultRoleSelection roleSelection,
        CancellationToken cancellationToken = default);

    AudioControlResult OpenSoundSettings();
}
