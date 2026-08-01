namespace WinQuickSwitch.Features.Devices;

public interface IWirelessRadioService
{
    Task<WirelessRadioSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<WirelessRadioResult> SetStateAsync(
        WirelessRadioKind kind,
        bool enabled,
        CancellationToken cancellationToken = default);
}
