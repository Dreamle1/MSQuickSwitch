namespace WinQuickSwitch.Features.Devices;

public sealed record ConnectedDeviceInfo(
    string Id,
    string Name,
    string Category,
    DeviceTransport Transport,
    bool IsStarted,
    uint ProblemCode)
{
    public string ConnectionLabel =>
        Transport == DeviceTransport.Bluetooth ? "Bluetooth" : "Wired";

    public string StatusLabel =>
        ProblemCode != 0 ? "Needs attention" :
        IsStarted ? "Connected" :
        "Present";
}
