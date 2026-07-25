using WinQuickSwitch.Features.Devices;

namespace WinQuickSwitch.Platform.Windows.Devices;

public sealed class WindowsDeviceInventoryService : IDeviceInventoryService
{
    private readonly SetupApiDeviceSource _deviceSource;

    public WindowsDeviceInventoryService() : this(new SetupApiDeviceSource())
    {
    }

    internal WindowsDeviceInventoryService(SetupApiDeviceSource deviceSource)
    {
        _deviceSource = deviceSource;
    }

    public Task<DeviceInventory> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                IReadOnlyList<PnpDeviceDescriptor> descriptors =
                    _deviceSource.ReadPresentDevices(cancellationToken);

                return new DeviceInventory(
                    ConnectedDeviceClassifier.Classify(descriptors),
                    DateTimeOffset.Now);
            },
            cancellationToken);
    }
}
