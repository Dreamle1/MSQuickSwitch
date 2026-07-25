using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows;
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
    private readonly WindowsGlobalHotkey _globalHotkey = new();
    private readonly WindowsWidgetPlacementService _placementService = new();
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly DebouncedActionScheduler _deviceRefreshScheduler;
    private readonly SemaphoreSlim _audioRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource _visibleCancellation = new();
    private HwndSource? _windowSource;
    private DisplayMode? _currentDisplayMode;
    private WidgetPanel _activePanel = WidgetPanel.Display;
    private string? _audioWatcherStatusSuffix;
    private string? _hotkeyStatusSuffix;
    private bool _isAudioWatcherRunning;
    private bool _isExiting;
    private bool _suppressAutoHide;
    private bool _deviceInventoryDirty = true;
    private bool _isLoaded;
    private DateTime _autoHideAllowedAfterUtc;
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
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        WindowsWindowTheme.ApplyDarkTitleBar(windowHandle);
        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowMessageHook);

        try
        {
            _globalHotkey.Register(windowHandle);
        }
        catch (Win32Exception exception)
        {
            _hotkeyStatusSuffix = $" · {exception.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isExiting = true;
        _audioRefreshScheduler.Dispose();
        _deviceRefreshScheduler.Dispose();
        _audioChangeWatcher.Changed -= AudioChangeWatcher_Changed;
        _audioChangeWatcher.Dispose();
        _globalHotkey.Dispose();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        _visibleCancellation.Cancel();
        _visibleCancellation.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        await ActivatePanelAsync(_activePanel);
        PositionWidgetNearPointer();
    }

    private void AudioChangeWatcher_Changed(object? sender, EventArgs e)
    {
        if (IsVisible && _activePanel == WidgetPanel.Audio)
        {
            _audioRefreshScheduler.Schedule();
        }
    }

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
        if (message == WindowsGlobalHotkey.WmHotkey &&
            wordParameter.ToInt32() == WindowsGlobalHotkey.ToggleWidgetId)
        {
            ToggleWidget();
            handled = true;
        }
        else if (message == WmDeviceChange)
        {
            _deviceInventoryDirty = true;

            if (IsVisible && _activePanel == WidgetPanel.Devices)
            {
                _deviceRefreshScheduler.Schedule();
            }
        }

        return IntPtr.Zero;
    }

    internal void ShowFromExternalRequest()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowFromExternalRequest);
            return;
        }

        ShowWidget();
    }

    private void ToggleWidget()
    {
        if (IsVisible)
        {
            HideWidget();
        }
        else
        {
            ShowWidget();
        }
    }

    private async void ShowWidget()
    {
        if (_isExiting)
        {
            return;
        }

        _autoHideAllowedAfterUtc = DateTime.UtcNow.AddMilliseconds(500);
        ResetVisibleCancellation();
        Show();
        Activate();
        await ActivatePanelAsync(_activePanel);
        UpdateLayout();
        PositionWidgetNearPointer();
    }

    private void HideWidget()
    {
        if (!IsVisible || _isExiting)
        {
            return;
        }

        StopAudioWatcher();
        _visibleCancellation.Cancel();
        _deviceInventoryDirty = true;
        Hide();
    }

    private async void PanelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string panelName } &&
            Enum.TryParse(panelName, out WidgetPanel panel))
        {
            await ActivatePanelAsync(panel);
        }
    }

    private async Task ActivatePanelAsync(WidgetPanel panel)
    {
        _activePanel = panel;
        DisplayPanel.Visibility =
            panel == WidgetPanel.Display ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility =
            panel == WidgetPanel.Audio ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility =
            panel == WidgetPanel.Devices ? Visibility.Visible : Visibility.Collapsed;

        DisplayPanelTab.IsChecked = panel == WidgetPanel.Display;
        AudioPanelTab.IsChecked = panel == WidgetPanel.Audio;
        DevicesPanelTab.IsChecked = panel == WidgetPanel.Devices;

        if (panel != WidgetPanel.Audio)
        {
            StopAudioWatcher();
        }

        switch (panel)
        {
            case WidgetPanel.Display:
                RefreshDisplayTopology();
                break;
            case WidgetPanel.Audio:
                StartAudioWatcher();
                await RefreshAudioInventoryAsync();
                PlaybackEndpointsList.Focus();
                break;
            case WidgetPanel.Devices:
                if (_deviceInventoryDirty)
                {
                    await RefreshDeviceInventoryAsync();
                }

                ConnectedDevicesList.Focus();
                break;
        }

        if (_isLoaded && IsVisible)
        {
            await Dispatcher.InvokeAsync(
                PositionWidgetNearPointer,
                DispatcherPriority.Loaded);
        }
    }

    private void StartAudioWatcher()
    {
        if (_isAudioWatcherRunning)
        {
            return;
        }

        try
        {
            _audioChangeWatcher.Start();
            _isAudioWatcherRunning = true;
            _audioWatcherStatusSuffix = null;
        }
        catch (Exception)
        {
            _audioWatcherStatusSuffix = " · live updates unavailable";
        }
    }

    private void StopAudioWatcher()
    {
        if (!_isAudioWatcherRunning)
        {
            return;
        }

        try
        {
            _audioChangeWatcher.Stop();
        }
        catch (TimeoutException)
        {
            // Final disposal performs one more bounded cleanup attempt.
        }
        finally
        {
            _isAudioWatcherRunning = false;
        }
    }

    private void ResetVisibleCancellation()
    {
        if (!_visibleCancellation.IsCancellationRequested)
        {
            return;
        }

        _visibleCancellation.Dispose();
        _visibleCancellation = new CancellationTokenSource();
    }

    private void PositionWidgetNearPointer()
    {
        try
        {
            UpdateLayout();
            (ScreenPoint pointer, ScreenRectangle workArea) =
                _placementService.GetPointerWorkArea();
            PresentationSource? source = PresentationSource.FromVisual(this);
            Matrix toDevice =
                source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Matrix fromDevice =
                source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Vector pixelSize = toDevice.Transform(
                new Vector(
                    ActualWidth > 0 ? ActualWidth : Width,
                    ActualHeight > 0 ? ActualHeight : 500));
            ScreenPoint pixelPosition = WidgetPlacementCalculator.PlaceNearPointer(
                pointer,
                workArea,
                Math.Max(1, (int)Math.Ceiling(pixelSize.X)),
                Math.Max(1, (int)Math.Ceiling(pixelSize.Y)));
            Point dipPosition = fromDevice.Transform(
                new Point(pixelPosition.X, pixelPosition.Y));

            Left = dipPosition.X;
            Top = dipPosition.Y;
        }
        catch (Win32Exception)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        HideWidget();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_suppressAutoHide &&
            IsVisible &&
            !_isExiting &&
            DateTime.UtcNow >= _autoHideAllowedAfterUtc)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (!_suppressAutoHide && IsVisible && !IsActive && !_isExiting)
                    {
                        HideWidget();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideWidget();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            WidgetPanel? panel = e.Key switch
            {
                Key.D1 or Key.NumPad1 => WidgetPanel.Display,
                Key.D2 or Key.NumPad2 => WidgetPanel.Audio,
                Key.D3 or Key.NumPad3 => WidgetPanel.Devices,
                _ => null,
            };

            if (panel is WidgetPanel selectedPanel)
            {
                e.Handled = true;
                await ActivatePanelAsync(selectedPanel);
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (_activePanel == WidgetPanel.Display)
        {
            DisplayMode? mode = e.Key switch
            {
                Key.D1 or Key.NumPad1 => DisplayMode.PcScreenOnly,
                Key.D2 or Key.NumPad2 => DisplayMode.Duplicate,
                Key.D3 or Key.NumPad3 => DisplayMode.Extend,
                Key.D4 or Key.NumPad4 => DisplayMode.SecondScreenOnly,
                _ => null,
            };

            if (mode is DisplayMode selectedMode)
            {
                ToggleButton? button =
                    DisplayModeButtons.Children
                        .OfType<ToggleButton>()
                        .FirstOrDefault(candidate =>
                            candidate.Tag is string modeName &&
                            Enum.TryParse(modeName, out DisplayMode candidateMode) &&
                            candidateMode == selectedMode);

                if (button is not null)
                {
                    e.Handled = true;
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            }
        }
        else if (_activePanel == WidgetPanel.Audio)
        {
            switch (e.Key)
            {
                case Key.O:
                    PlaybackEndpointsList.Focus();
                    e.Handled = true;
                    break;
                case Key.I:
                    RecordingEndpointsList.Focus();
                    e.Handled = true;
                    break;
                case Key.A:
                    AudioSessionsList.Focus();
                    e.Handled = true;
                    break;
            }
        }
    }

    private async void ApplyDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string modeName } ||
            !Enum.TryParse(modeName, out DisplayMode mode))
        {
            DisplayStatusText.Text = "That display mode is not recognized.";
            return;
        }

        if (_currentDisplayMode == mode)
        {
            SetDisplayModeSelection(mode);
            return;
        }

        if (RequiresConfirmation(mode) && !ConfirmDisplayChange(mode))
        {
            DisplayStatusText.Text = "Display change cancelled.";
            SetDisplayModeSelection(_currentDisplayMode);
            return;
        }

        DisplayMode? previousMode = _currentDisplayMode;
        SetDisplayModeSelection(mode);
        DisplayModeButtons.IsEnabled = false;
        DisplayStatusText.Text = $"Switching to {mode.GetDisplayName()}...";

        try
        {
            DisplayModeResult result = await _displayModeService.ApplyAsync(
                mode,
                _lifetimeCancellation.Token);

            if (result.Succeeded)
            {
                DisplayTopologySnapshot snapshot =
                    await DisplayTransitionMonitor.WaitForModeAsync(
                        _displayTopologyService,
                        mode,
                        TimeSpan.FromMilliseconds(150),
                        maximumAttempts: 18,
                        _lifetimeCancellation.Token);

                ApplyDisplayTopologySnapshot(snapshot);

                if (snapshot.CurrentMode != mode)
                {
                    DisplayStatusText.Text =
                        $"{result.Message} Windows is still updating.";
                    SetDisplayModeSelection(mode);
                }
            }
            else
            {
                DisplayStatusText.Text = result.Message;
                SetDisplayModeSelection(previousMode);
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
        ApplyDisplayTopologySnapshot(snapshot);
    }

    private void ApplyDisplayTopologySnapshot(DisplayTopologySnapshot snapshot)
    {
        _currentDisplayMode = snapshot.CurrentMode;
        DisplayStatusText.Text = snapshot.Status + _hotkeyStatusSuffix;
        SetDisplayModeSelection(snapshot.CurrentMode);

        bool multiDisplayChoiceAvailable =
            !snapshot.IsReliable || snapshot.SupportsMultipleDisplays;

        DuplicateDisplayButton.IsEnabled = multiDisplayChoiceAvailable;
        ExtendDisplayButton.IsEnabled = multiDisplayChoiceAvailable;
    }

    private void SetDisplayModeSelection(DisplayMode? selectedMode)
    {
        foreach (ToggleButton button in
                 DisplayModeButtons.Children.OfType<ToggleButton>())
        {
            button.IsChecked =
                button.Tag is string modeName &&
                Enum.TryParse(modeName, out DisplayMode buttonMode) &&
                buttonMode == selectedMode;
        }
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
        CancellationToken cancellationToken = _visibleCancellation.Token;

        try
        {
            entered = await _audioRefreshGate.WaitAsync(
                TimeSpan.Zero,
                cancellationToken);

            if (!entered)
            {
                return;
            }

            RefreshAudioButton.IsEnabled = false;
            AudioStatusText.Text = "Reading Windows audio state...";

            AudioInventory inventory = await _audioInventoryService.GetInventoryAsync(
                cancellationToken);

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
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            _lifetimeCancellation.IsCancellationRequested)
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

                if (IsVisible && !cancellationToken.IsCancellationRequested)
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
        CancellationToken cancellationToken = _visibleCancellation.Token;

        try
        {
            entered = await _deviceRefreshGate.WaitAsync(
                TimeSpan.Zero,
                cancellationToken);

            if (!entered)
            {
                return;
            }

            RefreshDevicesButton.IsEnabled = false;
            DeviceStatusText.Text = "Reading connected devices...";

            DeviceInventory inventory = await _deviceInventoryService.GetInventoryAsync(
                cancellationToken);

            ConnectedDevicesList.ItemsSource = inventory.Devices;
            _deviceInventoryDirty = false;

            int bluetoothCount = inventory.Devices.Count(
                device => device.Transport == DeviceTransport.Bluetooth);
            int wiredCount = inventory.Devices.Count - bluetoothCount;

            DeviceStatusText.Text =
                $"{bluetoothCount} Bluetooth · " +
                $"{wiredCount} wired · " +
                $"{inventory.CapturedAt.ToLocalTime():t}";
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            _lifetimeCancellation.IsCancellationRequested)
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

                if (IsVisible && !cancellationToken.IsCancellationRequested)
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
        _suppressAutoHide = true;

        try
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
        finally
        {
            _suppressAutoHide = false;
        }
    }
}
