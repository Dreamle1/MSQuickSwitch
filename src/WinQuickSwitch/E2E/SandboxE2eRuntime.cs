using System.IO;
using System.Text.Json;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Taskbar;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.E2E;

internal static class SandboxE2eRuntime
{
    public static MainWindow CreateMainWindow()
    {
        SandboxE2eServices services = new();
        return new MainWindow(
            services, services, services, services, services, services,
            services, services, services, services, services,
            new JsonWidgetSettingsStore(), services, services);
    }
}

internal sealed class SandboxE2eServices :
    IDisplayModeService,
    IDisplayTopologyService,
    IAudioInventoryService,
    IAudioChangeWatcher,
    IAudioSessionControlService,
    IAudioEndpointControlService,
    IDefaultAudioEndpointService,
    IDeviceInventoryService,
    IDeviceSettingsService,
    IWirelessRadioService,
    ITaskbarService,
    IProfileStore,
    IStartupRegistrationService
{
    private readonly object _sync = new();
    private readonly string? _journalPath =
        Environment.GetEnvironmentVariable("WINQUICKSWITCH_E2E_JOURNAL");
    private readonly Dictionary<string, AudioEndpointControlSnapshot> _endpointState =
        new(StringComparer.Ordinal)
        {
            ["playback-speakers"] = new(0.72f, false),
            ["playback-headset"] = new(0.58f, false),
            ["recording-mic"] = new(0.81f, false),
            ["recording-webcam"] = new(0.65f, true),
        };
    private readonly Dictionary<string, AudioSessionInfo> _audioSessions =
        new(StringComparer.Ordinal)
        {
            ["session-browser"] = new(
                "session-browser", "Sample Browser", "Studio Speakers", 0.64f, false),
            ["session-music"] = new(
                "session-music", "Sample Music", "Studio Speakers", 0.42f, true),
        };
    private DisplayMode _displayMode = DisplayMode.Extend;
    private WirelessRadioSnapshot _radios = new(
        WirelessRadioState.On,
        WirelessRadioState.On);
    private TaskbarState _taskbarState = TaskbarState.Visible;
    private bool _startupEnabled;
    private ProfileCatalog _profiles = CreateProfiles();

    public event EventHandler? Changed;

    public bool CanRestoreExtendedLayout => true;

    public bool IsEnabled => _startupEnabled;

    public Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _displayMode = mode;
        Record("display.apply", new { Mode = mode.ToString() });
        return Task.FromResult(new DisplayModeResult(
            true,
            $"Simulated display mode changed to {mode.GetDisplayName()}."));
    }

    DisplayTopologySnapshot IDisplayTopologyService.GetSnapshot()
    {
        Record("display.refresh");
        return new DisplayTopologySnapshot(
            _displayMode,
            _displayMode is DisplayMode.Extend ? 3 : 1,
            3,
            true,
            "Simulated three-monitor topology is ready.");
    }

    Task<AudioInventory> IAudioInventoryService.GetInventoryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("audio.refresh");
        return Task.FromResult(new AudioInventory(
            [
                new AudioEndpointInfo("playback-speakers", "Studio Speakers", AudioEndpointKind.Playback, true, true, false),
                new AudioEndpointInfo("playback-headset", "Wireless Headset", AudioEndpointKind.Playback, false, false, true),
                new AudioEndpointInfo("playback-hdmi", "Conference Display Audio", AudioEndpointKind.Playback, false, false, false),
            ],
            [
                new AudioEndpointInfo("recording-mic", "Desk Microphone", AudioEndpointKind.Recording, true, true, true),
                new AudioEndpointInfo("recording-webcam", "Conference Camera Microphone", AudioEndpointKind.Recording, false, false, false),
            ],
            [
                _audioSessions["session-browser"],
                _audioSessions["session-music"],
            ],
            DateTimeOffset.Now));
    }

    public void Start() => Record("audio.watcher.start");

    public void Stop() => Record("audio.watcher.stop");

    public void Dispose() => Record("audio.watcher.dispose");

    public Task<AudioControlResult> SetVolumeAsync(
        string sessionId,
        float volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_audioSessions.TryGetValue(sessionId, out AudioSessionInfo? session))
        {
            _audioSessions[sessionId] = session with { Volume = volume };
        }
        Record("audio.session.volume", new { SessionId = sessionId, Volume = volume });
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(AudioControlResult.Success("Simulated app volume updated."));
    }

    public Task<AudioControlResult> SetMuteAsync(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_audioSessions.TryGetValue(sessionId, out AudioSessionInfo? session))
        {
            _audioSessions[sessionId] = session with { IsMuted = isMuted };
        }
        Record("audio.session.mute", new { SessionId = sessionId, IsMuted = isMuted });
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(AudioControlResult.Success("Simulated app mute updated."));
    }

    public Task<AudioEndpointControlSnapshot?> GetStateAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _endpointState.TryGetValue(endpointId, out AudioEndpointControlSnapshot? state);
        Record("audio.endpoint.state", new { EndpointId = endpointId });
        return Task.FromResult(state);
    }

    public Task<AudioControlResult> SetMasterVolumeAsync(
        string endpointId,
        string endpointName,
        float volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioEndpointControlSnapshot current = GetEndpointState(endpointId);
        _endpointState[endpointId] = current with { MasterVolume = volume };
        Record("audio.endpoint.volume", new { EndpointId = endpointId, EndpointName = endpointName, Volume = volume });
        return Task.FromResult(AudioControlResult.Success("Simulated endpoint volume updated."));
    }

    public Task<AudioControlResult> SetMuteAsync(
        string endpointId,
        string endpointName,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioEndpointControlSnapshot current = GetEndpointState(endpointId);
        _endpointState[endpointId] = current with { IsMuted = isMuted };
        Record("audio.endpoint.mute", new { EndpointId = endpointId, EndpointName = endpointName, IsMuted = isMuted });
        return Task.FromResult(AudioControlResult.Success("Simulated endpoint mute updated."));
    }

    public Task<AudioControlResult> SetDefaultAsync(
        string endpointId,
        string endpointName,
        AudioDefaultRoleSelection roleSelection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("audio.default", new { EndpointId = endpointId, EndpointName = endpointName, Role = roleSelection.ToString() });
        return Task.FromResult(AudioControlResult.Success("Simulated default endpoint updated."));
    }

    public AudioControlResult OpenSoundSettings()
    {
        Record("settings.sound");
        return AudioControlResult.Success("Simulated Sound Settings opened.");
    }

    public AudioControlResult OpenVolumeMixerSettings()
    {
        Record("settings.volumeMixer");
        return AudioControlResult.Success("Simulated Volume Mixer opened.");
    }

    Task<DeviceInventory> IDeviceInventoryService.GetInventoryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("devices.refresh");
        return Task.FromResult(new DeviceInventory(
            [
                new ConnectedDeviceInfo("bt-headset", "Travel Headset", "Audio", DeviceTransport.Bluetooth, true, 0),
                new ConnectedDeviceInfo("bt-keyboard", "Compact Keyboard", "Keyboard", DeviceTransport.Bluetooth, true, 0),
                new ConnectedDeviceInfo("bt-controller", "Game Controller", "Bluetooth device", DeviceTransport.Bluetooth, false, 0),
                new ConnectedDeviceInfo("usb-camera", "Conference Camera", "Camera", DeviceTransport.Wired, true, 0),
                new ConnectedDeviceInfo("usb-dock", "USB-C Dock", "System device", DeviceTransport.Wired, true, 0),
                new ConnectedDeviceInfo("usb-warning", "Sample Device", "Input device", DeviceTransport.Wired, false, 22),
            ],
            DateTimeOffset.Now));
    }

    public DeviceActionResult OpenBluetoothSettings() => OpenDeviceSettings("bluetooth");

    public DeviceActionResult OpenConnectedDevicesSettings() => OpenDeviceSettings("connectedDevices");

    public DeviceActionResult OpenWiFiSettings() => OpenDeviceSettings("wifi");

    public DeviceActionResult OpenNetworkSettings() => OpenDeviceSettings("network");

    public DeviceActionResult OpenAirplaneModeSettings() => OpenDeviceSettings("airplaneMode");

    public Task<WirelessRadioSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("radios.refresh");
        return Task.FromResult(_radios);
    }

    public Task<WirelessRadioResult> SetStateAsync(
        WirelessRadioKind kind,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WirelessRadioState state = enabled ? WirelessRadioState.On : WirelessRadioState.Off;
        _radios = kind == WirelessRadioKind.WiFi
            ? _radios with { WiFi = state }
            : _radios with { Bluetooth = state };
        Record("radios.set", new { Kind = kind.ToString(), Enabled = enabled });
        return Task.FromResult(WirelessRadioResult.Success($"Simulated {kind} state updated."));
    }

    TaskbarSnapshot ITaskbarService.GetSnapshot()
    {
        Record("taskbar.refresh");
        return new TaskbarSnapshot(_taskbarState);
    }

    public TaskbarActionResult SetAutoHide(bool enabled)
    {
        _taskbarState = enabled ? TaskbarState.AutoHidden : TaskbarState.Visible;
        Record("taskbar.autoHide", new { Enabled = enabled });
        return new TaskbarActionResult(true, "Simulated taskbar state updated.");
    }

    public TaskbarActionResult OpenTaskbarSettings() => OpenWindowsSettings("taskbar");

    public TaskbarActionResult OpenDisplaySettings() => OpenWindowsSettings("display");

    public TaskbarActionResult OpenNotificationSettings() => OpenWindowsSettings("notifications");

    public ProfileCatalog Load()
    {
        Record("profiles.load");
        return _profiles;
    }

    public bool TrySave(ProfileCatalog catalog, out string? errorMessage)
    {
        _profiles = catalog.Normalize();
        errorMessage = null;
        Record("profiles.save", new { Count = _profiles.Profiles.Count });
        return true;
    }

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        _startupEnabled = enabled;
        Record("startup.set", new { Enabled = enabled });
        return new StartupRegistrationResult(true, "Simulated startup state updated.");
    }

    private AudioEndpointControlSnapshot GetEndpointState(string endpointId) =>
        _endpointState.TryGetValue(endpointId, out AudioEndpointControlSnapshot? state)
            ? state
            : new AudioEndpointControlSnapshot(0.5f, false);

    private DeviceActionResult OpenDeviceSettings(string page)
    {
        Record("settings.devices", new { Page = page });
        return new DeviceActionResult(true, $"Simulated {page} Settings opened.");
    }

    private TaskbarActionResult OpenWindowsSettings(string page)
    {
        Record("settings.windows", new { Page = page });
        return new TaskbarActionResult(true, $"Simulated {page} Settings opened.");
    }

    private void Record(string action, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(_journalPath))
        {
            return;
        }

        lock (_sync)
        {
            string? directory = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string line = JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = action,
                Data = data,
            });
            File.AppendAllText(_journalPath, line + Environment.NewLine);
        }
    }

    private static ProfileCatalog CreateProfiles() => new(
        ProfileCatalog.CurrentSchemaVersion,
        [
            new ProfileDefinition(
                "workday", "Workday setup", true, null,
                DisplayMode.Extend,
                new ProfileEndpointTarget("playback-speakers", "Studio Speakers"),
                null,
                new ProfileEndpointTarget("recording-mic", "Desk Microphone"),
                null,
                TaskbarState.Visible,
                false,
                0.72f),
            new ProfileDefinition(
                "presentation", "Presentation setup", false, null,
                DisplayMode.Duplicate,
                new ProfileEndpointTarget("playback-hdmi", "Conference Display Audio"),
                null, null, null,
                TaskbarState.AutoHidden),
        ]);
}
