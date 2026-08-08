using System.IO;
using System.Windows.Media;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Taskbar;
using WinQuickSwitch.Features.Widget;
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
            ("PC screen only uses the internal topology flag", () => MapsMode(DisplayMode.PcScreenOnly, WindowsDisplayModeService.SdcTopologyInternal)),
            ("Duplicate uses the clone topology flag", () => MapsMode(DisplayMode.Duplicate, WindowsDisplayModeService.SdcTopologyClone)),
            ("Extend uses the extend topology flag", () => MapsMode(DisplayMode.Extend, WindowsDisplayModeService.SdcTopologyExtend)),
            ("Second screen only uses the external topology flag", () => MapsMode(DisplayMode.SecondScreenOnly, WindowsDisplayModeService.SdcTopologyExternal)),
            ("Zero SetDisplayConfig result succeeds", ZeroDisplayConfigResultSucceeds),
            ("Non-zero SetDisplayConfig result fails", NonZeroDisplayConfigResultFails),
            ("Native display failure becomes a result", NativeDisplayFailureBecomesResult),
            ("Cancellation is preserved", CancellationIsPreserved),
            ("Unknown modes are rejected before the native call", UnknownModeIsRejected),
            ("Direct display call runs away from the UI thread", DisplayCallRunsOffCallingThread),
            ("Single internal display is classified as PC screen only", SingleInternalDisplayIsClassified),
            ("Single external display is classified as second screen only", SingleExternalDisplayIsClassified),
            ("Shared display source is classified as duplicate", SharedSourceIsClassifiedAsDuplicate),
            ("Distinct display sources are classified as extend", DistinctSourcesAreClassifiedAsExtend),
            ("Mixed display sources are not mislabeled", MixedSourcesAreNotMislabeled),
            ("Inactive available display enables multi-display choices", AvailableInactiveDisplayEnablesChoices),
            ("No active display produces an unreliable snapshot", NoActiveDisplayIsUnreliable),
            ("Display transition returns an already-settled mode immediately", DisplayTransitionReturnsImmediately),
            ("Display transition waits for Windows topology to settle", DisplayTransitionWaitsForExpectedMode),
            ("Display transition settle checks are bounded", DisplayTransitionChecksAreBounded),
            ("Audio endpoint labels use the friendly name", AudioEndpointLabelsUseFriendlyName),
            ("Audio endpoint roles have clear active descriptions", AudioEndpointRolesAreClear),
            ("Audio session volume is formatted and clamped", AudioSessionVolumeIsFormatted),
            ("Audio refresh notifications are debounced", AudioRefreshNotificationsAreDebounced),
            ("Disposed audio debounce cancels pending work", DisposedAudioDebounceCancelsWork),
            ("Session volume delegates the requested level", SessionVolumeDelegatesRequestedLevel),
            ("Invalid session volume is rejected", InvalidSessionVolumeIsRejected),
            ("Session mute delegates the requested state", SessionMuteDelegatesRequestedState),
            ("Endpoint volume controls validate and delegate", EndpointVolumeControlsValidateAndDelegate),
            ("General endpoint selection updates both default roles", GeneralEndpointSelectionUpdatesBothRoles),
            ("Communications endpoint selection updates only calls", CommunicationsEndpointSelectionUpdatesCalls),
            ("Both endpoint selection updates all roles", BothEndpointSelectionUpdatesAllRoles),
            ("Unsupported endpoint selection preserves Settings fallback", UnsupportedEndpointSelectionPreservesFallback),
            ("Volume mixer uses the exact Windows Settings URI", VolumeMixerUsesExactSettingsUri),
            ("Physical device interfaces are grouped by container", PhysicalDeviceInterfacesAreGrouped),
            ("Bluetooth takes precedence for mixed-interface devices", BluetoothTakesPrecedence),
            ("Bluetooth profile names collapse into one device", BluetoothProfilesCollapse),
            ("Generic device infrastructure is hidden", GenericDeviceInfrastructureIsHidden),
            ("Unrelated USB devices remain separate", UnrelatedUsbDevicesRemainSeparate),
            ("Device status labels are human readable", DeviceStatusLabelsAreHumanReadable),
            ("Device Settings shortcuts use exact Windows URIs", DeviceSettingsShortcutsUseExactUris),
            ("Taskbar state maps the Windows auto-hide flag", TaskbarStateMapsAutoHideFlag),
            ("Taskbar controls delegate the requested state", TaskbarControlsDelegateState),
            ("Taskbar Settings shortcuts use exact Windows URIs", TaskbarSettingsShortcutsUseExactUris),
            ("Taskbar API failures remain nonfatal", TaskbarApiFailuresRemainNonfatal),
            ("Wireless radio states remain independent", WirelessRadioStatesRemainIndependent),
            ("Wireless radio control delegates the desired state", WirelessRadioControlDelegatesState),
            ("Denied wireless radio control fails clearly", DeniedWirelessRadioControlFailsClearly),
            ("Wireless radio API failures remain nonfatal", WirelessRadioApiFailuresRemainNonfatal),
            ("Dark title bar uses the application palette", DarkTitleBarUsesAppPalette),
            ("Light title bar uses the application palette", LightTitleBarUsesAppPalette),
            ("Widget shortcuts validate and format supported chords", WidgetShortcutsValidateAndFormat),
            ("Widget settings remove duplicate shortcuts", WidgetSettingsRemoveDuplicates),
            ("Display and favorite shortcuts map to distinct actions", DisplayAndFavoriteShortcutsMapToActions),
            ("Favorite outputs normalize invalid and duplicate devices", FavoriteOutputsNormalizeDevices),
            ("Favorite inputs normalize and map actions", FavoriteInputsNormalizeAndMapActions),
            ("Reset shortcuts preserves favorite output devices", ResetShortcutsPreservesFavorites),
            ("Widget settings persist without dependencies", WidgetSettingsPersist),
            ("Older widget settings migrate with new shortcuts empty", OlderWidgetSettingsMigrate),
            ("Profile catalogs normalize duplicates and pinned profiles", ProfileCatalogNormalizes),
            ("Profile catalogs persist independently", ProfileCatalogPersists),
            ("Profile actions normalize unsupported values", ProfileActionsNormalize),
            ("Startup registration uses a quoted hidden-start command", StartupRegistrationUsesHiddenCommand),
            ("Startup registration recognizes only the current executable", StartupRegistrationRecognizesCurrentExecutable),
            ("Startup registration can be removed safely", StartupRegistrationCanBeRemoved),
            ("Startup registration failures remain nonfatal", StartupRegistrationFailuresRemainNonfatal),
            ("Global hotkeys register and resolve actions", GlobalHotkeysRegisterAndResolve),
            ("Global hotkeys register and resolve profiles", GlobalHotkeysRegisterAndResolveProfiles),
            ("Global hotkey conflicts stay isolated", GlobalHotkeyConflictsStayIsolated),
            ("Widget opens below and right of the pointer", WidgetOpensBelowPointer),
            ("Widget flips away from monitor edges", WidgetFlipsAtMonitorEdges),
            ("Widget stays inside negative-coordinate work areas", WidgetStaysInsideWorkArea),
            ("Growing widget clamps only when it crosses a work-area edge", GrowingWidgetClampsAtEdges),
        ];

        if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
        {
            tests.Add(("Live display topology can be read", LiveDisplayTopologyCanBeRead));
            tests.Add(("Live Core Audio inventory can be read", LiveAudioInventoryCanBeRead));
            tests.Add(("Live audio notification watcher starts and stops", LiveAudioWatcherStartsAndStops));
            tests.Add(("Live connected-device inventory can be read", LiveDeviceInventoryCanBeRead));
            tests.Add(("Live wireless radio states can be read", LiveWirelessRadioStatesCanBeRead));
            tests.Add(("Live taskbar state can be read", LiveTaskbarStateCanBeRead));
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

    private static async Task DisplayTransitionReturnsImmediately()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Extend));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                4,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        Equal(1, topology.CallCount);
        Equal(0, delayCount);
    }

    private static async Task DisplayTransitionWaitsForExpectedMode()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Duplicate),
            Topology(null),
            Topology(DisplayMode.Extend));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                5,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        Equal(3, topology.CallCount);
        Equal(2, delayCount);
    }

    private static async Task DisplayTransitionChecksAreBounded()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Duplicate));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                3,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Duplicate, snapshot.CurrentMode);
        Equal(3, topology.CallCount);
        Equal(2, delayCount);
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

    private static async Task EndpointVolumeControlsValidateAndDelegate()
    {
        FakeAudioEndpointVolumeBackend backend = new()
        {
            Snapshot = new AudioEndpointControlSnapshot(0.6f, false),
        };
        WindowsAudioEndpointControlService service = new(backend);

        AudioEndpointControlSnapshot? state = await service.GetStateAsync(
            "endpoint-1");
        Equal(0.6f, state?.MasterVolume);
        Equal(false, state?.IsMuted);

        AudioControlResult volumeResult =
            await service.SetMasterVolumeAsync(
                "endpoint-1",
                "USB headset",
                0.42f);
        AudioControlResult muteResult = await service.SetMuteAsync(
            "endpoint-1",
            "USB headset",
            true);

        True(volumeResult.Succeeded, volumeResult.Message);
        True(muteResult.Succeeded, muteResult.Message);
        Equal(0.42f, backend.Volume);
        True(backend.IsMuted, "The endpoint mute state should be delegated.");
        Equal(1, backend.VolumeCallCount);
        Equal(1, backend.MuteCallCount);

        AudioControlResult invalidResult =
            await service.SetMasterVolumeAsync(
                "endpoint-1",
                "USB headset",
                1.1f);
        True(!invalidResult.Succeeded, "Out-of-range master volume should fail.");
        Equal(1, backend.VolumeCallCount);
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

    private static async Task BothEndpointSelectionUpdatesAllRoles()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        WindowsDefaultAudioEndpointService service = new(
            setter,
            new FakeWindowsSettingsLauncher());

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-3",
            "Dock headset",
            AudioDefaultRoleSelection.Both);

        True(result.Succeeded, result.Message);
        Equal(3, setter.Calls.Count);
        Equal(("endpoint-3", AudioRole.Console), setter.Calls[0]);
        Equal(("endpoint-3", AudioRole.Multimedia), setter.Calls[1]);
        Equal(("endpoint-3", AudioRole.Communications), setter.Calls[2]);
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

    private static Task VolumeMixerUsesExactSettingsUri()
    {
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(
            new FakeDefaultAudioEndpointSetter(),
            settings);

        AudioControlResult result = service.OpenVolumeMixerSettings();

        True(result.Succeeded, result.Message);
        Equal(1, settings.Uris.Count);
        Equal("ms-settings:apps-volume", settings.Uris[0]);
        return Task.CompletedTask;
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
        DeviceActionResult wifi = service.OpenWiFiSettings();
        DeviceActionResult network = service.OpenNetworkSettings();
        DeviceActionResult airplane = service.OpenAirplaneModeSettings();

        True(bluetooth.Succeeded, bluetooth.Message);
        True(devices.Succeeded, devices.Message);
        True(wifi.Succeeded, wifi.Message);
        True(network.Succeeded, network.Message);
        True(airplane.Succeeded, airplane.Message);
        Equal(5, launcher.Uris.Count);
        Equal("ms-settings:bluetooth", launcher.Uris[0]);
        Equal("ms-settings:connecteddevices", launcher.Uris[1]);
        Equal("ms-settings:network-wifi", launcher.Uris[2]);
        Equal("ms-settings:network-status", launcher.Uris[3]);
        Equal("ms-settings:network-airplanemode", launcher.Uris[4]);
        return Task.CompletedTask;
    }

    private static Task TaskbarStateMapsAutoHideFlag()
    {
        FakeTaskbarBackend backend = new()
        {
            State = WindowsTaskbarService.AutoHideState,
        };
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        Equal(TaskbarState.AutoHidden, service.GetSnapshot().State);

        backend.State = 0;
        Equal(TaskbarState.Visible, service.GetSnapshot().State);
        return Task.CompletedTask;
    }

    private static Task TaskbarControlsDelegateState()
    {
        FakeTaskbarBackend backend = new();
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        TaskbarActionResult hide = service.SetAutoHide(true);
        TaskbarActionResult show = service.SetAutoHide(false);

        True(hide.Succeeded, hide.Message);
        True(show.Succeeded, show.Message);
        Equal(2, backend.SetCalls.Count);
        Equal(WindowsTaskbarService.AutoHideState, backend.SetCalls[0]);
        Equal(0u, backend.SetCalls[1]);
        return Task.CompletedTask;
    }

    private static Task TaskbarSettingsShortcutsUseExactUris()
    {
        FakeTaskbarSettingsLauncher launcher = new();
        WindowsTaskbarService service = new(
            new FakeTaskbarBackend(),
            launcher);

        TaskbarActionResult taskbar = service.OpenTaskbarSettings();
        TaskbarActionResult display = service.OpenDisplaySettings();
        TaskbarActionResult notifications = service.OpenNotificationSettings();

        True(taskbar.Succeeded, taskbar.Message);
        True(display.Succeeded, display.Message);
        True(notifications.Succeeded, notifications.Message);
        Equal(3, launcher.Uris.Count);
        Equal("ms-settings:taskbar", launcher.Uris[0]);
        Equal("ms-settings:display", launcher.Uris[1]);
        Equal("ms-settings:notifications", launcher.Uris[2]);
        return Task.CompletedTask;
    }

    private static Task TaskbarApiFailuresRemainNonfatal()
    {
        FakeTaskbarBackend backend = new()
        {
            Exception = new InvalidOperationException("shell unavailable"),
        };
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        TaskbarSnapshot snapshot = service.GetSnapshot();
        TaskbarActionResult result = service.SetAutoHide(true);

        Equal(TaskbarState.Unavailable, snapshot.State);
        True(!result.Succeeded, "Taskbar API failure should become a result.");
        True(
            result.Message.Contains("shell unavailable", StringComparison.Ordinal),
            result.Message);
        return Task.CompletedTask;
    }

    private static async Task WirelessRadioStatesRemainIndependent()
    {
        FakeWirelessRadioBackend backend = new()
        {
            Radios =
            [
                new WirelessRadioDevice(
                    WirelessRadioKind.WiFi,
                    WirelessRadioState.On),
                new WirelessRadioDevice(
                    WirelessRadioKind.Bluetooth,
                    WirelessRadioState.Off),
            ],
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioSnapshot snapshot = await service.GetSnapshotAsync();

        Equal(WirelessRadioState.On, snapshot.WiFi);
        Equal(WirelessRadioState.Off, snapshot.Bluetooth);
    }

    private static async Task WirelessRadioControlDelegatesState()
    {
        FakeWirelessRadioBackend backend = new();
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.Bluetooth,
            true);

        True(result.Succeeded, result.Message);
        Equal(1, backend.SetCalls.Count);
        Equal((WirelessRadioKind.Bluetooth, true), backend.SetCalls[0]);
    }

    private static async Task DeniedWirelessRadioControlFailsClearly()
    {
        FakeWirelessRadioBackend backend = new()
        {
            ControlStatus = WirelessRadioControlStatus.DeniedBySystem,
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.WiFi,
            false);

        True(!result.Succeeded, "Denied radio control should fail.");
        True(
            result.Message.Contains("policy", StringComparison.OrdinalIgnoreCase),
            result.Message);
    }

    private static async Task WirelessRadioApiFailuresRemainNonfatal()
    {
        FakeWirelessRadioBackend backend = new()
        {
            Exception = new UnauthorizedAccessException("radio access"),
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.Bluetooth,
            false);

        True(!result.Succeeded, "A radio API exception should become a failure result.");
        True(
            result.Message.Contains("radio access", StringComparison.Ordinal),
            result.Message);
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
        Equal(1, setter.RefreshCount);

        FakeDwmAttributeSetter zeroHandleSetter = new();
        WindowsWindowTheme.ApplyDarkTitleBar(IntPtr.Zero, zeroHandleSetter);
        Equal(0, zeroHandleSetter.Calls.Count);
        return Task.CompletedTask;
    }

    private static Task LightTitleBarUsesAppPalette()
    {
        FakeDwmAttributeSetter setter = new();
        IntPtr windowHandle = new(43);

        WindowsWindowTheme.Apply(
            windowHandle,
            false,
            Color.FromRgb(0xF4, 0xF6, 0xF8),
            Color.FromRgb(0xCC, 0xD3, 0xDC),
            Color.FromRgb(0x1B, 0x1F, 0x24),
            setter);

        Equal(4, setter.Calls.Count);
        Equal(
            (windowHandle, WindowsWindowTheme.UseImmersiveDarkMode, 0),
            setter.Calls[0]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.BorderColor,
                WindowsWindowTheme.ToColorReference(0xCC, 0xD3, 0xDC)),
            setter.Calls[1]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.CaptionColor,
                WindowsWindowTheme.ToColorReference(0xF4, 0xF6, 0xF8)),
            setter.Calls[2]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.TextColor,
                WindowsWindowTheme.ToColorReference(0x1B, 0x1F, 0x24)),
            setter.Calls[3]);
        Equal(1, setter.RefreshCount);
        return Task.CompletedTask;
    }

    private static Task WidgetShortcutsValidateAndFormat()
    {
        bool shiftOnly = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Shift,
            0x51,
            out WidgetShortcut? invalid);
        bool valid = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Win | WidgetHotkeyModifiers.Shift,
            0x51,
            out WidgetShortcut? shortcut);
        bool functionKey = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x7B,
            out WidgetShortcut? f12);

        True(!shiftOnly, "A Shift-only system shortcut should be rejected.");
        Equal<WidgetShortcut?>(null, invalid);
        True(valid, "Win + Shift + Q should be accepted.");
        Equal("Win + Shift + Q", shortcut!.DisplayText);
        True(functionKey, "Modified function keys should be accepted.");
        Equal("Ctrl + Alt + F12", f12!.DisplayText);
        return Task.CompletedTask;
    }

    private static Task WidgetSettingsRemoveDuplicates()
    {
        WidgetShortcut duplicate = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x41);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Display = duplicate,
            Audio = duplicate,
        };

        WidgetSettings normalized = settings.Normalize();

        Equal(duplicate, normalized.Display);
        Equal<WidgetShortcut?>(null, normalized.Audio);
        True(
            normalized.IsShortcutUsedByAnotherAction(
                WidgetHotkeyAction.Audio,
                duplicate),
            "The surviving Display shortcut should be reported as in use.");
        return Task.CompletedTask;
    }

    private static Task DisplayAndFavoriteShortcutsMapToActions()
    {
        WidgetShortcut displayShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x31);
        WidgetShortcut favoriteShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x32);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Extend = displayShortcut,
            FavoriteOutput2 = new(
                "endpoint-2",
                "Desk speakers",
                favoriteShortcut),
        };

        Equal(
            displayShortcut,
            settings.GetShortcut(WidgetHotkeyAction.Extend));
        Equal(
            favoriteShortcut,
            settings.GetShortcut(WidgetHotkeyAction.FavoriteOutput2));
        Equal(1, settings.FindFavoriteSlot("endpoint-2"));
        True(
            WidgetSettings.TryGetFavoriteSlot(
                WidgetHotkeyAction.FavoriteOutput2,
                out int slot),
            "The favorite action should resolve to a slot.");
        Equal(1, slot);
        return Task.CompletedTask;
    }

    private static Task FavoriteOutputsNormalizeDevices()
    {
        WidgetSettings settings = WidgetSettings.Default with
        {
            FavoriteOutput1 = new("endpoint-1", "Speakers", null),
            FavoriteOutput2 = new("endpoint-1", "Duplicate speakers", null),
            FavoriteOutput3 = new("", "Missing identifier", null),
            FavoriteOutput4 = new("endpoint-4", "", null),
        };

        WidgetSettings normalized = settings.Normalize();

        Equal("Speakers", normalized.FavoriteOutput1?.Name);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput2);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput3);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput4);
        Equal(1, normalized.FindOpenFavoriteSlot());
        return Task.CompletedTask;
    }

    private static Task FavoriteInputsNormalizeAndMapActions()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x49);
        WidgetSettings settings = WidgetSettings.Default with
        {
            FavoriteInput1 = new(
                "mic-1",
                "  Desk microphone  ",
                shortcut,
                FavoriteEndpointRole.Communications,
                new string('m', WidgetSettings.MaximumFavoriteAliasLength + 5)),
            FavoriteInput2 = new("mic-1", "Duplicate microphone", null),
            FavoriteInput3 = new("", "Missing identifier", null),
            FavoriteInput4 = new("mic-4", "", null),
        };

        WidgetSettings normalized = settings.Normalize();

        Equal("Desk microphone", normalized.FavoriteInput1?.Name);
        Equal(FavoriteEndpointRole.Communications, normalized.FavoriteInput1?.Role);
        Equal(
            WidgetSettings.MaximumFavoriteAliasLength,
            normalized.FavoriteInput1?.Alias?.Length);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput2);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput3);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput4);
        Equal(1, normalized.FindOpenInputFavoriteSlot());
        Equal(
            shortcut,
            normalized.GetShortcut(WidgetHotkeyAction.FavoriteInput1));
        True(
            WidgetSettings.TryGetInputFavoriteSlot(
                WidgetHotkeyAction.FavoriteInput1,
                out int slot),
            "The favorite-input action should resolve to a slot.");
        Equal(0, slot);
        return Task.CompletedTask;
    }

    private static Task ResetShortcutsPreservesFavorites()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x33);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Duplicate = shortcut,
            FavoriteOutput1 = new("endpoint-1", "Headphones", shortcut),
            FavoriteInput1 = new("mic-1", "Desk microphone", shortcut),
        };

        WidgetSettings reset = settings.ResetShortcuts();

        Equal(WidgetSettings.Default.ToggleWidget, reset.ToggleWidget);
        Equal<WidgetShortcut?>(null, reset.Duplicate);
        Equal("endpoint-1", reset.FavoriteOutput1?.EndpointId);
        Equal<WidgetShortcut?>(null, reset.FavoriteOutput1?.Shortcut);
        Equal("mic-1", reset.FavoriteInput1?.EndpointId);
        Equal<WidgetShortcut?>(null, reset.FavoriteInput1?.Shortcut);
        return Task.CompletedTask;
    }

    private static Task WidgetSettingsPersist()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            JsonWidgetSettingsStore store = new(settingsPath);
            WidgetSettings expected = WidgetSettings.Default with
            {
                UseDarkTheme = false,
                Audio = new WidgetShortcut(
                    WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                    0x41),
                Extend = new WidgetShortcut(
                    WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                    0x45),
                FavoriteOutput1 = new FavoriteOutputSetting(
                    "endpoint-1",
                    "Desk speakers",
                    new WidgetShortcut(
                        WidgetHotkeyModifiers.Control |
                        WidgetHotkeyModifiers.Alt,
                        0x31)),
            };

            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Settings save failed.");
            Equal(expected, store.Load());
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task OlderWidgetSettingsMigrate()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "UseDarkTheme": false,
                  "ToggleWidget": {
                    "Modifiers": 12,
                    "VirtualKey": 81
                  },
                  "Display": null,
                  "Audio": null,
                  "Devices": null
                }
                """);

            WidgetSettings loaded =
                new JsonWidgetSettingsStore(settingsPath).Load();

            True(!loaded.UseDarkTheme, "The existing theme should be retained.");
            Equal("Win + Shift + Q", loaded.ToggleWidget?.DisplayText);
            Equal<WidgetShortcut?>(null, loaded.Extend);
            Equal<FavoriteOutputSetting?>(null, loaded.FavoriteOutput1);
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task ProfileCatalogNormalizes()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x50);

        ProfileCatalog catalog = new ProfileCatalog(
            0,
            [
                new ProfileDefinition("work", "  Work  ", true, shortcut),
                new ProfileDefinition("work", "Duplicate", true, null),
                new ProfileDefinition("gaming", "Gaming", true, null),
                new ProfileDefinition("meeting", "Meeting", true, null),
                new ProfileDefinition("focus", "Focus", true, null),
                new ProfileDefinition("travel", "Travel", true, null),
                new ProfileDefinition("", "Invalid", true, null),
                new ProfileDefinition("missing-name", "", true, null),
            ]).Normalize();

        Equal(5, catalog.Profiles.Count);
        Equal(1, catalog.SchemaVersion);
        Equal("Work", catalog.Profiles[0].Name);
        True(catalog.Profiles[0].IsPinned, "The first four valid profiles should remain pinned.");
        True(!catalog.Profiles[4].IsPinned, "Additional pinned profiles should be unpinned.");
        Equal("Ctrl + Alt + P", catalog.Profiles[0].Shortcut?.DisplayText);
        return Task.CompletedTask;
    }

    private static Task ProfileCatalogPersists()
    {
        string profilesPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Profiles.{Guid.NewGuid():N}.json");

        try
        {
            ProfileDefinition expected = new(
                "work",
                "Work",
                true,
                null,
                DisplayMode.Extend,
                new ProfileEndpointTarget("speakers", "Desk speakers"),
                null,
                new ProfileEndpointTarget("microphone", "USB microphone"),
                null,
                TaskbarState.Visible,
                true,
                0.42f);
            JsonProfileStore store = new(profilesPath);

            True(
                store.TrySave(new ProfileCatalog(0, [expected]), out string? saveError),
                saveError ?? "Profile save failed.");

            ProfileDefinition loaded = store.Load().Profiles.Single();
            Equal(expected.Id, loaded.Id);
            Equal(expected.Name, loaded.Name);
            Equal(expected.DisplayMode, loaded.DisplayMode);
            Equal(expected.PlaybackGeneral, loaded.PlaybackGeneral);
            Equal(expected.RecordingGeneral, loaded.RecordingGeneral);
            Equal(expected.TaskbarState, loaded.TaskbarState);
            Equal(expected.MicrophoneMuted, loaded.MicrophoneMuted);
            Equal(expected.MasterVolume, loaded.MasterVolume);
        }
        finally
        {
            File.Delete(profilesPath);
            File.Delete(profilesPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task ProfileActionsNormalize()
    {
        ProfileDefinition normalized = new ProfileDefinition(
            "profile",
            "Profile",
            false,
            null,
            null,
            new ProfileEndpointTarget("", "Missing endpoint"),
            new ProfileEndpointTarget("endpoint", "  Valid endpoint  "),
            null,
            null,
            TaskbarState.Unavailable,
            null,
            1.5f).Normalize();

        Equal<ProfileEndpointTarget?>(null, normalized.PlaybackGeneral);
        Equal(
            new ProfileEndpointTarget("endpoint", "Valid endpoint"),
            normalized.PlaybackCommunications);
        Equal<TaskbarState?>(null, normalized.TaskbarState);
        Equal<float?>(null, normalized.MasterVolume);
        True(normalized.HasActions, "A valid endpoint action should remain present.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationUsesHiddenCommand()
    {
        FakeStartupRegistry registry = new();
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Program Files\WinQuickSwitch\WinQuickSwitch.exe");

        StartupRegistrationResult result = service.SetEnabled(true);

        True(result.Succeeded, result.Message);
        Equal(
            "\"C:\\Program Files\\WinQuickSwitch\\WinQuickSwitch.exe\" --startup",
            registry.Value);
        True(service.IsEnabled, "The newly written startup command should be active.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationRecognizesCurrentExecutable()
    {
        FakeStartupRegistry registry = new()
        {
            Value = "\"C:\\Old\\WinQuickSwitch.exe\" --startup",
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\New\WinQuickSwitch.exe");

        True(
            !service.IsEnabled,
            "A stale registration for a moved executable should not appear enabled.");

        registry.Value = "\"c:\\new\\winquickswitch.exe\" --startup";
        True(
            service.IsEnabled,
            "Windows paths should be matched without case sensitivity.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationCanBeRemoved()
    {
        FakeStartupRegistry registry = new()
        {
            Value = "\"C:\\Apps\\WinQuickSwitch.exe\" --startup",
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Apps\WinQuickSwitch.exe");

        StartupRegistrationResult result = service.SetEnabled(false);

        True(result.Succeeded, result.Message);
        Equal<string?>(null, registry.Value);
        Equal(1, registry.DeleteCount);
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationFailuresRemainNonfatal()
    {
        FakeStartupRegistry registry = new()
        {
            Exception = new IOException("blocked"),
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Apps\WinQuickSwitch.exe");

        True(
            !service.IsEnabled,
            "An unreadable startup entry should be treated as disabled.");

        StartupRegistrationResult result = service.SetEnabled(true);

        True(!result.Succeeded, "A registry failure should be reported.");
        Contains("blocked", result.Message);
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRegisterAndResolve()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x41),
            Extend = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x45),
            FavoriteOutput1 = new FavoriteOutputSetting(
                "endpoint-1",
                "Desk speakers",
                new WidgetShortcut(
                    WidgetHotkeyModifiers.Control |
                    WidgetHotkeyModifiers.Alt,
                    0x31)),
            FavoriteInput1 = new FavoriteInputSetting(
                "mic-1",
                "Desk microphone",
                new WidgetShortcut(
                    WidgetHotkeyModifiers.Control |
                    WidgetHotkeyModifiers.Alt,
                    0x32)),
        };

        HotkeyRegistrationResult result =
            hotkeys.ApplyBindings(new IntPtr(44), settings);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(5, native.Registrations.Count);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleWidget),
                out WidgetHotkeyAction toggleAction),
            "The toggle shortcut was not registered.");
        Equal(WidgetHotkeyAction.ToggleWidget, toggleAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Audio),
                out WidgetHotkeyAction audioAction),
            "The Audio shortcut was not registered.");
        Equal(WidgetHotkeyAction.Audio, audioAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Extend),
                out WidgetHotkeyAction extendAction),
            "The Extend shortcut was not registered.");
        Equal(WidgetHotkeyAction.Extend, extendAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.FavoriteOutput1),
                out WidgetHotkeyAction favoriteAction),
            "The favorite-output shortcut was not registered.");
        Equal(WidgetHotkeyAction.FavoriteOutput1, favoriteAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.FavoriteInput1),
                out WidgetHotkeyAction favoriteInputAction),
            "The favorite-microphone shortcut was not registered.");
        Equal(WidgetHotkeyAction.FavoriteInput1, favoriteInputAction);
        True(
            (native.Registrations[0].Modifiers & 0x4000) != 0,
            "MOD_NOREPEAT was not applied.");
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeyConflictsStayIsolated()
    {
        FakeGlobalHotkeyNative native = new()
        {
            FailingVirtualKey = 0x41,
        };
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x41),
        };

        HotkeyRegistrationResult result =
            hotkeys.ApplyBindings(new IntPtr(45), settings);

        True(!result.Succeeded, "The simulated conflict should be reported.");
        True(
            result.Failures.ContainsKey(WidgetHotkeyAction.Audio),
            "The Audio conflict should remain associated with Audio.");
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleWidget),
                out WidgetHotkeyAction action),
            "A conflicting Audio binding should not disable the toggle binding.");
        Equal(WidgetHotkeyAction.ToggleWidget, action);
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRegisterAndResolveProfiles()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        ProfileHotkeyBinding binding = new(
            "profile-work",
            new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x57));

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(46),
            WidgetSettings.Default,
            [binding]);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(2, native.Registrations.Count);
        True(
            hotkeys.TryResolveProfileId(
                native.Registrations[1].Id,
                out string profileId),
            "The profile shortcut was not registered.");
        Equal("profile-work", profileId);
        return Task.CompletedTask;
    }

    private static Task WidgetOpensBelowPointer()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(400, 300),
            new ScreenRectangle(0, 0, 1920, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        Equal(new ScreenPoint(412, 312), position);
        return Task.CompletedTask;
    }

    private static Task WidgetFlipsAtMonitorEdges()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(1850, 1000),
            new ScreenRectangle(0, 0, 1920, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        Equal(new ScreenPoint(1338, 488), position);
        return Task.CompletedTask;
    }

    private static Task WidgetStaysInsideWorkArea()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(-1800, 900),
            new ScreenRectangle(-1920, 0, 0, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        True(position.X >= -1920, "The widget crossed the work area's left edge.");
        True(position.X + 500 <= 0, "The widget crossed the work area's right edge.");
        True(position.Y >= 0, "The widget crossed the work area's top edge.");
        True(position.Y + 500 <= 1040, "The widget crossed the work area's bottom edge.");
        return Task.CompletedTask;
    }

    private static Task GrowingWidgetClampsAtEdges()
    {
        ScreenRectangle workArea = new(0, 0, 1920, 1040);

        ScreenPoint unchanged = WidgetPlacementCalculator.ClampToWorkArea(
            new ScreenPoint(700, 200),
            workArea,
            widgetWidth: 500,
            widgetHeight: 700);
        ScreenPoint clamped = WidgetPlacementCalculator.ClampToWorkArea(
            new ScreenPoint(1500, 700),
            workArea,
            widgetWidth: 500,
            widgetHeight: 700);

        Equal(new ScreenPoint(700, 200), unchanged);
        Equal(new ScreenPoint(1420, 340), clamped);
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
        watcher.Stop();
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

    private static async Task LiveWirelessRadioStatesCanBeRead()
    {
        WirelessRadioSnapshot snapshot =
            await new WindowsWirelessRadioService().GetSnapshotAsync();

        Console.WriteLine(
            $"     wifi={snapshot.WiFi}, bluetooth={snapshot.Bluetooth}");
    }

    private static Task LiveTaskbarStateCanBeRead()
    {
        TaskbarSnapshot snapshot = new WindowsTaskbarService().GetSnapshot();

        True(
            snapshot.State is TaskbarState.Visible or
                TaskbarState.AutoHidden or
                TaskbarState.Unavailable,
            "Windows returned an unknown taskbar state.");
        Console.WriteLine($"     taskbar={snapshot.State}");
        return Task.CompletedTask;
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

    private static DisplayTopologySnapshot Topology(DisplayMode? mode) =>
        new(
            mode,
            ActiveDisplayCount: mode is DisplayMode.Duplicate or DisplayMode.Extend ? 2 : 1,
            AvailableDisplayCount: 2,
            IsReliable: true,
            Status: mode?.GetDisplayName() ?? "Updating");

    private static async Task MapsMode(DisplayMode mode, uint topologyFlag)
    {
        FakeDisplayConfigNative native = new();
        WindowsDisplayModeService service = new(native);

        await service.ApplyAsync(mode);

        Equal(WindowsDisplayModeService.SdcApply | topologyFlag, native.LastFlags);
        Equal(1, native.CallCount);
    }

    private static async Task ZeroDisplayConfigResultSucceeds()
    {
        FakeDisplayConfigNative native = new() { ResultCode = 0 };
        DisplayModeResult result =
            await new WindowsDisplayModeService(native).ApplyAsync(
                DisplayMode.Extend);

        True(result.Succeeded, "Result code zero should be successful.");
        Equal(0, result.ErrorCode);
        Contains("Extend", result.Message);
    }

    private static async Task NonZeroDisplayConfigResultFails()
    {
        FakeDisplayConfigNative native = new() { ResultCode = 5 };
        DisplayModeResult result =
            await new WindowsDisplayModeService(native).ApplyAsync(
                DisplayMode.Duplicate);

        True(!result.Succeeded, "A non-zero result code should fail.");
        Equal(5, result.ErrorCode);
        Contains("error code 5", result.Message);
    }

    private static async Task NativeDisplayFailureBecomesResult()
    {
        FakeDisplayConfigNative native = new()
        {
            Exception = new InvalidOperationException("test failure"),
        };

        DisplayModeResult result =
            await new WindowsDisplayModeService(native).ApplyAsync(
                DisplayMode.Extend);

        True(!result.Succeeded, "A native exception should return a failure result.");
        Contains("test failure", result.Message);
    }

    private static async Task CancellationIsPreserved()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FakeDisplayConfigNative native = new();

        await ThrowsAsync<OperationCanceledException>(
            () => new WindowsDisplayModeService(native).ApplyAsync(
                DisplayMode.Extend,
                cancellation.Token));
        Equal(0, native.CallCount);
    }

    private static async Task UnknownModeIsRejected()
    {
        FakeDisplayConfigNative native = new();

        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => new WindowsDisplayModeService(native).ApplyAsync(
                (DisplayMode)999));

        Equal(0, native.CallCount);
    }

    private static async Task DisplayCallRunsOffCallingThread()
    {
        int callingThreadId = Environment.CurrentManagedThreadId;
        FakeDisplayConfigNative native = new();

        await new WindowsDisplayModeService(native).ApplyAsync(
            DisplayMode.Extend);

        True(
            native.ApplyThreadId != callingThreadId,
            "SetDisplayConfig should not block the calling UI thread.");
    }

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

    private sealed class FakeDisplayConfigNative : IDisplayConfigNative
    {
        public int ResultCode { get; init; }

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public uint LastFlags { get; private set; }

        public int ApplyThreadId { get; private set; }

        public int Apply(uint flags)
        {
            CallCount++;
            LastFlags = flags;
            ApplyThreadId = Environment.CurrentManagedThreadId;

            if (Exception is not null)
            {
                throw Exception;
            }

            return ResultCode;
        }
    }

    private sealed class FakeDisplayTopologyService(
        params DisplayTopologySnapshot[] snapshots) : IDisplayTopologyService
    {
        private readonly Queue<DisplayTopologySnapshot> _snapshots = new(snapshots);
        private DisplayTopologySnapshot _last = snapshots.First();

        public int CallCount { get; private set; }

        public DisplayTopologySnapshot GetSnapshot()
        {
            CallCount++;

            if (_snapshots.TryDequeue(out DisplayTopologySnapshot? snapshot))
            {
                _last = snapshot;
            }

            return _last;
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

    private sealed class FakeAudioEndpointVolumeBackend : IAudioEndpointVolumeBackend
    {
        public int VolumeCallCount { get; private set; }

        public int MuteCallCount { get; private set; }

        public float Volume { get; private set; }

        public bool IsMuted { get; private set; }

        public AudioEndpointControlSnapshot? Snapshot { get; init; }

        public AudioEndpointControlSnapshot? GetState(
            string endpointId,
            CancellationToken cancellationToken) => Snapshot;

        public AudioControlResult SetMasterVolume(
            string endpointId,
            string endpointName,
            float volume,
            CancellationToken cancellationToken)
        {
            VolumeCallCount++;
            Volume = volume;
            return AudioControlResult.Success("updated");
        }

        public AudioControlResult SetMute(
            string endpointId,
            string endpointName,
            bool isMuted,
            CancellationToken cancellationToken)
        {
            MuteCallCount++;
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

    private sealed class FakeTaskbarBackend : ITaskbarBackend
    {
        public uint State { get; set; }

        public Exception? Exception { get; init; }

        public bool SetResult { get; init; } = true;

        public List<uint> SetCalls { get; } = [];

        public uint GetState()
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return State;
        }

        public bool SetState(uint state)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            SetCalls.Add(state);
            State = state;
            return SetResult;
        }
    }

    private sealed class FakeTaskbarSettingsLauncher : ITaskbarSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string uri) => Uris.Add(uri);
    }

    private sealed class FakeWirelessRadioBackend : IWirelessRadioBackend
    {
        public IReadOnlyList<WirelessRadioDevice> Radios { get; init; } = [];

        public WirelessRadioControlStatus ControlStatus { get; init; } =
            WirelessRadioControlStatus.Allowed;

        public Exception? Exception { get; init; }

        public List<(WirelessRadioKind Kind, bool Enabled)> SetCalls { get; } = [];

        public Task<IReadOnlyList<WirelessRadioDevice>> GetRadiosAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Radios);
        }

        public Task<WirelessRadioControlStatus> SetStateAsync(
            WirelessRadioKind kind,
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Exception is not null)
            {
                throw Exception;
            }

            SetCalls.Add((kind, enabled));
            return Task.FromResult(ControlStatus);
        }
    }

    private sealed class FakeDwmAttributeSetter : IDwmAttributeSetter
    {
        public List<(IntPtr Handle, int Attribute, int Value)> Calls { get; } = [];

        public int RefreshCount { get; private set; }

        public void Set(IntPtr windowHandle, int attribute, int value) =>
            Calls.Add((windowHandle, attribute, value));

        public void RefreshFrame(IntPtr windowHandle) => RefreshCount++;
    }

    private sealed class FakeGlobalHotkeyNative : IGlobalHotkeyNative
    {
        public int? FailingVirtualKey { get; init; }

        public List<(IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)> Registrations
        {
            get;
        } = [];

        public List<(IntPtr Handle, int Id)> Unregistrations { get; } = [];

        public bool Register(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey,
            out int errorCode)
        {
            if (virtualKey == FailingVirtualKey)
            {
                errorCode = 1409;
                return false;
            }

            Registrations.Add((windowHandle, id, modifiers, virtualKey));
            errorCode = 0;
            return true;
        }

        public void Unregister(IntPtr windowHandle, int id) =>
            Unregistrations.Add((windowHandle, id));
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public string? Value { get; set; }

        public Exception? Exception { get; init; }

        public int DeleteCount { get; private set; }

        public string? ReadValue()
        {
            ThrowIfConfigured();
            return Value;
        }

        public void WriteValue(string command)
        {
            ThrowIfConfigured();
            Value = command;
        }

        public void DeleteValue()
        {
            ThrowIfConfigured();
            DeleteCount++;
            Value = null;
        }

        private void ThrowIfConfigured()
        {
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }
}
