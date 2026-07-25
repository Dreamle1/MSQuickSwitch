namespace WinQuickSwitch.Platform.Windows.Devices;

internal sealed record PnpDeviceDescriptor(
    string InstanceId,
    Guid? ContainerId,
    string Name,
    string DeviceClass,
    string EnumeratorName,
    string HardwareIds,
    bool IsStarted,
    uint ProblemCode);
