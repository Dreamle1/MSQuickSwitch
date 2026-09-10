using System.Windows.Input;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Overlay;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class DevicesFeatureViewModel : SharedFeatureViewModel
{
    private DevicesQuickSwitchState _state = DevicesQuickSwitchState.Empty;

    public DevicesFeatureViewModel(IQuickSwitchCoordinator coordinator)
        : base(coordinator, QuickSwitchSurfaceDemand.Devices)
    {
        RefreshCommand = new AsyncRelayCommand(
            token => RefreshAsync(QuickSwitchRefreshScope.Devices, token));
        ToggleWiFiCommand = new AsyncRelayCommand(
            token => ToggleRadioAsync(WirelessRadioKind.WiFi, token),
            () => IsWiFiAvailable && !_state.IsBusy);
        ToggleBluetoothCommand = new AsyncRelayCommand(
            token => ToggleRadioAsync(WirelessRadioKind.Bluetooth, token),
            () => IsBluetoothAvailable && !_state.IsBusy);
        OpenWiFiSettingsCommand = CreateSettingsCommand(OverlaySystemSettingsTarget.Wifi);
        OpenBluetoothSettingsCommand = CreateSettingsCommand(OverlaySystemSettingsTarget.Bluetooth);
        OpenAirplaneModeSettingsCommand = CreateSettingsCommand(OverlaySystemSettingsTarget.AirplaneMode);
        OpenNetworkSettingsCommand = CreateSettingsCommand(OverlaySystemSettingsTarget.Network);
        OpenConnectedDevicesSettingsCommand = CreateSettingsCommand(OverlaySystemSettingsTarget.Devices);
        OpenFloatingDevicesCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenFloatingWidgetCommand(OverlayFeatureId.Devices), token));
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.Devices;

    public IReadOnlyList<ConnectedDeviceInfo> Devices => _state.Devices;

    public string StatusMessage => _state.StatusMessage;

    public string DeviceCountText => _state.Devices.Count == 1
        ? "1 connected or present device"
        : $"{_state.Devices.Count} connected or present devices";

    public bool IsBusy => _state.IsBusy;

    public bool IsWiFiOn =>
        _state.Radios?.WiFi == WirelessRadioState.On;

    public bool IsBluetoothOn =>
        _state.Radios?.Bluetooth == WirelessRadioState.On;

    public bool IsWiFiAvailable =>
        _state.Radios?.WiFi is WirelessRadioState.On or WirelessRadioState.Off;

    public bool IsBluetoothAvailable =>
        _state.Radios?.Bluetooth is WirelessRadioState.On or WirelessRadioState.Off;

    public string WiFiLabel => IsWiFiAvailable
        ? $"Wi-Fi {(IsWiFiOn ? "on" : "off")}" : "Wi-Fi unavailable";

    public string BluetoothLabel => IsBluetoothAvailable
        ? $"Bluetooth {(IsBluetoothOn ? "on" : "off")}" : "Bluetooth unavailable";

    public ICommand RefreshCommand { get; }
    public ICommand ToggleWiFiCommand { get; }
    public ICommand ToggleBluetoothCommand { get; }
    public ICommand OpenWiFiSettingsCommand { get; }
    public ICommand OpenBluetoothSettingsCommand { get; }
    public ICommand OpenAirplaneModeSettingsCommand { get; }
    public ICommand OpenNetworkSettingsCommand { get; }
    public ICommand OpenConnectedDevicesSettingsCommand { get; }
    public ICommand OpenFloatingDevicesCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        _state = state.Devices;
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsWiFiOn));
        OnPropertyChanged(nameof(IsBluetoothOn));
        OnPropertyChanged(nameof(IsWiFiAvailable));
        OnPropertyChanged(nameof(IsBluetoothAvailable));
        OnPropertyChanged(nameof(WiFiLabel));
        OnPropertyChanged(nameof(BluetoothLabel));
        (ToggleWiFiCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ToggleBluetoothCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private Task ToggleRadioAsync(
        WirelessRadioKind kind,
        CancellationToken cancellationToken)
    {
        bool isOn = kind == WirelessRadioKind.WiFi ? IsWiFiOn : IsBluetoothOn;
        return ExecuteAsync(
            new SetWirelessRadioStateCommand(kind, !isOn),
            cancellationToken);
    }

    private ICommand CreateSettingsCommand(OverlaySystemSettingsTarget target) =>
        new AsyncRelayCommand(
            token => ExecuteAsync(new OpenDeviceSettingsCommand(target), token));
}
