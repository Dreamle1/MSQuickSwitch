namespace WinQuickSwitch.Features.Devices;

public interface IDeviceInventoryService
{
    Task<DeviceInventory> GetInventoryAsync(
        CancellationToken cancellationToken = default);
}
