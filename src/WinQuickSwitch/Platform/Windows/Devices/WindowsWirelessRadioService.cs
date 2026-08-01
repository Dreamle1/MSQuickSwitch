using Windows.Devices.Radios;
using WinQuickSwitch.Features.Devices;

namespace WinQuickSwitch.Platform.Windows.Devices;

public sealed class WindowsWirelessRadioService : IWirelessRadioService
{
    private readonly IWirelessRadioBackend _backend;

    public WindowsWirelessRadioService() : this(new WinRtWirelessRadioBackend())
    {
    }

    internal WindowsWirelessRadioService(IWirelessRadioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    public async Task<WirelessRadioSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WirelessRadioDevice> radios =
            await _backend.GetRadiosAsync(cancellationToken);

        return new WirelessRadioSnapshot(
            GetCombinedState(radios, WirelessRadioKind.WiFi),
            GetCombinedState(radios, WirelessRadioKind.Bluetooth));
    }

    public async Task<WirelessRadioResult> SetStateAsync(
        WirelessRadioKind kind,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        string name = GetDisplayName(kind);

        try
        {
            WirelessRadioControlStatus status = await _backend.SetStateAsync(
                kind,
                enabled,
                cancellationToken);

            return status switch
            {
                WirelessRadioControlStatus.Allowed => WirelessRadioResult.Success(
                    $"{name} turned {(enabled ? "on" : "off")}."),
                WirelessRadioControlStatus.Unavailable => WirelessRadioResult.Failure(
                    $"No {name} radio is available."),
                WirelessRadioControlStatus.DeniedByUser => WirelessRadioResult.Failure(
                    $"Windows did not grant permission to control {name}."),
                WirelessRadioControlStatus.DeniedBySystem => WirelessRadioResult.Failure(
                    $"Windows or device policy does not allow {name} control here."),
                _ => WirelessRadioResult.Failure(
                    $"Windows could not change {name}."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return WirelessRadioResult.Failure(
                $"Windows could not change {name}: {exception.Message}");
        }
    }

    internal static WirelessRadioState GetCombinedState(
        IReadOnlyList<WirelessRadioDevice> radios,
        WirelessRadioKind kind)
    {
        WirelessRadioState[] states = radios
            .Where(radio => radio.Kind == kind)
            .Select(radio => radio.State)
            .ToArray();

        if (states.Length == 0)
        {
            return WirelessRadioState.Unavailable;
        }

        if (states.Contains(WirelessRadioState.On))
        {
            return WirelessRadioState.On;
        }

        return states.All(state => state == WirelessRadioState.Off)
            ? WirelessRadioState.Off
            : WirelessRadioState.Disabled;
    }

    internal static string GetDisplayName(WirelessRadioKind kind) =>
        kind == WirelessRadioKind.WiFi ? "Wi-Fi" : "Bluetooth";
}

internal sealed record WirelessRadioDevice(
    WirelessRadioKind Kind,
    WirelessRadioState State);

internal enum WirelessRadioControlStatus
{
    Unspecified,
    Allowed,
    DeniedByUser,
    DeniedBySystem,
    Unavailable,
}

internal interface IWirelessRadioBackend
{
    Task<IReadOnlyList<WirelessRadioDevice>> GetRadiosAsync(
        CancellationToken cancellationToken);

    Task<WirelessRadioControlStatus> SetStateAsync(
        WirelessRadioKind kind,
        bool enabled,
        CancellationToken cancellationToken);
}

internal sealed class WinRtWirelessRadioBackend : IWirelessRadioBackend
{
    public async Task<IReadOnlyList<WirelessRadioDevice>> GetRadiosAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return radios
            .Where(radio => TryMapKind(radio.Kind, out _))
            .Select(radio => new WirelessRadioDevice(
                MapKind(radio.Kind),
                MapState(radio.State)))
            .ToArray();
    }

    public async Task<WirelessRadioControlStatus> SetStateAsync(
        WirelessRadioKind kind,
        bool enabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RadioAccessStatus access = await Radio.RequestAccessAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (access != RadioAccessStatus.Allowed)
        {
            return MapAccess(access);
        }

        IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
        Radio[] matching = radios
            .Where(radio =>
                TryMapKind(radio.Kind, out WirelessRadioKind mappedKind) &&
                mappedKind == kind)
            .ToArray();

        if (matching.Length == 0)
        {
            return WirelessRadioControlStatus.Unavailable;
        }

        RadioState targetState = enabled ? RadioState.On : RadioState.Off;

        foreach (Radio radio in matching)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RadioAccessStatus result = await radio.SetStateAsync(targetState);

            if (result != RadioAccessStatus.Allowed)
            {
                return MapAccess(result);
            }
        }

        return WirelessRadioControlStatus.Allowed;
    }

    private static bool TryMapKind(
        RadioKind kind,
        out WirelessRadioKind mappedKind)
    {
        mappedKind = kind == RadioKind.WiFi
            ? WirelessRadioKind.WiFi
            : WirelessRadioKind.Bluetooth;
        return kind is RadioKind.WiFi or RadioKind.Bluetooth;
    }

    private static WirelessRadioKind MapKind(RadioKind kind) =>
        kind == RadioKind.WiFi
            ? WirelessRadioKind.WiFi
            : WirelessRadioKind.Bluetooth;

    private static WirelessRadioState MapState(RadioState state) =>
        state switch
        {
            RadioState.On => WirelessRadioState.On,
            RadioState.Off => WirelessRadioState.Off,
            RadioState.Disabled => WirelessRadioState.Disabled,
            _ => WirelessRadioState.Unavailable,
        };

    private static WirelessRadioControlStatus MapAccess(RadioAccessStatus access) =>
        access switch
        {
            RadioAccessStatus.Allowed => WirelessRadioControlStatus.Allowed,
            RadioAccessStatus.DeniedByUser => WirelessRadioControlStatus.DeniedByUser,
            RadioAccessStatus.DeniedBySystem => WirelessRadioControlStatus.DeniedBySystem,
            _ => WirelessRadioControlStatus.Unspecified,
        };
}
