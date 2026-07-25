using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows;
using WinQuickSwitch.Platform.Windows.Audio;
using WinQuickSwitch.Platform.Windows.Devices;
using WinQuickSwitch.Platform.Windows.Display;

namespace WinQuickSwitch.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        List<(string Name, Func<Task> Run)> tests =
        [
            ("PC screen only uses /internal", () => MapsMode(DisplayMode.PcScreenOnly, "/internal")),
            ("Duplicate uses /clone", () => MapsMode(DisplayMode.Duplicate, "/clone")),
            ("Extend uses /extend", () => MapsMode(DisplayMode.Extend, "/extend")),
            ("Second screen only uses /external", () => MapsMode(DisplayMode.SecondScreenOnly, "/external")),
            ("Zero exit code succeeds", ZeroExitCodeSucceeds),
            ("Non-zero exit code fails", NonZeroExitCodeFails),
            ("Process startup failure becomes a result", ProcessFailureBecomesResult),
            ("Cancellation is preserved", CancellationIsPreserved),
            ("Unknown modes are rejected before process execution", UnknownModeIsRejected),
            ("Hidden process runner returns the process exit code", HiddenProcessRunnerReturnsExitCode),
            ("Single internal display is classified as PC screen only", SingleInternalDisplayIsClassified),
            ("Single external display is classified as second screen only", SingleExternalDisplayIsClassified),
            ("Shared display source is classified as duplicate", SharedSourceIsClassifiedAsDuplicate),
            ("Distinct display sources are classified as extend", DistinctSourcesAreClassifiedAsExtend),
            ("Mixed display sources are not mislabeled", MixedSourcesAreNotMislabeled),
            ("Inactive available display enables multi-display choices", AvailableInactiveDisplayEnablesChoices),
            ("No active display produces an unreliable snapshot", NoActiveDisplayIsUnreliable),
            ("Audio endpoint labels use the friendly name", AudioEndpointLabelsUseFriendlyName),
            ("Audio endpoint roles have clear active descriptions", AudioEndpointRolesAreClear),
            ("Audio session volume is formatted and clamped", AudioSessionVolumeIsFormatted),
            ("Audio refresh notifications are debounced", AudioRefreshNotificationsAreDebounced),
            ("Disposed audio debounce cancels pending work", DisposedAudioDebounceCancelsWork),
            ("Session volume delegates the requested level", SessionVolumeDelegatesRequestedLevel),
            ("Invalid session volume is rejected", InvalidSessionVolumeIsRejected),
            ("Session mute delegates the requested state", SessionMuteDelegatesRequestedState),
            ("General endpoint selection updates both default roles", GeneralEndpointSelectionUpdatesBothRoles),
            ("Communications endpoint selection updates only calls", CommunicationsEndpointSelectionUpdatesCalls),
            ("Unsupported endpoint selection preserves Settings fallback", UnsupportedEndpointSelectionPreservesFallback),
            ("Physical device interfaces are grouped by container", PhysicalDeviceInterfacesAreGrouped),
            ("Bluetooth takes precedence for mixed-interface devices", BluetoothTakesPrecedence),
            ("Bluetooth profile names collapse into one device", BluetoothProfilesCollapse),
            ("Generic device infrastructure is hidden", GenericDeviceInfrastructureIsHidden),
            ("Unrelated USB devices remain separate", UnrelatedUsbDevicesRemainSeparate),
            ("Device status labels are human readable", DeviceStatusLabelsAreHumanReadable),
            ("Device Settings shortcuts use exact Windows URIs", DeviceSettingsShortcutsUseExactUris),
            ("Dark title bar uses the application palette", DarkTitleBarUsesAppPalette),
        ];

        if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
        {
            tests.Add(("Live display topology can be read", LiveDisplayTopologyCanBeRead));
            tests.Add(("Live Core Audio inventory can be read", LiveAudioInventoryCanBeRead));
            tests.Add(("Live audio notification watcher starts and stops", LiveAudioWatcherStartsAndStops));
            tests.Add(("Live connected-device inventory can be read", LiveDeviceInventoryCanBeRead));
        }

        int failed = 0;

        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine($"     {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task SingleInternalDisplayIsClassified()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
        ]);

        Equal(DisplayMode.PcScreenOnly, snapshot.CurrentMode);
        True(snapshot.IsReliable, "A reported active path should be reliable.");
        return Task.CompletedTask;
    }

    private static Task SingleExternalDisplayIsClassified()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.SecondScreenOnly, snapshot.CurrentMode);
        return Task.CompletedTask;
    }

    private static Task SharedSourceIsClassifiedAsDuplicate()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 0, 11, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.Duplicate, snapshot.CurrentMode);
        Equal(2, snapshot.ActiveDisplayCount);
        return Task.CompletedTask;
    }

    private static Task DistinctSourcesAreClassifiedAsExtend()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 1, 11, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        return Task.CompletedTask;
    }

    private static Task AvailableInactiveDisplayEnablesChoices()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 1, 11, DisplayOutputTechnology.Hdmi, false, true),
        ]);

        Equal(1, snapshot.ActiveDisplayCount);
        Equal(2, snapshot.AvailableDisplayCount);
        True(snapshot.SupportsMultipleDisplays, "The inactive available target should count.");
        return Task.CompletedTask;
    }

    private static Task MixedSourcesAreNotMislabeled()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 0, 11, DisplayOutputTechnology.Hdmi, true, true),
            DisplayPath(1, 1, 12, DisplayOutputTechnology.DisplayPortExternal, true, true),
        ]);

        True(snapshot.CurrentMode is null, "A mixed topology should not be called duplicate or extend.");
        Contains("Custom or mixed", snapshot.Status);
        return Task.CompletedTask;
    }

    private static Task NoActiveDisplayIsUnreliable()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.Hdmi, false, true),
        ]);

        True(!snapshot.IsReliable, "A snapshot without an active path is not reliable.");
        True(snapshot.CurrentMode is null, "An unreliable snapshot should not invent a mode.");
        return Task.CompletedTask;
    }

    private static Task AudioEndpointLabelsUseFriendlyName()
    {
        AudioEndpointInfo endpoint = new(
            "test-id",
            "USB Headset",
            AudioEndpointKind.Playback,
            IsConsoleDefault: true,
            IsMultimediaDefault: true,
            IsCommunicationsDefault: true);

        Equal("USB Headset", endpoint.DisplayLabel);
        return Task.CompletedTask;
    }

    private static Task AudioSessionVolumeIsFormatted()
    {
        AudioSessionInfo normal = new("1", "Player", "Speakers", 0.426f, false);
        AudioSessionInfo high = new("2", "Player", "Speakers", 1.5f, true);

        Equal("43%", normal.VolumeLabel);
        Equal("100%", high.VolumeLabel);
        Equal("Yes", high.MuteLabel);
        return Task.CompletedTask;
    }

    private static Task AudioEndpointRolesAreClear()
    {
        AudioEndpointInfo available = new(
            "1",
            "Speakers",
            AudioEndpointKind.Playback,
            false,
            false,
            false);
        AudioEndpointInfo normal = available with { IsConsoleDefault = true };
        AudioEndpointInfo calls = available with { IsCommunicationsDefault = true };
        AudioEndpointInfo both = normal with { IsCommunicationsDefault = true };

        Equal("Available audio device", available.ActiveRoleDescription);
        Equal("Default audio device", normal.ActiveRoleDescription);
        Equal("Calls device", calls.ActiveRoleDescription);
        Equal("Default audio and calls device", both.ActiveRoleDescription);
        return Task.CompletedTask;
    }

    private static async Task AudioRefreshNotificationsAreDebounced()
    {
        int callCount = 0;
        TaskCompletionSource actionCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using DebouncedActionScheduler scheduler = new(
            TimeSpan.FromMilliseconds(40),
            cancellationToken =>
            {
                Interlocked.Increment(ref callCount);
                actionCompleted.TrySetResult();
                return Task.CompletedTask;
            });

        scheduler.Schedule();
        scheduler.Schedule();
        scheduler.Schedule();

        await actionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(80);

        Equal(1, callCount);
    }

    private static async Task DisposedAudioDebounceCancelsWork()
    {
        int callCount = 0;
        DebouncedActionScheduler scheduler = new(
            TimeSpan.FromMilliseconds(80),
            cancellationToken =>
            {
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        scheduler.Schedule();
        scheduler.Dispose();
        scheduler.Schedule();

        await Task.Delay(140);
        Equal(0, callCount);
    }

    private static async Task SessionVolumeDelegatesRequestedLevel()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetVolumeAsync("session-1", 0.42f);

        True(result.Succeeded, result.Message);
        Equal("session-1", backend.SessionId);
        Equal(0.42f, backend.Volume);
        Equal(1, backend.VolumeCallCount);
    }

    private static async Task InvalidSessionVolumeIsRejected()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetVolumeAsync("session-1", 1.01f);

        True(!result.Succeeded, "Out-of-range volume should fail.");
        Equal(0, backend.VolumeCallCount);
    }

    private static async Task SessionMuteDelegatesRequestedState()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetMuteAsync("session-2", true);

        True(result.Succeeded, result.Message);
        Equal("session-2", backend.SessionId);
        True(backend.IsMuted, "Mute should be delegated as true.");
        Equal(1, backend.MuteCallCount);
    }

    private static async Task GeneralEndpointSelectionUpdatesBothRoles()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(setter, settings);

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-1",
            "USB Headset",
            AudioDefaultRoleSelection.General);

        True(result.Succeeded, result.Message);
        Equal(2, setter.Calls.Count);
        Equal(("endpoint-1", AudioRole.Console), setter.Calls[0]);
        Equal(("endpoint-1", AudioRole.Multimedia), setter.Calls[1]);
        Equal(0, settings.Uris.Count);
    }

    private static async Task CommunicationsEndpointSelectionUpdatesCalls()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        WindowsDefaultAudioEndpointService service = new(
            setter,
            new FakeWindowsSettingsLauncher());

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-2",
            "Desk microphone",
            AudioDefaultRoleSelection.Communications);

        True(result.Succeeded, result.Message);
        Equal(1, setter.Calls.Count);
        Equal(("endpoint-2", AudioRole.Communications), setter.Calls[0]);
    }

    private static async Task UnsupportedEndpointSelectionPreservesFallback()
    {
        FakeDefaultAudioEndpointSetter setter = new()
        {
            Exception = new InvalidOperationException("unsupported"),
        };
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(setter, settings);

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-3",
            "Speakers",
            AudioDefaultRoleSelection.General);
        AudioControlResult fallback = service.OpenSoundSettings();

        True(!result.Succeeded, "Unsupported policy calls should fail clearly.");
        True(fallback.Succeeded, fallback.Message);
        Equal(1, settings.Uris.Count);
        Equal("ms-settings:sound", settings.Uris[0]);
    }

    private static Task PhysicalDeviceInterfacesAreGrouped()
    {
        Guid containerId = Guid.NewGuid();

        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\COMPOSITE",
                containerId,
                "USB Composite Device",
                "USB",
                "USB"),
            PnpDevice(
                "USB\\KEYBOARD",
                containerId,
                "Ergo Keyboard",
                "Keyboard",
                "USB"),
        ]);

        Equal(1, devices.Count);
        Equal("Ergo Keyboard", devices[0].Name);
        Equal("Keyboard", devices[0].Category);
        Equal(DeviceTransport.Wired, devices[0].Transport);
        return Task.CompletedTask;
    }

    private static Task BluetoothTakesPrecedence()
    {
        Guid containerId = Guid.NewGuid();

        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\DONGLE",
                containerId,
                "USB Composite Device",
                "USB",
                "USB"),
            PnpDevice(
                "BTHENUM\\HEADSET",
                containerId,
                "Andrew Headphones",
                "AudioEndpoint",
                "BTHENUM"),
        ]);

        Equal(1, devices.Count);
        Equal("Andrew Headphones", devices[0].Name);
        Equal(DeviceTransport.Bluetooth, devices[0].Transport);
        Equal("Audio", devices[0].Category);
        return Task.CompletedTask;
    }

    private static Task GenericDeviceInfrastructureIsHidden()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\ROOT_HUB",
                Guid.NewGuid(),
                "USB Root Hub (USB 3.0)",
                "USB",
                "USB"),
            PnpDevice(
                "BTH\\ENUMERATOR",
                Guid.NewGuid(),
                "Microsoft Bluetooth Enumerator",
                "Bluetooth",
                "BTH"),
            PnpDevice(
                "BTHENUM\\SERVICE",
                Guid.NewGuid(),
                "Device Information Service",
                "Bluetooth",
                "BTHENUM"),
            PnpDevice(
                "USB\\KEYBOARD",
                Guid.NewGuid(),
                "HID Keyboard Device",
                "Keyboard",
                "USB"),
        ]);

        Equal(0, devices.Count);
        return Task.CompletedTask;
    }

    private static Task BluetoothProfilesCollapse()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "BTHENUM\\HEADSET",
                Guid.NewGuid(),
                "LE_WH-1000XM4",
                "Bluetooth",
                "BTHENUM"),
            PnpDevice(
                "BTHENUM\\AUDIO",
                Guid.NewGuid(),
                "WH-1000XM4 Hands-Free AG Audio",
                "AudioEndpoint",
                "BTHENUM"),
        ]);

        Equal(1, devices.Count);
        Equal("WH-1000XM4", devices[0].Name);
        return Task.CompletedTask;
    }

    private static Task UnrelatedUsbDevicesRemainSeparate()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\CAMERA",
                Guid.NewGuid(),
                "Conference Camera",
                "Camera",
                "USB"),
            PnpDevice(
                "USB\\DRIVE",
                Guid.NewGuid(),
                "Backup Drive",
                "DiskDrive",
                "USB"),
        ]);

        Equal(2, devices.Count);
        True(
            devices.Select(device => device.Name).Contains("Conference Camera"),
            "The camera should remain visible.");
        True(
            devices.Select(device => device.Name).Contains("Backup Drive"),
            "The drive should remain visible.");
        return Task.CompletedTask;
    }

    private static Task DeviceStatusLabelsAreHumanReadable()
    {
        ConnectedDeviceInfo connected = new(
            "1",
            "Device",
            "USB device",
            DeviceTransport.Wired,
            true,
            0);
        ConnectedDeviceInfo present = connected with { IsStarted = false };
        ConnectedDeviceInfo problem = connected with { ProblemCode = 22 };

        Equal("Connected", connected.StatusLabel);
        Equal("Present", present.StatusLabel);
        Equal("Needs attention", problem.StatusLabel);
        return Task.CompletedTask;
    }

    private static Task DeviceSettingsShortcutsUseExactUris()
    {
        FakeDeviceSettingsLauncher launcher = new();
        WindowsDeviceSettingsService service = new(launcher);

        DeviceActionResult bluetooth = service.OpenBluetoothSettings();
        DeviceActionResult devices = service.OpenConnectedDevicesSettings();

        True(bluetooth.Succeeded, bluetooth.Message);
        True(devices.Succeeded, devices.Message);
        Equal(2, launcher.Uris.Count);
        Equal("ms-settings:bluetooth", launcher.Uris[0]);
        Equal("ms-settings:connecteddevices", launcher.Uris[1]);
        return Task.CompletedTask;
    }

    private static Task DarkTitleBarUsesAppPalette()
    {
        FakeDwmAttributeSetter setter = new();
        IntPtr windowHandle = new(42);

        WindowsWindowTheme.ApplyDarkTitleBar(windowHandle, setter);

        Equal(4, setter.Calls.Count);
        Equal(
            (windowHandle, WindowsWindowTheme.UseImmersiveDarkMode, 1),
            setter.Calls[0]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.BorderColor,
                WindowsWindowTheme.ToColorReference(0x2A, 0x30, 0x3A)),
            setter.Calls[1]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.CaptionColor,
                WindowsWindowTheme.ToColorReference(0x10, 0x13, 0x18)),
            setter.Calls[2]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.TextColor,
                WindowsWindowTheme.ToColorReference(0xF3, 0xF5, 0xF7)),
            setter.Calls[3]);

        FakeDwmAttributeSetter zeroHandleSetter = new();
        WindowsWindowTheme.ApplyDarkTitleBar(IntPtr.Zero, zeroHandleSetter);
        Equal(0, zeroHandleSetter.Calls.Count);
        return Task.CompletedTask;
    }

    private static Task LiveDisplayTopologyCanBeRead()
    {
        DisplayTopologySnapshot snapshot = new WindowsDisplayTopologyService().GetSnapshot();

        True(snapshot.IsReliable, snapshot.Status);
        True(snapshot.ActiveDisplayCount > 0, "Windows should report an active display.");
        Console.WriteLine(
            $"     topology={snapshot.CurrentMode}, " +
            $"active={snapshot.ActiveDisplayCount}, " +
            $"available={snapshot.AvailableDisplayCount}");

        return Task.CompletedTask;
    }

    private static async Task LiveAudioInventoryCanBeRead()
    {
        AudioInventory inventory = await new WindowsAudioInventoryService().GetInventoryAsync();

        foreach (AudioSessionInfo session in inventory.Sessions)
        {
            True(
                session.Volume is >= 0 and <= 1,
                $"Session volume for {session.ApplicationName} was outside 0-1.");
        }

        Console.WriteLine(
            $"     playback={inventory.PlaybackEndpoints.Count}, " +
            $"recording={inventory.RecordingEndpoints.Count}, " +
            $"sessions={inventory.Sessions.Count}");
    }

    private static Task LiveAudioWatcherStartsAndStops()
    {
        using WindowsAudioChangeWatcher watcher = new();
        watcher.Start();
        return Task.CompletedTask;
    }

    private static async Task LiveDeviceInventoryCanBeRead()
    {
        DeviceInventory inventory =
            await new WindowsDeviceInventoryService().GetInventoryAsync();

        foreach (ConnectedDeviceInfo device in inventory.Devices)
        {
            True(!string.IsNullOrWhiteSpace(device.Id), "A device ID was empty.");
            True(!string.IsNullOrWhiteSpace(device.Name), "A device name was empty.");
        }

        Equal(
            inventory.Devices.Count,
            inventory.Devices.Select(device => device.Id).Distinct().Count());

        Console.WriteLine(
            $"     bluetooth={inventory.Devices.Count(device => device.Transport == DeviceTransport.Bluetooth)}, " +
            $"wired={inventory.Devices.Count(device => device.Transport == DeviceTransport.Wired)}");
    }

    private static PnpDeviceDescriptor PnpDevice(
        string instanceId,
        Guid? containerId,
        string name,
        string deviceClass,
        string enumeratorName,
        bool isStarted = true,
        uint problemCode = 0) =>
        new(
            instanceId,
            containerId,
            name,
            deviceClass,
            enumeratorName,
            $"{enumeratorName}\\TEST",
            isStarted,
            problemCode);

    private static DisplayPathDescriptor DisplayPath(
        long adapterId,
        uint sourceId,
        uint targetId,
        DisplayOutputTechnology technology,
        bool active,
        bool available) =>
        new(adapterId, sourceId, targetId, technology, active, available);

    private static async Task MapsMode(DisplayMode mode, string expectedArgument)
    {
        FakeDisplaySwitchProcess process = new();
        WindowsDisplayModeService service = CreateService(process);

        await service.ApplyAsync(mode);

        Equal(expectedArgument, process.LastArguments);
        True(process.CallCount == 1, "The process should run exactly once.");
        True(
            process.LastExecutablePath.EndsWith("DisplaySwitch.exe", StringComparison.OrdinalIgnoreCase),
            "The service should invoke DisplaySwitch.exe.");
    }

    private static async Task ZeroExitCodeSucceeds()
    {
        FakeDisplaySwitchProcess process = new() { ExitCode = 0 };
        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Extend);

        True(result.Succeeded, "Exit code zero should be successful.");
        Equal(0, result.ExitCode);
        Contains("Extend", result.Message);
    }

    private static async Task NonZeroExitCodeFails()
    {
        FakeDisplaySwitchProcess process = new() { ExitCode = 5 };
        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Duplicate);

        True(!result.Succeeded, "A non-zero exit code should fail.");
        Equal(5, result.ExitCode);
        Contains("exit code 5", result.Message);
    }

    private static async Task ProcessFailureBecomesResult()
    {
        FakeDisplaySwitchProcess process = new()
        {
            Exception = new InvalidOperationException("test failure"),
        };

        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Extend);

        True(!result.Succeeded, "A process exception should return a failure result.");
        Contains("test failure", result.Message);
    }

    private static async Task CancellationIsPreserved()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FakeDisplaySwitchProcess process = new()
        {
            Exception = new OperationCanceledException(cancellation.Token),
        };

        await ThrowsAsync<OperationCanceledException>(
            () => CreateService(process).ApplyAsync(DisplayMode.Extend, cancellation.Token));
    }

    private static async Task UnknownModeIsRejected()
    {
        FakeDisplaySwitchProcess process = new();

        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateService(process).ApplyAsync((DisplayMode)999));

        Equal(0, process.CallCount);
    }

    private static async Task HiddenProcessRunnerReturnsExitCode()
    {
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");

        HiddenProcessRunner process = new();
        int exitCode = await process.RunAsync(
            commandProcessor,
            "/d /c exit 7",
            CancellationToken.None);

        Equal(7, exitCode);
    }

    private static WindowsDisplayModeService CreateService(FakeDisplaySwitchProcess process) =>
        new(process, @"C:\Windows\System32\DisplaySwitch.exe");

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailureException(message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                $"Expected '{actual}' to contain '{expectedSubstring}'.");
        }
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new TestFailureException($"Expected {typeof(TException).Name}.");
    }

    private sealed class TestFailureException(string message) : Exception(message);

    private sealed class FakeDisplaySwitchProcess : IDisplaySwitchProcess
    {
        public int ExitCode { get; init; }

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public string LastExecutablePath { get; private set; } = string.Empty;

        public string LastArguments { get; private set; } = string.Empty;

        public Task<int> RunAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastExecutablePath = executablePath;
            LastArguments = arguments;

            return Exception is null
                ? Task.FromResult(ExitCode)
                : Task.FromException<int>(Exception);
        }
    }

    private sealed class FakeAudioSessionMutationBackend : IAudioSessionMutationBackend
    {
        public int VolumeCallCount { get; private set; }

        public int MuteCallCount { get; private set; }

        public string SessionId { get; private set; } = string.Empty;

        public float Volume { get; private set; }

        public bool IsMuted { get; private set; }

        public AudioControlResult SetVolume(
            string sessionId,
            float volume,
            CancellationToken cancellationToken)
        {
            VolumeCallCount++;
            SessionId = sessionId;
            Volume = volume;
            return AudioControlResult.Success("updated");
        }

        public AudioControlResult SetMute(
            string sessionId,
            bool isMuted,
            CancellationToken cancellationToken)
        {
            MuteCallCount++;
            SessionId = sessionId;
            IsMuted = isMuted;
            return AudioControlResult.Success("updated");
        }
    }

    private sealed class FakeDefaultAudioEndpointSetter : IDefaultAudioEndpointSetter
    {
        public List<(string EndpointId, AudioRole Role)> Calls { get; } = [];

        public Exception? Exception { get; init; }

        public void SetDefaultEndpoint(string endpointId, AudioRole role)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            Calls.Add((endpointId, role));
        }
    }

    private sealed class FakeWindowsSettingsLauncher : IWindowsSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string settingsUri) => Uris.Add(settingsUri);
    }

    private sealed class FakeDeviceSettingsLauncher : IDeviceSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string settingsUri) => Uris.Add(settingsUri);
    }

    private sealed class FakeDwmAttributeSetter : IDwmAttributeSetter
    {
        public List<(IntPtr Handle, int Attribute, int Value)> Calls { get; } = [];

        public void Set(IntPtr windowHandle, int attribute, int value) =>
            Calls.Add((windowHandle, attribute, value));
    }
}
