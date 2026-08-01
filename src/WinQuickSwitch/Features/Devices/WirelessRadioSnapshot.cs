namespace WinQuickSwitch.Features.Devices;

public sealed record WirelessRadioSnapshot(
    WirelessRadioState WiFi,
    WirelessRadioState Bluetooth)
{
    public WirelessRadioState GetState(WirelessRadioKind kind) =>
        kind == WirelessRadioKind.WiFi ? WiFi : Bluetooth;
}
