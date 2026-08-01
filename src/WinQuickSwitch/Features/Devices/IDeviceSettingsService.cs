namespace WinQuickSwitch.Features.Devices;

public interface IDeviceSettingsService
{
    DeviceActionResult OpenBluetoothSettings();

    DeviceActionResult OpenConnectedDevicesSettings();

    DeviceActionResult OpenWiFiSettings();

    DeviceActionResult OpenNetworkSettings();

    DeviceActionResult OpenAirplaneModeSettings();
}
