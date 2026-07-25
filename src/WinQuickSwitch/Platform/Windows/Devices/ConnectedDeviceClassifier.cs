using WinQuickSwitch.Features.Devices;

namespace WinQuickSwitch.Platform.Windows.Devices;

internal static class ConnectedDeviceClassifier
{
    private static readonly string[] InfrastructureNames =
    [
        "Bluetooth Device (RFCOMM Protocol TDI)",
        "Bluetooth LE XINPUT compatible input device",
        "Bluetooth LE Generic Attribute Service",
        "Device Information Service",
        "Device Identification Service",
        "Generic Access Profile",
        "Generic Attribute Profile",
        "Generic Bluetooth Adapter",
        "Generic SuperSpeed USB Hub",
        "Generic USB Hub",
        "HID-compliant consumer control device",
        "HID-compliant device",
        "HID-compliant system controller",
        "HID Keyboard Device",
        "Intel(R) Wireless Bluetooth(R)",
        "Microsoft Bluetooth Enumerator",
        "Microsoft Bluetooth LE Enumerator",
        "Microsoft Bluetooth Protocol Support",
        "USB Composite Device",
        "USB Input Device",
        "USB Root Hub",
        "USB Root Hub (USB 3.0)",
    ];

    public static IReadOnlyList<ConnectedDeviceInfo> Classify(
        IEnumerable<PnpDeviceDescriptor> descriptors)
    {
        ConnectedDeviceInfo[] classified = descriptors
            .GroupBy(GetPhysicalDeviceKey, StringComparer.OrdinalIgnoreCase)
            .Select(ClassifyGroup)
            .Where(device => device is not null)
            .Cast<ConnectedDeviceInfo>()
            .ToArray();

        IEnumerable<ConnectedDeviceInfo> bluetooth = classified
            .Where(device => device.Transport == DeviceTransport.Bluetooth)
            .GroupBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(MergeBluetoothProfiles);
        IEnumerable<ConnectedDeviceInfo> wired = classified
            .Where(device => device.Transport == DeviceTransport.Wired);

        return bluetooth
            .Concat(wired)
            .OrderBy(device => device.Transport)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ConnectedDeviceInfo? ClassifyGroup(
        IGrouping<string, PnpDeviceDescriptor> group)
    {
        PnpDeviceDescriptor[] members = group.ToArray();
        DeviceTransport? transport = GetTransport(members);

        if (transport is null)
        {
            return null;
        }

        PnpDeviceDescriptor best = members
            .OrderByDescending(GetNameScore)
            .ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .First();

        if (GetNameScore(best) <= 0)
        {
            return null;
        }

        uint problemCode = members
            .Select(member => member.ProblemCode)
            .FirstOrDefault(code => code != 0);

        return new ConnectedDeviceInfo(
            group.Key,
            NormalizeName(best.Name, transport.Value),
            GetCategory(members, best),
            transport.Value,
            members.Any(member => member.IsStarted),
            problemCode);
    }

    private static ConnectedDeviceInfo MergeBluetoothProfiles(
        IGrouping<string, ConnectedDeviceInfo> profiles)
    {
        ConnectedDeviceInfo[] devices = profiles.ToArray();
        ConnectedDeviceInfo best = devices
            .OrderBy(device => device.ProblemCode != 0)
            .ThenByDescending(device => device.IsStarted)
            .ThenBy(device => device.Category == "Bluetooth device")
            .First();

        return best with
        {
            IsStarted = devices.Any(device => device.IsStarted),
            ProblemCode = devices
                .Select(device => device.ProblemCode)
                .FirstOrDefault(code => code != 0),
        };
    }

    private static string GetPhysicalDeviceKey(PnpDeviceDescriptor descriptor) =>
        descriptor.ContainerId is Guid containerId && containerId != Guid.Empty
            ? $"container:{containerId:D}"
            : $"instance:{descriptor.InstanceId}";

    private static DeviceTransport? GetTransport(
        IReadOnlyCollection<PnpDeviceDescriptor> members)
    {
        if (members.Any(IsBluetooth))
        {
            return DeviceTransport.Bluetooth;
        }

        return members.Any(IsUsb)
            ? DeviceTransport.Wired
            : null;
    }

    private static bool IsBluetooth(PnpDeviceDescriptor descriptor) =>
        StartsWith(descriptor.EnumeratorName, "BTH") ||
        StartsWith(descriptor.InstanceId, "BTH");

    private static bool IsUsb(PnpDeviceDescriptor descriptor) =>
        StartsWith(descriptor.EnumeratorName, "USB") ||
        StartsWith(descriptor.InstanceId, "USB") ||
        descriptor.HardwareIds.Contains("USB\\", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static int GetNameScore(PnpDeviceDescriptor descriptor)
    {
        string name = descriptor.Name.Trim();

        if (string.IsNullOrWhiteSpace(name) || IsInfrastructureName(name))
        {
            return 0;
        }

        int score = 10;

        if (descriptor.DeviceClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase) ||
            descriptor.DeviceClass.Equals("Media", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (descriptor.DeviceClass.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
            descriptor.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase) ||
            descriptor.DeviceClass.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
            descriptor.DeviceClass.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (!name.Contains("compliant", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("generic", StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static bool IsInfrastructureName(string name) =>
        InfrastructureNames.Any(infrastructureName =>
            name.Equals(infrastructureName, StringComparison.OrdinalIgnoreCase)) ||
        name.StartsWith("USB Root Hub", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(
            "Bluetooth Device (Personal Area Network)",
            StringComparison.OrdinalIgnoreCase);

    private static string GetCategory(
        IReadOnlyCollection<PnpDeviceDescriptor> members,
        PnpDeviceDescriptor best)
    {
        string combined = string.Join(
            " ",
            members.Select(member => $"{member.DeviceClass} {member.Name}"));

        if (Contains(combined, "keyboard"))
        {
            return "Keyboard";
        }

        if (Contains(combined, "mouse"))
        {
            return "Mouse";
        }

        if (Contains(combined, "audio") ||
            Contains(combined, "headset") ||
            Contains(combined, "headphone") ||
            Contains(combined, "speaker") ||
            Contains(combined, "microphone") ||
            best.DeviceClass.Equals("Media", StringComparison.OrdinalIgnoreCase))
        {
            return "Audio";
        }

        if (Contains(combined, "camera") ||
            best.DeviceClass.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            return "Camera";
        }

        if (Contains(combined, "display") || Contains(combined, "monitor"))
        {
            return "Display";
        }

        return best.DeviceClass switch
        {
            "HIDClass" => "Input device",
            _ when members.Any(IsBluetooth) => "Bluetooth device",
            _ => "USB device",
        };
    }

    private static bool Contains(string value, string part) =>
        value.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name, DeviceTransport transport)
    {
        string normalized = name.Trim();

        if (transport != DeviceTransport.Bluetooth)
        {
            return normalized;
        }

        if (normalized.StartsWith("LE_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        string[] profileSuffixes =
        [
            " Hands-Free AG Audio",
            " Hands-Free",
            " Stereo",
            " Avrcp Transport",
        ];

        foreach (string suffix in profileSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized[..^suffix.Length].TrimEnd();
            }
        }

        return normalized;
    }
}
