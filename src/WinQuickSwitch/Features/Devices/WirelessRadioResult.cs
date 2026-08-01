namespace WinQuickSwitch.Features.Devices;

public sealed record WirelessRadioResult(bool Succeeded, string Message)
{
    public static WirelessRadioResult Success(string message) => new(true, message);

    public static WirelessRadioResult Failure(string message) => new(false, message);
}
