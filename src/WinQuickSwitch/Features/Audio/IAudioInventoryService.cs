namespace WinQuickSwitch.Features.Audio;

public interface IAudioInventoryService
{
    Task<AudioInventory> GetInventoryAsync(
        CancellationToken cancellationToken = default);
}
