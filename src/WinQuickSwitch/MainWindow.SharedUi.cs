using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

namespace WinQuickSwitch;

public partial class MainWindow : IQuickSwitchCoordinator
{
    private FeatureViewFactory? _sharedFeatureViewFactory;
    private event EventHandler<QuickSwitchStateChangedEventArgs>? SharedStateChanged;
    private long _sharedStateRevision;
    private QuickSwitchState _sharedState = QuickSwitchState.Empty;
    private QuickSwitchFeedbackState _sharedFeedback = QuickSwitchFeedbackState.Empty;
    private DisplayTopologySnapshot _sharedDisplayTopology = new(
        null,
        0,
        0,
        false,
        DisplayQuickSwitchState.Empty.StatusMessage);
    private AudioInventory? _sharedAudioInventory;
    private DeviceInventory? _sharedDeviceInventory;
    private QuickSwitchLoadState _sharedAudioLoadState = QuickSwitchLoadState.Loading;
    private QuickSwitchLoadState _sharedDevicesLoadState = QuickSwitchLoadState.Loading;
    private string _sharedDisplayStatus = DisplayQuickSwitchState.Empty.StatusMessage;
    private string _sharedAudioStatus = AudioQuickSwitchState.Empty.StatusMessage;
    private string _sharedDevicesStatus = DevicesQuickSwitchState.Empty.StatusMessage;
    private string _sharedOptionsStatus = OptionsQuickSwitchState.Empty.StatusMessage;
    private string _sharedProfilesStatus = ProfilesQuickSwitchState.Empty.StatusMessage;
    private bool _sharedDisplayBusy;
    private bool _sharedAudioBusy;
    private bool _sharedDevicesBusy;
    private int _sharedAudioDemand;
    private int _sharedDisplayDemand;
    private int _sharedDevicesDemand;
    private readonly LatestRefreshGate _sharedAudioRefreshOrder = new();
    private readonly LatestRefreshGate _sharedDevicesRefreshOrder = new();

    private FeatureViewFactory SharedFeatureViewFactory =>
        _sharedFeatureViewFactory ??= new FeatureViewFactory(this, _productConfiguration);

    private void MountSharedMainWindowViews()
    {
        MountSharedMainWindowView(DisplayPanel, OverlayFeatureId.Display);
        MountSharedMainWindowView(AudioPanel, OverlayFeatureId.Audio);
        MountSharedMainWindowView(DevicesPanel, OverlayFeatureId.Devices);
        MountSharedMainWindowView(OptionsPanel, OverlayFeatureId.Options);
        if (_profileFeature.IsEnabled)
        {
            MountSharedMainWindowView(ProfilesPanel, OverlayFeatureId.Profiles);
        }
        MountSharedMainWindowView(AboutPanel, OverlayFeatureId.About);
    }

    private void MountSharedMainWindowView(
        Border panel,
        OverlayFeatureId feature)
    {
        UIElement? legacyControllerView = panel.Child;
        panel.Child = null;

        Grid host = new();
        if (legacyControllerView is not null)
        {
            legacyControllerView.Visibility = Visibility.Collapsed;
            host.Children.Add(legacyControllerView);
        }
        host.Children.Add(CreateSharedFeatureView(
            feature,
            SharedFeatureSurface.MainWindow));
        panel.Child = host;
    }

    QuickSwitchState IQuickSwitchCoordinator.State => _sharedState;

    event EventHandler<QuickSwitchStateChangedEventArgs>?
        IQuickSwitchCoordinator.StateChanged
    {
        add => SharedStateChanged += value;
        remove => SharedStateChanged -= value;
    }

    Task<QuickSwitchResult> IQuickSwitchCoordinator.ExecuteAsync(
        QuickSwitchCommand command,
        CancellationToken cancellationToken) =>
        ExecuteSharedCommandAsync(command, cancellationToken);

    Task IQuickSwitchCoordinator.RefreshAsync(
        QuickSwitchRefreshScope scope,
        CancellationToken cancellationToken) =>
        RefreshSharedStateAsync(scope, cancellationToken);

    IDisposable IQuickSwitchCoordinator.AcquireSurfaceDemand(
        QuickSwitchSurfaceDemand demand) =>
        AcquireSharedSurfaceDemand(demand);

    private QuickSwitchState BuildSharedState()
    {
        DisplayTopologySnapshot topology = _sharedDisplayTopology;
        IReadOnlyList<AudioEndpointInfo> playback =
            _sharedAudioInventory?.PlaybackEndpoints ?? [];
        IReadOnlyList<AudioEndpointInfo> recording =
            _sharedAudioInventory?.RecordingEndpoints ?? [];
        IReadOnlyList<AudioSessionInfo> sessions =
            _sharedAudioInventory?.Sessions ?? [];
        IReadOnlyList<ConnectedDeviceInfo> devices =
            _sharedDeviceInventory?.Devices ?? [];
        OverlayWorkspaceSettings workspaceSettings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled);

        return new QuickSwitchState(
            new DisplayQuickSwitchState(
                topology.IsReliable ? QuickSwitchLoadState.Ready : QuickSwitchLoadState.Loading,
                _currentDisplayMode,
                topology.ActiveDisplayCount,
                topology.AvailableDisplayCount,
                topology.IsReliable,
                _sharedDisplayBusy,
                _sharedDisplayStatus),
            new AudioQuickSwitchState(
                _sharedAudioLoadState,
                playback,
                recording,
                sessions,
                BuildSharedOutputFavorites(playback),
                BuildSharedInputFavorites(recording),
                _sharedAudioBusy,
                _sharedAudioStatus),
            new DevicesQuickSwitchState(
                _sharedDevicesLoadState,
                _lastWirelessRadioSnapshot,
                devices,
                _sharedDevicesBusy,
                _sharedDevicesStatus),
            new OptionsQuickSwitchState(
                _widgetSettings.UseDarkTheme,
                _widgetSettings.OverlayEnabled,
                _startupRegistrationService.IsEnabled,
                _widgetSettings.WindowPinned,
                _widgetSettings.OverlayDimmingOpacity,
                workspaceSettings.InspectorWidth,
                Enum.GetValues<WidgetHotkeyAction>().ToDictionary(
                    action => action,
                    action => FormatShortcut(_widgetSettings.GetShortcut(action))),
                _sharedOptionsStatus,
                QuickSwitchCommandCatalog.GetKeyedCommands().ToDictionary(
                    item => item.Key, item => FormatShortcut(_widgetSettings.GetCommandShortcut(item.Key))),
                _widgetSettings.AudioVolumeStepPercent),
            new ProfilesQuickSwitchState(
                _profileCatalog.Profiles.Count == 0
                    ? QuickSwitchLoadState.Empty
                    : QuickSwitchLoadState.Ready,
                _profileCatalog.Profiles.ToArray(),
                _isApplyingProfile,
                _sharedProfilesStatus),
            new AboutQuickSwitchState(
                _productConfiguration.DisplayName,
                ProductConfiguration.ReleaseVersion,
                "Andrew Chang",
                "Preferences, shortcuts, device names, and profiles are stored locally on this PC."),
            _sharedFeedback);
    }

    private IReadOnlyList<FavoriteAudioEndpointState> BuildSharedOutputFavorites(
        IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        List<FavoriteAudioEndpointState> favorites = [];
        for (int slot = 0; slot < WidgetSettings.MaximumFavoriteOutputs; slot++)
        {
            if (_widgetSettings.GetFavorite(slot) is not FavoriteOutputSetting favorite)
            {
                continue;
            }

            favorites.Add(new FavoriteAudioEndpointState(
                slot,
                favorite.EndpointId,
                GetFavoriteDisplayName(favorite.Name, favorite.Alias),
                AudioEndpointKind.Playback,
                favorite.Role,
                endpoints.Any(endpoint => endpoint.Id == favorite.EndpointId),
                FormatShortcut(favorite.Shortcut)));
        }

        return favorites;
    }

    private IReadOnlyList<FavoriteAudioEndpointState> BuildSharedInputFavorites(
        IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        List<FavoriteAudioEndpointState> favorites = [];
        for (int slot = 0; slot < WidgetSettings.MaximumFavoriteInputs; slot++)
        {
            if (_widgetSettings.GetInputFavorite(slot) is not FavoriteInputSetting favorite)
            {
                continue;
            }

            favorites.Add(new FavoriteAudioEndpointState(
                slot,
                favorite.EndpointId,
                GetFavoriteDisplayName(favorite.Name, favorite.Alias),
                AudioEndpointKind.Recording,
                favorite.Role,
                endpoints.Any(endpoint => endpoint.Id == favorite.EndpointId),
                FormatShortcut(favorite.Shortcut)));
        }

        return favorites;
    }

    private void PublishSharedState(
        QuickSwitchRefreshScope scopes = QuickSwitchRefreshScope.All,
        QuickSwitchResult? result = null)
    {
        long revision = Interlocked.Increment(ref _sharedStateRevision);
        if (result is not null)
        {
            _sharedFeedback = new QuickSwitchFeedbackState(
                result.Severity,
                result.Message,
                revision);
        }

        QuickSwitchState state = BuildSharedState();
        _sharedState = state;

        SharedStateChanged?.Invoke(
            this,
            new QuickSwitchStateChangedEventArgs(
                state,
                scopes,
                revision));
    }

    private async Task RefreshSharedStateAsync(
        QuickSwitchRefreshScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope == QuickSwitchRefreshScope.None)
        {
            PublishSharedState(QuickSwitchRefreshScope.None);
            return;
        }

        if ((scope & QuickSwitchRefreshScope.Display) != 0)
        {
            RefreshDisplayTopology();
            cancellationToken.ThrowIfCancellationRequested();
            PublishSharedState(QuickSwitchRefreshScope.Display);
        }

        List<Task> refreshes = [];
        if ((scope & (QuickSwitchRefreshScope.Audio | QuickSwitchRefreshScope.Favorites)) != 0)
        {
            refreshes.Add(RefreshLatestSharedAudioAsync(cancellationToken));
        }

        if ((scope & QuickSwitchRefreshScope.Devices) != 0)
        {
            refreshes.Add(RefreshLatestSharedDevicesAsync(cancellationToken));
        }

        if (refreshes.Count > 0)
        {
            await Task.WhenAll(refreshes);
        }

        QuickSwitchRefreshScope snapshotOnlyScopes = scope &
            (QuickSwitchRefreshScope.Options |
             QuickSwitchRefreshScope.Profiles);
        if (snapshotOnlyScopes != QuickSwitchRefreshScope.None)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishSharedState(snapshotOnlyScopes);
        }
    }

    private async Task RefreshLatestSharedAudioAsync(
        CancellationToken cancellationToken)
    {
        using LatestRefreshLease refresh =
            _sharedAudioRefreshOrder.Begin(cancellationToken);
        await RefreshAudioInventoryAsync(refresh.Token, waitForActive: true);
        refresh.ThrowIfSuperseded();
    }

    private async Task RefreshLatestSharedDevicesAsync(
        CancellationToken cancellationToken)
    {
        using LatestRefreshLease refresh =
            _sharedDevicesRefreshOrder.Begin(cancellationToken);
        await Task.WhenAll(
            RefreshWirelessRadioStateAsync(refresh.Token, waitForActive: true),
            RefreshDeviceInventoryAsync(refresh.Token));
        refresh.ThrowIfSuperseded();
    }

    private async Task<QuickSwitchResult> ExecuteSharedCommandAsync(
        QuickSwitchCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuickSwitchResult result;

        try
        {
            result = command switch
            {
                ToggleConfiguredSurfaceCommand =>
                    await ToggleConfiguredSurfaceAsync(),
                RefreshOverlayFeatureCommand refresh =>
                    await ExecuteSharedRefreshAsync(refresh, cancellationToken),
                ApplyDisplayModeCommand apply => await ExecuteDisplayCommandAsync(apply),
                SetDefaultAudioEndpointCommand setDefault =>
                    ToQuickSwitchResult(await _defaultAudioEndpointService.SetDefaultAsync(
                        setDefault.EndpointId,
                        setDefault.EndpointName,
                        setDefault.Role,
                        cancellationToken), QuickSwitchRefreshScope.Audio),
                AddAudioFavoriteCommand addFavorite => AddSharedFavorite(addFavorite),
                RemoveAudioFavoriteCommand removeFavorite => RemoveSharedFavorite(removeFavorite),
                SetAudioSessionVolumeCommand setVolume =>
                    await ExecuteSharedSessionVolumeAsync(setVolume, cancellationToken),
                SetAudioSessionMuteCommand setMute =>
                    await ExecuteSharedSessionMuteAsync(setMute, cancellationToken),
                ExecuteAudioQuickActionCommand quickAudio =>
                    ToQuickSwitchResult(await _audioQuickActionExecutor.ExecuteAsync(
                        quickAudio.Action, _widgetSettings.AudioVolumeStepPercent, cancellationToken),
                        QuickSwitchRefreshScope.Audio),
                SetAudioShortcutStepCommand step => SetSharedAudioShortcutStep(step.Percent),
                CaptureCommandShortcutCommand capture => CaptureSharedCommandShortcut(capture.CommandKey),
                ClearCommandShortcutCommand clear => ApplySharedCommandShortcut(clear.CommandKey, null),
                OpenAudioSettingsCommand openAudio => OpenSharedAudioSettings(openAudio),
                SetWirelessRadioStateCommand setRadio =>
                    ToQuickSwitchResult(await _wirelessRadioService.SetStateAsync(
                        setRadio.Radio, setRadio.IsEnabled, cancellationToken),
                        QuickSwitchRefreshScope.Devices),
                ToggleWirelessRadioCommand toggleRadio =>
                    await ToggleSharedWirelessRadioAsync(toggleRadio, cancellationToken),
                OpenDeviceSettingsCommand openDevices => OpenSharedDeviceSettings(openDevices.Target),
                OpenSystemSettingsCommand openSettings => OpenSharedSystemSettings(openSettings.Target),
                SetOverlayOptionCommand setOption => SetSharedOption(setOption),
                SetTaskbarAutoHideCommand taskbar => SetSharedTaskbar(taskbar.IsEnabled),
                ToggleTaskbarAutoHideCommand => ToggleSharedTaskbar(),
                SetOverlayDimmingOpacityCommand dimming => SetSharedDimming(dimming.Opacity),
                SetOverlayInspectorWidthCommand inspector => SetSharedInspectorWidth(inspector.Width),
                ResetAllShortcutsCommand => ResetSharedShortcuts(),
                RestoreOptionsDefaultsCommand => RestoreSharedOptionsDefaults(),
                ClearWidgetShortcutCommand clearShortcut => ClearSharedShortcut(clearShortcut.Action),
                CaptureWidgetShortcutCommand captureShortcut => CaptureSharedShortcut(captureShortcut.Action),
                UpdateFavoriteAliasCommand alias => UpdateSharedFavoriteAlias(alias),
                MoveFavoriteCommand move => MoveSharedFavorite(move),
                CaptureFavoriteShortcutCommand captureFavorite => CaptureSharedFavoriteShortcut(captureFavorite),
                ApplyAudioFavoriteCommand applyFavorite => await ApplySharedFavoriteAsync(applyFavorite),
                ApplyProfileCommand applyProfile => await ApplySharedProfileAsync(applyProfile),
                SaveProfileDraftCommand saveProfile => await SaveSharedProfileAsync(saveProfile.Draft),
                UndoLastProfileCommand => await UndoSharedProfileAsync(),
                DuplicateProfileCommand duplicate => DuplicateSharedProfile(duplicate.ProfileId),
                ToggleProfilePinCommand togglePin => ToggleSharedProfilePin(togglePin.ProfileId),
                DeleteProfileCommand delete => DeleteSharedProfile(delete.ProfileId),
                ClearProfileShortcutCommand clearProfile => ClearSharedProfileShortcut(clearProfile.ProfileId),
                CaptureProfileShortcutCommand captureProfile => CaptureSharedProfileShortcut(captureProfile.ProfileId),
                OpenLegalDocumentCommand legal => OpenSharedLegalDocument(legal.Document),
                OpenFloatingWidgetCommand floating => OpenSharedFloatingWidget(floating.Feature),
                OpenMainWindowCommand openMain =>
                    await OpenSharedMainWindowAsync(openMain.Feature),
                OpenOverlayWorkspaceCommand => OpenSharedOverlayWorkspace(),
                _ => QuickSwitchResult.Warning($"{command.GetType().Name} is not available on this surface."),
            };
        }
        catch (OperationCanceledException)
        {
            return QuickSwitchResult.Cancelled();
        }
        catch (Exception exception)
        {
            result = QuickSwitchResult.Failure(exception.Message);
        }

        QuickSwitchRefreshScope affected = result.AffectedScopes.Aggregate(
            QuickSwitchRefreshScope.None,
            (current, scope) => current | scope);
        UpdateSharedCommandStatus(command, result.Message);
        PublishSharedState(
            affected == QuickSwitchRefreshScope.None
                ? QuickSwitchRefreshScope.All
                : affected,
            result);

        if (result.Succeeded && result.Disposition == OverlayDisposition.CloseOverlay)
        {
            _overlayWorkspace?.Dismiss();
        }

        return result;
    }

    private async Task<QuickSwitchResult> ExecuteSharedRefreshAsync(
        RefreshOverlayFeatureCommand command,
        CancellationToken cancellationToken)
    {
        QuickSwitchRefreshScope scope =
            QuickSwitchCommandMetadata.GetDefaultRefreshScope(command);
        await RefreshSharedStateAsync(scope, cancellationToken);
        return QuickSwitchResult.Success(
            $"{command.Feature} refreshed.",
            OverlayDisposition.StayOpen,
            scope);
    }

    private void UpdateSharedCommandStatus(
        QuickSwitchCommand command,
        string message)
    {
        switch (command)
        {
            case ApplyDisplayModeCommand:
                _sharedDisplayStatus = message;
                break;
            case SetDefaultAudioEndpointCommand:
            case AddAudioFavoriteCommand:
            case RemoveAudioFavoriteCommand:
            case SetAudioSessionVolumeCommand:
            case SetAudioSessionMuteCommand:
            case ExecuteAudioQuickActionCommand:
            case OpenAudioSettingsCommand:
            case UpdateFavoriteAliasCommand:
            case MoveFavoriteCommand:
            case CaptureFavoriteShortcutCommand:
            case ApplyAudioFavoriteCommand:
                _sharedAudioStatus = message;
                break;
            case SetWirelessRadioStateCommand:
            case ToggleWirelessRadioCommand:
            case OpenDeviceSettingsCommand:
                _sharedDevicesStatus = message;
                break;
            case SetOverlayOptionCommand:
            case SetTaskbarAutoHideCommand:
            case ToggleTaskbarAutoHideCommand:
            case SetOverlayDimmingOpacityCommand:
            case SetOverlayInspectorWidthCommand:
            case ResetAllShortcutsCommand:
            case RestoreOptionsDefaultsCommand:
            case ClearWidgetShortcutCommand:
            case CaptureWidgetShortcutCommand:
            case CaptureCommandShortcutCommand:
            case ClearCommandShortcutCommand:
            case SetAudioShortcutStepCommand:
                _sharedOptionsStatus = message;
                break;
            case ApplyProfileCommand:
            case SaveProfileDraftCommand:
            case UndoLastProfileCommand:
            case DuplicateProfileCommand:
            case ToggleProfilePinCommand:
            case DeleteProfileCommand:
            case ClearProfileShortcutCommand:
            case CaptureProfileShortcutCommand:
                _sharedProfilesStatus = message;
                break;
        }
    }

    private void SetSharedProfilesStatus(string message)
    {
        _sharedProfilesStatus = message;
        ProfilesStatusText.Text = message;
    }

    private async Task<QuickSwitchResult> ExecuteDisplayCommandAsync(
        ApplyDisplayModeCommand command)
    {
        bool succeeded = await ApplyDisplayModeAsync(command.Mode);
        return succeeded
            ? QuickSwitchResult.Success(
                $"{command.Mode.GetDisplayName()} applied.",
                OverlayDisposition.CloseOverlay,
                QuickSwitchRefreshScope.Display)
            : QuickSwitchResult.Failure(
                _sharedDisplayStatus,
                QuickSwitchRefreshScope.Display);
    }

    private static QuickSwitchResult ToQuickSwitchResult(
        AudioControlResult result,
        QuickSwitchRefreshScope scope) =>
        result.Succeeded
            ? QuickSwitchResult.Success(result.Message, OverlayDisposition.StayOpen, scope)
            : QuickSwitchResult.Failure(result.Message, scope);

    private async Task<QuickSwitchResult> ExecuteSharedSessionVolumeAsync(
        SetAudioSessionVolumeCommand command,
        CancellationToken cancellationToken)
    {
        AudioControlResult result = await _audioSessionControlService.SetVolumeAsync(
            command.SessionId,
            command.Volume,
            cancellationToken);
        if (result.Succeeded)
        {
            await RefreshAudioInventoryAsync();
        }

        return ToQuickSwitchResult(result, QuickSwitchRefreshScope.Audio);
    }

    private async Task<QuickSwitchResult> ExecuteSharedSessionMuteAsync(
        SetAudioSessionMuteCommand command,
        CancellationToken cancellationToken)
    {
        AudioControlResult result = await _audioSessionControlService.SetMuteAsync(
            command.SessionId,
            command.IsMuted,
            cancellationToken);
        if (result.Succeeded)
        {
            await RefreshAudioInventoryAsync();
        }

        return ToQuickSwitchResult(result, QuickSwitchRefreshScope.Audio);
    }

    private static QuickSwitchResult ToQuickSwitchResult(
        WirelessRadioResult result,
        QuickSwitchRefreshScope scope) =>
        result.Succeeded
            ? QuickSwitchResult.Success(result.Message, OverlayDisposition.StayOpen, scope)
            : QuickSwitchResult.Failure(result.Message, scope);

    private async Task<QuickSwitchResult> ToggleSharedWirelessRadioAsync(
        ToggleWirelessRadioCommand command,
        CancellationToken cancellationToken)
    {
        WirelessRadioSnapshot snapshot =
            await _wirelessRadioService.GetSnapshotAsync(cancellationToken);
        WirelessRadioState state = snapshot.GetState(command.Radio);
        if (state is not WirelessRadioState.On and not WirelessRadioState.Off)
        {
            return QuickSwitchResult.Warning(
                $"{GetWirelessRadioName(command.Radio)} cannot be changed right now.",
                QuickSwitchRefreshScope.Devices);
        }

        WirelessRadioResult result = await _wirelessRadioService.SetStateAsync(
            command.Radio,
            state == WirelessRadioState.Off,
            cancellationToken);
        if (result.Succeeded)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            await RefreshWirelessRadioStateAsync(
                cancellationToken,
                waitForActive: true);
        }

        return ToQuickSwitchResult(result, QuickSwitchRefreshScope.Devices);
    }

    private static string GetWirelessRadioName(WirelessRadioKind kind) =>
        kind == WirelessRadioKind.WiFi ? "Wi-Fi" : "Bluetooth";

    private QuickSwitchResult AddSharedFavorite(AddAudioFavoriteCommand command)
    {
        int slot = command.EndpointKind == AudioEndpointKind.Playback
            ? _widgetSettings.FindOpenFavoriteSlot()
            : _widgetSettings.FindOpenInputFavoriteSlot();
        if (slot < 0)
        {
            return QuickSwitchResult.Warning("All favorite slots are already in use.");
        }

        _widgetSettings = command.EndpointKind == AudioEndpointKind.Playback
            ? _widgetSettings.WithFavorite(slot, new FavoriteOutputSetting(
                command.EndpointId, command.EndpointName, null, command.Role))
            : _widgetSettings.WithInputFavorite(slot, new FavoriteInputSetting(
                command.EndpointId, command.EndpointName, null, command.Role));
        ApplyHotkeyBindings(_widgetSettings);
        UpdateOptionsControls();
        SaveSettings($"{command.EndpointName} added to favorites.", AudioStatusText);
        return QuickSwitchResult.Success(
            $"{command.EndpointName} added to favorites.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Audio,
            QuickSwitchRefreshScope.Favorites,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult RemoveSharedFavorite(RemoveAudioFavoriteCommand command)
    {
        _widgetSettings = command.EndpointKind == AudioEndpointKind.Playback
            ? _widgetSettings.WithFavorite(command.Slot, null)
            : _widgetSettings.WithInputFavorite(command.Slot, null);
        ApplyHotkeyBindings(_widgetSettings);
        UpdateOptionsControls();
        SaveSettings("Favorite removed.", AudioStatusText);
        return QuickSwitchResult.Success(
            "Favorite removed.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Audio,
            QuickSwitchRefreshScope.Favorites,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult OpenSharedAudioSettings(OpenAudioSettingsCommand command)
    {
        CloseOverlayWorkspace();
        AudioControlResult result = command.Target == AudioSettingsTarget.VolumeMixer
            ? _defaultAudioEndpointService.OpenVolumeMixerSettings()
            : _defaultAudioEndpointService.OpenSoundSettings();
        return result.Succeeded
            ? QuickSwitchResult.Success(result.Message, OverlayDisposition.OpenSystemSettings)
            : QuickSwitchResult.Failure(result.Message);
    }

    private QuickSwitchResult OpenSharedDeviceSettings(OverlaySystemSettingsTarget target)
    {
        CloseOverlayWorkspace();
        DeviceActionResult result = target switch
        {
            OverlaySystemSettingsTarget.Wifi => _deviceSettingsService.OpenWiFiSettings(),
            OverlaySystemSettingsTarget.Bluetooth => _deviceSettingsService.OpenBluetoothSettings(),
            OverlaySystemSettingsTarget.AirplaneMode => _deviceSettingsService.OpenAirplaneModeSettings(),
            OverlaySystemSettingsTarget.Network => _deviceSettingsService.OpenNetworkSettings(),
            _ => _deviceSettingsService.OpenConnectedDevicesSettings(),
        };
        return result.Succeeded
            ? QuickSwitchResult.Success(result.Message, OverlayDisposition.OpenSystemSettings)
            : QuickSwitchResult.Failure(result.Message);
    }

    private QuickSwitchResult OpenSharedSystemSettings(OverlaySystemSettingsTarget target)
    {
        if (target is OverlaySystemSettingsTarget.Wifi or
            OverlaySystemSettingsTarget.Bluetooth or
            OverlaySystemSettingsTarget.AirplaneMode or
            OverlaySystemSettingsTarget.Network or
            OverlaySystemSettingsTarget.Devices)
        {
            return OpenSharedDeviceSettings(target);
        }

        CloseOverlayWorkspace();
        TaskbarActionResult result = target switch
        {
            OverlaySystemSettingsTarget.Taskbar => _taskbarService.OpenTaskbarSettings(),
            OverlaySystemSettingsTarget.Notifications => _taskbarService.OpenNotificationSettings(),
            OverlaySystemSettingsTarget.Display => _taskbarService.OpenDisplaySettings(),
            _ => new TaskbarActionResult(
                _defaultAudioEndpointService.OpenSoundSettings().Succeeded,
                "Windows sound settings opened."),
        };
        return result.Succeeded
            ? QuickSwitchResult.Success(result.Message, OverlayDisposition.OpenSystemSettings)
            : QuickSwitchResult.Failure(result.Message);
    }

    private QuickSwitchResult SetSharedOption(SetOverlayOptionCommand command)
    {
        switch (command.Option)
        {
            case OverlayOption.DarkTheme:
                SetDarkThemeFromOverlay(command.IsEnabled);
                break;
            case OverlayOption.OverlayEnabled:
                SetOverlayEnabledFromOverlay(command.IsEnabled);
                break;
            case OverlayOption.StartWithWindows:
                SetStartupFromOverlay(command.IsEnabled);
                break;
            case OverlayOption.MainWindowPinned:
                SetWindowPinnedFromOverlay(command.IsEnabled);
                break;
            case OverlayOption.TaskbarAutoHide:
                return SetSharedTaskbar(command.IsEnabled);
        }

        return QuickSwitchResult.Success(
            "Option saved.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult SetSharedTaskbar(bool enabled)
    {
        TaskbarActionResult result = _taskbarService.SetAutoHide(enabled);
        ShowTaskbarActionResult(result);
        if (result.Succeeded)
        {
            RefreshTaskbarState();
        }

        return result.Succeeded
            ? QuickSwitchResult.Success(
                result.Message,
                OverlayDisposition.StayOpen,
                QuickSwitchRefreshScope.Options)
            : QuickSwitchResult.Failure(result.Message, QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult ToggleSharedTaskbar()
    {
        TaskbarState state = _taskbarService.GetSnapshot().State;
        return state switch
        {
            TaskbarState.Visible => SetSharedTaskbar(enabled: true),
            TaskbarState.AutoHidden => SetSharedTaskbar(enabled: false),
            _ => QuickSwitchResult.Warning(
                "Taskbar state is unavailable.",
                QuickSwitchRefreshScope.Options),
        };
    }

    private QuickSwitchResult SetSharedDimming(double opacity)
    {
        double normalized = Math.Clamp(
            opacity,
            WidgetSettings.MinimumOverlayDimmingOpacity,
            WidgetSettings.MaximumOverlayDimmingOpacity);
        _widgetSettings = _widgetSettings with { OverlayDimmingOpacity = normalized };
        _overlayWorkspace?.SetDimmingOpacity(normalized);
        SaveSettings("Overlay dimming saved.");
        return QuickSwitchResult.Success(
            "Overlay dimming saved.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult SetSharedInspectorWidth(double width)
    {
        OverlayWorkspaceSettings settings =
            (_widgetSettings.OverlayWorkspace ?? OverlayWorkspaceSettings.Default)
            .Normalize(_profileFeature.IsEnabled) with
            {
                InspectorWidth = OverlayWorkspaceSettings.NormalizeInspectorWidth(width),
            };
        _widgetSettings = _widgetSettings with { OverlayWorkspace = settings };
        _overlayWorkspace?.SetInspectorWidth(settings.InspectorWidth);
        SaveSettings("Overlay inspector width saved.");
        return QuickSwitchResult.Success(
            "Overlay inspector width saved.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult ResetSharedShortcuts()
    {
        WidgetSettings candidate = _widgetSettings.ResetShortcuts();
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        if (result.Succeeded) _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings(result.Succeeded
            ? "Shortcuts reset."
            : result.FirstFailure ?? "One or more shortcuts are unavailable.");
        return result.Succeeded
            ? QuickSwitchResult.Success(
                "Shortcuts reset.",
                OverlayDisposition.StayOpen,
                QuickSwitchRefreshScope.Options)
            : QuickSwitchResult.Warning(
                result.FirstFailure ?? "One or more shortcuts are unavailable.",
                QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult RestoreSharedOptionsDefaults()
    {
        SetDarkThemeFromOverlay(true);
        SetWindowPinnedFromOverlay(false);
        SetStartupFromOverlay(false);
        _widgetSettings = _widgetSettings with
        {
            OverlayEnabled = true,
            OverlayDimmingOpacity = WidgetSettings.DefaultOverlayDimmingOpacity,
        };
        ResetSharedShortcuts();
        SetSharedInspectorWidth(OverlayWorkspaceSettings.DefaultInspectorWidth);
        UpdateOptionsControls();
        SaveSettings("Application options restored to defaults.");
        return QuickSwitchResult.Success(
            "Application options restored to defaults.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult SetSharedAudioShortcutStep(int percent)
    {
        if (percent is < 1 or > 20)
            return QuickSwitchResult.Warning("Volume step must be between 1 and 20 percent.", QuickSwitchRefreshScope.Options);
        _widgetSettings = _widgetSettings with { AudioVolumeStepPercent = percent };
        SaveSettings("Volume shortcut step saved.");
        return QuickSwitchResult.Success("Volume shortcut step saved.", OverlayDisposition.StayOpen, QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult CaptureSharedCommandShortcut(string key)
    {
        QuickSwitchCommandDescriptor? descriptor = QuickSwitchCommandCatalog.GetKeyedCommands()
            .FirstOrDefault(item => item.Key == key && item.IsAvailable(_productConfiguration));
        return descriptor is null
            ? QuickSwitchResult.Warning("That shortcut is unavailable.", QuickSwitchRefreshScope.Options)
            : CaptureSharedShortcutWithDialog($"Set {descriptor.Label} shortcut",
                shortcut => ApplySharedCommandShortcut(key, shortcut));
    }

    private QuickSwitchResult ApplySharedCommandShortcut(string key, WidgetShortcut? shortcut)
    {
        if (!QuickSwitchCommandCatalog.GetKeyedCommands().Any(item => item.Key == key && item.IsAvailable(_productConfiguration)))
            return QuickSwitchResult.Warning("That shortcut is unavailable.", QuickSwitchRefreshScope.Options);
        WidgetSettings candidate = _widgetSettings.WithCommandShortcut(key, shortcut);
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        if (!result.Succeeded)
            return QuickSwitchResult.Warning(result.FirstFailure ?? "Shortcut unavailable.", QuickSwitchRefreshScope.Options);
        _widgetSettings = candidate;
        SaveSettings(shortcut is null ? "Shortcut cleared." : $"{shortcut.DisplayText} is active.");
        return QuickSwitchResult.Success(shortcut is null ? "Shortcut cleared." : $"{shortcut.DisplayText} is active.",
            OverlayDisposition.StayOpen, QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult ClearSharedShortcut(WidgetHotkeyAction action)
        => ApplySharedShortcutChange(action, null);

    private QuickSwitchResult CaptureSharedShortcut(WidgetHotkeyAction action) =>
        CaptureSharedShortcutWithDialog(
            $"Set {action} shortcut",
            shortcut => ApplySharedShortcutChange(action, shortcut));

    private QuickSwitchResult ApplySharedShortcutChange(
        WidgetHotkeyAction action,
        WidgetShortcut? shortcut)
    {
        HotkeyRegistrationResult result = ApplyShortcutChange(action, shortcut);
        return result.Succeeded
            ? QuickSwitchResult.Success(
                shortcut is null
                    ? "Shortcut cleared."
                    : $"{shortcut.DisplayText} is active.",
                OverlayDisposition.StayOpen,
                QuickSwitchRefreshScope.Options)
            : QuickSwitchResult.Warning(
                result.FirstFailure ?? "The shortcut could not be registered.",
                QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult CaptureSharedFavoriteShortcut(
        CaptureFavoriteShortcutCommand command)
    {
        WidgetHotkeyAction action = command.EndpointKind == AudioEndpointKind.Playback
            ? WidgetSettings.GetFavoriteAction(command.Slot)
            : WidgetSettings.GetInputFavoriteAction(command.Slot);
        return CaptureSharedShortcut(action);
    }

    private QuickSwitchResult CaptureSharedShortcutWithDialog(
        string title,
        Func<WidgetShortcut, QuickSwitchResult> apply)
    {
        QuickSwitchResult result = QuickSwitchResult.Cancelled(
            "Shortcut capture closed without a change.");
        Window dialog = new()
        {
            Title = title,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Background = (System.Windows.Media.Brush)FindResource("PageBrush"),
        };
        TextBox capture = new()
        {
            Margin = new Thickness(20),
            IsReadOnly = true,
            Text = "Press a Windows, Ctrl, or Alt shortcut with a letter, number, or F-key.",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        capture.PreviewKeyDown += (_, e) =>
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            int key = KeyInterop.VirtualKeyFromKey(e.Key == Key.System
                ? e.SystemKey
                : e.Key);
            if (!WidgetShortcut.TryCreate(ConvertModifiers(modifiers), key, out WidgetShortcut? shortcut) ||
                shortcut is null)
            {
                capture.Text = "Use Windows, Ctrl, or Alt plus a letter, number, or F-key.";
                e.Handled = true;
                return;
            }

            result = apply(shortcut);
            if (result.Succeeded)
            {
                dialog.DialogResult = true;
            }
            else
            {
                capture.Text = result.Message;
            }
            e.Handled = true;
        };
        dialog.Content = capture;
        dialog.Loaded += (_, _) => capture.Focus();
        dialog.ShowDialog();
        return result;
    }

    private QuickSwitchResult UpdateSharedFavoriteAlias(UpdateFavoriteAliasCommand command)
    {
        if (command.EndpointKind == AudioEndpointKind.Playback &&
            _widgetSettings.GetFavorite(command.Slot) is FavoriteOutputSetting output)
        {
            _widgetSettings = _widgetSettings.WithFavorite(
                command.Slot,
                output with { Alias = command.Alias });
        }
        else if (_widgetSettings.GetInputFavorite(command.Slot) is FavoriteInputSetting input)
        {
            _widgetSettings = _widgetSettings.WithInputFavorite(
                command.Slot,
                input with { Alias = command.Alias });
        }
        else
        {
            return QuickSwitchResult.Warning("That favorite is no longer available.");
        }

        UpdateOptionsControls();
        SaveSettings("Favorite alias saved.");
        return QuickSwitchResult.Success(
            "Favorite alias saved.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Audio,
            QuickSwitchRefreshScope.Options);
    }

    private QuickSwitchResult MoveSharedFavorite(MoveFavoriteCommand command)
    {
        int target = command.Slot + Math.Sign(command.Direction);
        if (target is < 0 or >= WidgetSettings.MaximumFavoriteOutputs)
        {
            return QuickSwitchResult.Info("That favorite is already at the edge.");
        }

        if (command.EndpointKind == AudioEndpointKind.Playback)
        {
            FavoriteOutputSetting? favorite = _widgetSettings.GetFavorite(command.Slot);
            FavoriteOutputSetting? displaced = _widgetSettings.GetFavorite(target);
            WidgetSettings candidate = _widgetSettings
                .WithFavorite(command.Slot, displaced)
                .WithFavorite(target, favorite);
            return CommitSharedFavoriteOrder(candidate);
        }
        else
        {
            FavoriteInputSetting? favorite = _widgetSettings.GetInputFavorite(command.Slot);
            FavoriteInputSetting? displaced = _widgetSettings.GetInputFavorite(target);
            WidgetSettings candidate = _widgetSettings
                .WithInputFavorite(command.Slot, displaced)
                .WithInputFavorite(target, favorite);
            return CommitSharedFavoriteOrder(candidate);
        }
    }

    private QuickSwitchResult CommitSharedFavoriteOrder(
        WidgetSettings candidate)
    {
        HotkeyRegistrationResult result = ApplyHotkeyBindings(candidate);
        if (!result.Succeeded)
        {
            ApplyHotkeyBindings(_widgetSettings);
            return QuickSwitchResult.Warning(
                result.FirstFailure ?? "Favorite order could not be updated.",
                QuickSwitchRefreshScope.Audio,
                QuickSwitchRefreshScope.Options);
        }

        _widgetSettings = candidate;
        UpdateOptionsControls();
        SaveSettings("Favorite order updated.");
        return QuickSwitchResult.Success(
            "Favorite order updated.",
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Audio,
            QuickSwitchRefreshScope.Options);
    }

    private async Task<QuickSwitchResult> ApplySharedFavoriteAsync(
        ApplyAudioFavoriteCommand command)
    {
        if (!command.IsValid)
        {
            return QuickSwitchResult.Warning("That favorite slot is invalid.");
        }

        int slot = command.Slot;
        AudioControlResult? result;
        if (command.EndpointKind == AudioEndpointKind.Playback)
        {
            result = await ApplyFavoriteOutputFromHotkeyAsync(slot);
        }
        else if (command.EndpointKind == AudioEndpointKind.Recording)
        {
            result = await ApplyFavoriteInputFromHotkeyAsync(slot);
        }
        else
        {
            return QuickSwitchResult.Warning("That favorite type is unavailable.");
        }

        if (result is null)
        {
            return QuickSwitchResult.Cancelled("Favorite application was canceled.");
        }

        return result.Succeeded
            ? QuickSwitchResult.Success(
                result.Message,
                OverlayDisposition.CloseOverlay,
                QuickSwitchRefreshScope.Audio,
                QuickSwitchRefreshScope.Favorites)
            : QuickSwitchResult.Failure(
                result.Message,
                QuickSwitchRefreshScope.Audio,
                QuickSwitchRefreshScope.Favorites);
    }

    private QuickSwitchResult OpenSharedFloatingWidget(OverlayFeatureId feature)
    {
        CloseOverlayWorkspace();
        Dispatcher.BeginInvoke(() =>
        {
            switch (feature)
            {
                case OverlayFeatureId.Display:
                    ToggleFloatingDisplayWidget();
                    break;
                case OverlayFeatureId.Audio:
                    ToggleFloatingAudioWidget();
                    break;
                case OverlayFeatureId.Devices:
                    ToggleFloatingDevicesWidget();
                    break;
            }
        });
        return QuickSwitchResult.Success(
            $"Opening floating {feature} controls.",
            OverlayDisposition.OpenFloatingWidget);
    }

    private async Task<QuickSwitchResult> ToggleConfiguredSurfaceAsync()
    {
        if (_widgetSettings.OverlayEnabled)
        {
            ToggleSidebarOverlay();
            return QuickSwitchResult.Info("Workspace overlay toggled.");
        }

        if (IsVisible)
        {
            HideWidget();
            return QuickSwitchResult.Info("WinQuickSwitch hidden.");
        }

        await ShowWidgetAsync();
        return QuickSwitchResult.Info(
            "WinQuickSwitch opened.",
            OverlayDisposition.OpenMainWindow);
    }

    private async Task<QuickSwitchResult> OpenSharedMainWindowAsync(
        OverlayFeatureId? feature)
    {
        CloseOverlayWorkspace();
        WidgetPanel? panel = feature is OverlayFeatureId id &&
            FeatureDescriptorCatalog.TryGetFeature(
                id,
                _productConfiguration,
                out OverlayFeatureDescriptor? descriptor)
            ? descriptor?.MainPanel
            : null;
        await ShowWidgetAsync(panel);
        return QuickSwitchResult.Success(
            "Main window opened.",
            OverlayDisposition.OpenMainWindow);
    }

    private QuickSwitchResult OpenSharedOverlayWorkspace()
    {
        ToggleSidebarOverlay();
        return QuickSwitchResult.Success(
            "Workspace overlay opened.",
            OverlayDisposition.StayOpen);
    }

    private QuickSwitchResult OpenSharedLegalDocument(LegalDocumentKind document)
    {
        CloseOverlayWorkspace();
        Dispatcher.BeginInvoke(() => ShowLegalDocument(document));
        return QuickSwitchResult.Success(
            "Legal document opened.",
            OverlayDisposition.OpenMainWindow);
    }

    private async Task<QuickSwitchResult> ApplySharedProfileAsync(
        ApplyProfileCommand command)
    {
        if (!_profileFeature.IsEnabled || !command.IsValid ||
            FindProfile(command.ProfileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is not available.");
        }

        await ApplyProfileAsync(profile);
        string message = _sharedProfilesStatus;
        if (message.Contains("could not", StringComparison.OrdinalIgnoreCase))
        {
            return QuickSwitchResult.Failure(message, QuickSwitchRefreshScope.All);
        }
        if (message.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return QuickSwitchResult.Warning(message, QuickSwitchRefreshScope.All);
        }

        return QuickSwitchResult.Success(
            message,
            OverlayDisposition.CloseOverlay,
            QuickSwitchRefreshScope.All);
    }

    private async Task<QuickSwitchResult> SaveSharedProfileAsync(ProfileDraft draft)
    {
        if (!_profileFeature.IsEnabled)
        {
            return QuickSwitchResult.Warning("Profiles are available in the paid edition.");
        }
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            return QuickSwitchResult.Warning("Enter a profile name first.");
        }

        bool needsAudio = draft.IncludePlayback || draft.IncludeRecording ||
            draft.IncludeMasterVolume || draft.IncludeMicrophoneMute;
        AudioInventory? inventory = needsAudio
            ? await _audioInventoryService.GetInventoryAsync(_lifetimeCancellation.Token)
            : null;
        AudioEndpointInfo? playbackDefault = inventory?.PlaybackEndpoints
            .FirstOrDefault(endpoint => endpoint.IsDefault);
        AudioEndpointInfo? recordingDefault = inventory?.RecordingEndpoints
            .FirstOrDefault(endpoint => endpoint.IsDefault);
        AudioEndpointControlSnapshot? playbackState =
            draft.IncludeMasterVolume && playbackDefault is not null
                ? await _audioEndpointControlService.GetStateAsync(
                    playbackDefault.Id,
                    _lifetimeCancellation.Token)
                : null;
        AudioEndpointControlSnapshot? recordingState =
            draft.IncludeMicrophoneMute && recordingDefault is not null
                ? await _audioEndpointControlService.GetStateAsync(
                    recordingDefault.Id,
                    _lifetimeCancellation.Token)
                : null;

        ProfileEndpointTarget? playbackGeneral = draft.IncludePlayback
            ? ToProfileEndpoint(playbackDefault)
            : null;
        ProfileEndpointTarget? playbackCalls = draft.IncludePlayback
            ? ToProfileEndpoint(inventory?.PlaybackEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault))
            : null;
        ProfileEndpointTarget? recordingGeneral = draft.IncludeRecording
            ? ToProfileEndpoint(recordingDefault)
            : null;
        ProfileEndpointTarget? recordingCalls = draft.IncludeRecording
            ? ToProfileEndpoint(inventory?.RecordingEndpoints.FirstOrDefault(
                endpoint => endpoint.IsCommunicationsDefault))
            : null;

        ApplySharedProfileFavorite(
            draft.PlaybackFavoriteId,
            isInput: false,
            ref playbackGeneral,
            ref playbackCalls);
        ApplySharedProfileFavorite(
            draft.RecordingFavoriteId,
            isInput: true,
            ref recordingGeneral,
            ref recordingCalls);

        ProfileDefinition? existing = string.IsNullOrWhiteSpace(draft.ExistingProfileId)
            ? null
            : FindProfile(draft.ExistingProfileId);
        TaskbarState? taskbar = draft.IncludeTaskbar
            ? _taskbarService.GetSnapshot().State
            : null;
        if (taskbar == TaskbarState.Unavailable)
        {
            taskbar = null;
        }

        ProfileDefinition profile = new ProfileDefinition(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            draft.Name.Trim(),
            draft.IsPinned,
            existing?.Shortcut,
            draft.IncludeDisplay
                ? _displayTopologyService.GetSnapshot().CurrentMode ?? _currentDisplayMode
                : null,
            playbackGeneral,
            playbackCalls,
            recordingGeneral,
            recordingCalls,
            taskbar,
            draft.IncludeMicrophoneMute ? recordingState?.IsMuted : null,
            draft.IncludeMasterVolume ? playbackState?.MasterVolume : null)
            .Normalize();

        if (!profile.HasActions)
        {
            return QuickSwitchResult.Warning(
                "Select at least one available setting to save in the profile.");
        }

        IReadOnlyList<ProfileDefinition> profiles = existing is null
            ? _profileCatalog.Profiles.Append(profile).ToArray()
            : _profileCatalog.Profiles
                .Select(candidate => candidate.Id == existing.Id ? profile : candidate)
                .ToArray();
        ProfileCatalog candidate = new(
            ProfileCatalog.CurrentSchemaVersion,
            profiles);
        if (!TryApplyAndSaveProfileCatalog(candidate.Normalize(), out string? error))
        {
            return QuickSwitchResult.Failure(error ?? "The profile could not be saved.");
        }

        string message = existing is null
            ? $"{profile.Name} saved."
            : $"{profile.Name} updated.";
        SetSharedProfilesStatus(message);
        return QuickSwitchResult.Success(
            message,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Profiles);
    }

    private void ApplySharedProfileFavorite(
        string? endpointId,
        bool isInput,
        ref ProfileEndpointTarget? general,
        ref ProfileEndpointTarget? communications)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return;
        }

        if (isInput)
        {
            FavoriteInputSetting? favorite = Enumerable.Range(
                    0,
                    WidgetSettings.MaximumFavoriteInputs)
                .Select(_widgetSettings.GetInputFavorite)
                .FirstOrDefault(candidate => candidate?.EndpointId == endpointId);
            if (favorite is null)
            {
                return;
            }
            ProfileEndpointTarget target = new(favorite.EndpointId, favorite.Name);
            if (favorite.Role is FavoriteEndpointRole.General or FavoriteEndpointRole.Both)
            {
                general = target;
            }
            if (favorite.Role is FavoriteEndpointRole.Communications or FavoriteEndpointRole.Both)
            {
                communications = target;
            }
            return;
        }

        FavoriteOutputSetting? output = Enumerable.Range(
                0,
                WidgetSettings.MaximumFavoriteOutputs)
            .Select(_widgetSettings.GetFavorite)
            .FirstOrDefault(candidate => candidate?.EndpointId == endpointId);
        if (output is null)
        {
            return;
        }
        ProfileEndpointTarget outputTarget = new(output.EndpointId, output.Name);
        if (output.Role is FavoriteEndpointRole.General or FavoriteEndpointRole.Both)
        {
            general = outputTarget;
        }
        if (output.Role is FavoriteEndpointRole.Communications or FavoriteEndpointRole.Both)
        {
            communications = outputTarget;
        }
    }

    private async Task<QuickSwitchResult> UndoSharedProfileAsync()
    {
        if (_profileUndo is not ProfileDefinition undo)
        {
            return QuickSwitchResult.Info("There is no profile action to undo.");
        }

        await ApplyProfileAsync(undo);
        return QuickSwitchResult.Success(
            _sharedProfilesStatus,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.All);
    }

    private QuickSwitchResult DuplicateSharedProfile(string profileId)
    {
        if (FindProfile(profileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is no longer available.");
        }

        ProfileDefinition duplicate = profile with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{profile.Name} copy",
            IsPinned = false,
            Shortcut = null,
        };
        SaveProfileCatalog(
            _profileCatalog.Profiles.Append(duplicate).ToArray(),
            $"{duplicate.Name} created.");
        return QuickSwitchResult.Success(
            _sharedProfilesStatus,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Profiles);
    }

    private QuickSwitchResult ToggleSharedProfilePin(string profileId)
    {
        if (FindProfile(profileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is no longer available.");
        }
        if (!profile.IsPinned &&
            _profileCatalog.Profiles.Count(candidate => candidate.IsPinned) >=
                ProfileCatalog.MaximumPinnedProfiles)
        {
            return QuickSwitchResult.Warning(
                $"You can pin up to {ProfileCatalog.MaximumPinnedProfiles} profiles.");
        }

        SaveProfileCatalog(
            _profileCatalog.Profiles
                .Select(candidate => candidate.Id == profileId
                    ? candidate with { IsPinned = !candidate.IsPinned }
                    : candidate)
                .ToArray(),
            profile.IsPinned ? "Profile unpinned." : "Profile pinned.");
        return QuickSwitchResult.Success(
            _sharedProfilesStatus,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Profiles);
    }

    private QuickSwitchResult DeleteSharedProfile(string profileId)
    {
        if (FindProfile(profileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is no longer available.");
        }
        MessageBoxResult confirmation = MessageBox.Show(
            $"Delete {profile.Name}?",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return QuickSwitchResult.Cancelled("Profile deletion canceled.");
        }

        SaveProfileCatalog(
            _profileCatalog.Profiles
                .Where(candidate => candidate.Id != profileId)
                .ToArray(),
            $"{profile.Name} deleted.");
        return QuickSwitchResult.Success(
            _sharedProfilesStatus,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Profiles);
    }

    private QuickSwitchResult ClearSharedProfileShortcut(string profileId)
    {
        if (FindProfile(profileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is no longer available.");
        }
        SaveProfileCatalog(
            _profileCatalog.Profiles
                .Select(candidate => candidate.Id == profileId
                    ? candidate with { Shortcut = null }
                    : candidate)
                .ToArray(),
            $"{profile.Name} shortcut cleared.");
        return QuickSwitchResult.Success(
            _sharedProfilesStatus,
            OverlayDisposition.StayOpen,
            QuickSwitchRefreshScope.Profiles);
    }

    private QuickSwitchResult CaptureSharedProfileShortcut(string profileId)
    {
        if (FindProfile(profileId) is not ProfileDefinition profile)
        {
            return QuickSwitchResult.Warning("That profile is no longer available.");
        }

        return CaptureSharedShortcutWithDialog(
            $"Set shortcut for {profile.Name}",
            shortcut =>
            {
                ProfileCatalog candidate = new(
                    ProfileCatalog.CurrentSchemaVersion,
                    _profileCatalog.Profiles
                        .Select(item => item.Id == profileId
                            ? item with { Shortcut = shortcut }
                            : item)
                        .ToArray());
                if (!TryApplyAndSaveProfileCatalog(candidate.Normalize(), out string? error))
                {
                    string message = error ?? "The shortcut could not be saved.";
                    SetSharedProfilesStatus(message);
                    return QuickSwitchResult.Warning(
                        message,
                        QuickSwitchRefreshScope.Profiles);
                }

                string success = $"{profile.Name} shortcut saved.";
                SetSharedProfilesStatus(success);
                return QuickSwitchResult.Success(
                    success,
                    OverlayDisposition.StayOpen,
                    QuickSwitchRefreshScope.Profiles);
            });
    }

    private IDisposable AcquireSharedSurfaceDemand(QuickSwitchSurfaceDemand demand)
    {
        if ((demand & QuickSwitchSurfaceDemand.Audio) != 0 &&
            Interlocked.Increment(ref _sharedAudioDemand) == 1)
        {
            StartAudioWatcher();
        }
        if ((demand & QuickSwitchSurfaceDemand.Display) != 0)
        {
            Interlocked.Increment(ref _sharedDisplayDemand);
        }
        if ((demand & QuickSwitchSurfaceDemand.Devices) != 0)
        {
            Interlocked.Increment(ref _sharedDevicesDemand);
        }

        return new SharedSurfaceDemandLease(this, demand);
    }

    private void ReleaseSharedSurfaceDemand(QuickSwitchSurfaceDemand demand)
    {
        if ((demand & QuickSwitchSurfaceDemand.Audio) != 0 &&
            Interlocked.Decrement(ref _sharedAudioDemand) <= 0)
        {
            Interlocked.Exchange(ref _sharedAudioDemand, 0);
            if (!IsVisible && _overlayWorkspace is null &&
                _floatingAudioWidget is null)
            {
                StopAudioWatcher();
            }
        }
        if ((demand & QuickSwitchSurfaceDemand.Display) != 0)
        {
            Interlocked.Decrement(ref _sharedDisplayDemand);
        }
        if ((demand & QuickSwitchSurfaceDemand.Devices) != 0)
        {
            Interlocked.Decrement(ref _sharedDevicesDemand);
        }
    }

    private sealed class SharedSurfaceDemandLease(
        MainWindow owner,
        QuickSwitchSurfaceDemand demand) : IDisposable
    {
        private MainWindow? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.ReleaseSharedSurfaceDemand(demand);
    }
}
