namespace WinQuickSwitch.Features.Devices;

public interface IDeviceSettingsService
{
    DeviceActionResult OpenBluetoothSettings();

    DeviceActionResult OpenConnectedDevicesSettings();
}
