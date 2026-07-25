using System.Windows;
using System.Windows.Controls;
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
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly SemaphoreSlim _audioRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private string? _audioWatcherStatusSuffix;

    public MainWindow() : this(
        new WindowsDisplayModeService(),
        new WindowsDisplayTopologyService(),
        new WindowsAudioInventoryService(),
        new WindowsAudioChangeWatcher())
    {
    }

    internal MainWindow(
        IDisplayModeService displayModeService,
        IDisplayTopologyService displayTopologyService,
        IAudioInventoryService audioInventoryService,
        IAudioChangeWatcher audioChangeWatcher)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
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
