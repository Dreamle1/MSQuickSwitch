using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows.Audio;
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
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly SemaphoreSlim _audioRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private string? _audioWatcherStatusSuffix;

    public MainWindow() : this(
        new WindowsDisplayModeService(),
        new WindowsDisplayTopologyService(),
        new WindowsAudioInventoryService(),
        new WindowsAudioChangeWatcher(),
        new WindowsAudioSessionControlService(),
        new WindowsDefaultAudioEndpointService())
    {
    }

    internal MainWindow(
        IDisplayModeService displayModeService,
        IDisplayTopologyService displayTopologyService,
        IAudioInventoryService audioInventoryService,
        IAudioChangeWatcher audioChangeWatcher,
        IAudioSessionControlService audioSessionControlService,
        IDefaultAudioEndpointService defaultAudioEndpointService)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
        _audioSessionControlService = audioSessionControlService;
        _defaultAudioEndpointService = defaultAudioEndpointService;
        InitializeComponent();

        _audioRefreshScheduler = new DebouncedActionScheduler(
            TimeSpan.FromMilliseconds(350),
            RefreshAudioFromNotificationAsync);
        _audioChangeWatcher.Changed += AudioChangeWatcher_Changed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _audioRefreshScheduler.Dispose();
        _audioChangeWatcher.Changed -= AudioChangeWatcher_Changed;
        _audioChangeWatcher.Dispose();
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

        await RefreshAudioInventoryAsync();
    }

    private void AudioChangeWatcher_Changed(object? sender, EventArgs e) =>
        _audioRefreshScheduler.Schedule();

    private Task RefreshAudioFromNotificationAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            RefreshAudioInventoryAsync,
            DispatcherPriority.Background,
            cancellationToken).Task.Unwrap();

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
                $"{inventory.PlaybackEndpoints.Count} playback · " +
                $"{inventory.RecordingEndpoints.Count} recording · " +
                $"{inventory.Sessions.Count} active sessions · " +
                $"updated {inventory.CapturedAt.ToLocalTime():t}" +
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
