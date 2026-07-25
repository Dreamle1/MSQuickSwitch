namespace WinQuickSwitch.Features.Devices;

public sealed record DeviceInventory(
    IReadOnlyList<ConnectedDeviceInfo> Devices,
    DateTimeOffset CapturedAt);
