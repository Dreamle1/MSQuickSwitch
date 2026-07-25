using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WinQuickSwitch.Platform.Windows.Devices;

internal sealed class SetupApiDeviceSource
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint SpdrpEnumeratorName = 0x00000016;
    private const uint DnStarted = 0x00000008;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidData = 13;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private static readonly DevPropKey DeviceContainerIdKey = new()
    {
        FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        PropertyId = 2,
    };

    public IReadOnlyList<PnpDeviceDescriptor> ReadPresentDevices(
        CancellationToken cancellationToken)
    {
        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfAllClasses);

        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not open the present-device inventory.");
        }

        try
        {
            List<PnpDeviceDescriptor> devices = [];

            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SpDevInfoData deviceInfo = new()
                {
                    Size = (uint)Marshal.SizeOf<SpDevInfoData>(),
                };

                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    int error = Marshal.GetLastWin32Error();

                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(
                        error,
                        "Windows could not enumerate the present devices.");
                }

                string instanceId = GetInstanceId(deviceInfoSet, ref deviceInfo);
                string name = GetRegistryString(
                    deviceInfoSet,
                    ref deviceInfo,
                    SpdrpFriendlyName);

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = GetRegistryString(
                        deviceInfoSet,
                        ref deviceInfo,
                        SpdrpDeviceDesc);
                }

                uint status = 0;
                uint problemCode = 0;
                int statusResult = CmGetDevNodeStatus(
                    out status,
                    out problemCode,
                    deviceInfo.DeviceInstance,
                    0);

                if (statusResult != 0)
                {
                    status = 0;
                    problemCode = 0;
                }

                devices.Add(new PnpDeviceDescriptor(
                    instanceId,
                    GetContainerId(deviceInfoSet, ref deviceInfo),
                    name,
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpClass),
                    GetRegistryString(
                        deviceInfoSet,
                        ref deviceInfo,
                        SpdrpEnumeratorName),
                    GetRegistryString(
                        deviceInfoSet,
                        ref deviceInfo,
                        SpdrpHardwareId,
                        preserveMultipleValues: true),
                    (status & DnStarted) != 0,
                    problemCode));
            }

            return devices;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string GetInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfo)
    {
        StringBuilder buffer = new(512);

        if (SetupDiGetDeviceInstanceId(
            deviceInfoSet,
            ref deviceInfo,
            buffer,
            (uint)buffer.Capacity,
            out uint requiredSize))
        {
            return buffer.ToString();
        }

        int error = Marshal.GetLastWin32Error();

        if (error != ErrorInsufficientBuffer || requiredSize == 0)
        {
            throw new Win32Exception(error, "Windows could not read a device identifier.");
        }

        buffer.EnsureCapacity(checked((int)requiredSize));

        if (!SetupDiGetDeviceInstanceId(
            deviceInfoSet,
            ref deviceInfo,
            buffer,
            (uint)buffer.Capacity,
            out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not read a device identifier.");
        }

        return buffer.ToString();
    }

    private static string GetRegistryString(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfo,
        uint property,
        bool preserveMultipleValues = false)
    {
        if (!SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref deviceInfo,
            property,
            out _,
            null,
            0,
            out uint requiredSize))
        {
            int error = Marshal.GetLastWin32Error();

            if (error is ErrorInvalidData || requiredSize == 0)
            {
                return string.Empty;
            }

            if (error != ErrorInsufficientBuffer)
            {
                return string.Empty;
            }
        }

        if (requiredSize == 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[requiredSize];

        if (!SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref deviceInfo,
            property,
            out _,
            buffer,
            (uint)buffer.Length,
            out _))
        {
            return string.Empty;
        }

        string value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');

        return preserveMultipleValues
            ? value.Replace('\0', ';')
            : value.Split('\0', 2)[0];
    }

    private static Guid? GetContainerId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfo)
    {
        DevPropKey key = DeviceContainerIdKey;
        byte[] buffer = new byte[Marshal.SizeOf<Guid>()];

        if (!SetupDiGetDeviceProperty(
            deviceInfoSet,
            ref deviceInfo,
            ref key,
            out _,
            buffer,
            (uint)buffer.Length,
            out uint requiredSize,
            0) ||
            requiredSize != buffer.Length)
        {
            return null;
        }

        return new Guid(buffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        ref DevPropKey propertyKey,
        out uint propertyType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static extern int CmGetDevNodeStatus(
        out uint status,
        out uint problemNumber,
        uint deviceInstance,
        uint flags);
}
