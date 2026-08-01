using System.Diagnostics;
using WinQuickSwitch.Features.Devices;

namespace WinQuickSwitch.Platform.Windows.Devices;

public sealed class WindowsDeviceSettingsService : IDeviceSettingsService
{
    private readonly IDeviceSettingsLauncher _launcher;

    public WindowsDeviceSettingsService() : this(new ShellDeviceSettingsLauncher())
    {
    }

    internal WindowsDeviceSettingsService(IDeviceSettingsLauncher launcher)
    {
        _launcher = launcher;
    }

    public DeviceActionResult OpenBluetoothSettings() =>
        Open("ms-settings:bluetooth", "Bluetooth settings");

    public DeviceActionResult OpenConnectedDevicesSettings() =>
        Open("ms-settings:connecteddevices", "Connected devices settings");

    public DeviceActionResult OpenWiFiSettings() =>
        Open("ms-settings:network-wifi", "Wi-Fi settings");

    public DeviceActionResult OpenNetworkSettings() =>
        Open("ms-settings:network-status", "Network settings");

    public DeviceActionResult OpenAirplaneModeSettings() =>
        Open("ms-settings:network-airplanemode", "Airplane mode settings");

    private DeviceActionResult Open(string uri, string description)
    {
        try
        {
            _launcher.Open(uri);
            return new DeviceActionResult(true, $"{description} opened.");
        }
        catch (Exception exception)
        {
            return new DeviceActionResult(
                false,
                $"{description} could not be opened: {exception.Message}");
        }
    }
}

internal interface IDeviceSettingsLauncher
{
    void Open(string uri);
}

internal sealed class ShellDeviceSettingsLauncher : IDeviceSettingsLauncher
{
    public void Open(string uri)
    {
        Process.Start(new ProcessStartInfo(uri)
        {
            UseShellExecute = true,
        });
    }
}
