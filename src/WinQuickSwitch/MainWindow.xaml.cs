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
using WinQuickSwitch.Features.Profiles;
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
    private readonly IAudioEndpointControlService _audioEndpointControlService;
    private readonly IDefaultAudioEndpointService _defaultAudioEndpointService;
    private readonly IDeviceInventoryService _deviceInventoryService;
    private readonly IDeviceSettingsService _deviceSettingsService;
    private readonly IWirelessRadioService _wirelessRadioService;
    private readonly ITaskbarService _taskbarService;
    private readonly IWidgetSettingsStore _widgetSettingsStore;
    private readonly IProfileStore _profileStore;
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
    private ProfileCatalog _profileCatalog;
    private string? _editingProfileId;
    private ProfileDefinition? _profileUndo;
    private bool _isApplyingProfile;
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
        new WindowsAudioEndpointControlService(),
        new WindowsDefaultAudioEndpointService(),
        new WindowsDeviceInventoryService(),
        new WindowsDeviceSettingsService(),
        new WindowsWirelessRadioService(),
        new WindowsTaskbarService(),
        new JsonWidgetSettingsStore(),
        new JsonProfileStore(),
        new WindowsStartupRegistrationService())
    {
    }

    internal MainWindow(
        IDisplayModeService displayModeService,
        IDisplayTopologyService displayTopologyService,
        IAudioInventoryService audioInventoryService,
        IAudioChangeWatcher audioChangeWatcher,
        IAudioSessionControlService audioSessionControlService,
        IAudioEndpointControlService audioEndpointControlService,
        IDefaultAudioEndpointService defaultAudioEndpointService,
        IDeviceInventoryService deviceInventoryService,
        IDeviceSettingsService deviceSettingsService,
        IWirelessRadioService wirelessRadioService,
        ITaskbarService taskbarService,
        IWidgetSettingsStore widgetSettingsStore,
        IProfileStore profileStore,
        IStartupRegistrationService startupRegistrationService)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
        _audioSessionControlService = audioSessionControlService;
        _audioEndpointControlService = audioEndpointControlService;
        _defaultAudioEndpointService = defaultAudioEndpointService;
        _deviceInventoryService = deviceInventoryService;
        _deviceSettingsService = deviceSettingsService;
        _wirelessRadioService = wirelessRadioService;
        _taskbarService = taskbarService;
        _widgetSettingsStore = widgetSettingsStore;
        _profileStore = profileStore;
        _startupRegistrationService = startupRegistrationService;
        _widgetSettings = _widgetSettingsStore.Load();
        _profileCatalog = _profileStore.Load();
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
            _trayIcon.FavoriteRequested += TrayIcon_FavoriteRequested;
            _trayIcon.SetFavoriteItems(BuildTrayFavoriteLabels());
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
            _trayIcon.FavoriteRequested -= TrayIcon_FavoriteRequested;
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
        else if (message == WindowsGlobalHotkey.WmHotkey &&
            _globalHotkey.TryResolveProfileId(
                wordParameter.ToInt32(),
                out string profileId))
        {
            HandleProfileHotkey(profileId);
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
                else if (WidgetSettings.TryGetInputFavoriteSlot(action, out int inputSlot))
                {
                    await ApplyFavoriteInputFromHotkeyAsync(inputSlot);
                }

                break;
        }
    }

    private async void HandleProfileHotkey(string profileId)
    {
        ProfileDefinition? profile = FindProfile(profileId);

        if (profile is not null)
        {
            await ApplyProfileAsync(profile);
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

        await ApplyFavoriteEndpointFromHotkeyAsync(
            favorite.EndpointId,
            favorite.Name,
            favorite.Role);
    }

    private async Task ApplyFavoriteInputFromHotkeyAsync(int slot)
    {
        FavoriteInputSetting? favorite = _widgetSettings.GetInputFavorite(slot);

        if (favorite is null)
        {
            return;
        }

        await ApplyFavoriteEndpointFromHotkeyAsync(
            favorite.EndpointId,
            favorite.Name,
            favorite.Role);
    }

    private async Task ApplyFavoriteEndpointFromHotkeyAsync(
        string endpointId,
        string endpointName,
        FavoriteEndpointRole favoriteRole)
    {
        AudioDefaultRoleSelection roleSelection = ToAudioRoleSelection(favoriteRole);

        AudioControlResult result;

        try
        {
            result = await _defaultAudioEndpointService.SetDefaultAsync(
                endpointId,
                endpointName,
                roleSelection,
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

    private static AudioDefaultRoleSelection ToAudioRoleSelection(
        FavoriteEndpointRole role) =>
        role switch
        {
            FavoriteEndpointRole.Communications =>
                AudioDefaultRoleSelection.Communications,
            FavoriteEndpointRole.Both => AudioDefaultRoleSelection.Both,
            _ => AudioDefaultRoleSelection.General,
        };

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
        ProfilesPanel.Visibility =
            panel == WidgetPanel.Profiles ? Visibility.Visible : Visibility.Collapsed;

        _isApplyingPanelSelection = true;

        try
        {
            DisplayPanelTab.IsChecked = panel == WidgetPanel.Display;
            AudioPanelTab.IsChecked = panel == WidgetPanel.Audio;
            DevicesPanelTab.IsChecked = panel == WidgetPanel.Devices;
            OptionsPanelTab.IsChecked = panel == WidgetPanel.Options;
            ProfilesPanelTab.IsChecked = panel == WidgetPanel.Profiles;
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
            case WidgetPanel.Profiles:
                UpdateProfilesControls();
                ProfileNameBox.Focus();
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

    private async void TrayIcon_FavoriteRequested(
        object? sender,
        TrayFavoriteRequestedEventArgs e)
    {
        List<TrayFavoriteTarget> targets = GetTrayFavoriteTargets();

        if (e.Index < 0 || e.Index >= targets.Count)
        {
            return;
        }

        TrayFavoriteTarget target = targets[e.Index];
        await ApplyFavoriteEndpointFromHotkeyAsync(
            target.EndpointId,
            target.Name,
            target.Role);
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

    private HotkeyRegistrationResult ApplyHotkeyBindings(
        WidgetSettings settings,
        ProfileCatalog? profileCatalog = null)
    {
        ProfileCatalog catalog = profileCatalog ?? _profileCatalog;
        List<ProfileHotkeyBinding> profileBindings = catalog.Profiles
            .Where(profile => profile.Shortcut is { IsValid: true })
            .Select(profile => new ProfileHotkeyBinding(
                profile.Id,
                profile.Shortcut!))
            .ToList();
        HotkeyRegistrationResult result =
            _globalHotkey.ApplyBindings(
                _windowHandle,
                settings,
                profileBindings);

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
                    slot,
                    WidgetSettings.GetFavoriteAction(slot),
                    favorite.Name,
                    favorite.Alias ?? string.Empty,
                    FormatShortcut(favorite.Shortcut),
                    $"{favorite.Name} shortcut"));
            }

            FavoriteOutputShortcutItems.ItemsSource = favoriteOptions;
            FavoriteOutputsEmptyText.Visibility = favoriteOptions.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            List<FavoriteInputShortcutOption> favoriteInputOptions = [];

            for (int slot = 0;
                 slot < WidgetSettings.MaximumFavoriteInputs;
                 slot++)
            {
                FavoriteInputSetting? favorite =
                    _widgetSettings.GetInputFavorite(slot);

                if (favorite is null)
                {
                    continue;
                }

                favoriteInputOptions.Add(new(
                    slot,
                    WidgetSettings.GetInputFavoriteAction(slot),
                    favorite.Name,
                    favorite.Alias ?? string.Empty,
                    FormatShortcut(favorite.Shortcut),
                    $"{favorite.Name} shortcut"));
            }

            FavoriteInputShortcutItems.ItemsSource = favoriteInputOptions;
            FavoriteInputsEmptyText.Visibility = favoriteInputOptions.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            DarkThemeCheckBox.IsChecked = _widgetSettings.UseDarkTheme;
            StartWithWindowsCheckBox.IsChecked =
                _startupRegistrationService.IsEnabled;
            UpdateFavoriteOutputsSummary();
            _trayIcon?.SetFavoriteItems(BuildTrayFavoriteLabels());
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

    private void FavoriteAlias_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: WidgetHotkeyAction action, Text: string alias })
        {
            return;
        }

        if (WidgetSettings.TryGetFavoriteSlot(action, out int outputSlot) &&
            _widgetSettings.GetFavorite(outputSlot) is FavoriteOutputSetting outputFavorite)
        {
            _widgetSettings = _widgetSettings.WithFavorite(
                outputSlot,
                outputFavorite with { Alias = alias });
            UpdateOptionsControls();
            SaveSettings("Favorite alias saved.");
            return;
        }

        if (WidgetSettings.TryGetInputFavoriteSlot(action, out int inputSlot) &&
            _widgetSettings.GetInputFavorite(inputSlot) is FavoriteInputSetting inputFavorite)
        {
            _widgetSettings = _widgetSettings.WithInputFavorite(
                inputSlot,
                inputFavorite with { Alias = alias });
            UpdateOptionsControls();
            SaveSettings("Favorite alias saved.");
        }
    }

    private void MoveFavoriteUp_Click(object sender, RoutedEventArgs e) =>
        MoveFavorite(sender, -1);

    private void MoveFavoriteDown_Click(object sender, RoutedEventArgs e) =>
        MoveFavorite(sender, 1);

    private void MoveFavorite(object sender, int offset)
    {
        if (sender is not Button { Tag: WidgetHotkeyAction action })
        {
            return;
        }

        if (WidgetSettings.TryGetFavoriteSlot(action, out int outputSlot))
        {
            int targetSlot = outputSlot + offset;

            if (targetSlot is < 0 or >= WidgetSettings.MaximumFavoriteOutputs ||
                _widgetSettings.GetFavorite(outputSlot) is not FavoriteOutputSetting favorite)
            {
                return;
            }

            FavoriteOutputSetting? displaced =
                _widgetSettings.GetFavorite(targetSlot);
            WidgetSettings candidate = _widgetSettings
                .WithFavorite(outputSlot, displaced)
                .WithFavorite(targetSlot, favorite);
            HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
            _widgetSettings = candidate;
            UpdateOptionsControls();
            SaveSettings(
                result.Succeeded
                    ? "Favorite order updated."
                    : result.FirstFailure ??
                        "Favorite order updated; a shortcut is unavailable.");
            return;
        }

        if (WidgetSettings.TryGetInputFavoriteSlot(action, out int inputSlot))
        {
            int targetSlot = inputSlot + offset;

            if (targetSlot is < 0 or >= WidgetSettings.MaximumFavoriteInputs ||
                _widgetSettings.GetInputFavorite(inputSlot) is not FavoriteInputSetting favorite)
            {
                return;
            }

            FavoriteInputSetting? displaced =
                _widgetSettings.GetInputFavorite(targetSlot);
            WidgetSettings candidate = _widgetSettings
                .WithInputFavorite(inputSlot, displaced)
                .WithInputFavorite(targetSlot, favorite);
            HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
            _widgetSettings = candidate;
            UpdateOptionsControls();
            SaveSettings(
                result.Succeeded
                    ? "Favorite order updated."
                    : result.FirstFailure ??
                        "Favorite order updated; a shortcut is unavailable.");
        }
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
                Key.D5 or Key.NumPad5 => WidgetPanel.Profiles,
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

    private async Task<bool> ApplyDisplayModeAsync(DisplayMode mode)
    {
        if (_currentDisplayMode == mode)
        {
            SetDisplayModeSelection(mode);
            return true;
        }

        DisplayMode? previousMode = _currentDisplayMode;
        bool succeeded = false;
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
                succeeded = true;
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

        return succeeded;
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

        if (_widgetSettings.FindFavoriteSlot(endpoint.Id) >= 0)
        {
            AudioStatusText.Text =
                "That output is already a favorite; choose Remove to delete it.";
            return;
        }

        int openSlot = _widgetSettings.FindOpenFavoriteSlot();

        if (openSlot < 0)
        {
            AudioStatusText.Text =
                $"You can save up to {WidgetSettings.MaximumFavoriteOutputs} favorites.";
            return;
        }

        WidgetSettings candidate = _widgetSettings.WithFavorite(
            openSlot,
            new FavoriteOutputSetting(
                endpoint.Id,
                endpoint.Name,
                null,
                GetSelectedFavoriteRole(PlaybackFavoriteRoleBox)));
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            result.Succeeded
                ? $"{endpoint.Name} added to favorites."
                : result.FirstFailure ??
                    "Favorite added; a shortcut is unavailable.",
            AudioStatusText);
    }

    private void RemoveFavoriteOutput_Click(object sender, RoutedEventArgs e)
    {
        if (PlaybackEndpointsList.SelectedItem is not AudioEndpointInfo endpoint)
        {
            AudioStatusText.Text = "Select an output device first.";
            return;
        }

        int slot = _widgetSettings.FindFavoriteSlot(endpoint.Id);

        if (slot < 0)
        {
            AudioStatusText.Text = "That output is not a favorite.";
            return;
        }

        WidgetSettings candidate = _widgetSettings.WithFavorite(slot, null);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            result.Succeeded
                ? $"{endpoint.Name} removed from favorites."
                : result.FirstFailure ??
                    "Favorite removed; a shortcut is unavailable.",
            AudioStatusText);
    }

    private void AddFavoriteInput_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingEndpointsList.SelectedItem is not AudioEndpointInfo endpoint)
        {
            AudioStatusText.Text = "Select a microphone first.";
            return;
        }

        if (_widgetSettings.FindInputFavoriteSlot(endpoint.Id) >= 0)
        {
            AudioStatusText.Text =
                "That microphone is already a favorite; choose Remove to delete it.";
            return;
        }

        int openSlot = _widgetSettings.FindOpenInputFavoriteSlot();

        if (openSlot < 0)
        {
            AudioStatusText.Text =
                $"You can save up to {WidgetSettings.MaximumFavoriteInputs} microphone favorites.";
            return;
        }

        WidgetSettings candidate = _widgetSettings.WithInputFavorite(
            openSlot,
            new FavoriteInputSetting(
                endpoint.Id,
                endpoint.Name,
                null,
                GetSelectedFavoriteRole(RecordingFavoriteRoleBox)));
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            result.Succeeded
                ? $"{endpoint.Name} added to microphone favorites."
                : result.FirstFailure ??
                    "Favorite added; a shortcut is unavailable.",
            AudioStatusText);
    }

    private void RemoveFavoriteInput_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingEndpointsList.SelectedItem is not AudioEndpointInfo endpoint)
        {
            AudioStatusText.Text = "Select a microphone first.";
            return;
        }

        int slot = _widgetSettings.FindInputFavoriteSlot(endpoint.Id);

        if (slot < 0)
        {
            AudioStatusText.Text = "That microphone is not a favorite.";
            return;
        }

        WidgetSettings candidate = _widgetSettings.WithInputFavorite(slot, null);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            result.Succeeded
                ? $"{endpoint.Name} removed from microphone favorites."
                : result.FirstFailure ??
                    "Favorite removed; a shortcut is unavailable.",
            AudioStatusText);
    }

    private static FavoriteEndpointRole GetSelectedFavoriteRole(
        ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
        Enum.TryParse(tag, out FavoriteEndpointRole role)
            ? role
            : FavoriteEndpointRole.General;

    private static string GetFavoriteDisplayName(
        string name,
        string? alias) =>
        string.IsNullOrWhiteSpace(alias) ? name : alias;

    private static string GetFavoriteRoleLabel(FavoriteEndpointRole role) =>
        role switch
        {
            FavoriteEndpointRole.Communications => "calls",
            FavoriteEndpointRole.Both => "default + calls",
            _ => "default",
        };

    private static string FormatFavoriteSummary(
        string name,
        string? alias,
        FavoriteEndpointRole role) =>
        $"{GetFavoriteDisplayName(name, alias)} ({GetFavoriteRoleLabel(role)})";

    private void UpdateFavoriteOutputsSummary()
    {
        List<string> names = [];

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteOutputs;
             slot++)
        {
            if (_widgetSettings.GetFavorite(slot) is FavoriteOutputSetting favorite)
            {
                names.Add(FormatFavoriteSummary(
                    favorite.Name,
                    favorite.Alias,
                    favorite.Role));
            }
        }

        FavoriteOutputsSummaryText.Text = names.Count == 0
            ? "Favorites: none"
            : $"Favorites: {string.Join(", ", names)}";

        names.Clear();

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteInputs;
             slot++)
        {
            if (_widgetSettings.GetInputFavorite(slot) is FavoriteInputSetting favorite)
            {
                names.Add(FormatFavoriteSummary(
                    favorite.Name,
                    favorite.Alias,
                    favorite.Role));
            }
        }

        FavoriteInputsSummaryText.Text = names.Count == 0
            ? "Favorites: none"
            : $"Favorites: {string.Join(", ", names)}";
    }

    private List<TrayFavoriteTarget> GetTrayFavoriteTargets()
    {
        List<TrayFavoriteTarget> targets = [];

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteOutputs;
             slot++)
        {
            if (_widgetSettings.GetFavorite(slot) is FavoriteOutputSetting favorite)
            {
                targets.Add(new(
                    favorite.EndpointId,
                    favorite.Name,
                    favorite.Role,
                    false,
                    favorite.Alias));
            }
        }

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteInputs;
             slot++)
        {
            if (_widgetSettings.GetInputFavorite(slot) is FavoriteInputSetting favorite)
            {
                targets.Add(new(
                    favorite.EndpointId,
                    favorite.Name,
                    favorite.Role,
                    true,
                    favorite.Alias));
            }
        }

        return targets;
    }

    private List<string> BuildTrayFavoriteLabels() =>
        GetTrayFavoriteTargets()
            .Select(target =>
                $"{(target.IsInput ? "Microphone" : "Output")}: " +
                FormatFavoriteSummary(
                    target.Name,
                    target.Alias,
                    target.Role))
            .ToList();

    private async void SaveCurrentProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ProfilesStatusText.Text = "Enter a profile name first.";
            ProfileNameBox.Focus();
            return;
        }

        try
        {
            ProfileDefinition? profile = await CaptureCurrentProfileAsync(name);

            if (profile is null)
            {
                ProfilesStatusText.Text =
                    "Select at least one setting to save in the profile.";
                return;
            }

            ProfileDefinition savedProfile = profile;
            ProfileDefinition? existingProfile = _editingProfileId is null
                ? null
                : FindProfile(_editingProfileId);

            if (existingProfile is not null)
            {
                savedProfile = profile with
                {
                    Id = existingProfile.Id,
                    IsPinned = ProfilePinCheckBox.IsChecked == true,
                    Shortcut = existingProfile.Shortcut,
                };
            }

            IReadOnlyList<ProfileDefinition> profiles = existingProfile is null
                ? _profileCatalog.Profiles.Append(savedProfile).ToList()
                : _profileCatalog.Profiles
                    .Select(candidate => candidate.Id == existingProfile.Id
                        ? savedProfile
                        : candidate)
                    .ToList();
            ProfileCatalog candidate = new ProfileCatalog(
                ProfileCatalog.CurrentSchemaVersion,
                profiles).Normalize();

            if (!TryApplyAndSaveProfileCatalog(
                candidate,
                out string? errorMessage))
            {
                ProfilesStatusText.Text = errorMessage;
                return;
            }

            bool wasEditing = existingProfile is not null;
            ResetProfileEditor();
            UpdateProfilesControls();
            ProfilesStatusText.Text = wasEditing
                ? $"{savedProfile.Name} updated."
                : $"{savedProfile.Name} saved.";
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            ProfilesStatusText.Text =
                $"The current setup could not be saved: {exception.Message}";
        }
    }

    private async Task<ProfileDefinition?> CaptureCurrentProfileAsync(
        string name)
    {
        bool includeDisplay = ProfileIncludeDisplayCheckBox.IsChecked == true;
        bool includePlayback = ProfileIncludePlaybackCheckBox.IsChecked == true;
        bool includeRecording = ProfileIncludeRecordingCheckBox.IsChecked == true;
        bool includeTaskbar = ProfileIncludeTaskbarCheckBox.IsChecked == true;
        AudioInventory? inventory = null;

        if (includePlayback || includeRecording)
        {
            inventory = await _audioInventoryService.GetInventoryAsync(
                _lifetimeCancellation.Token);
        }

        DisplayMode? displayMode = includeDisplay
            ? _displayTopologyService.GetSnapshot().CurrentMode ?? _currentDisplayMode
            : null;
        TaskbarState? taskbarState = includeTaskbar
            ? _taskbarService.GetSnapshot().State
            : null;

        if (taskbarState == Features.Taskbar.TaskbarState.Unavailable)
        {
            taskbarState = null;
        }

        AudioEndpointControlSnapshot? playbackState = null;
        AudioEndpointControlSnapshot? recordingState = null;

        if (inventory is not null && includePlayback)
        {
            AudioEndpointInfo? playbackDefault = inventory.PlaybackEndpoints
                .FirstOrDefault(endpoint => endpoint.IsDefault);
            playbackState = playbackDefault is null
                ? null
                : await _audioEndpointControlService.GetStateAsync(
                    playbackDefault.Id,
                    _lifetimeCancellation.Token);
        }

        if (inventory is not null && includeRecording)
        {
            AudioEndpointInfo? recordingDefault = inventory.RecordingEndpoints
                .FirstOrDefault(endpoint => endpoint.IsDefault);
            recordingState = recordingDefault is null
                ? null
                : await _audioEndpointControlService.GetStateAsync(
                    recordingDefault.Id,
                    _lifetimeCancellation.Token);
        }

        ProfileEndpointTarget? playbackGeneral = includePlayback
            ? ToProfileEndpoint(inventory?.PlaybackEndpoints.FirstOrDefault(
                endpoint => endpoint.IsDefault))
            : null;
        ProfileEndpointTarget? playbackCommunications = includePlayback
            ? ToProfileEndpoint(inventory?.PlaybackEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault))
            : null;
        ProfileEndpointTarget? recordingGeneral = includeRecording
            ? ToProfileEndpoint(inventory?.RecordingEndpoints.FirstOrDefault(
                endpoint => endpoint.IsDefault))
            : null;
        ProfileEndpointTarget? recordingCommunications = includeRecording
            ? ToProfileEndpoint(inventory?.RecordingEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault))
            : null;

        ApplySelectedProfileFavorite(
            ProfilePlaybackFavoriteBox.SelectedItem as ProfileFavoriteOption,
            ref playbackGeneral,
            ref playbackCommunications);
        ApplySelectedProfileFavorite(
            ProfileRecordingFavoriteBox.SelectedItem as ProfileFavoriteOption,
            ref recordingGeneral,
            ref recordingCommunications);

        ProfileDefinition profile = new(
            Guid.NewGuid().ToString("N"),
            name,
            ProfilePinCheckBox.IsChecked == true,
            null,
            displayMode,
            playbackGeneral,
            playbackCommunications,
            recordingGeneral,
            recordingCommunications,
            taskbarState,
            recordingState?.IsMuted,
            playbackState?.MasterVolume);

        ProfileDefinition normalized = profile.Normalize();
        return normalized.HasActions ? normalized : null;
    }

    private async Task<ProfileDefinition?> CaptureCurrentStateAsync()
    {
        AudioInventory inventory = await _audioInventoryService.GetInventoryAsync(
            _lifetimeCancellation.Token);
        DisplayTopologySnapshot displaySnapshot = _displayTopologyService.GetSnapshot();
        TaskbarState? taskbarState = _taskbarService.GetSnapshot().State;

        if (taskbarState == Features.Taskbar.TaskbarState.Unavailable)
        {
            taskbarState = null;
        }

        AudioEndpointInfo? playbackDefault = inventory.PlaybackEndpoints
            .FirstOrDefault(endpoint => endpoint.IsDefault);
        AudioEndpointInfo? recordingDefault = inventory.RecordingEndpoints
            .FirstOrDefault(endpoint => endpoint.IsDefault);
        AudioEndpointControlSnapshot? playbackState = playbackDefault is null
            ? null
            : await _audioEndpointControlService.GetStateAsync(
                playbackDefault.Id,
                _lifetimeCancellation.Token);
        AudioEndpointControlSnapshot? recordingState = recordingDefault is null
            ? null
            : await _audioEndpointControlService.GetStateAsync(
                recordingDefault.Id,
                _lifetimeCancellation.Token);

        ProfileDefinition profile = new(
            Guid.NewGuid().ToString("N"),
            "Previous setup",
            false,
            null,
            displaySnapshot.CurrentMode ?? _currentDisplayMode,
            ToProfileEndpoint(playbackDefault),
            ToProfileEndpoint(inventory.PlaybackEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault)),
            ToProfileEndpoint(recordingDefault),
            ToProfileEndpoint(inventory.RecordingEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault)),
            taskbarState,
            recordingState?.IsMuted,
            playbackState?.MasterVolume);

        ProfileDefinition normalized = profile.Normalize();
        return normalized.HasActions ? normalized : null;
    }

    private void UpdateProfilesControls()
    {
        UpdateProfileFavoriteControls();
        List<ProfileOption> options = _profileCatalog.Profiles
            .Select(profile => new ProfileOption(
                profile.Id,
                profile.Name,
                BuildProfileSummary(profile),
                FormatShortcut(profile.Shortcut),
                profile.IsPinned ? "Unpin" : "Pin"))
            .ToList();

        ProfileItems.ItemsSource = options;
        ProfileEmptyText.Visibility = options.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_editingProfileId is not null &&
            FindProfile(_editingProfileId) is null)
        {
            ResetProfileEditor();
        }
    }

    private void ProfileShortcut_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string profileId })
        {
            return;
        }

        e.Handled = true;
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
            ProfilesStatusText.Text =
                "Keep holding the modifier and press a letter, number, or F-key.";
            return;
        }

        if (key is Key.Delete or Key.Back &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            ApplyProfileShortcutChange(profileId, null);
            return;
        }

        WidgetHotkeyModifiers modifiers =
            ConvertModifiers(Keyboard.Modifiers);
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);

        if (!WidgetShortcut.TryCreate(
            modifiers,
            virtualKey,
            out WidgetShortcut? shortcut))
        {
            ProfilesStatusText.Text =
                "Use Ctrl, Alt, or Win with A-Z, 0-9, or F1-F12. Shift is optional.";
            return;
        }

        if (IsShortcutUsedByWidgetAction(shortcut!) ||
            IsShortcutUsedByAnotherProfile(profileId, shortcut!))
        {
            ProfilesStatusText.Text =
                $"{shortcut!.DisplayText} is already assigned in WinQuickSwitch.";
            return;
        }

        ApplyProfileShortcutChange(profileId, shortcut);
    }

    private void UnsetProfileShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: string profileId })
        {
            ApplyProfileShortcutChange(profileId, null);
        }
    }

    private bool IsShortcutUsedByWidgetAction(WidgetShortcut shortcut) =>
        Enum.GetValues<WidgetHotkeyAction>()
            .Any(action => _widgetSettings.GetShortcut(action) == shortcut);

    private bool IsShortcutUsedByAnotherProfile(
        string profileId,
        WidgetShortcut shortcut) =>
        _profileCatalog.Profiles.Any(profile =>
            !string.Equals(profile.Id, profileId, StringComparison.Ordinal) &&
            profile.Shortcut == shortcut);

    private void ApplyProfileShortcutChange(
        string profileId,
        WidgetShortcut? shortcut)
    {
        ProfileDefinition? profile = FindProfile(profileId);

        if (profile is null)
        {
            return;
        }

        ProfileCatalog candidate = new ProfileCatalog(
            ProfileCatalog.CurrentSchemaVersion,
            _profileCatalog.Profiles
                .Select(item => item.Id == profileId
                    ? item with { Shortcut = shortcut }
                    : item)
                .ToList()).Normalize();

        if (!TryApplyAndSaveProfileCatalog(candidate, out string? errorMessage))
        {
            ProfilesStatusText.Text = errorMessage;
            return;
        }

        ProfilesStatusText.Text = shortcut is null
            ? $"{profile.Name} shortcut cleared."
            : $"{shortcut.DisplayText} now applies {profile.Name}.";
    }

    private void EditProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId } ||
            FindProfile(profileId) is not ProfileDefinition profile)
        {
            return;
        }

        _editingProfileId = profile.Id;
        ProfileNameBox.Text = profile.Name;
        ProfileIncludeDisplayCheckBox.IsChecked = profile.DisplayMode is not null;
        ProfileIncludePlaybackCheckBox.IsChecked =
            profile.PlaybackGeneral is not null ||
            profile.PlaybackCommunications is not null;
        ProfileIncludeRecordingCheckBox.IsChecked =
            profile.RecordingGeneral is not null ||
            profile.RecordingCommunications is not null;
        ProfileIncludeTaskbarCheckBox.IsChecked = profile.TaskbarState is not null;
        ProfilePinCheckBox.IsChecked = profile.IsPinned;
        SelectProfileFavorite(
            ProfilePlaybackFavoriteBox,
            profile.PlaybackGeneral,
            profile.PlaybackCommunications);
        SelectProfileFavorite(
            ProfileRecordingFavoriteBox,
            profile.RecordingGeneral,
            profile.RecordingCommunications);
        SaveProfileButton.Content = "Update profile";
        CancelProfileEditButton.Visibility = Visibility.Visible;
        ProfilesStatusText.Text = $"Editing {profile.Name}.";
        ProfileNameBox.Focus();
        ProfileNameBox.SelectAll();
    }

    private void CancelProfileEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        ResetProfileEditor();
        ProfilesStatusText.Text = "Profile edit canceled.";
    }

    private void DuplicateProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId } ||
            FindProfile(profileId) is not ProfileDefinition profile)
        {
            return;
        }

        ProfileDefinition duplicate = profile with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{profile.Name} copy",
            IsPinned = false,
            Shortcut = null,
        };

        SaveProfileCatalog(
            _profileCatalog.Profiles.Append(duplicate).ToList(),
            $"{duplicate.Name} created.");
    }

    private void ResetProfileEditor()
    {
        _editingProfileId = null;
        ProfileNameBox.Text = "New profile";
        ProfileIncludeDisplayCheckBox.IsChecked = true;
        ProfileIncludePlaybackCheckBox.IsChecked = true;
        ProfileIncludeRecordingCheckBox.IsChecked = true;
        ProfileIncludeTaskbarCheckBox.IsChecked = true;
        ProfilePinCheckBox.IsChecked = false;
        ProfilePlaybackFavoriteBox.SelectedIndex = 0;
        ProfileRecordingFavoriteBox.SelectedIndex = 0;
        SaveProfileButton.Content = "Save current setup";
        CancelProfileEditButton.Visibility = Visibility.Collapsed;
    }

    private async void ApplyProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId })
        {
            return;
        }

        ProfileDefinition? profile = FindProfile(profileId);

        if (profile is not null)
        {
            await ApplyProfileAsync(profile);
        }
    }

    private async Task ApplyProfileAsync(ProfileDefinition profile)
    {
        if (_isApplyingProfile)
        {
            return;
        }

        _isApplyingProfile = true;
        ProfilesStatusText.Text = $"Applying {profile.Name}...";
        ProfileItems.IsEnabled = false;
        UndoProfileButton.IsEnabled = false;
        List<string> warnings = [];

        try
        {
            _profileUndo = null;
            UndoProfileButton.Visibility = Visibility.Collapsed;

            try
            {
                _profileUndo = await CaptureCurrentStateAsync();
            }
            catch (OperationCanceledException) when (
                _lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Undo is best-effort and must not block profile application.
            }

            UndoProfileButton.Visibility = _profileUndo is null
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (profile.DisplayMode is DisplayMode displayMode &&
                !await ApplyDisplayModeAsync(displayMode))
            {
                ProfilesStatusText.Text =
                    $"{profile.Name} could not change the display mode.";
                return;
            }

            bool needsPlayback =
                profile.PlaybackGeneral is not null ||
                profile.PlaybackCommunications is not null;
            bool needsRecording =
                profile.RecordingGeneral is not null ||
                profile.RecordingCommunications is not null;
            AudioInventory? inventory = null;

            if (needsPlayback || needsRecording)
            {
                inventory = await _audioInventoryService.GetInventoryAsync(
                    _lifetimeCancellation.Token);
            }

            if (inventory is not null)
            {
                await ApplyProfileEndpointAsync(
                    profile.PlaybackGeneral,
                    inventory.PlaybackEndpoints,
                    AudioDefaultRoleSelection.General,
                    warnings);
                await ApplyProfileEndpointAsync(
                    profile.PlaybackCommunications,
                    inventory.PlaybackEndpoints,
                    AudioDefaultRoleSelection.Communications,
                    warnings);
                await ApplyProfileEndpointAsync(
                    profile.RecordingGeneral,
                    inventory.RecordingEndpoints,
                    AudioDefaultRoleSelection.General,
                    warnings);
                await ApplyProfileEndpointAsync(
                    profile.RecordingCommunications,
                    inventory.RecordingEndpoints,
                    AudioDefaultRoleSelection.Communications,
                    warnings);
            }

            if (profile.MasterVolume is not null ||
                profile.MicrophoneMuted is not null)
            {
                AudioInventory controlInventory =
                    await _audioInventoryService.GetInventoryAsync(
                        _lifetimeCancellation.Token);
                AudioEndpointInfo? playbackDefault = controlInventory
                    .PlaybackEndpoints
                    .FirstOrDefault(endpoint => endpoint.IsDefault);
                AudioEndpointInfo? recordingDefault = controlInventory
                    .RecordingEndpoints
                    .FirstOrDefault(endpoint => endpoint.IsDefault);

                if (profile.MasterVolume is float masterVolume)
                {
                    if (playbackDefault is null)
                    {
                        warnings.Add("There is no default playback device for master volume.");
                    }
                    else
                    {
                        AudioControlResult result =
                            await _audioEndpointControlService.SetMasterVolumeAsync(
                                playbackDefault.Id,
                                playbackDefault.Name,
                                masterVolume,
                                _lifetimeCancellation.Token);

                        if (!result.Succeeded)
                        {
                            warnings.Add(result.Message);
                        }
                    }
                }

                if (profile.MicrophoneMuted is bool microphoneMuted)
                {
                    if (recordingDefault is null)
                    {
                        warnings.Add("There is no default recording device for microphone mute.");
                    }
                    else
                    {
                        AudioControlResult result =
                            await _audioEndpointControlService.SetMuteAsync(
                                recordingDefault.Id,
                                recordingDefault.Name,
                                microphoneMuted,
                                _lifetimeCancellation.Token);

                        if (!result.Succeeded)
                        {
                            warnings.Add(result.Message);
                        }
                    }
                }
            }

            if (profile.TaskbarState is TaskbarState taskbarState)
            {
                bool autoHide = taskbarState == Features.Taskbar.TaskbarState.AutoHidden;
                TaskbarActionResult result = _taskbarService.SetAutoHide(autoHide);

                if (!result.Succeeded)
                {
                    warnings.Add(result.Message);
                }
            }

            if (warnings.Count == 0)
            {
                ProfilesStatusText.Text = $"{profile.Name} applied.";
            }
            else
            {
                ProfilesStatusText.Text =
                    $"{profile.Name} applied with warnings: " +
                    string.Join(" ", warnings);
            }

            RefreshDisplayTopology();
            RefreshTaskbarState();
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            ProfilesStatusText.Text =
                $"{profile.Name} could not be applied: {exception.Message}";
        }
        finally
        {
            _isApplyingProfile = false;
            ProfileItems.IsEnabled = true;
            UndoProfileButton.IsEnabled = true;
        }
    }

    private async void UndoProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_profileUndo is ProfileDefinition undoProfile)
        {
            await ApplyProfileAsync(undoProfile);
        }
    }

    private async Task ApplyProfileEndpointAsync(
        ProfileEndpointTarget? target,
        IReadOnlyList<AudioEndpointInfo> endpoints,
        AudioDefaultRoleSelection role,
        ICollection<string> warnings)
    {
        if (target is null)
        {
            return;
        }

        AudioEndpointInfo? endpoint = endpoints.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                target.EndpointId,
                StringComparison.Ordinal));

        if (endpoint is null)
        {
            warnings.Add($"{target.Name} is not connected.");
            return;
        }

        AudioControlResult result = await _defaultAudioEndpointService.SetDefaultAsync(
            endpoint.Id,
            endpoint.Name,
            role,
            _lifetimeCancellation.Token);

        if (!result.Succeeded)
        {
            warnings.Add(result.Message);
        }
    }

    private void ToggleProfilePin_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId } ||
            FindProfile(profileId) is not ProfileDefinition profile)
        {
            return;
        }

        if (!profile.IsPinned &&
            _profileCatalog.Profiles.Count(candidate => candidate.IsPinned) >=
                ProfileCatalog.MaximumPinnedProfiles)
        {
            ProfilesStatusText.Text =
                $"You can pin up to {ProfileCatalog.MaximumPinnedProfiles} profiles.";
            return;
        }

        SaveProfileCatalog(
            _profileCatalog.Profiles
                .Select(candidate => candidate.Id == profileId
                    ? candidate with { IsPinned = !candidate.IsPinned }
                    : candidate)
                .ToList(),
            profile.IsPinned ? "Profile unpinned." : "Profile pinned.");
    }

    private void DeleteProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId })
        {
            return;
        }

        ProfileDefinition? profile = FindProfile(profileId);

        if (profile is null)
        {
            return;
        }

        SaveProfileCatalog(
            _profileCatalog.Profiles
                .Where(candidate => candidate.Id != profileId)
                .ToList(),
            $"{profile.Name} deleted.");
    }

    private void SaveProfileCatalog(
        IReadOnlyList<ProfileDefinition> profiles,
        string successMessage)
    {
        ProfileCatalog candidate = new ProfileCatalog(
            ProfileCatalog.CurrentSchemaVersion,
            profiles).Normalize();

        if (!TryApplyAndSaveProfileCatalog(
            candidate,
            out string? errorMessage))
        {
            ProfilesStatusText.Text = errorMessage;
            return;
        }

        ProfilesStatusText.Text = successMessage;
    }

    private bool TryApplyAndSaveProfileCatalog(
        ProfileCatalog candidate,
        out string? errorMessage)
    {
        HotkeyRegistrationResult result =
            ApplyHotkeyBindings(_widgetSettings, candidate);

        if (!result.Succeeded)
        {
            ApplyHotkeyBindings(_widgetSettings, _profileCatalog);
            errorMessage = result.FirstFailure ??
                "One or more profile shortcuts are unavailable.";
            return false;
        }

        if (!_profileStore.TrySave(candidate, out errorMessage))
        {
            ApplyHotkeyBindings(_widgetSettings, _profileCatalog);
            return false;
        }

        _profileCatalog = candidate;
        UpdateProfilesControls();
        errorMessage = null;
        return true;
    }

    private ProfileDefinition? FindProfile(string profileId) =>
        _profileCatalog.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.Ordinal));

    private static ProfileEndpointTarget? ToProfileEndpoint(
        AudioEndpointInfo? endpoint) =>
        endpoint is null
            ? null
            : new ProfileEndpointTarget(endpoint.Id, endpoint.Name);

    private void UpdateProfileFavoriteControls()
    {
        List<ProfileFavoriteOption> playbackOptions =
        [
            new("", "Use current default", "", "", FavoriteEndpointRole.General),
        ];

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteOutputs;
             slot++)
        {
            if (_widgetSettings.GetFavorite(slot) is FavoriteOutputSetting favorite)
            {
                playbackOptions.Add(new(
                    $"output:{slot}",
                    FormatFavoriteSummary(
                        favorite.Name,
                        favorite.Alias,
                        favorite.Role),
                    favorite.EndpointId,
                    favorite.Name,
                    favorite.Role));
            }
        }

        List<ProfileFavoriteOption> recordingOptions =
        [
            new("", "Use current default", "", "", FavoriteEndpointRole.General),
        ];

        for (int slot = 0;
             slot < WidgetSettings.MaximumFavoriteInputs;
             slot++)
        {
            if (_widgetSettings.GetInputFavorite(slot) is FavoriteInputSetting favorite)
            {
                recordingOptions.Add(new(
                    $"input:{slot}",
                    FormatFavoriteSummary(
                        favorite.Name,
                        favorite.Alias,
                        favorite.Role),
                    favorite.EndpointId,
                    favorite.Name,
                    favorite.Role));
            }
        }

        ProfilePlaybackFavoriteBox.ItemsSource = playbackOptions;
        ProfileRecordingFavoriteBox.ItemsSource = recordingOptions;

        if (_editingProfileId is null)
        {
            ProfilePlaybackFavoriteBox.SelectedIndex = 0;
            ProfileRecordingFavoriteBox.SelectedIndex = 0;
        }
    }

    private static void ApplySelectedProfileFavorite(
        ProfileFavoriteOption? favorite,
        ref ProfileEndpointTarget? general,
        ref ProfileEndpointTarget? communications)
    {
        if (favorite is null || string.IsNullOrWhiteSpace(favorite.Id))
        {
            return;
        }

        ProfileEndpointTarget target = new(
            favorite.EndpointId,
            favorite.EndpointName);

        switch (favorite.Role)
        {
            case FavoriteEndpointRole.Communications:
                general = null;
                communications = target;
                break;
            case FavoriteEndpointRole.Both:
                general = target;
                communications = target;
                break;
            default:
                general = target;
                communications = null;
                break;
        }
    }

    private static void SelectProfileFavorite(
        ComboBox comboBox,
        ProfileEndpointTarget? general,
        ProfileEndpointTarget? communications)
    {
        ProfileEndpointTarget? target = general ?? communications;

        if (target is null)
        {
            comboBox.SelectedIndex = 0;
            return;
        }

        FavoriteEndpointRole role = general is not null &&
            communications is not null &&
            string.Equals(general.EndpointId, communications.EndpointId, StringComparison.Ordinal)
            ? FavoriteEndpointRole.Both
            : general is not null
                ? FavoriteEndpointRole.General
                : FavoriteEndpointRole.Communications;

        ProfileFavoriteOption? option = comboBox.Items
            .OfType<ProfileFavoriteOption>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.EndpointId, target.EndpointId, StringComparison.Ordinal) &&
                candidate.Role == role);

        comboBox.SelectedItem = option;
        if (option is null)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static string BuildProfileSummary(ProfileDefinition profile)
    {
        List<string> parts = [];

        if (profile.DisplayMode is DisplayMode displayMode)
        {
            parts.Add(displayMode.GetDisplayName());
        }

        if (profile.PlaybackGeneral is not null)
        {
            parts.Add(profile.PlaybackGeneral.Name);
        }

        if (profile.RecordingGeneral is not null)
        {
            parts.Add(profile.RecordingGeneral.Name);
        }

        if (profile.TaskbarState is Features.Taskbar.TaskbarState taskbarState)
        {
            parts.Add(taskbarState == Features.Taskbar.TaskbarState.AutoHidden
                ? "Taskbar auto-hide"
                : "Taskbar visible");
        }

        return parts.Count == 0
            ? "No settings selected"
            : string.Join(" · ", parts);
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
    int Slot,
    WidgetHotkeyAction Action,
    string Name,
    string Alias,
    string ShortcutText,
    string EditorName);

internal sealed record FavoriteInputShortcutOption(
    int Slot,
    WidgetHotkeyAction Action,
    string Name,
    string Alias,
    string ShortcutText,
    string EditorName);

internal sealed record TrayFavoriteTarget(
    string EndpointId,
    string Name,
    FavoriteEndpointRole Role,
    bool IsInput,
    string? Alias);

internal sealed record ProfileFavoriteOption(
    string Id,
    string Name,
    string EndpointId,
    string EndpointName,
    FavoriteEndpointRole Role);

internal sealed record ProfileOption(
    string Id,
    string Name,
    string Summary,
    string ShortcutText,
    string PinLabel);
