using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows.Audio;
using WinQuickSwitch.Platform.Windows.Devices;
using WinQuickSwitch.Platform.Windows.Display;

namespace WinQuickSwitch;

public partial class MainWindow : Window
{
    private readonly IDisplayModeService _displayModeService;
    private readonly IDisplayTopologyService _displayTopologyService;
    private readonly IAudioInventoryService _audioInventoryService;
    private readonly IAudioChangeWatcher _audioChangeWatcher;
    private readonly IAudioSessionControlService _audioSessionControlService;
    private readonly IDefaultAudioEndpointService _defaultAudioEndpointService;
    private readonly IDeviceInventoryService _deviceInventoryService;
    private readonly IDeviceSettingsService _deviceSettingsService;
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly DebouncedActionScheduler _deviceRefreshScheduler;
    private readonly SemaphoreSlim _audioRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private HwndSource? _windowSource;
    private string? _audioWatcherStatusSuffix;
    private const int WmDeviceChange = 0x0219;

    public MainWindow() : this(
        new WindowsDisplayModeService(),
        new WindowsDisplayTopologyService(),
        new WindowsAudioInventoryService(),
        new WindowsAudioChangeWatcher(),
        new WindowsAudioSessionControlService(),
        new WindowsDefaultAudioEndpointService(),
        new WindowsDeviceInventoryService(),
        new WindowsDeviceSettingsService())
    {
    }

    internal MainWindow(
        IDisplayModeService displayModeService,
        IDisplayTopologyService displayTopologyService,
        IAudioInventoryService audioInventoryService,
        IAudioChangeWatcher audioChangeWatcher,
        IAudioSessionControlService audioSessionControlService,
        IDefaultAudioEndpointService defaultAudioEndpointService,
        IDeviceInventoryService deviceInventoryService,
        IDeviceSettingsService deviceSettingsService)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
        _audioSessionControlService = audioSessionControlService;
        _defaultAudioEndpointService = defaultAudioEndpointService;
        _deviceInventoryService = deviceInventoryService;
        _deviceSettingsService = deviceSettingsService;
        InitializeComponent();

        _audioRefreshScheduler = new DebouncedActionScheduler(
            TimeSpan.FromMilliseconds(350),
            RefreshAudioFromNotificationAsync);
        _deviceRefreshScheduler = new DebouncedActionScheduler(
            TimeSpan.FromMilliseconds(450),
            RefreshDevicesFromNotificationAsync);
        _audioChangeWatcher.Changed += AudioChangeWatcher_Changed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _audioRefreshScheduler.Dispose();
        _deviceRefreshScheduler.Dispose();
        _audioChangeWatcher.Changed -= AudioChangeWatcher_Changed;
        _audioChangeWatcher.Dispose();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDisplayTopology();

        try
        {
            _audioChangeWatcher.Start();
        }
        catch (Exception)
        {
            _audioWatcherStatusSuffix = " · live updates unavailable; use Refresh";
        }

        await Task.WhenAll(
            RefreshAudioInventoryAsync(),
            RefreshDeviceInventoryAsync());
    }

    private void AudioChangeWatcher_Changed(object? sender, EventArgs e) =>
        _audioRefreshScheduler.Schedule();

    private Task RefreshAudioFromNotificationAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            RefreshAudioInventoryAsync,
            DispatcherPriority.Background,
            cancellationToken).Task.Unwrap();

    private Task RefreshDevicesFromNotificationAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            RefreshDeviceInventoryAsync,
            DispatcherPriority.Background,
            cancellationToken).Task.Unwrap();

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == WmDeviceChange)
        {
            _deviceRefreshScheduler.Schedule();
        }

        return IntPtr.Zero;
    }

    private async void ApplyDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeName } ||
            !Enum.TryParse(modeName, out DisplayMode mode))
        {
            DisplayStatusText.Text = "That display mode is not recognized.";
            return;
        }

        if (RequiresConfirmation(mode) && !ConfirmDisplayChange(mode))
        {
            DisplayStatusText.Text = "Display change cancelled.";
            return;
        }

        DisplayModeButtons.IsEnabled = false;
        DisplayStatusText.Text = $"Switching to {mode.GetDisplayName()}...";

        try
        {
            DisplayModeResult result = await _displayModeService.ApplyAsync(
                mode,
                _lifetimeCancellation.Token);

            DisplayStatusText.Text = result.Message;

            if (result.Succeeded)
            {
                RefreshDisplayTopology();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                DisplayModeButtons.IsEnabled = true;
            }
        }
    }

    private void RefreshDisplayTopology()
    {
        DisplayTopologySnapshot snapshot = _displayTopologyService.GetSnapshot();
        DisplayStatusText.Text = snapshot.Status;

        bool multiDisplayChoiceAvailable =
            !snapshot.IsReliable || snapshot.SupportsMultipleDisplays;

        DuplicateDisplayButton.IsEnabled = multiDisplayChoiceAvailable;
        ExtendDisplayButton.IsEnabled = multiDisplayChoiceAvailable;
    }

    private async void RefreshAudio_Click(object sender, RoutedEventArgs e) =>
        await RefreshAudioInventoryAsync();

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) =>
        await RefreshDeviceInventoryAsync();

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenBluetoothSettings();
        DeviceStatusText.Text = result.Message;
    }

    private void OpenConnectedDevicesSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenConnectedDevicesSettings();
        DeviceStatusText.Text = result.Message;
    }

    private async void SessionVolumeSlider_Commit(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            await ApplySessionVolumeAsync(slider);
        }
    }

    private async void SessionVolumeSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is Slider slider &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            await ApplySessionVolumeAsync(slider);
        }
    }

    private async Task ApplySessionVolumeAsync(Slider slider)
    {
        if (slider.Tag is not string sessionId)
        {
            return;
        }

        slider.IsEnabled = false;

        try
        {
            AudioControlResult result = await _audioSessionControlService.SetVolumeAsync(
                sessionId,
                (float)(slider.Value / 100),
                _lifetimeCancellation.Token);

            AudioStatusText.Text = result.Message;
            _audioRefreshScheduler.Schedule();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                slider.IsEnabled = true;
            }
        }
    }

    private async void SessionMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string sessionId } checkBox)
        {
            return;
        }

        bool requestedMute = checkBox.IsChecked == true;
        checkBox.IsEnabled = false;

        try
        {
            AudioControlResult result = await _audioSessionControlService.SetMuteAsync(
                sessionId,
                requestedMute,
                _lifetimeCancellation.Token);

            AudioStatusText.Text = result.Message;

            if (!result.Succeeded && checkBox.DataContext is AudioSessionInfo session)
            {
                checkBox.IsChecked = session.IsMuted;
            }

            _audioRefreshScheduler.Schedule();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                checkBox.IsEnabled = true;
            }
        }
    }

    private async void SetDefaultAudioEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } button)
        {
            return;
        }

        ListBox endpointList = action.StartsWith(
            "Playback",
            StringComparison.Ordinal)
                ? PlaybackEndpointsList
                : RecordingEndpointsList;

        if (endpointList.SelectedItem is not AudioEndpointInfo endpoint)
        {
            AudioStatusText.Text = "Select an audio device first.";
            return;
        }

        AudioDefaultRoleSelection roleSelection = action.EndsWith(
            "Communications",
            StringComparison.Ordinal)
                ? AudioDefaultRoleSelection.Communications
                : AudioDefaultRoleSelection.General;

        button.IsEnabled = false;

        try
        {
            AudioControlResult result = await _defaultAudioEndpointService.SetDefaultAsync(
                endpoint.Id,
                endpoint.Name,
                roleSelection,
                _lifetimeCancellation.Token);

            if (!result.Succeeded)
            {
                AudioControlResult fallback = _defaultAudioEndpointService.OpenSoundSettings();
                AudioStatusText.Text = $"{result.Message} {fallback.Message}";
                return;
            }

            AudioStatusText.Text = result.Message;
            _audioRefreshScheduler.Schedule();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                button.IsEnabled = true;
            }
        }
    }

    private async Task RefreshAudioInventoryAsync()
    {
        bool entered = false;

        try
        {
            entered = await _audioRefreshGate.WaitAsync(
                TimeSpan.Zero,
                _lifetimeCancellation.Token);

            if (!entered)
            {
                return;
            }

            RefreshAudioButton.IsEnabled = false;
            AudioStatusText.Text = "Reading Windows audio state...";

            AudioInventory inventory = await _audioInventoryService.GetInventoryAsync(
                _lifetimeCancellation.Token);

            PlaybackEndpointsList.ItemsSource = inventory.PlaybackEndpoints;
            RecordingEndpointsList.ItemsSource = inventory.RecordingEndpoints;
            AudioSessionsList.ItemsSource = inventory.Sessions;

            SelectAudioEndpoint(PlaybackEndpointsList, inventory.PlaybackEndpoints);
            SelectAudioEndpoint(RecordingEndpointsList, inventory.RecordingEndpoints);

            AudioStatusText.Text =
                $"{inventory.PlaybackEndpoints.Count} out · " +
                $"{inventory.RecordingEndpoints.Count} in · " +
                $"{inventory.Sessions.Count} apps · " +
                $"{inventory.CapturedAt.ToLocalTime():t}" +
                _audioWatcherStatusSuffix;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            AudioStatusText.Text = $"Audio inventory is unavailable: {exception.Message}";
        }
        finally
        {
            if (entered)
            {
                _audioRefreshGate.Release();

                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    RefreshAudioButton.IsEnabled = true;
                }
            }
        }
    }

    private static void SelectAudioEndpoint(
        ListBox listBox,
        IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        string? selectedId = (listBox.SelectedItem as AudioEndpointInfo)?.Id;

        listBox.SelectedItem = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, selectedId, StringComparison.Ordinal))
            ?? endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)
            ?? endpoints.FirstOrDefault();
    }

    private async Task RefreshDeviceInventoryAsync()
    {
        bool entered = false;

        try
        {
            entered = await _deviceRefreshGate.WaitAsync(
                TimeSpan.Zero,
                _lifetimeCancellation.Token);

            if (!entered)
            {
                return;
            }

            RefreshDevicesButton.IsEnabled = false;
            DeviceStatusText.Text = "Reading connected devices...";

            DeviceInventory inventory = await _deviceInventoryService.GetInventoryAsync(
                _lifetimeCancellation.Token);

            ConnectedDevicesList.ItemsSource = inventory.Devices;

            int bluetoothCount = inventory.Devices.Count(
                device => device.Transport == DeviceTransport.Bluetooth);
            int wiredCount = inventory.Devices.Count - bluetoothCount;

            DeviceStatusText.Text =
                $"{bluetoothCount} Bluetooth · " +
                $"{wiredCount} wired · " +
                $"{inventory.CapturedAt.ToLocalTime():t}";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            DeviceStatusText.Text =
                $"Connected-device inventory is unavailable: {exception.Message}";
        }
        finally
        {
            if (entered)
            {
                _deviceRefreshGate.Release();

                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    RefreshDevicesButton.IsEnabled = true;
                }
            }
        }
    }

    private static bool RequiresConfirmation(DisplayMode mode) =>
        mode is DisplayMode.PcScreenOnly or DisplayMode.SecondScreenOnly;

    private bool ConfirmDisplayChange(DisplayMode mode)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"{mode.GetDisplayName()} can turn off the display you are currently using. Continue?",
            "Confirm display change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }
}
