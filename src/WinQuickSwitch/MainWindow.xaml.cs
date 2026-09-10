using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Legal;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.SharedUi;
using WinQuickSwitch.Features.Taskbar;
using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows;
using WinQuickSwitch.Platform.Windows.Audio;
using WinQuickSwitch.Platform.Windows.Devices;
using WinQuickSwitch.Platform.Windows.Display;
using WinQuickSwitch.Platform.Windows.Overlay;

namespace WinQuickSwitch;

public partial class MainWindow : Window
{
    private readonly IDisplayModeService _displayModeService;
    private readonly IDisplayTopologyService _displayTopologyService;
    private readonly IAudioInventoryService _audioInventoryService;
    private readonly IAudioChangeWatcher _audioChangeWatcher;
    private readonly IAudioSessionControlService _audioSessionControlService;
    private readonly IAudioEndpointControlService _audioEndpointControlService;
    private readonly AudioQuickActionExecutor _audioQuickActionExecutor;
    private readonly IDefaultAudioEndpointService _defaultAudioEndpointService;
    private readonly IDeviceInventoryService _deviceInventoryService;
    private readonly IDeviceSettingsService _deviceSettingsService;
    private readonly IWirelessRadioService _wirelessRadioService;
    private readonly ITaskbarService _taskbarService;
    private readonly IWidgetSettingsStore _widgetSettingsStore;
    private readonly IProfileFeature _profileFeature;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly ProductConfiguration _productConfiguration;
    private readonly WindowsGlobalHotkey _globalHotkey = new();
    private readonly WindowsWidgetPlacementService _placementService = new();
    private OverlayWorkspaceWindow? _overlayWorkspace;
    private string? _overlayWorkspaceMonitorId;
    private long _overlayNavigationRevision;
    private FloatingDisplayWidgetWindow? _floatingDisplayWidget;
    private FloatingQuickWidgetWindow? _floatingAudioWidget;
    private FloatingQuickWidgetWindow? _floatingDevicesWidget;
    private WirelessRadioSnapshot? _lastWirelessRadioSnapshot;
    private WindowsTrayIcon? _trayIcon;
    private readonly DebouncedActionScheduler _audioRefreshScheduler;
    private readonly DebouncedActionScheduler _deviceRefreshScheduler;
    private readonly DispatcherTimer _displayTopologyRefreshTimer;
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
    private readonly bool _isScopedValidationInstance =
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("WINQUICKSWITCH_INSTANCE_ID"));
    private const int WmDeviceChange = 0x0219;
    private const int WmDisplayChange = 0x007E;

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
        ProfileFeature.CreateStore(ProductConfiguration.Default),
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
        IStartupRegistrationService startupRegistrationService,
        ProductConfiguration? productConfiguration = null)
    {
        _displayModeService = displayModeService;
        _displayTopologyService = displayTopologyService;
        _audioInventoryService = audioInventoryService;
        _audioChangeWatcher = audioChangeWatcher;
        _audioSessionControlService = audioSessionControlService;
        _audioEndpointControlService = audioEndpointControlService;
        _audioQuickActionExecutor = new(audioInventoryService, audioEndpointControlService);
        _defaultAudioEndpointService = defaultAudioEndpointService;
        _deviceInventoryService = deviceInventoryService;
        _deviceSettingsService = deviceSettingsService;
        _wirelessRadioService = wirelessRadioService;
        _taskbarService = taskbarService;
        _widgetSettingsStore = widgetSettingsStore;
        _startupRegistrationService = startupRegistrationService;
        _productConfiguration = productConfiguration ?? ProductConfiguration.Default;
        _profileFeature = ProfileFeature.Create(
            _productConfiguration,
            profileStore);
        _widgetSettings = _widgetSettingsStore.Load();
        _profileCatalog = _profileFeature.Load();
        WidgetTheme.Apply(_widgetSettings.UseDarkTheme);
        InitializeComponent();
        WidgetTheme.AttachResourceScope(this);
        Title = _productConfiguration.VersionedDisplayName;
        ApplyEditionUi();
        MountSharedMainWindowViews();
        UpdateOptionsControls();

        _audioRefreshScheduler = new DebouncedActionScheduler(
            TimeSpan.FromMilliseconds(350),
            RefreshAudioFromNotificationAsync);
        _deviceRefreshScheduler = new DebouncedActionScheduler(
            TimeSpan.FromMilliseconds(450),
            RefreshDevicesFromNotificationAsync);
        _displayTopologyRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _displayTopologyRefreshTimer.Tick += DisplayTopologyRefreshTimer_Tick;
        _audioChangeWatcher.Changed += AudioChangeWatcher_Changed;
    }

    private void ApplyEditionUi()
    {
        bool profilesEnabled = _profileFeature.IsEnabled;

        ProfilesPanel.Visibility = profilesEnabled && _activePanel == WidgetPanel.Profiles
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProfilesPanelTab.Visibility = profilesEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!profilesEnabled)
        {
            PanelTabs.ColumnDefinitions[4].Width = new GridLength(0);
        }

        AboutTitleText.Text =
            $"About {_productConfiguration.VersionedDisplayName}";
        AboutDescriptionText.Text = profilesEnabled
            ? $"Version {ProductConfiguration.ReleaseVersion}. A local Windows utility for quick display, audio, device, taskbar, favorite, and profile controls."
            : $"Version {ProductConfiguration.ReleaseVersion}. A local Windows utility for quick display, audio, device, taskbar, and favorite controls.";
        AboutLicenseHeadingText.Text = profilesEnabled
            ? "Licensed software"
            : "Free edition";
        AboutLicenseBodyText.Text = profilesEnabled
            ? "A Microsoft Store purchase grants permission to use WinQuickSwitch; it does not transfer ownership of the app or its source code."
            : "WinQuickSwitch Free provides the core controls for display, audio, devices, taskbar, favorites, and keybinds. Profiles and combined setups are available in the Paid edition.";
        AboutSharingText.Text = profilesEnabled
            ? "Keep local settings, saved profiles, device names, and shortcuts private."
            : "Keep local settings, device names, and shortcuts private.";
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
            _trayIcon = new WindowsTrayIcon(
                _windowHandle,
                tooltip: _productConfiguration.VersionedDisplayName);
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
        _sharedAudioRefreshOrder.Dispose();
        _sharedDevicesRefreshOrder.Dispose();
        _displayTopologyRefreshTimer.Stop();
        _displayTopologyRefreshTimer.Tick -= DisplayTopologyRefreshTimer_Tick;
        _audioChangeWatcher.Changed -= AudioChangeWatcher_Changed;
        _audioChangeWatcher.Dispose();
        _globalHotkey.Dispose();
        CloseOverlayWorkspace();
        CloseFloatingDisplayWidget();
        CloseFloatingAudioWidget();
        CloseFloatingDevicesWidget();
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
        if ((IsVisible && _activePanel == WidgetPanel.Audio) ||
            _overlayWorkspace is { IsVisible: true })
        {
            _audioRefreshScheduler.Schedule();
        }
    }

    private void ScheduleDisplayTopologyRefresh()
    {
        if (_isExiting ||
            (!IsVisible && _overlayWorkspace is not { IsVisible: true }) ||
            (IsVisible && _overlayWorkspace is null && _activePanel != WidgetPanel.Display))
        {
            return;
        }

        _displayTopologyRefreshTimer.Stop();
        _displayTopologyRefreshTimer.Start();
    }

    private void DisplayTopologyRefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _displayTopologyRefreshTimer.Stop();

        if (!_isExiting &&
            (_overlayWorkspace is { IsVisible: true } ||
             (IsVisible && _activePanel == WidgetPanel.Display)))
        {
            RefreshDisplayTopology();
        }
    }

    private Task RefreshAudioFromNotificationAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            () => RefreshAudioInventoryAsync(cancellationToken),
            DispatcherPriority.Background,
            cancellationToken).Task.Unwrap();

    private Task RefreshDevicesFromNotificationAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            () => RefreshDeviceInventoryAsync(cancellationToken),
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
        else if (message == WindowsGlobalHotkey.WmHotkey &&
            _globalHotkey.TryResolveCommandKey(wordParameter.ToInt32(), out string commandKey))
        {
            HandleCommandHotkey(commandKey);
            handled = true;
        }
        else if (_trayIcon?.HandleWindowMessage(
                     message,
                     wordParameter,
                     longParameter) == true)
        {
            handled = true;
        }
        else if (message is WmDeviceChange or WmDisplayChange)
        {
            if (message == WmDeviceChange)
            {
                _deviceInventoryDirty = true;
                UpdateQuickSummary();

                if ((IsVisible && _activePanel == WidgetPanel.Devices) ||
                    _overlayWorkspace is { IsVisible: true })
                {
                    _deviceRefreshScheduler.Schedule();
                }
            }

            ScheduleDisplayTopologyRefresh();
        }

        return IntPtr.Zero;
    }

    private async void HandleGlobalHotkey(WidgetHotkeyAction action)
    {
        try
        {
            if (!QuickSwitchCommandCatalog.TryGetLegacyHotkey(
                    action,
                    _productConfiguration,
                    out QuickSwitchCommandDescriptor descriptor))
            {
                OptionsStatusText.Text =
                    "That shortcut is not available in this edition.";
                return;
            }

            await ExecuteSharedCommandAsync(
                descriptor.CreateCommand(),
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            OptionsStatusText.Text =
                $"Shortcut action could not complete: {exception.Message}";
            if (_overlayWorkspace is { IsVisible: true } workspace)
            {
                workspace.SetFeedback(
                    OptionsStatusText.Text,
                    OverlayWorkspaceFeedbackSeverity.Error);
            }
        }
    }

    private async void HandleCommandHotkey(string commandKey)
    {
        try
        {
            QuickSwitchCommandDescriptor? descriptor = QuickSwitchCommandCatalog.GetKeyedCommands()
                .FirstOrDefault(item => item.Key == commandKey && item.IsAvailable(_productConfiguration));
            if (descriptor is not null)
                await ExecuteSharedCommandAsync(descriptor.CreateCommand(), _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _sharedAudioStatus = "Audio shortcut could not be completed.";
            PublishSharedState(QuickSwitchRefreshScope.Audio);
        }
    }

    private async void HandleProfileHotkey(string profileId)
    {
        if (!_profileFeature.IsEnabled)
        {
            return;
        }

        ProfileDefinition? profile = FindProfile(profileId);

        if (profile is null)
        {
            return;
        }

        try
        {
            QuickSwitchCommandDescriptor descriptor =
                QuickSwitchCommandCatalog.CreateProfile(profile);
            if (!descriptor.IsAvailable(_productConfiguration))
            {
                return;
            }

            await ExecuteSharedCommandAsync(
                descriptor.CreateCommand(),
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ProfilesStatusText.Text =
                $"Profile shortcut could not complete: {exception.Message}";
        }
    }

    internal void ShowConfiguredSurfaceFromStartup()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ShowConfiguredSurfaceFromStartup);
            return;
        }

        if (!_widgetSettings.OverlayEnabled)
        {
            ShowFromExternalRequest();
            return;
        }

        if (_windowHandle == IntPtr.Zero)
        {
            // An overlay-first launch never shows the resident main window,
            // so explicitly create its HWND. This establishes the tray icon
            // and global shortcuts before the workspace is the only surface.
            _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        }

        if (_overlayWorkspace is { IsVisible: true } workspace)
        {
            workspace.Activate();
            workspace.FocusLauncher();
            return;
        }

        ToggleSidebarOverlay();
    }

    internal async void ShowFromExternalRequest()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ShowFromExternalRequest);
            return;
        }

        CloseOverlayWorkspace();
        try
        {
            await ShowWidgetAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            OptionsStatusText.Text =
                $"WinQuickSwitch could not open: {exception.Message}";
            MessageBox.Show(
                OptionsStatusText.Text,
                _productConfiguration.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal void ShowForAttendedValidation()
    {
        _autoHideAllowedAfterUtc = DateTime.MaxValue;
        Show();
        Activate();
        RefreshDisplayTopology();
    }

    private async Task ShowWidgetAsync(WidgetPanel? requestedPanel = null)
    {
        if (_isExiting)
        {
            return;
        }

        if (requestedPanel is not null)
        {
            CloseOverlayWorkspace();
        }

        _autoHideAllowedAfterUtc = _isScopedValidationInstance
            ? DateTime.MaxValue
            : DateTime.UtcNow.AddMilliseconds(500);
        ResetVisibleCancellation();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = _widgetSettings.WindowPinned;
        Show();
        Activate();
        await ActivatePanelAsync(requestedPanel ?? _activePanel);
        UpdateLayout();

        if (!_hasPositionedWidget)
        {
            PositionWidgetNearPointer();
            _hasPositionedWidget = true;
        }

        // Panel activation can yield while Windows is still unwinding a tray
        // menu or a second-instance launch. Reassert foreground activation
        // after the asynchronous refresh so click-away handling cannot hide
        // the window before the user has a chance to interact with it.
        if (IsVisible && !_isExiting)
        {
            _autoHideAllowedAfterUtc = _isScopedValidationInstance
                ? DateTime.MaxValue
                : DateTime.UtcNow.AddSeconds(3);
            Activate();
            Focus();
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

    private void ToggleSidebarOverlay()
    {
        if (_overlayWorkspace is { IsVisible: true })
        {
            _overlayWorkspace.Dismiss();
            return;
        }

        if (IsVisible)
        {
            HideWidget();
        }

        // The compact window cancels its short-lived refresh token when it is
        // hidden.  The overlay is another active surface, so give it a fresh
        // token and keep the audio watcher alive while the workspace is open.
        ResetVisibleCancellation();
        StartAudioWatcher();

        OverlayMonitorInfo monitor;
        string? monitorLookupNotice = null;

        try
        {
            monitor = WindowsOverlayMonitor.GetPointerMonitorInfo();
        }
        catch (Win32Exception pointerException)
        {
            // GetCursorPos can be denied by a protected/elevated foreground
            // surface.  Keep the overlay usable by resolving the monitor from
            // our own window before falling back to one safe monitor.
            try
            {
                if (_windowHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "The main window handle is not ready.");
                }

                monitor = WindowsOverlayMonitor.GetWindowMonitorInfo(_windowHandle);
                monitorLookupNotice =
                    "Pointer location was unavailable; opened on the WinQuickSwitch monitor.";
            }
            catch (Exception windowException)
            {
                try
                {
                    monitor = WindowsOverlayMonitor.GetFallbackMonitorInfo();
                    monitorLookupNotice =
                        "Windows did not provide a monitor location; opened on the primary monitor.";
                }
                catch (Win32Exception fallbackMonitorException)
                {
                    OptionsStatusText.Text =
                        $"Overlay could not determine a monitor: {pointerException.Message} " +
                        $"({windowException.Message}; {fallbackMonitorException.Message})";
                    _ = ShowWidgetAsync();
                    return;
                }
            }
        }

        _overlayWorkspaceMonitorId = string.IsNullOrWhiteSpace(monitor.DeviceName)
            ? $"{monitor.Bounds.Left}:{monitor.Bounds.Top}:{monitor.Bounds.Right}:{monitor.Bounds.Bottom}"
            : monitor.DeviceName;

        try
        {
            OverlayWorkspaceWindow workspace = new(
                closeRequested: CloseOverlayWorkspace,
                commandRequested: HandleWorkspaceCommandRequested,
                widgetRequested: HandleWorkspaceWidgetRequested,
                inspectorClosed: RestoreWorkspaceInspectorPanel,
                layoutChanged: HandleWorkspaceLayoutChanged,
                inspectorWidthChanged: HandleWorkspaceInspectorWidthChanged,
                monitorBounds: monitor.Bounds,
                dimmingOpacity: _widgetSettings.OverlayDimmingOpacity,
                launcherWidth: 300);

            _overlayWorkspace = workspace;
            workspace.Closed += OverlayWorkspace_Closed;
            workspace.SetProfilesAvailable(_profileFeature.IsEnabled);
            workspace.SetWidgets(BuildWorkspaceWidgets());
            PublishSharedState(QuickSwitchRefreshScope.All);
            RestoreWorkspaceLayout(workspace);
            workspace.Show();
            workspace.Activate();
            workspace.FocusLauncher();
            _ = InitializeOverlaySharedStateAsync(workspace);

            if (!string.IsNullOrWhiteSpace(monitorLookupNotice))
            {
                workspace.SetFeedback(
                    monitorLookupNotice,
                    OverlayWorkspaceFeedbackSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            OptionsStatusText.Text =
                $"Overlay could not open: {exception.Message}";
            CloseOverlayWorkspace();
            _ = ShowWidgetAsync();
        }
    }

    private async Task InitializeOverlaySharedStateAsync(
        OverlayWorkspaceWindow workspace)
    {
        try
        {
            RefreshDisplayTopology();
            await Task.WhenAll(
                RefreshAudioInventoryAsync(),
                RefreshWirelessRadioStateAsync(),
                RefreshDeviceInventoryAsync());
            if (ReferenceEquals(_overlayWorkspace, workspace) && workspace.IsVisible)
            {
                PublishSharedState(QuickSwitchRefreshScope.All);
                workspace.FitPendingContent();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_overlayWorkspace, workspace) &&
                workspace.IsVisible)
            {
                workspace.SetFeedback(
                    $"The workspace could not finish loading: {exception.Message}",
                    OverlayWorkspaceFeedbackSeverity.Error);
            }
        }
    }

    private void CloseOverlayWorkspace()
    {
        Interlocked.Increment(ref _overlayNavigationRevision);
        OverlayWorkspaceWindow? workspace = _overlayWorkspace;

        if (workspace is null)
        {
            return;
        }

        if (workspace.IsInspectorOpen)
        {
            workspace.HideInspector();
        }
        else
        {
            RestoreWorkspaceInspectorPanel();
        }

        // Capture the live card bounds before WPF starts tearing down the
        // window.  OnClosed also reports the layout, but saving this snapshot
        // first prevents a late SizeChanged/refresh callback from replacing a
        // user's final drag or resize with the startup arrangement.
        HandleWorkspaceLayoutChanged(workspace.CaptureLayout());

        workspace.Close();

        workspace.Closed -= OverlayWorkspace_Closed;
        _overlayWorkspace = null;
        _overlayWorkspaceMonitorId = null;
        StopAudioWatcher();
    }

    private void OverlayWorkspace_Closed(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _overlayNavigationRevision);
        if (sender is OverlayWorkspaceWindow workspace &&
            ReferenceEquals(workspace, _overlayWorkspace))
        {
            HandleWorkspaceLayoutChanged(workspace.CaptureLayout());
            workspace.Closed -= OverlayWorkspace_Closed;
            _overlayWorkspace = null;
            _overlayWorkspaceMonitorId = null;
            StopAudioWatcher();
        }
    }

    private void RequestQuitFromOverlay() =>
        Dispatcher.BeginInvoke(QuitWidget, DispatcherPriority.Normal);

    private async void HandleWorkspaceCommandRequested(
        QuickSwitchCommand command)
    {
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (command is ExitApplicationCommand)
        {
            RequestQuitFromOverlay();
            return;
        }

        if (command is NavigateToOverlayFeatureCommand navigate)
        {
            if (!FeatureDescriptorCatalog.TryGetFeature(
                    navigate.Feature,
                    _productConfiguration,
                    out _) ||
                !FeatureDescriptorCatalog.TryGetWorkspacePanel(
                    navigate.Feature,
                    out OverlayWorkspacePanel panel))
            {
                _overlayWorkspace?.SetFeedback(
                    "That feature is not available in this edition.",
                    OverlayWorkspaceFeedbackSeverity.Warning);
                return;
            }

            long revision = Interlocked.Increment(
                ref _overlayNavigationRevision);
            await HandleWorkspacePanelRequestedAsync(panel, revision);
            return;
        }

        await ExecuteSharedCommandAsync(command, _lifetimeCancellation.Token);
    }

    private async Task HandleWorkspacePanelRequestedAsync(
        OverlayWorkspacePanel panel,
        long navigationRevision)
    {
        if (_overlayWorkspace is not { IsVisible: true } workspace)
        {
            return;
        }

        if (workspace.IsInspectorOpen)
        {
            workspace.HideInspector();
        }

        workspace.SetFeedback(null);

        if (panel == OverlayWorkspacePanel.Home)
        {
            return;
        }

        try
        {
            switch (panel)
            {
                case OverlayWorkspacePanel.Display:
                    RefreshDisplayTopology();
                    break;
                case OverlayWorkspacePanel.Audio:
                    StartAudioWatcher();
                    await RefreshAudioInventoryAsync();
                    break;
                case OverlayWorkspacePanel.Devices:
                    await Task.WhenAll(
                        RefreshWirelessRadioStateAsync(),
                        RefreshDeviceInventoryAsync());
                    break;
                case OverlayWorkspacePanel.Options:
                    UpdateOptionsControls();
                    RefreshTaskbarState();
                    break;
                case OverlayWorkspacePanel.Profiles:
                    if (_profileFeature.IsEnabled)
                    {
                        UpdateProfilesControls();
                    }

                    break;
                case OverlayWorkspacePanel.Home:
                case OverlayWorkspacePanel.About:
                case OverlayWorkspacePanel.Favorites:
                    break;
            }

            if (navigationRevision != Volatile.Read(
                    ref _overlayNavigationRevision) ||
                !ReferenceEquals(_overlayWorkspace, workspace) ||
                !workspace.IsVisible ||
                workspace.CurrentPanel != panel)
            {
                return;
            }

            PublishSharedState(QuickSwitchRefreshScope.All);
            OverlayWorkspaceWidget? widget = CreateWorkspaceWidgetForPanel(panel);
            if (widget is not null)
            {
                workspace.ShowInspector(
                    widget.Panel,
                    widget.Title,
                    GetWorkspaceInspectorDescription(widget.Panel),
                    BuildWorkspaceCompleteInspectorContent(widget),
                    "The right navigation contains complete controls. Esc returns to quick widgets.");
            }
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            // Application shutdown is already in progress.
        }
        catch (Exception exception)
        {
            workspace.SetFeedback(
                $"{GetWorkspacePanelTitle(panel)} could not refresh: {exception.Message}",
                OverlayWorkspaceFeedbackSeverity.Error);
        }
    }

    private async void HandleWorkspaceWidgetRequested(string widgetId)
    {
        if (_overlayWorkspace is not { IsVisible: true } workspace)
        {
            return;
        }

        OverlayWorkspaceWidget? widget = CreateWorkspaceWidgetForId(widgetId);

        if (widget is null)
        {
            return;
        }

        OverlayWorkspaceWidgetLayout? layout = workspace
            .CaptureLayout()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, widgetId, StringComparison.OrdinalIgnoreCase));

        // The shell uses this callback for both Expand and Hide.  A hidden
        // card is restored by the launcher, but it should not immediately
        // reopen its inspector.
        if (layout is { IsVisible: false })
        {
            workspace.SetFeedback(
                $"{widget.Title} removed from quick widgets. Open it from the right navigation and choose Add quick widget to restore it.",
                OverlayWorkspaceFeedbackSeverity.Information);
            return;
        }

        try
        {
            if (widget.Panel == OverlayWorkspacePanel.Audio)
            {
                StartAudioWatcher();
                await RefreshAudioInventoryAsync();
            }
            else if (widget.Panel == OverlayWorkspacePanel.Devices)
            {
                await Task.WhenAll(
                    RefreshWirelessRadioStateAsync(),
                    RefreshDeviceInventoryAsync());
            }
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        workspace.ShowInspector(
            widget.Panel,
            widget.Title,
            GetWorkspaceInspectorDescription(widget.Panel),
            BuildWorkspaceCompleteInspectorContent(widget),
            "The right navigation contains complete controls. Esc returns to quick widgets.");
    }

    private IReadOnlyList<OverlayWorkspaceWidget> BuildWorkspaceWidgets()
    {
        List<OverlayWorkspaceWidget> widgets =
        [
            CreateWorkspaceWidget(
                OverlayFeatureId.Display,
                OverlayWorkspacePanel.Display,
                0),
            CreateWorkspaceWidget(
                OverlayFeatureId.Audio,
                OverlayWorkspacePanel.Audio,
                1),
            CreateWorkspaceWidget(
                OverlayFeatureId.Devices,
                OverlayWorkspacePanel.Devices,
                2),
            CreateWorkspaceWidget(
                OverlayFeatureId.Options,
                OverlayWorkspacePanel.Options,
                4),
            CreateWorkspaceWidget(
                OverlayFeatureId.About,
                OverlayWorkspacePanel.About,
                5),
        ];

        if (_profileFeature.IsEnabled)
        {
            widgets.Insert(
                3,
                CreateWorkspaceWidget(
                    OverlayFeatureId.Profiles,
                    OverlayWorkspacePanel.Profiles,
                    3));
        }

        return widgets;
    }

    private OverlayWorkspaceWidget? CreateWorkspaceWidgetForId(string widgetId)
    {
        if (!Enum.TryParse(
                widgetId,
                ignoreCase: true,
                out OverlayWorkspacePanel panel) ||
            !string.Equals(
                panel.ToString(),
                widgetId,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CreateWorkspaceWidgetForPanel(panel);
    }

    private OverlayWorkspaceWidget? CreateWorkspaceWidgetForPanel(
        OverlayWorkspacePanel panel)
    {
        if (!TryGetWorkspaceWidgetDescriptor(
                panel,
                out OverlayFeatureDescriptor? descriptor) ||
            descriptor is null ||
            !TryGetWorkspaceWidgetZIndex(panel, out int zIndex))
        {
            return null;
        }

        return CreateWorkspaceWidget(descriptor, panel, zIndex);
    }

    private bool TryGetWorkspaceWidgetDescriptor(
        OverlayWorkspacePanel panel,
        out OverlayFeatureDescriptor? descriptor)
    {
        descriptor = null;
        if (!FeatureDescriptorCatalog.TryFromWorkspacePanel(
                panel,
                out OverlayFeatureId feature) ||
            feature == OverlayFeatureId.Home ||
            (feature == OverlayFeatureId.Profiles &&
                !_profileFeature.IsEnabled) ||
            !FeatureDescriptorCatalog.TryGetFeature(
                feature,
                _productConfiguration,
                out descriptor) ||
            descriptor is null ||
            !descriptor.HasOverlayWidget)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetWorkspaceWidgetZIndex(
        OverlayWorkspacePanel panel,
        out int zIndex)
    {
        switch (panel)
        {
            case OverlayWorkspacePanel.Display:
                zIndex = 0;
                return true;
            case OverlayWorkspacePanel.Audio:
                zIndex = 1;
                return true;
            case OverlayWorkspacePanel.Devices:
                zIndex = 2;
                return true;
            case OverlayWorkspacePanel.Profiles:
                zIndex = 3;
                return true;
            case OverlayWorkspacePanel.Options:
                zIndex = 4;
                return true;
            case OverlayWorkspacePanel.About:
                zIndex = 5;
                return true;
            default:
                zIndex = 0;
                return false;
        }
    }

    private OverlayWorkspaceWidget CreateWorkspaceWidget(
        OverlayFeatureId feature,
        OverlayWorkspacePanel panel,
        int zIndex)
    {
        if (!FeatureDescriptorCatalog.TryGetFeature(
                feature,
                _productConfiguration,
                out OverlayFeatureDescriptor? descriptor) ||
            descriptor is null)
        {
            throw new InvalidOperationException(
                $"The {feature} workspace feature is unavailable.");
        }

        return CreateWorkspaceWidget(descriptor, panel, zIndex);
    }

    private OverlayWorkspaceWidget CreateWorkspaceWidget(
        OverlayFeatureDescriptor descriptor,
        OverlayWorkspacePanel panel,
        int zIndex)
    {
        OverlayFeatureId feature = descriptor.Id;

        return new(
            panel.ToString(),
            panel,
            descriptor.Title,
            string.Empty,
            Actions: [],
            Content: new DeferredFeatureContent(
                () => CreateSharedFeatureView(
                    feature,
                    SharedFeatureSurface.OverlayWidget)),
            Layout: DefaultWorkspaceCardLayout(panel, zIndex),
            CanResize: descriptor.CanResize,
            CanCollapse: panel == OverlayWorkspacePanel.Devices,
            MinimumWidth: descriptor.MinimumWidth,
            MinimumHeight: descriptor.MinimumHeight);
    }

    private UIElement CreateSharedFeatureView(
        OverlayFeatureId feature,
        SharedFeatureSurface surface) =>
        SharedFeatureViewFactory.Create(feature, surface).View;

    private OverlayWorkspaceWidgetLayout DefaultWorkspaceCardLayout(
        OverlayWorkspacePanel panel,
        int zIndex)
    {
        double width = OverlayWidgetPlacement.DefaultWidth;
        double height = OverlayWidgetPlacement.DefaultHeight;
        if (Enum.TryParse(
                panel.ToString(),
                ignoreCase: true,
                out OverlayFeatureId feature) &&
            FeatureDescriptorCatalog.TryGetFeature(
                feature,
                _productConfiguration,
                out OverlayFeatureDescriptor? descriptor) &&
            descriptor is not null)
        {
            width = descriptor.DefaultWidth;
            height = descriptor.DefaultHeight;
        }

        return new(
            panel.ToString(),
            24,
            24,
            width,
            height,
            IsCollapsed: false,
            ZIndex: zIndex,
            IsVisible: panel is OverlayWorkspacePanel.Display or
                OverlayWorkspacePanel.Audio or
                OverlayWorkspacePanel.Devices);
    }

    private void RestoreWorkspaceLayout(OverlayWorkspaceWindow workspace)
    {
        OverlayWorkspaceSettings settings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled);
        workspace.SetInspectorWidth(settings.InspectorWidth);

        if (_overlayWorkspaceMonitorId is not string monitorId ||
            settings.MonitorLayouts is null)
        {
            return;
        }

        OverlayMonitorLayout? saved = settings.FindSavedLayout(monitorId);

        if (saved is null)
        {
            // Layouts are monitor-scoped.  Do not restore the legacy
            // virtual-screen bucket on an unknown monitor: it can contain
            // stale dimensions and positions from a different work area.
            // The workspace will arrange its current feature presets and save
            // them under this monitor after WPF has measured it.
            return;
        }

        workspace.RestoreLayout(
            (saved.Widgets ?? [])
            .Select(CreateRestoredWorkspaceLayout));
    }

    private static OverlayWorkspaceWidgetLayout CreateRestoredWorkspaceLayout(
        OverlayWidgetPlacement placement)
    {
        return new(
            placement.Widget.ToString(),
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            placement.IsCollapsed,
            placement.ZIndex,
            placement.IsVisible,
            placement.HasCustomSize,
            placement.ContentLayoutVersion);
    }

    private void HandleWorkspaceLayoutChanged(
        IReadOnlyList<OverlayWorkspaceWidgetLayout> layouts)
    {
        if (_overlayWorkspaceMonitorId is not string monitorId ||
            string.IsNullOrWhiteSpace(monitorId))
        {
            return;
        }

        List<OverlayWidgetPlacement> placements = [];

        foreach (OverlayWorkspaceWidgetLayout layout in layouts)
        {
            if (!Enum.TryParse(
                    layout.Id,
                    ignoreCase: true,
                    out OverlayWidgetType widget))
            {
                continue;
            }

            placements.Add(
                new OverlayWidgetPlacement(
                    widget,
                    layout.Left,
                    layout.Top,
                    layout.Width,
                    layout.Height,
                    layout.IsVisible,
                    layout.IsCollapsed,
                    layout.ZIndex,
                    layout.HasCustomSize,
                    layout.ContentLayoutVersion));
        }

        OverlayMonitorLayout monitorLayout =
            new OverlayMonitorLayout(monitorId, placements)
                .Normalize(_profileFeature.IsEnabled);
        OverlayWorkspaceSettings settings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled)
            .WithLayout(monitorLayout);
        _widgetSettings = _widgetSettings with { OverlayWorkspace = settings };
        SaveSettings("Overlay layout saved.");
    }

    private void HandleWorkspaceInspectorWidthChanged(double width)
    {
        OverlayWorkspaceSettings settings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled) with
            {
                InspectorWidth = width,
            };
        _widgetSettings = _widgetSettings with { OverlayWorkspace = settings };
        SaveSettings("Overlay inspector size saved.");
    }

    private static string GetWorkspacePanelTitle(OverlayWorkspacePanel panel) =>
        panel switch
        {
            OverlayWorkspacePanel.Home => "Home",
            OverlayWorkspacePanel.Display => "Display",
            OverlayWorkspacePanel.Audio => "Audio",
            OverlayWorkspacePanel.Devices => "Devices",
            OverlayWorkspacePanel.Options => "Options",
            OverlayWorkspacePanel.Profiles => "Profiles",
            OverlayWorkspacePanel.About => "About",
            _ => panel.ToString(),
        };

    private static string GetWorkspaceInspectorDescription(
        OverlayWorkspacePanel panel) =>
        panel switch
        {
            OverlayWorkspacePanel.Home =>
                "Choose a feature, restore hidden cards, or reset this monitor's layout.",
            OverlayWorkspacePanel.Display =>
                "Switch projection modes and open display diagnostics without leaving the overlay.",
            OverlayWorkspacePanel.Audio =>
                "Apply favorites, refresh endpoints, and open Windows audio controls.",
            OverlayWorkspacePanel.Devices =>
                "Refresh radios and connected devices, or open the relevant Windows settings page.",
            OverlayWorkspacePanel.Options =>
                "Change appearance, startup, pinning, taskbar, overlay, and shortcut behavior.",
            OverlayWorkspacePanel.Profiles =>
                "Apply saved desktop profiles. Warnings remain visible so partial results are not hidden.",
            OverlayWorkspacePanel.About =>
                "Review edition, licensing, privacy, and responsible-use information.",
            _ => "Complete feature controls.",
        };

    private UIElement BuildWorkspaceCompleteInspectorContent(
        OverlayWorkspaceWidget widget)
    {
        RestoreWorkspaceInspectorPanel();

        if (Enum.TryParse(
                widget.Panel.ToString(),
                ignoreCase: true,
                out OverlayFeatureId feature) &&
            SharedFeatureViewFactory.CanCreate(feature))
        {
            return CreateSharedFeatureView(
                feature,
                SharedFeatureSurface.OverlaySidebar);
        }

        throw new InvalidOperationException(
            $"The {widget.Panel} workspace feature has no shared view.");
    }

    private void RestoreWorkspaceInspectorPanel()
    {
        // Shared feature views are fresh per host. No WPF element is reparented.
    }

    private void ResetWorkspaceLayout()
    {
        if (_overlayWorkspace is not { IsVisible: true } workspace ||
            _overlayWorkspaceMonitorId is not string monitorId)
        {
            return;
        }

        workspace.RestoreLayout([]);
        OverlayWorkspaceSettings settings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled)
            .WithLayout(new OverlayMonitorLayout(monitorId, []));
        _widgetSettings = _widgetSettings with { OverlayWorkspace = settings };
        SaveSettings("Overlay layout reset.");
        workspace.SetFeedback(
            "This monitor's widget layout was reset.",
            OverlayWorkspaceFeedbackSeverity.Success);
    }

    private void OpenFloatingDisplayWidget_Click(
        object sender,
        RoutedEventArgs e) =>
        ToggleFloatingDisplayWidget();

    private void ToggleFloatingDisplayWidget()
    {
        if (_floatingDisplayWidget is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        FloatingWidgetLayout layout =
            (_widgetSettings.FloatingDisplayWidget ?? FloatingWidgetLayout.Default)
            .Normalize();
        if (layout.Left is null && layout.Top is null &&
            layout.Width == FloatingWidgetLayout.DefaultWidth &&
            layout.Height == FloatingWidgetLayout.DefaultHeight)
        {
            layout = layout with { Width = 720, Height = 460 };
        }
        FloatingDisplayWidgetWindow widget = new(
            layout,
            closeRequested: CloseFloatingDisplayWidget,
            reattachRequested: ReattachFloatingDisplayWidget,
            resetLayoutRequested: ResetFloatingDisplayWidgetLayout,
            displayActionRequested: mode => _ = ApplyDisplayModeAsync(mode),
            layoutChanged: FloatingDisplayWidget_LayoutChanged);

        _floatingDisplayWidget = widget;
        widget.Closed += FloatingDisplayWidget_Closed;
        widget.SetFeatureContent(CreateSharedFeatureView(
            OverlayFeatureId.Display,
            SharedFeatureSurface.FloatingWindow));

        try
        {
            widget.Show();
            KeepFloatingWidgetInsideWorkArea(widget);
            widget.Activate();
        }
        catch (Win32Exception exception)
        {
            OptionsStatusText.Text =
                $"Floating display widget could not open: {exception.Message}";
            CloseFloatingDisplayWidget();
        }
    }

    private void FloatingDisplayWidget_LayoutChanged(
        FloatingWidgetLayout layout)
    {
        _widgetSettings = _widgetSettings with
        {
            FloatingDisplayWidget = layout.Normalize(),
        };
    }

    private void FloatingDisplayWidget_Closed(object? sender, EventArgs e)
    {
        if (sender is not FloatingDisplayWidgetWindow widget ||
            !ReferenceEquals(widget, _floatingDisplayWidget))
        {
            return;
        }

        FloatingDisplayWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        widget.Closed -= FloatingDisplayWidget_Closed;
        _floatingDisplayWidget = null;
        SaveSettings("Floating display widget closed.");
    }

    private void ReattachFloatingDisplayWidget()
    {
        CloseFloatingDisplayWidget();
        _ = ShowWidgetAsync(WidgetPanel.Display);
    }

    private void ResetFloatingDisplayWidgetLayout()
    {
        CloseFloatingDisplayWidget();
        _widgetSettings = _widgetSettings with
        {
            FloatingDisplayWidget = FloatingWidgetLayout.Default with
            {
                Width = 720,
                Height = 460,
            },
        };
        SaveSettings("Floating display widget layout reset.");
        ToggleFloatingDisplayWidget();
    }

    private void CloseFloatingDisplayWidget()
    {
        FloatingDisplayWidgetWindow? widget = _floatingDisplayWidget;

        if (widget is null)
        {
            return;
        }

        FloatingDisplayWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        _floatingDisplayWidget = null;
        widget.Closed -= FloatingDisplayWidget_Closed;

        widget.Close();

        SaveSettings("Floating display widget closed.");
    }

    private void KeepFloatingWidgetInsideWorkArea(
        FloatingDisplayWidgetWindow widget)
    {
        try
        {
            widget.UpdateLayout();
            PresentationSource? source = PresentationSource.FromVisual(widget);
            Matrix toDevice =
                source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Matrix fromDevice =
                source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point pixelOrigin = toDevice.Transform(
                new Point(widget.Left, widget.Top));
            Vector pixelSize = toDevice.Transform(
                new Vector(
                    widget.ActualWidth > 0 ? widget.ActualWidth : widget.Width,
                    widget.ActualHeight > 0 ? widget.ActualHeight : widget.Height));
            ScreenRectangle workArea = _placementService.GetWindowWorkArea(
                new WindowInteropHelper(widget).Handle);
            ScreenPoint clamped = WidgetPlacementCalculator.ClampToWorkArea(
                new ScreenPoint(
                    (int)Math.Round(pixelOrigin.X),
                    (int)Math.Round(pixelOrigin.Y)),
                workArea,
                Math.Max(1, (int)Math.Ceiling(pixelSize.X)),
                Math.Max(1, (int)Math.Ceiling(pixelSize.Y)));
            Point dipPosition = fromDevice.Transform(
                new Point(clamped.X, clamped.Y));

            widget.Left = dipPosition.X;
            widget.Top = dipPosition.Y;
        }
        catch (Win32Exception)
        {
            widget.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void OpenFloatingAudioWidget_Click(
        object sender,
        RoutedEventArgs e) =>
        ToggleFloatingAudioWidget();

    private void ToggleFloatingAudioWidget()
    {
        if (_floatingAudioWidget is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        FloatingWidgetLayout layout =
            (_widgetSettings.FloatingAudioWidget ?? FloatingWidgetLayout.Default)
            .Normalize();
        if (layout.Left is null && layout.Top is null &&
            layout.Width == FloatingWidgetLayout.DefaultWidth &&
            layout.Height == FloatingWidgetLayout.DefaultHeight)
        {
            layout = layout with { Width = 760, Height = 760 };
        }
        FloatingQuickWidgetWindow widget = new(
            "Audio favorites",
            layout,
            closeRequested: CloseFloatingAudioWidget,
            resetLayoutRequested: ResetFloatingAudioWidgetLayout,
            layoutChanged: FloatingAudioWidget_LayoutChanged);

        _floatingAudioWidget = widget;
        widget.Closed += FloatingAudioWidget_Closed;
        widget.SetFeatureContent(CreateSharedFeatureView(
            OverlayFeatureId.Audio,
            SharedFeatureSurface.FloatingWindow));

        try
        {
            widget.Show();
            KeepFloatingQuickWidgetInsideWorkArea(widget);
            widget.Activate();
        }
        catch (Win32Exception exception)
        {
            OptionsStatusText.Text =
                $"Floating audio widget could not open: {exception.Message}";
            CloseFloatingAudioWidget();
        }
    }

    private void UpdateFloatingAudioWidget()
    {
        PublishSharedState(
            QuickSwitchRefreshScope.Audio |
            QuickSwitchRefreshScope.Favorites);
    }

    private void FloatingAudioWidget_LayoutChanged(FloatingWidgetLayout layout)
    {
        _widgetSettings = _widgetSettings with
        {
            FloatingAudioWidget = layout.Normalize(),
        };
    }

    private void FloatingAudioWidget_Closed(object? sender, EventArgs e)
    {
        if (sender is not FloatingQuickWidgetWindow widget ||
            !ReferenceEquals(widget, _floatingAudioWidget))
        {
            return;
        }

        FloatingAudioWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        widget.Closed -= FloatingAudioWidget_Closed;
        _floatingAudioWidget = null;
        SaveSettings("Floating audio widget closed.");
    }

    private void ResetFloatingAudioWidgetLayout()
    {
        CloseFloatingAudioWidget();
        _widgetSettings = _widgetSettings with
        {
            FloatingAudioWidget = FloatingWidgetLayout.Default with
            {
                Width = 760,
                Height = 760,
            },
        };
        SaveSettings("Floating audio widget layout reset.");
        ToggleFloatingAudioWidget();
    }

    private void CloseFloatingAudioWidget()
    {
        FloatingQuickWidgetWindow? widget = _floatingAudioWidget;

        if (widget is null)
        {
            return;
        }

        FloatingAudioWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        _floatingAudioWidget = null;
        widget.Closed -= FloatingAudioWidget_Closed;

        widget.Close();

        SaveSettings("Floating audio widget closed.");
    }

    private void OpenFloatingDevicesWidget_Click(
        object sender,
        RoutedEventArgs e) =>
        ToggleFloatingDevicesWidget();

    private void ToggleFloatingDevicesWidget()
    {
        if (_floatingDevicesWidget is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        FloatingWidgetLayout layout =
            (_widgetSettings.FloatingDevicesWidget ?? FloatingWidgetLayout.Default)
            .Normalize();
        if (layout.Left is null && layout.Top is null &&
            layout.Width == FloatingWidgetLayout.DefaultWidth &&
            layout.Height == FloatingWidgetLayout.DefaultHeight)
        {
            layout = layout with { Width = 840, Height = 720 };
        }
        FloatingQuickWidgetWindow widget = new(
            "Wireless devices",
            layout,
            closeRequested: CloseFloatingDevicesWidget,
            resetLayoutRequested: ResetFloatingDevicesWidgetLayout,
            layoutChanged: FloatingDevicesWidget_LayoutChanged);

        _floatingDevicesWidget = widget;
        widget.Closed += FloatingDevicesWidget_Closed;
        widget.SetFeatureContent(CreateSharedFeatureView(
            OverlayFeatureId.Devices,
            SharedFeatureSurface.FloatingWindow));

        try
        {
            widget.Show();
            KeepFloatingQuickWidgetInsideWorkArea(widget);
            widget.Activate();
            _ = RefreshWirelessRadioStateAsync();
        }
        catch (Win32Exception exception)
        {
            OptionsStatusText.Text =
                $"Floating devices widget could not open: {exception.Message}";
            CloseFloatingDevicesWidget();
        }
    }

    private void UpdateFloatingDevicesWidget()
    {
        PublishSharedState(QuickSwitchRefreshScope.Devices);
    }

    private void FloatingDevicesWidget_LayoutChanged(FloatingWidgetLayout layout)
    {
        _widgetSettings = _widgetSettings with
        {
            FloatingDevicesWidget = layout.Normalize(),
        };
    }

    private void FloatingDevicesWidget_Closed(object? sender, EventArgs e)
    {
        if (sender is not FloatingQuickWidgetWindow widget ||
            !ReferenceEquals(widget, _floatingDevicesWidget))
        {
            return;
        }

        FloatingDevicesWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        widget.Closed -= FloatingDevicesWidget_Closed;
        _floatingDevicesWidget = null;
        SaveSettings("Floating devices widget closed.");
    }

    private void ResetFloatingDevicesWidgetLayout()
    {
        CloseFloatingDevicesWidget();
        _widgetSettings = _widgetSettings with
        {
            FloatingDevicesWidget = FloatingWidgetLayout.Default with
            {
                Width = 840,
                Height = 720,
            },
        };
        SaveSettings("Floating devices widget layout reset.");
        ToggleFloatingDevicesWidget();
    }

    private void CloseFloatingDevicesWidget()
    {
        FloatingQuickWidgetWindow? widget = _floatingDevicesWidget;

        if (widget is null)
        {
            return;
        }

        FloatingDevicesWidget_LayoutChanged(
            widget.CaptureLayout() with { IsOpen = false });
        _floatingDevicesWidget = null;
        widget.Closed -= FloatingDevicesWidget_Closed;

        widget.Close();

        SaveSettings("Floating devices widget closed.");
    }

    private void KeepFloatingQuickWidgetInsideWorkArea(
        FloatingQuickWidgetWindow widget)
    {
        try
        {
            widget.UpdateLayout();
            PresentationSource? source = PresentationSource.FromVisual(widget);
            Matrix toDevice =
                source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Matrix fromDevice =
                source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point pixelOrigin = toDevice.Transform(
                new Point(widget.Left, widget.Top));
            Vector pixelSize = toDevice.Transform(
                new Vector(
                    widget.ActualWidth > 0 ? widget.ActualWidth : widget.Width,
                    widget.ActualHeight > 0 ? widget.ActualHeight : widget.Height));
            ScreenRectangle workArea = _placementService.GetWindowWorkArea(
                new WindowInteropHelper(widget).Handle);
            ScreenPoint clamped = WidgetPlacementCalculator.ClampToWorkArea(
                new ScreenPoint(
                    (int)Math.Round(pixelOrigin.X),
                    (int)Math.Round(pixelOrigin.Y)),
                workArea,
                Math.Max(1, (int)Math.Ceiling(pixelSize.X)),
                Math.Max(1, (int)Math.Ceiling(pixelSize.Y)));
            Point dipPosition = fromDevice.Transform(
                new Point(clamped.X, clamped.Y));

            widget.Left = dipPosition.X;
            widget.Top = dipPosition.Y;
        }
        catch (Win32Exception)
        {
            widget.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static string FormatWirelessRadioState(
        WirelessRadioState? state) =>
        state switch
        {
            WirelessRadioState.On => "on",
            WirelessRadioState.Off => "off",
            WirelessRadioState.Disabled => "locked",
            WirelessRadioState.Unavailable => "unavailable",
            _ => "reading...",
        };

    private async Task ApplyDisplayModeFromHotkeyAsync(DisplayMode mode)
    {
        await ApplyDisplayModeAsync(mode);
    }

    private async Task<AudioControlResult?> ApplyFavoriteOutputFromHotkeyAsync(int slot)
    {
        FavoriteOutputSetting? favorite = _widgetSettings.GetFavorite(slot);

        if (favorite is null)
        {
            return null;
        }

        return await ApplyFavoriteEndpointFromHotkeyAsync(
            favorite.EndpointId,
            favorite.Name,
            favorite.Role);
    }

    private async Task<AudioControlResult?> ApplyFavoriteInputFromHotkeyAsync(int slot)
    {
        FavoriteInputSetting? favorite = _widgetSettings.GetInputFavorite(slot);

        if (favorite is null)
        {
            return null;
        }

        return await ApplyFavoriteEndpointFromHotkeyAsync(
            favorite.EndpointId,
            favorite.Name,
            favorite.Role);
    }

    private async Task<AudioControlResult?> ApplyFavoriteEndpointFromHotkeyAsync(
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
            return null;
        }

        if (!result.Succeeded)
        {
            if (_overlayWorkspace is null)
            {
                await ShowWidgetAsync(WidgetPanel.Audio);
            }
        }

        AudioStatusText.Text = result.Message;
        _sharedAudioStatus = result.Message;

        if (IsVisible && _activePanel == WidgetPanel.Audio)
        {
            _audioRefreshScheduler.Schedule();
        }

        return result;
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

    private async void PanelTab_Click(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized ||
            _isApplyingPanelSelection ||
            sender is not ToggleButton { Tag: string panelName } ||
            !Enum.TryParse(
                panelName,
                ignoreCase: true,
                out WidgetPanel panel))
        {
            return;
        }

        await ActivatePanelAsync(panel);
    }

    private async Task ActivatePanelAsync(WidgetPanel panel)
    {
        if (panel == WidgetPanel.Profiles && !_profileFeature.IsEnabled)
        {
            return;
        }

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
        AboutPanel.Visibility =
            panel == WidgetPanel.About ? Visibility.Visible : Visibility.Collapsed;

        _isApplyingPanelSelection = true;

        try
        {
            DisplayPanelTab.IsChecked = panel == WidgetPanel.Display;
            AudioPanelTab.IsChecked = panel == WidgetPanel.Audio;
            DevicesPanelTab.IsChecked = panel == WidgetPanel.Devices;
            OptionsPanelTab.IsChecked = panel == WidgetPanel.Options;
            ProfilesPanelTab.IsChecked = panel == WidgetPanel.Profiles;
            AboutPanelTab.IsChecked = panel == WidgetPanel.About;
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
            case WidgetPanel.About:
                AboutPanelTab.Focus();
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
        // TrackPopupMenu still owns foreground activation while its callback
        // is running. Defer the reveal until the native tray menu has fully
        // returned, matching the existing deferred Exit path.
        QueueTrayCommand(
            QuickSwitchCommandCatalog.OpenMainWindow.CreateCommand());
    }

    private void TrayIcon_QuitRequested(object? sender, EventArgs e)
    {
        // The tray callback runs while TrackPopupMenu is still unwinding. Defer
        // window destruction until the native menu has returned so the Exit
        // command cannot be swallowed by teardown of its owner window.
        QueueTrayCommand(
            QuickSwitchCommandCatalog.ExitApplication.CreateCommand());
    }

    private void TrayIcon_FavoriteRequested(
        object? sender,
        TrayFavoriteRequestedEventArgs e)
    {
        List<TrayFavoriteTarget> targets = GetTrayFavoriteTargets();
        if (e.Index < 0 || e.Index >= targets.Count)
        {
            return;
        }

        TrayFavoriteTarget target = targets[e.Index];
        if (!QuickSwitchCommandCatalog.TryGetLegacyHotkey(
                target.Action,
                _productConfiguration,
                out QuickSwitchCommandDescriptor descriptor))
        {
            return;
        }

        QueueTrayCommand(descriptor.CreateCommand());
    }

    private void QueueTrayCommand(QuickSwitchCommand command) =>
        Dispatcher.BeginInvoke(
            () => ExecuteTrayCommandAfterMenu(command),
            DispatcherPriority.Normal);

    private async void ExecuteTrayCommandAfterMenu(QuickSwitchCommand command)
    {
        if (_isExiting || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (command is ExitApplicationCommand)
        {
            QuitWidget();
            return;
        }

        try
        {
            await ExecuteSharedCommandAsync(command, _lifetimeCancellation.Token);
            if (command is OpenMainWindowCommand && IsVisible)
            {
                Activate();
            }
        }
        catch (OperationCanceledException) when (
            _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            OptionsStatusText.Text =
                $"Tray command could not complete: {exception.Message}";
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible &&
            !_isExiting &&
            !_isScopedValidationInstance &&
            !_widgetSettings.WindowPinned &&
            WindowState != WindowState.Minimized &&
            DateTime.UtcNow >= _autoHideAllowedAfterUtc)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (IsVisible &&
                        !IsActive &&
                        !_isExiting &&
                        !_widgetSettings.WindowPinned &&
                        WindowState != WindowState.Minimized)
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

    private void ViewLicense_Click(object sender, RoutedEventArgs e) =>
        ShowLegalDocument(LegalDocumentKind.Eula);

    private void ViewPrivacyPolicy_Click(object sender, RoutedEventArgs e) =>
        ShowLegalDocument(LegalDocumentKind.PrivacyPolicy);

    private void ShowLegalDocument(LegalDocumentKind kind)
    {
        _autoHideAllowedAfterUtc = DateTime.MaxValue;

        try
        {
            LegalDocument document = LegalDocuments.Get(kind);
            LegalDocumentWindow window = new(
                this,
                document,
                _widgetSettings.UseDarkTheme);
            window.ShowDialog();
        }
        finally
        {
            _autoHideAllowedAfterUtc =
                DateTime.UtcNow.AddMilliseconds(350);

            if (IsVisible)
            {
                Activate();
            }
        }
    }

    private void QuitWidget()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Close();

        // The resident window is normally hidden, and auxiliary floating
        // windows may have existed during the close. Request application
        // shutdown explicitly as a final guard so tray Exit always terminates
        // the resident process after the normal OnClosed cleanup runs.
        if (!Dispatcher.HasShutdownStarted &&
            !Dispatcher.HasShutdownFinished)
        {
            Application.Current?.Shutdown();
        }
    }

    private HotkeyRegistrationResult ApplyHotkeyBindings(
        WidgetSettings settings,
        ProfileCatalog? profileCatalog = null)
    {
        ProfileCatalog catalog = profileCatalog ?? _profileCatalog;
        List<ProfileHotkeyBinding> profileBindings = _profileFeature
            .GetShortcutBindings(catalog)
            .Select(binding => new ProfileHotkeyBinding(
                binding.ProfileId,
                binding.Shortcut,
                binding.DisplayName))
            .ToList();
        HotkeyRegistrationResult result =
            _globalHotkey.ApplyBindings(
                _windowHandle,
                settings,
                profileBindings,
                QuickSwitchCommandCatalog.GetKeyedCommands()
                    .Where(item => item.IsAvailable(_productConfiguration) && settings.GetCommandShortcut(item.Key) is not null)
                    .Select(item => new CommandHotkeyBinding(item.Key, item.Label, settings.GetCommandShortcut(item.Key)!))
                    .ToArray());

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
            OverlayEnabledCheckBox.IsChecked = _widgetSettings.OverlayEnabled;
            WindowPinnedCheckBox.IsChecked = _widgetSettings.WindowPinned;
            PinWindowButton.IsChecked = _widgetSettings.WindowPinned;
            Topmost = _widgetSettings.WindowPinned;
            StartWithWindowsCheckBox.IsChecked =
                _startupRegistrationService.IsEnabled;
            UpdateFavoriteOutputsSummary();
            UpdateQuickSummary();
            UpdateFloatingAudioWidget();
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

    private HotkeyRegistrationResult ApplyShortcutChange(
        WidgetHotkeyAction action,
        WidgetShortcut? shortcut)
    {
        WidgetSettings candidate =
            _widgetSettings.WithShortcut(action, shortcut);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);

        if (!result.Succeeded)
        {
            ApplyHotkeyBindings(_widgetSettings);
            UpdateOptionsControls();
            OptionsStatusText.Text =
                result.FirstFailure ?? "The shortcut could not be registered.";
            return result;
        }

        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(
            shortcut is null
                ? "Shortcut cleared."
                : $"{shortcut.DisplayText} is active.");
        return result;
    }

    private void DarkThemeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        SetDarkThemeFromOverlay(DarkThemeCheckBox.IsChecked == true);
    }

    private void SetDarkThemeFromOverlay(bool useDarkTheme)
    {
        _widgetSettings = _widgetSettings with
        {
            UseDarkTheme = useDarkTheme,
        };
        WidgetTheme.Apply(useDarkTheme, _windowHandle);
        UpdateOptionsControls();
        SaveSettings(useDarkTheme ? "Dark theme enabled." : "Light theme enabled.");
    }

    private void OverlayEnabledCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        SetOverlayEnabledFromOverlay(OverlayEnabledCheckBox.IsChecked == true);
    }

    private void SetOverlayEnabledFromOverlay(bool enabled)
    {
        _widgetSettings = _widgetSettings with
        {
            OverlayEnabled = enabled,
        };
        UpdateQuickSummary();

        if (!enabled)
        {
            CloseOverlayWorkspace();
        }

        SaveSettings(
            enabled
                ? "Sidebar overlay enabled. Win + Shift + Q will open it."
                : "Sidebar overlay disabled. Win + Shift + Q will open the compact window.");
    }

    private void WindowPinnedCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        bool pinned = sender is ToggleButton toggle
            ? toggle.IsChecked == true
            : WindowPinnedCheckBox.IsChecked == true;
        SetWindowPinnedFromOverlay(pinned);
    }

    private void SetWindowPinnedFromOverlay(bool pinned)
    {
        _widgetSettings = _widgetSettings with
        {
            WindowPinned = pinned,
        };
        _isApplyingSettingsUi = true;

        try
        {
            WindowPinnedCheckBox.IsChecked = pinned;
            PinWindowButton.IsChecked = pinned;
        }
        finally
        {
            _isApplyingSettingsUi = false;
        }

        Topmost = pinned;
        SaveSettings(
            pinned
                ? "Main window pinned open."
                : "Main window will hide when focus changes.");
    }

    private void StartWithWindowsCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isApplyingSettingsUi)
        {
            return;
        }

        SetStartupFromOverlay(StartWithWindowsCheckBox.IsChecked == true);
    }

    private void SetStartupFromOverlay(bool enabled)
    {
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

    private void SetTaskbarFromOverlay(bool enabled) =>
        SetTaskbarAutoHide(enabled);

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
            if (!result.Succeeded)
            {
                ApplyHotkeyBindings(_widgetSettings);
                UpdateOptionsControls();
                OptionsStatusText.Text = result.FirstFailure ??
                    "Favorite order could not be updated.";
                return;
            }
            _widgetSettings = candidate;
            UpdateOptionsControls();
            SaveSettings("Favorite order updated.");
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
            if (!result.Succeeded)
            {
                ApplyHotkeyBindings(_widgetSettings);
                UpdateOptionsControls();
                OptionsStatusText.Text = result.FirstFailure ??
                    "Favorite order could not be updated.";
                return;
            }
            _widgetSettings = candidate;
            UpdateOptionsControls();
            SaveSettings("Favorite order updated.");
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
            int? controlNumber = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 1,
                Key.D2 or Key.NumPad2 => 2,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                Key.D6 or Key.NumPad6 => 6,
                _ => null,
            };

            WidgetPanel? panel = controlNumber is int number
                ? WidgetPanelNavigation.FromControlNumber(
                    _productConfiguration,
                    number)
                : null;

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

    private void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (!_widgetSettings.OverlayEnabled)
        {
            OptionsStatusText.Text =
                "Enable the Quick workspace overlay in Options before opening it.";
            return;
        }

        ToggleSidebarOverlay();
    }

    private async void OpenAudioPanel_Click(object sender, RoutedEventArgs e) =>
        await ActivatePanelAsync(WidgetPanel.Audio);

    private async void OpenDevicesPanel_Click(object sender, RoutedEventArgs e) =>
        await ActivatePanelAsync(WidgetPanel.Devices);

    private void UpdateQuickSummary()
    {
        QuickDisplaySummaryText.Text = _currentDisplayMode?.GetDisplayName() ??
            "Windows is detecting the current mode.";

        QuickAudioSummaryText.Text = GetFavoriteAudioSummary();

        QuickDeviceSummaryText.Text = _deviceInventoryDirty
            ? "Open Devices to refresh."
            : "Device inventory ready.";

        OpenOverlayButton.IsEnabled = _widgetSettings.OverlayEnabled;
        OpenOverlayButton.ToolTip = _widgetSettings.OverlayEnabled
            ? $"Open the dimmed Quick workspace overlay (v{ProductConfiguration.ReleaseVersion})"
            : "Enable the Quick workspace overlay in Options first";

        string toggleShortcut = FormatShortcut(_widgetSettings.ToggleWidget);
        QuickShortcutHintText.Text = toggleShortcut == "Not set"
            ? "Set a show/hide shortcut in Options."
            : $"Shortcut: {toggleShortcut} opens WinQuickSwitch.";
    }

    private string GetFavoriteAudioSummary()
    {
        int outputFavorites = Enumerable.Range(
                0,
                WidgetSettings.MaximumFavoriteOutputs)
            .Count(slot => _widgetSettings.GetFavorite(slot) is not null);
        int inputFavorites = Enumerable.Range(
                0,
                WidgetSettings.MaximumFavoriteInputs)
            .Count(slot => _widgetSettings.GetInputFavorite(slot) is not null);

        return outputFavorites == 0 && inputFavorites == 0
            ? "No favorites saved yet."
            : $"{outputFavorites} output · {inputFavorites} input favorites";
    }

    private async Task<bool> ApplyDisplayModeAsync(DisplayMode mode)
    {
        DisplayTopologySnapshot currentSnapshot =
            _displayTopologyService.GetSnapshot();

        if (_currentDisplayMode == mode &&
            DisplayTransitionMonitor.IsModeSatisfied(currentSnapshot, mode))
        {
            SetDisplayModeSelection(mode);
            return true;
        }

        DisplayMode? previousMode = currentSnapshot.CurrentMode;
        bool succeeded = false;
        SetDisplayModeSelection(mode);
        _sharedDisplayBusy = true;
        DisplayModeButtons.IsEnabled = false;
        _sharedDisplayStatus = $"Switching to {mode.GetDisplayName()}...";
        DisplayStatusText.Text = _sharedDisplayStatus;
        PublishSharedState(QuickSwitchRefreshScope.Display);

        try
        {
            DisplayModeTransitionOutcome outcome =
                await DisplayModeTransitionExecutor.ExecuteAsync(
                    _displayModeService,
                    _displayTopologyService,
                    previousMode,
                    mode,
                    TimeSpan.FromMilliseconds(150),
                    bridgeMaximumAttempts: 12,
                    finalMaximumAttempts: 18,
                    requiredStableSnapshots: 3,
                    recoveryApplyCount: 1,
                    _lifetimeCancellation.Token);

            succeeded = outcome.Succeeded;
            ApplyDisplayTopologySnapshot(outcome.Snapshot);
            _sharedDisplayStatus = outcome.Message + _hotkeyStatusSuffix;
            DisplayStatusText.Text = _sharedDisplayStatus;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                _sharedDisplayBusy = false;
                DisplayModeButtons.IsEnabled = true;
                PublishSharedState(QuickSwitchRefreshScope.Display);
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
        _sharedDisplayTopology = snapshot;
        DisplayMode? settledMode = snapshot.CurrentMode is DisplayMode currentMode &&
            DisplayTransitionMonitor.IsModeSatisfied(snapshot, currentMode)
                ? currentMode
                : null;

        _currentDisplayMode = settledMode;
        _sharedDisplayStatus = snapshot.Status + _hotkeyStatusSuffix;
        DisplayStatusText.Text = _sharedDisplayStatus;
        UpdateQuickSummary();
        SetDisplayModeSelection(settledMode);

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
                    slot,
                    WidgetSettings.GetFavoriteAction(slot),
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
                    slot,
                    WidgetSettings.GetInputFavoriteAction(slot),
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
        if (_sharedProfilesStatus == ProfilesQuickSwitchState.Empty.StatusMessage)
        {
            _sharedProfilesStatus = options.Count == 0
                ? ProfilesQuickSwitchState.Empty.StatusMessage
                : $"{options.Count} saved profiles.";
        }

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
        SetSharedProfilesStatus($"Applying {profile.Name}...");
        PublishSharedState(QuickSwitchRefreshScope.Profiles);
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
                SetSharedProfilesStatus(
                    $"{profile.Name} could not change the display mode.");
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
                SetSharedProfilesStatus($"{profile.Name} applied.");
            }
            else
            {
                SetSharedProfilesStatus(
                    $"{profile.Name} applied with warnings: " +
                    string.Join(" ", warnings));
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
            SetSharedProfilesStatus(
                $"{profile.Name} could not be applied: {exception.Message}");
        }
        finally
        {
            _isApplyingProfile = false;
            ProfileItems.IsEnabled = true;
            UndoProfileButton.IsEnabled = true;
            PublishSharedState(QuickSwitchRefreshScope.Profiles);
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
            SetSharedProfilesStatus(
                errorMessage ?? "The profile catalog could not be saved.");
            return;
        }

        SetSharedProfilesStatus(successMessage);
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

        if (!_profileFeature.TrySave(candidate, out errorMessage))
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

    private async Task RefreshAudioInventoryAsync(
        CancellationToken requestCancellationToken = default,
        bool waitForActive = false)
    {
        bool entered = false;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken,
                _visibleCancellation.Token,
                _lifetimeCancellation.Token);
        CancellationToken cancellationToken = linkedCancellation.Token;

        try
        {
            if (waitForActive)
            {
                await _audioRefreshGate.WaitAsync(cancellationToken);
                entered = true;
            }
            else
            {
                entered = await _audioRefreshGate.WaitAsync(
                    TimeSpan.Zero,
                    cancellationToken);
            }

            if (!entered)
            {
                return;
            }

            RefreshAudioButton.IsEnabled = false;
            _sharedAudioBusy = true;
            _sharedAudioLoadState = QuickSwitchLoadState.Loading;
            _sharedAudioStatus = "Reading Windows audio state...";
            AudioStatusText.Text = "Reading Windows audio state...";
            PublishSharedState(
                QuickSwitchRefreshScope.Audio |
                QuickSwitchRefreshScope.Favorites);

            AudioInventory inventory = await _audioInventoryService.GetInventoryAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _sharedAudioInventory = new AudioInventory(
                inventory.PlaybackEndpoints.ToArray(),
                inventory.RecordingEndpoints.ToArray(),
                inventory.Sessions.ToArray(),
                inventory.CapturedAt);
            _sharedAudioLoadState = inventory.PlaybackEndpoints.Count +
                inventory.RecordingEndpoints.Count +
                inventory.Sessions.Count == 0
                    ? QuickSwitchLoadState.Empty
                    : QuickSwitchLoadState.Ready;
            PlaybackEndpointsList.ItemsSource = inventory.PlaybackEndpoints;
            RecordingEndpointsList.ItemsSource = inventory.RecordingEndpoints;
            AudioSessionsList.ItemsSource = inventory.Sessions;

            SelectAudioEndpoint(PlaybackEndpointsList, inventory.PlaybackEndpoints);
            SelectAudioEndpoint(RecordingEndpointsList, inventory.RecordingEndpoints);

            _sharedAudioStatus =
                $"{inventory.PlaybackEndpoints.Count} out · " +
                $"{inventory.RecordingEndpoints.Count} in · " +
                $"{inventory.Sessions.Count} apps · " +
                $"{inventory.CapturedAt.ToLocalTime():t}" +
                _audioWatcherStatusSuffix;
            AudioStatusText.Text = _sharedAudioStatus;
            QuickAudioSummaryText.Text =
                $"{inventory.PlaybackEndpoints.Count} outputs · " +
                $"{inventory.RecordingEndpoints.Count} inputs · " +
                $"{GetFavoriteAudioSummary()}";

            _sharedAudioBusy = false;
            RefreshAudioButton.IsEnabled = true;
            PublishSharedState(
                QuickSwitchRefreshScope.Audio |
                QuickSwitchRefreshScope.Favorites);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            _lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            _sharedAudioStatus =
                $"Audio inventory is unavailable: {exception.Message}";
            AudioStatusText.Text = _sharedAudioStatus;
            _sharedAudioLoadState = QuickSwitchLoadState.Error;
            _sharedAudioBusy = false;
            QuickAudioSummaryText.Text = "Audio inventory unavailable.";
            PublishSharedState(
                QuickSwitchRefreshScope.Audio |
                QuickSwitchRefreshScope.Favorites);
        }
        finally
        {
            if (entered)
            {
                bool publishIdleState = _sharedAudioBusy &&
                    !_lifetimeCancellation.IsCancellationRequested;
                _sharedAudioBusy = false;
                _audioRefreshGate.Release();

                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    RefreshAudioButton.IsEnabled = true;
                    if (publishIdleState)
                    {
                        PublishSharedState(
                            QuickSwitchRefreshScope.Audio |
                            QuickSwitchRefreshScope.Favorites);
                    }
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

    private async Task RefreshWirelessRadioStateAsync(
        CancellationToken requestCancellationToken = default,
        bool waitForActive = false)
    {
        bool entered = false;
        bool stateApplied = false;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken,
                _lifetimeCancellation.Token);
        CancellationToken cancellationToken = linkedCancellation.Token;

        try
        {
            if (waitForActive)
            {
                await _wirelessRadioGate.WaitAsync(cancellationToken);
                entered = true;
            }
            else
            {
                entered = await _wirelessRadioGate.WaitAsync(
                    TimeSpan.Zero,
                    cancellationToken);
            }

            if (!entered)
            {
                return;
            }

            WiFiRadioToggle.IsEnabled = false;
            BluetoothRadioToggle.IsEnabled = false;

            WirelessRadioSnapshot snapshot =
                await _wirelessRadioService.GetSnapshotAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _lastWirelessRadioSnapshot = snapshot;

            ApplyWirelessRadioState(
                WiFiRadioToggle,
                "Wi-Fi",
                snapshot.WiFi);
            ApplyWirelessRadioState(
                BluetoothRadioToggle,
                "Bluetooth",
                snapshot.Bluetooth);
            stateApplied = true;
            UpdateFloatingDevicesWidget();
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
            _lastWirelessRadioSnapshot = null;
            stateApplied = true;
            UpdateFloatingDevicesWidget();
            WirelessStatusText.Text =
                $"Wireless state is unavailable: {exception.Message}";
            WirelessStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (entered)
            {
                if (!stateApplied &&
                    !_lifetimeCancellation.IsCancellationRequested &&
                    _lastWirelessRadioSnapshot is { } previousSnapshot)
                {
                    ApplyWirelessRadioState(
                        WiFiRadioToggle,
                        "Wi-Fi",
                        previousSnapshot.WiFi);
                    ApplyWirelessRadioState(
                        BluetoothRadioToggle,
                        "Bluetooth",
                        previousSnapshot.Bluetooth);
                }

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

    private async Task RefreshDeviceInventoryAsync(
        CancellationToken requestCancellationToken = default)
    {
        bool entered = false;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken,
                _visibleCancellation.Token,
                _lifetimeCancellation.Token);
        CancellationToken cancellationToken = linkedCancellation.Token;

        try
        {
            await _deviceRefreshGate.WaitAsync(cancellationToken);
            entered = true;

            RefreshDevicesButton.IsEnabled = false;
            _sharedDevicesBusy = true;
            _sharedDevicesLoadState = QuickSwitchLoadState.Loading;
            _sharedDevicesStatus = "Reading connected devices...";
            DeviceStatusText.Text = "Reading connected devices...";
            PublishSharedState(QuickSwitchRefreshScope.Devices);

            DeviceInventory inventory = await _deviceInventoryService.GetInventoryAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ConnectedDevicesList.ItemsSource = inventory.Devices;
            _sharedDeviceInventory = new DeviceInventory(
                inventory.Devices.ToArray(),
                inventory.CapturedAt);
            _deviceInventoryDirty = false;

            int bluetoothCount = inventory.Devices.Count(
                device => device.Transport == DeviceTransport.Bluetooth);
            int wiredCount = inventory.Devices.Count - bluetoothCount;

            _sharedDevicesStatus =
                $"{bluetoothCount} Bluetooth · " +
                $"{wiredCount} wired · " +
                $"{inventory.CapturedAt.ToLocalTime():t}";
            _sharedDevicesLoadState = inventory.Devices.Count == 0
                ? QuickSwitchLoadState.Empty
                : QuickSwitchLoadState.Ready;
            DeviceStatusText.Text = _sharedDevicesStatus;
            QuickDeviceSummaryText.Text =
                $"{inventory.Devices.Count} connected · " +
                $"{bluetoothCount} Bluetooth";

            _sharedDevicesBusy = false;
            RefreshDevicesButton.IsEnabled = true;
            PublishSharedState(QuickSwitchRefreshScope.Devices);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            _lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        catch (Exception exception)
        {
            _sharedDevicesStatus =
                $"Connected-device inventory is unavailable: {exception.Message}";
            DeviceStatusText.Text = _sharedDevicesStatus;
            _sharedDevicesLoadState = QuickSwitchLoadState.Error;
            _sharedDevicesBusy = false;
            QuickDeviceSummaryText.Text = "Device inventory unavailable.";
            PublishSharedState(QuickSwitchRefreshScope.Devices);
        }
        finally
        {
            if (entered)
            {
                bool publishIdleState = _sharedDevicesBusy &&
                    !_lifetimeCancellation.IsCancellationRequested;
                _sharedDevicesBusy = false;
                _deviceRefreshGate.Release();

                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    RefreshDevicesButton.IsEnabled = true;
                    if (publishIdleState)
                    {
                        PublishSharedState(QuickSwitchRefreshScope.Devices);
                    }
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
    int Slot,
    WidgetHotkeyAction Action,
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
