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
using WinQuickSwitch.Features.Taskbar;
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
    private readonly IWirelessRadioService _wirelessRadioService;
    private readonly ITaskbarService _taskbarService;
    private readonly IWidgetSettingsStore _widgetSettingsStore;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly WindowsGlobalHotkey _globalHotkey = new();
    private readonly WindowsWidgetPlacementService _placementService = new();
    private WindowsTrayIcon? _trayIcon;
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly DebouncedActionScheduler _deviceRefreshScheduler;
    private readonly SemaphoreSlim _audioRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _wirelessRadioGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource _visibleCancellation = new();
    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private DisplayMode? _currentDisplayMode;
    private WidgetPanel _activePanel = WidgetPanel.Display;
    private WidgetSettings _widgetSettings;
    private string? _audioWatcherStatusSuffix;
    private string? _hotkeyStatusSuffix;
    private bool _isAudioWatcherRunning;
    private bool _isExiting;
    private bool _deviceInventoryDirty = true;
    private bool _hasPositionedWidget;
    private bool _isApplyingSettingsUi;
    private bool _isApplyingPanelSelection;
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
        new WindowsDeviceSettingsService(),
        new WindowsWirelessRadioService(),
        new WindowsTaskbarService(),
        new JsonWidgetSettingsStore(),
        new WindowsStartupRegistrationService())
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
        IDeviceSettingsService deviceSettingsService,
        IWirelessRadioService wirelessRadioService,
        ITaskbarService taskbarService,
        IWidgetSettingsStore widgetSettingsStore,
        IStartupRegistrationService startupRegistrationService)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
        _audioSessionControlService = audioSessionControlService;
        _defaultAudioEndpointService = defaultAudioEndpointService;
        _deviceInventoryService = deviceInventoryService;
        _deviceSettingsService = deviceSettingsService;
        _wirelessRadioService = wirelessRadioService;
        _taskbarService = taskbarService;
        _widgetSettingsStore = widgetSettingsStore;
        _startupRegistrationService = startupRegistrationService;
        _widgetSettings = _widgetSettingsStore.Load();
        WidgetTheme.Apply(_widgetSettings.UseDarkTheme);
        InitializeComponent();
        UpdateOptionsControls();

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
        _windowHandle = new WindowInteropHelper(this).Handle;
        WidgetTheme.Apply(_widgetSettings.UseDarkTheme, _windowHandle);
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(_widgetSettings);

        try
        {
            _trayIcon = new WindowsTrayIcon(_windowHandle);
            _trayIcon.OpenRequested += TrayIcon_OpenRequested;
            _trayIcon.QuitRequested += TrayIcon_QuitRequested;
        }
        catch (Win32Exception)
        {
            // The widget remains usable if the shell cannot create a tray icon.
        }

        if (!result.Succeeded)
        {
            OptionsStatusText.Text =
                result.FirstFailure ?? "One or more shortcuts are unavailable.";
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
        if (_trayIcon is not null)
        {
            _trayIcon.OpenRequested -= TrayIcon_OpenRequested;
            _trayIcon.QuitRequested -= TrayIcon_QuitRequested;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        _visibleCancellation.Cancel();
        _visibleCancellation.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateOptionsControls();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || !_hasPositionedWidget || _isExiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            KeepWidgetInsideWorkArea,
            DispatcherPriority.Loaded);
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
            _globalHotkey.TryResolveAction(
                wordParameter.ToInt32(),
                out WidgetHotkeyAction action))
        {
            HandleGlobalHotkey(action);
            handled = true;
        }
        else if (_trayIcon?.HandleWindowMessage(message, longParameter) == true)
        {
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

    private async void HandleGlobalHotkey(WidgetHotkeyAction action)
    {
        switch (action)
        {
            case WidgetHotkeyAction.ToggleWidget:
                if (IsVisible)
                {
                    HideWidget();
                }
                else
                {
                    await ShowWidgetAsync();
                }

                break;
            case WidgetHotkeyAction.Display:
                await ShowWidgetAsync(WidgetPanel.Display);
                break;
            case WidgetHotkeyAction.Audio:
                await ShowWidgetAsync(WidgetPanel.Audio);
                break;
            case WidgetHotkeyAction.Devices:
                await ShowWidgetAsync(WidgetPanel.Devices);
                break;
            default:
                if (TryGetDisplayMode(action, out DisplayMode displayMode))
                {
                    await ApplyDisplayModeFromHotkeyAsync(displayMode);
                }
                else if (WidgetSettings.TryGetFavoriteSlot(action, out int slot))
                {
                    await ApplyFavoriteOutputFromHotkeyAsync(slot);
                }

                break;
        }
    }

    internal void ShowFromExternalRequest()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowFromExternalRequest);
            return;
        }

        _ = ShowWidgetAsync();
    }

    private async Task ShowWidgetAsync(WidgetPanel? requestedPanel = null)
    {
        if (_isExiting)
        {
            return;
        }

        _autoHideAllowedAfterUtc = DateTime.UtcNow.AddMilliseconds(500);
        ResetVisibleCancellation();
        Show();
        Activate();
        await ActivatePanelAsync(requestedPanel ?? _activePanel);
        UpdateLayout();

        if (!_hasPositionedWidget)
        {
            PositionWidgetNearPointer();
            _hasPositionedWidget = true;
        }
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

    private async Task ApplyDisplayModeFromHotkeyAsync(DisplayMode mode)
    {
        await ApplyDisplayModeAsync(mode);
    }

    private async Task ApplyFavoriteOutputFromHotkeyAsync(int slot)
    {
        FavoriteOutputSetting? favorite = _widgetSettings.GetFavorite(slot);

        if (favorite is null)
        {
            return;
        }

        AudioControlResult result;

        try
        {
            result = await _defaultAudioEndpointService.SetDefaultAsync(
                favorite.EndpointId,
                favorite.Name,
                AudioDefaultRoleSelection.General,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (!result.Succeeded)
        {
            await ShowWidgetAsync(WidgetPanel.Audio);
        }

        AudioStatusText.Text = result.Message;

        if (IsVisible && _activePanel == WidgetPanel.Audio)
        {
            _audioRefreshScheduler.Schedule();
        }
    }

    private static bool TryGetDisplayMode(
        WidgetHotkeyAction action,
        out DisplayMode mode)
    {
        mode = action switch
        {
            WidgetHotkeyAction.PcScreenOnly => DisplayMode.PcScreenOnly,
            WidgetHotkeyAction.Duplicate => DisplayMode.Duplicate,
            WidgetHotkeyAction.Extend => DisplayMode.Extend,
            WidgetHotkeyAction.SecondScreenOnly => DisplayMode.SecondScreenOnly,
            _ => default,
        };

        return action is
            WidgetHotkeyAction.PcScreenOnly or
            WidgetHotkeyAction.Duplicate or
            WidgetHotkeyAction.Extend or
            WidgetHotkeyAction.SecondScreenOnly;
    }

    private async void PanelTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized ||
            _isApplyingPanelSelection ||
            sender is not ToggleButton
            {
                IsChecked: true,
                Tag: string panelName,
            } ||
            !Enum.TryParse(panelName, out WidgetPanel panel))
        {
            return;
        }

        await ActivatePanelAsync(panel);
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
        OptionsPanel.Visibility =
            panel == WidgetPanel.Options ? Visibility.Visible : Visibility.Collapsed;

        _isApplyingPanelSelection = true;

        try
        {
            DisplayPanelTab.IsChecked = panel == WidgetPanel.Display;
            AudioPanelTab.IsChecked = panel == WidgetPanel.Audio;
            DevicesPanelTab.IsChecked = panel == WidgetPanel.Devices;
            OptionsPanelTab.IsChecked = panel == WidgetPanel.Options;
        }
        finally
        {
            _isApplyingPanelSelection = false;
        }

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
                Task inventoryRefresh = _deviceInventoryDirty
                    ? RefreshDeviceInventoryAsync()
                    : Task.CompletedTask;
                await Task.WhenAll(
                    RefreshWirelessRadioStateAsync(),
                    inventoryRefresh);

                ConnectedDevicesList.Focus();
                break;
            case WidgetPanel.Options:
                UpdateOptionsControls();
                RefreshTaskbarState();
                ToggleShortcutBox.Focus();
                break;
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

    private void KeepWidgetInsideWorkArea()
    {
        try
        {
            UpdateLayout();
            PresentationSource? source = PresentationSource.FromVisual(this);
            Matrix toDevice =
                source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Matrix fromDevice =
                source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point pixelOrigin = toDevice.Transform(new Point(Left, Top));
            Vector pixelSize = toDevice.Transform(
                new Vector(
                    ActualWidth > 0 ? ActualWidth : Width,
                    ActualHeight > 0 ? ActualHeight : 500));
            ScreenRectangle workArea =
                _placementService.GetWindowWorkArea(_windowHandle);
            ScreenPoint clamped = WidgetPlacementCalculator.ClampToWorkArea(
                new ScreenPoint(
                    (int)Math.Round(pixelOrigin.X),
                    (int)Math.Round(pixelOrigin.Y)),
                workArea,
                Math.Max(1, (int)Math.Ceiling(pixelSize.X)),
                Math.Max(1, (int)Math.Ceiling(pixelSize.Y)));
            Point dipPosition = fromDevice.Transform(
                new Point(clamped.X, clamped.Y));

            Left = dipPosition.X;
            Top = dipPosition.Y;
        }
        catch (Win32Exception)
        {
            // Retain the existing position if Windows cannot read the monitor.
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

    private void TrayIcon_OpenRequested(object? sender, EventArgs e)
    {
        _ = ShowWidgetAsync();
    }

    private void TrayIcon_QuitRequested(object? sender, EventArgs e)
    {
        QuitWidget();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible &&
            !_isExiting &&
            DateTime.UtcNow >= _autoHideAllowedAfterUtc)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (IsVisible && !IsActive && !_isExiting)
                    {
                        HideWidget();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        QuitWidget();
    }

    private void QuitWidget()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Close();
    }

    private HotkeyRegistrationResult ApplyHotkeyBindings(WidgetSettings settings)
    {
        HotkeyRegistrationResult result =
            _globalHotkey.ApplyBindings(_windowHandle, settings);

        if (result.Succeeded)
        {
            _hotkeyStatusSuffix = null;
        }
        else
        {
            _hotkeyStatusSuffix = " · shortcut unavailable; see Options";
        }

        return result;
    }

    private void UpdateOptionsControls()
    {
        _isApplyingSettingsUi = true;

        try
        {
            ToggleShortcutBox.Text = FormatShortcut(
                _widgetSettings.ToggleWidget);
            DisplayShortcutBox.Text = FormatShortcut(
                _widgetSettings.Display);
            AudioShortcutBox.Text = FormatShortcut(
                _widgetSettings.Audio);
            DevicesShortcutBox.Text = FormatShortcut(
                _widgetSettings.Devices);
            PcScreenOnlyShortcutBox.Text = FormatShortcut(
                _widgetSettings.PcScreenOnly);
            DuplicateShortcutBox.Text = FormatShortcut(
                _widgetSettings.Duplicate);
            ExtendShortcutBox.Text = FormatShortcut(
                _widgetSettings.Extend);
            SecondScreenOnlyShortcutBox.Text = FormatShortcut(
                _widgetSettings.SecondScreenOnly);

            List<FavoriteOutputShortcutOption> favoriteOptions = [];

            for (int slot = 0;
                 slot < WidgetSettings.MaximumFavoriteOutputs;
                 slot++)
            {
                FavoriteOutputSetting? favorite =
                    _widgetSettings.GetFavorite(slot);

                if (favorite is null)
                {
                    continue;
                }

                favoriteOptions.Add(new(
                    WidgetSettings.GetFavoriteAction(slot),
                    favorite.Name,
                    FormatShortcut(favorite.Shortcut),
                    $"{favorite.Name} shortcut"));
            }

            FavoriteOutputShortcutItems.ItemsSource = favoriteOptions;
            FavoriteOutputsEmptyText.Visibility = favoriteOptions.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            DarkThemeCheckBox.IsChecked = _widgetSettings.UseDarkTheme;
            StartWithWindowsCheckBox.IsChecked =
                _startupRegistrationService.IsEnabled;
            UpdateFavoriteOutputsSummary();
        }
        finally
        {
            _isApplyingSettingsUi = false;
        }
    }

    private void CaptureShortcut(
        TextBox shortcutBox,
        WidgetHotkeyAction action,
        KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or
            Key.RightCtrl or
            Key.LeftAlt or
            Key.RightAlt or
            Key.LeftShift or
            Key.RightShift or
            Key.LWin or
            Key.RWin)
        {
            e.Handled = true;
            OptionsStatusText.Text =
                "Keep holding the modifier and press a letter, number, or F-key.";
            return;
        }

        if (key is Key.Delete or Key.Back &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ApplyShortcutChange(action, null);
            return;
        }

        WidgetHotkeyModifiers modifiers =
            ConvertModifiers(Keyboard.Modifiers);
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);

        e.Handled = true;

        if (!WidgetShortcut.TryCreate(
            modifiers,
            virtualKey,
            out WidgetShortcut? shortcut))
        {
            OptionsStatusText.Text =
                "Use Ctrl, Alt, or Win with A-Z, 0-9, or F1-F12. Shift is optional.";
            return;
        }

        if (_widgetSettings.IsShortcutUsedByAnotherAction(action, shortcut!))
        {
            OptionsStatusText.Text =
                $"{shortcut!.DisplayText} is already assigned in WinQuickSwitch.";
            return;
        }

        shortcutBox.Text = shortcut!.DisplayText;
        ApplyShortcutChange(action, shortcut);
    }

    private void ApplyShortcutChange(
        WidgetHotkeyAction action,
        WidgetShortcut? shortcut)
    {
        WidgetSettings candidate =
            _widgetSettings.WithShortcut(action, shortcut);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);

        if (result.Failures.TryGetValue(action, out string? failure))
        {
            ApplyHotkeyBindings(_widgetSettings);
            UpdateOptionsControls();
            OptionsStatusText.Text = failure;
            return;
        }

        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            shortcut is null
                ? "Shortcut cleared."
                : $"{shortcut.DisplayText} is active.");
    }

    private void DarkThemeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        bool useDarkTheme = DarkThemeCheckBox.IsChecked == true;
        _widgetSettings = _widgetSettings with
        {
            UseDarkTheme = useDarkTheme,
        };
        WidgetTheme.Apply(useDarkTheme, _windowHandle);
        SaveSettings(useDarkTheme ? "Dark theme enabled." : "Light theme enabled.");
    }

    private void StartWithWindowsCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        bool enabled = StartWithWindowsCheckBox.IsChecked == true;
        StartupRegistrationResult result =
            _startupRegistrationService.SetEnabled(enabled);

        if (!result.Succeeded)
        {
            _isApplyingSettingsUi = true;

            try
            {
                StartWithWindowsCheckBox.IsChecked =
                    _startupRegistrationService.IsEnabled;
            }
            finally
            {
                _isApplyingSettingsUi = false;
            }
        }

        OptionsStatusText.Text = result.Message;
    }

    private void ShowTaskbar_Click(object sender, RoutedEventArgs e) =>
        SetTaskbarAutoHide(enabled: false);

    private void HideTaskbar_Click(object sender, RoutedEventArgs e) =>
        SetTaskbarAutoHide(enabled: true);

    private void OpenTaskbarSettings_Click(object sender, RoutedEventArgs e) =>
        ShowTaskbarActionResult(_taskbarService.OpenTaskbarSettings());

    private void OpenTaskbarDisplaySettings_Click(
        object sender,
        RoutedEventArgs e) =>
        ShowTaskbarActionResult(_taskbarService.OpenDisplaySettings());

    private void OpenTaskbarNotificationSettings_Click(
        object sender,
        RoutedEventArgs e) =>
        ShowTaskbarActionResult(_taskbarService.OpenNotificationSettings());

    private void SetTaskbarAutoHide(bool enabled)
    {
        TaskbarActionResult result = _taskbarService.SetAutoHide(enabled);
        ShowTaskbarActionResult(result);

        if (result.Succeeded)
        {
            RefreshTaskbarState();
        }
    }

    private void RefreshTaskbarState()
    {
        TaskbarSnapshot snapshot = _taskbarService.GetSnapshot();
        TaskbarStatusText.Text = snapshot.State switch
        {
            TaskbarState.Visible => "Taskbar is visible.",
            TaskbarState.AutoHidden => "Taskbar is set to auto-hide.",
            _ => "Taskbar state is unavailable.",
        };
    }

    private void ShowTaskbarActionResult(TaskbarActionResult result)
    {
        TaskbarStatusText.Text = result.Message;
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _widgetSettings = _widgetSettings.ResetShortcuts();
        HotkeyRegistrationResult result = ApplyHotkeyBindings(_widgetSettings);
        UpdateOptionsControls();
        SaveSettings(
            result.Succeeded
                ? "Shortcuts reset."
                : result.FirstFailure ?? "A default shortcut is unavailable.");
    }

    private void UnsetShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !TryGetHotkeyAction(button.Tag, out WidgetHotkeyAction action))
        {
            OptionsStatusText.Text = "That shortcut could not be cleared.";
            return;
        }

        ApplyShortcutChange(action, null);
    }

    private void SaveSettings(
        string successMessage,
        TextBlock? statusText = null)
    {
        TextBlock target = statusText ?? OptionsStatusText;

        if (_widgetSettingsStore.TrySave(
            _widgetSettings,
            out string? errorMessage))
        {
            target.Text = successMessage;
        }
        else
        {
            target.Text = errorMessage;
        }
    }

    private static string FormatShortcut(WidgetShortcut? shortcut) =>
        shortcut?.DisplayText ?? "Not set";

    private static bool TryGetHotkeyAction(
        object? tag,
        out WidgetHotkeyAction action)
    {
        if (tag is WidgetHotkeyAction typedAction)
        {
            action = typedAction;
            return true;
        }

        return Enum.TryParse(tag as string, out action);
    }

    private static WidgetHotkeyModifiers ConvertModifiers(
        ModifierKeys modifiers)
    {
        WidgetHotkeyModifiers result = WidgetHotkeyModifiers.None;

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= WidgetHotkeyModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= WidgetHotkeyModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= WidgetHotkeyModifiers.Shift;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            result |= WidgetHotkeyModifiers.Win;
        }

        return result;
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideWidget();
            return;
        }

        if (_activePanel == WidgetPanel.Options &&
            e.OriginalSource is TextBox
            {
            } shortcutBox &&
            TryGetHotkeyAction(shortcutBox.Tag, out WidgetHotkeyAction action))
        {
            CaptureShortcut(shortcutBox, action, e);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            WidgetPanel? panel = e.Key switch
            {
                Key.D1 or Key.NumPad1 => WidgetPanel.Display,
                Key.D2 or Key.NumPad2 => WidgetPanel.Audio,
                Key.D3 or Key.NumPad3 => WidgetPanel.Devices,
                Key.D4 or Key.NumPad4 => WidgetPanel.Options,
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

        await ApplyDisplayModeAsync(mode);
    }

    private async Task ApplyDisplayModeAsync(DisplayMode mode)
    {
        if (_currentDisplayMode == mode)
        {
            SetDisplayModeSelection(mode);
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

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        AudioControlResult result =
            _defaultAudioEndpointService.OpenSoundSettings();
        AudioStatusText.Text = result.Message;
    }

    private void OpenVolumeMixer_Click(object sender, RoutedEventArgs e)
    {
        AudioControlResult result =
            _defaultAudioEndpointService.OpenVolumeMixerSettings();
        AudioStatusText.Text = result.Message;
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) =>
        await Task.WhenAll(
            RefreshWirelessRadioStateAsync(),
            RefreshDeviceInventoryAsync());

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenBluetoothSettings();
        DeviceStatusText.Text = result.Message;
    }

    private void OpenWiFiSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenWiFiSettings();
        DeviceStatusText.Text = result.Message;
    }

    private void OpenNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenNetworkSettings();
        DeviceStatusText.Text = result.Message;
    }

    private void OpenAirplaneModeSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result =
            _deviceSettingsService.OpenAirplaneModeSettings();
        DeviceStatusText.Text = result.Message;
    }

    private void OpenConnectedDevicesSettings_Click(object sender, RoutedEventArgs e)
    {
        DeviceActionResult result = _deviceSettingsService.OpenConnectedDevicesSettings();
        DeviceStatusText.Text = result.Message;
    }

    private async void WirelessRadioToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string kindName } button ||
            !Enum.TryParse(kindName, out WirelessRadioKind kind))
        {
            WirelessStatusText.Text = "That wireless control is unavailable.";
            WirelessStatusText.Visibility = Visibility.Visible;
            return;
        }

        bool enabled = button.IsChecked == true;
        button.IsEnabled = false;

        try
        {
            WirelessRadioResult result = await _wirelessRadioService.SetStateAsync(
                kind,
                enabled,
                _lifetimeCancellation.Token);

            await Task.Delay(
                TimeSpan.FromMilliseconds(250),
                _lifetimeCancellation.Token);
            await RefreshWirelessRadioStateAsync();

            WirelessStatusText.Text = result.Message;
            WirelessStatusText.Visibility = result.Succeeded
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (!result.Succeeded)
            {
                OpenRadioSettings(kind);
            }
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            // The application is closing.
        }
    }

    private void OpenRadioSettings(WirelessRadioKind kind)
    {
        DeviceActionResult settingsResult = kind == WirelessRadioKind.WiFi
            ? _deviceSettingsService.OpenWiFiSettings()
            : _deviceSettingsService.OpenBluetoothSettings();

        if (!settingsResult.Succeeded)
        {
            WirelessStatusText.Text += $" {settingsResult.Message}";
        }
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

    private void AddFavoriteOutput_Click(object sender, RoutedEventArgs e)
    {
        if (PlaybackEndpointsList.SelectedItem is not AudioEndpointInfo endpoint)
        {
            AudioStatusText.Text = "Select an output device first.";
            return;
        }

        int existingSlot = _widgetSettings.FindFavoriteSlot(endpoint.Id);

        if (existingSlot >= 0)
        {
            WidgetSettings candidate =
                _widgetSettings.WithFavorite(existingSlot, null);
            HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
            _widgetSettings = candidate;
            UpdateOptionsControls();
            SaveSettings(
                result.Succeeded
                    ? $"{endpoint.Name} removed from favorites."
                    : result.FirstFailure ??
                        "Favorite removed; a shortcut is unavailable.",
                AudioStatusText);
            return;
        }

        int openSlot = _widgetSettings.FindOpenFavoriteSlot();

        if (openSlot < 0)
        {
            AudioStatusText.Text =
                $"You can save up to {WidgetSettings.MaximumFavoriteOutputs} favorites.";
            return;
        }

        _widgetSettings = _widgetSettings.WithFavorite(
            openSlot,
            new FavoriteOutputSetting(endpoint.Id, endpoint.Name, null));
        UpdateOptionsControls();
        SaveSettings(
            $"{endpoint.Name} added to favorites.",
            AudioStatusText);
    }

    private void UpdateFavoriteOutputsSummary()
    {
        List<string> names = [];

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteOutputs;
             slot++)
        {
            if (_widgetSettings.GetFavorite(slot) is FavoriteOutputSetting favorite)
            {
                names.Add(favorite.Name);
            }
        }

        FavoriteOutputsSummaryText.Text = names.Count == 0
            ? "Favorites: none"
            : $"Favorites: {string.Join(", ", names)}";
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

    private async Task RefreshWirelessRadioStateAsync()
    {
        bool entered = false;
        CancellationToken cancellationToken = _lifetimeCancellation.Token;

        try
        {
            entered = await _wirelessRadioGate.WaitAsync(
                TimeSpan.Zero,
                cancellationToken);

            if (!entered)
            {
                return;
            }

            WiFiRadioToggle.IsEnabled = false;
            BluetoothRadioToggle.IsEnabled = false;

            WirelessRadioSnapshot snapshot =
                await _wirelessRadioService.GetSnapshotAsync(cancellationToken);

            ApplyWirelessRadioState(
                WiFiRadioToggle,
                "Wi-Fi",
                snapshot.WiFi);
            ApplyWirelessRadioState(
                BluetoothRadioToggle,
                "Bluetooth",
                snapshot.Bluetooth);
            WirelessStatusText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch (Exception exception)
        {
            WiFiRadioToggle.IsChecked = false;
            BluetoothRadioToggle.IsChecked = false;
            WiFiRadioToggle.Content = "Wi-Fi unavailable";
            BluetoothRadioToggle.Content = "Bluetooth unavailable";
            WirelessStatusText.Text =
                $"Wireless state is unavailable: {exception.Message}";
            WirelessStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (entered)
            {
                _wirelessRadioGate.Release();
            }
        }
    }

    private static void ApplyWirelessRadioState(
        ToggleButton button,
        string name,
        WirelessRadioState state)
    {
        button.IsChecked = state == WirelessRadioState.On;
        button.IsEnabled = state is WirelessRadioState.On or WirelessRadioState.Off;
        button.Content = state switch
        {
            WirelessRadioState.On => $"{name} on",
            WirelessRadioState.Off => $"{name} off",
            WirelessRadioState.Disabled => $"{name} locked",
            _ => $"{name} unavailable",
        };
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

}

internal sealed record FavoriteOutputShortcutOption(
    WidgetHotkeyAction Action,
    string Name,
    string ShortcutText,
    string EditorName);
