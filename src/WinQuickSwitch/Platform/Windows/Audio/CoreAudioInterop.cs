using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows.Audio;

internal static class CoreAudioInterop
{
    public static readonly Guid MmDeviceEnumeratorClassId =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public const ushort VariantTypeString = 31;

    public const uint CoInitializeMultithreaded = 0;

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInitialize);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant variant);
}

internal enum AudioDataFlow
{
    Render,
    Capture,
    All,
}

internal enum AudioRole
{
    Console,
    Multimedia,
    Communications,
}

[Flags]
internal enum AudioDeviceState : uint
{
    Active = 0x00000001,
}

[Flags]
internal enum ComClassContext : uint
{
    InProcessServer = 0x1,
    InProcessHandler = 0x2,
    LocalServer = 0x4,
    RemoteServer = 0x10,
    All = InProcessServer | InProcessHandler | LocalServer | RemoteServer,
}

internal enum AudioSessionState
{
    Inactive,
    Active,
    Expired,
}

internal enum AudioSessionDisconnectReason
{
    DeviceRemoval,
    ServerShutdown,
    FormatChanged,
    SessionLogoff,
    SessionDisconnected,
    ExclusiveModeOverride,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)]
    public ushort VariantType;

    [FieldOffset(8)]
    public IntPtr PointerValue;

    public readonly string? GetString() =>
        VariantType == CoreAudioInterop.VariantTypeString
            ? Marshal.PtrToStringUni(PointerValue)
            : null;
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(
        AudioDataFlow dataFlow,
        AudioDeviceState stateMask,
        out IMMDeviceCollection devices);

    void GetDefaultAudioEndpoint(
        AudioDataFlow dataFlow,
        AudioRole role,
        out IMMDevice endpoint);

    void GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string id,
        out IMMDevice device);

    void RegisterEndpointNotificationCallback(
        [MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(
        [MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);
}

[ComVisible(true)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        AudioDeviceState newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(
        AudioDataFlow dataFlow,
        AudioRole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        PropertyKey propertyKey);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);

    void Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(
        ref Guid interfaceId,
        ComClassContext classContext,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);

    void OpenPropertyStore(
        uint storageAccessMode,
        out IPropertyStore properties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    void GetState(out AudioDeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint propertyCount);

    void GetAt(uint propertyIndex, out PropertyKey key);

    void GetValue(ref PropertyKey key, out PropVariant value);

    void SetValue(ref PropertyKey key, ref PropVariant value);

    void Commit();
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(
        ref Guid sessionGuid,
        uint streamFlags,
        out IAudioSessionControl sessionControl);

    void GetSimpleAudioVolume(
        ref Guid sessionGuid,
        uint streamFlags,
        out ISimpleAudioVolume audioVolume);

    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

    void RegisterSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionNotification sessionNotification);

    void UnregisterSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionNotification sessionNotification);

    void RegisterDuckNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string sessionId,
        IntPtr duckNotification);

    void UnregisterDuckNotification(IntPtr duckNotification);
}

[ComVisible(true)]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl newSession);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int sessionCount);

    void GetSession(
        int sessionIndex,
        out IAudioSessionControl sessionControl);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out AudioSessionState state);

    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    void SetDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string displayName,
        IntPtr eventContext);

    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    void SetIconPath(
        [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
        IntPtr eventContext);

    void GetGroupingParam(out Guid groupingId);

    void SetGroupingParam(ref Guid groupingId, IntPtr eventContext);

    void RegisterAudioSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionEvents client);

    void UnregisterAudioSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionEvents client);
}

[ComVisible(true)]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string newDisplayName,
        ref Guid eventContext);

    [PreserveSig]
    int OnIconPathChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string newIconPath,
        ref Guid eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(
        float newVolume,
        [MarshalAs(UnmanagedType.Bool)] bool newMute,
        ref Guid eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(
        uint channelCount,
        IntPtr newChannelVolumes,
        uint changedChannel,
        ref Guid eventContext);

    [PreserveSig]
    int OnGroupingParamChanged(ref Guid newGroupingId, ref Guid eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState newState);

    [PreserveSig]
    int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    void GetState(out AudioSessionState state);

    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    void SetDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string displayName,
        IntPtr eventContext);

    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    void SetIconPath(
        [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
        IntPtr eventContext);

    void GetGroupingParam(out Guid groupingId);

    void SetGroupingParam(ref Guid groupingId, IntPtr eventContext);

    void RegisterAudioSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionEvents client);

    void UnregisterAudioSessionNotification(
        [MarshalAs(UnmanagedType.Interface)] IAudioSessionEvents client);

    void GetSessionIdentifier(
        [MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);

    void GetSessionInstanceIdentifier(
        [MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);

    void GetProcessId(out uint processId);

    [PreserveSig]
    int IsSystemSoundsSession();

    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, IntPtr eventContext);

    void GetMasterVolume(out float level);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, IntPtr eventContext);

    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
}
