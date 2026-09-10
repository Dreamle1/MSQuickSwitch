using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class AudioFeatureViewModel : SharedFeatureViewModel
{
    private AudioQuickSwitchState _state = AudioQuickSwitchState.Empty;
    private AudioEndpointInfo? _selectedPlaybackEndpoint;
    private AudioEndpointInfo? _selectedRecordingEndpoint;
    private AudioDefaultRoleSelection _playbackFavoriteRole = AudioDefaultRoleSelection.General;
    private AudioDefaultRoleSelection _recordingFavoriteRole = AudioDefaultRoleSelection.General;
    private IReadOnlyList<AudioSessionRowViewModel> _sessionRows = Array.Empty<AudioSessionRowViewModel>();

    public AudioFeatureViewModel(IQuickSwitchCoordinator coordinator)
        : base(coordinator, QuickSwitchSurfaceDemand.Audio)
    {
        RefreshCommand = new AsyncRelayCommand(
            token => RefreshAsync(QuickSwitchRefreshScope.Audio, token));
        SetPlaybackDefaultCommand = new AsyncRelayCommand<AudioDefaultRoleSelection>(
            (role, token) => SetDefaultAsync(SelectedPlaybackEndpoint, role, token),
            _ => SelectedPlaybackEndpoint is not null && !_state.IsBusy);
        SetRecordingDefaultCommand = new AsyncRelayCommand<AudioDefaultRoleSelection>(
            (role, token) => SetDefaultAsync(SelectedRecordingEndpoint, role, token),
            _ => SelectedRecordingEndpoint is not null && !_state.IsBusy);
        AddPlaybackFavoriteCommand = new AsyncRelayCommand(
            token => AddFavoriteAsync(
                SelectedPlaybackEndpoint,
                PlaybackFavoriteRole,
                token),
            () => SelectedPlaybackEndpoint is not null && !_state.IsBusy);
        AddRecordingFavoriteCommand = new AsyncRelayCommand(
            token => AddFavoriteAsync(
                SelectedRecordingEndpoint,
                RecordingFavoriteRole,
                token),
            () => SelectedRecordingEndpoint is not null && !_state.IsBusy);
        RemoveFavoriteCommand = new AsyncRelayCommand<FavoriteAudioEndpointState>(
            (favorite, token) => favorite is null
                ? Task.CompletedTask
                : ExecuteAsync(
                    new RemoveAudioFavoriteCommand(favorite.Kind, favorite.Slot),
                    token));
        OpenVolumeMixerCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenAudioSettingsCommand(AudioSettingsTarget.VolumeMixer), token));
        OpenSoundSettingsCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenAudioSettingsCommand(AudioSettingsTarget.Sound), token));
        OpenFloatingAudioCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenFloatingWidgetCommand(OverlayFeatureId.Audio), token));
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.Audio | QuickSwitchRefreshScope.Favorites;

    public IReadOnlyList<AudioEndpointInfo> PlaybackEndpoints => _state.PlaybackEndpoints;

    public IReadOnlyList<AudioEndpointInfo> RecordingEndpoints => _state.RecordingEndpoints;

    public IReadOnlyList<FavoriteAudioEndpointState> FavoriteOutputs => _state.FavoriteOutputs;

    public IReadOnlyList<FavoriteAudioEndpointState> FavoriteInputs => _state.FavoriteInputs;

    public IReadOnlyList<AudioSessionRowViewModel> SessionRows => _sessionRows;

    public bool HasSessionRows => _sessionRows.Count > 0;

    public string SessionSummary => _sessionRows.Count switch
    {
        0 => "No active app volume sessions.",
        1 => "1 active app volume session.",
        _ => $"{_sessionRows.Count} active app volume sessions.",
    };

    public IReadOnlyList<AudioDefaultRoleSelection> RoleOptions { get; } =
        Enum.GetValues<AudioDefaultRoleSelection>();

    public string StatusMessage => _state.StatusMessage;

    public bool IsBusy => _state.IsBusy;

    public AudioEndpointInfo? SelectedPlaybackEndpoint
    {
        get => _selectedPlaybackEndpoint;
        set
        {
            if (SetProperty(ref _selectedPlaybackEndpoint, value))
            {
                RaiseSelectionCommandStates();
            }
        }
    }

    public AudioEndpointInfo? SelectedRecordingEndpoint
    {
        get => _selectedRecordingEndpoint;
        set
        {
            if (SetProperty(ref _selectedRecordingEndpoint, value))
            {
                RaiseSelectionCommandStates();
            }
        }
    }

    public AudioDefaultRoleSelection PlaybackFavoriteRole
    {
        get => _playbackFavoriteRole;
        set => SetProperty(ref _playbackFavoriteRole, value);
    }

    public AudioDefaultRoleSelection RecordingFavoriteRole
    {
        get => _recordingFavoriteRole;
        set => SetProperty(ref _recordingFavoriteRole, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SetPlaybackDefaultCommand { get; }
    public ICommand SetRecordingDefaultCommand { get; }
    public ICommand AddPlaybackFavoriteCommand { get; }
    public ICommand AddRecordingFavoriteCommand { get; }
    public ICommand RemoveFavoriteCommand { get; }
    public ICommand OpenVolumeMixerCommand { get; }
    public ICommand OpenSoundSettingsCommand { get; }
    public ICommand OpenFloatingAudioCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        string? playbackId = SelectedPlaybackEndpoint?.Id;
        string? recordingId = SelectedRecordingEndpoint?.Id;
        _state = state.Audio;
        Dictionary<string, AudioSessionRowViewModel> existingRows =
            _sessionRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        List<AudioSessionRowViewModel> nextRows = [];
        foreach (AudioSessionInfo session in _state.Sessions)
        {
            if (!existingRows.TryGetValue(session.Id, out AudioSessionRowViewModel? row))
            {
                row = new AudioSessionRowViewModel(Coordinator, session);
            }
            else
            {
                row.UpdateFrom(session);
            }

            nextRows.Add(row);
        }

        bool sessionRowsChanged = nextRows.Count != _sessionRows.Count ||
            nextRows.Where((row, index) => !ReferenceEquals(row, _sessionRows[index])).Any();
        if (sessionRowsChanged)
        {
            _sessionRows = nextRows.ToArray();
        }

        SelectedPlaybackEndpoint = FindEndpoint(_state.PlaybackEndpoints, playbackId);
        SelectedRecordingEndpoint = FindEndpoint(_state.RecordingEndpoints, recordingId);
        OnPropertyChanged(nameof(PlaybackEndpoints));
        OnPropertyChanged(nameof(RecordingEndpoints));
        OnPropertyChanged(nameof(FavoriteOutputs));
        OnPropertyChanged(nameof(FavoriteInputs));
        if (sessionRowsChanged)
        {
            OnPropertyChanged(nameof(SessionRows));
        }
        OnPropertyChanged(nameof(HasSessionRows));
        OnPropertyChanged(nameof(SessionSummary));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(IsBusy));
        RaiseSelectionCommandStates();
    }

    private Task SetDefaultAsync(
        AudioEndpointInfo? endpoint,
        AudioDefaultRoleSelection? role,
        CancellationToken cancellationToken) =>
        endpoint is null || role is null
            ? Task.CompletedTask
            : ExecuteAsync(
                new SetDefaultAudioEndpointCommand(
                    endpoint.Id,
                    endpoint.Name,
                    role.Value),
                cancellationToken);

    private Task AddFavoriteAsync(
        AudioEndpointInfo? endpoint,
        AudioDefaultRoleSelection role,
        CancellationToken cancellationToken) =>
        endpoint is null
            ? Task.CompletedTask
            : ExecuteAsync(
                new AddAudioFavoriteCommand(
                    endpoint.Id,
                    endpoint.Name,
                    endpoint.Kind,
                    ToFavoriteRole(role)),
                cancellationToken);

    private static FavoriteEndpointRole ToFavoriteRole(AudioDefaultRoleSelection role) =>
        role switch
        {
            AudioDefaultRoleSelection.Communications => FavoriteEndpointRole.Communications,
            AudioDefaultRoleSelection.Both => FavoriteEndpointRole.Both,
            _ => FavoriteEndpointRole.General,
        };

    private static AudioEndpointInfo? FindEndpoint(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        string? endpointId) =>
        endpoints.FirstOrDefault(endpoint => endpoint.Id == endpointId) ??
        endpoints.FirstOrDefault();

    private void RaiseSelectionCommandStates()
    {
        (SetPlaybackDefaultCommand as AsyncRelayCommand<AudioDefaultRoleSelection>)?
            .RaiseCanExecuteChanged();
        (SetRecordingDefaultCommand as AsyncRelayCommand<AudioDefaultRoleSelection>)?
            .RaiseCanExecuteChanged();
        (AddPlaybackFavoriteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AddRecordingFavoriteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}

internal sealed class AudioSessionRowViewModel : INotifyPropertyChanged
{
    private readonly IQuickSwitchCoordinator _coordinator;
    private int _volumePercent;
    private int _lastKnownVolumePercent;
    private int _pendingVolumePercent;
    private bool _hasPendingVolume;
    private bool _hasUncommittedVolumeEdit;
    private bool _isMuted;
    private bool _lastKnownIsMuted;
    private bool _pendingMute;
    private bool _hasPendingMute;
    private bool _hasUncommittedMuteEdit;

    public AudioSessionRowViewModel(
        IQuickSwitchCoordinator coordinator,
        AudioSessionInfo session)
    {
        _coordinator = coordinator;
        Id = session.Id;
        ApplicationName = session.ApplicationName;
        EndpointName = session.EndpointName;
        _volumePercent = session.VolumePercent;
        _lastKnownVolumePercent = _volumePercent;
        _isMuted = session.IsMuted;
        _lastKnownIsMuted = _isMuted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string ApplicationName { get; private set; }
    public string EndpointName { get; private set; }

    public string VolumeLabel => $"{VolumePercent}%";

    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            int normalized = Math.Clamp(value, 0, 100);
            if (_volumePercent == normalized)
            {
                return;
            }

            _volumePercent = normalized;
            _hasUncommittedVolumeEdit = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeLabel));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value)
            {
                return;
            }

            _isMuted = value;
            _hasUncommittedMuteEdit = true;
            OnPropertyChanged();
        }
    }

    public void UpdateFrom(AudioSessionInfo session)
    {
        if (!string.Equals(ApplicationName, session.ApplicationName, StringComparison.Ordinal))
        {
            ApplicationName = session.ApplicationName;
            OnPropertyChanged(nameof(ApplicationName));
        }

        if (!string.Equals(EndpointName, session.EndpointName, StringComparison.Ordinal))
        {
            EndpointName = session.EndpointName;
            OnPropertyChanged(nameof(EndpointName));
        }

        int volumePercent = session.VolumePercent;
        _lastKnownVolumePercent = volumePercent;
        bool canApplyVolume = true;
        if (_hasUncommittedVolumeEdit)
        {
            if (_volumePercent == volumePercent)
            {
                _hasUncommittedVolumeEdit = false;
            }
            else
            {
                canApplyVolume = false;
            }
        }

        if (canApplyVolume && (!_hasPendingVolume || _pendingVolumePercent == volumePercent))
        {
            _hasPendingVolume = false;
            SetVolumeFromState(volumePercent);
        }

        _lastKnownIsMuted = session.IsMuted;
        bool canApplyMute = true;
        if (_hasUncommittedMuteEdit)
        {
            if (_isMuted == session.IsMuted)
            {
                _hasUncommittedMuteEdit = false;
            }
            else
            {
                canApplyMute = false;
            }
        }

        if (canApplyMute && (!_hasPendingMute || _pendingMute == session.IsMuted))
        {
            _hasPendingMute = false;
            SetMuteFromState(session.IsMuted);
        }
    }

    public async Task CommitVolumeAsync(CancellationToken cancellationToken = default)
    {
        int requested = _volumePercent;
        if (requested == _lastKnownVolumePercent ||
            (_hasPendingVolume && _pendingVolumePercent == requested))
        {
            return;
        }

        _hasPendingVolume = true;
        _pendingVolumePercent = requested;
        _hasUncommittedVolumeEdit = false;

        try
        {
            QuickSwitchResult result = await _coordinator.ExecuteAsync(
                new SetAudioSessionVolumeCommand(Id, requested / 100f),
                cancellationToken);
            if (!_hasPendingVolume || _pendingVolumePercent != requested)
            {
                return;
            }

            if (result.Succeeded)
            {
                _lastKnownVolumePercent = requested;
            }
            else
            {
                _hasPendingVolume = false;
                _hasUncommittedVolumeEdit = false;
                SetVolumeFromState(_lastKnownVolumePercent);
            }
        }
        catch (OperationCanceledException)
        {
            if (_hasPendingVolume && _pendingVolumePercent == requested)
            {
                _hasPendingVolume = false;
                _hasUncommittedVolumeEdit = false;
                SetVolumeFromState(_lastKnownVolumePercent);
            }
        }
        catch
        {
            if (_hasPendingVolume && _pendingVolumePercent == requested)
            {
                _hasPendingVolume = false;
                SetVolumeFromState(_lastKnownVolumePercent);
            }
        }
    }

    public async Task CommitMuteAsync(CancellationToken cancellationToken = default)
    {
        bool requested = _isMuted;
        if (requested == _lastKnownIsMuted ||
            (_hasPendingMute && _pendingMute == requested))
        {
            return;
        }

        _hasPendingMute = true;
        _pendingMute = requested;
        _hasUncommittedMuteEdit = false;

        try
        {
            QuickSwitchResult result = await _coordinator.ExecuteAsync(
                new SetAudioSessionMuteCommand(Id, requested),
                cancellationToken);
            if (!_hasPendingMute || _pendingMute != requested)
            {
                return;
            }

            if (result.Succeeded)
            {
                _lastKnownIsMuted = requested;
            }
            else
            {
                _hasPendingMute = false;
                _hasUncommittedMuteEdit = false;
                SetMuteFromState(_lastKnownIsMuted);
            }
        }
        catch (OperationCanceledException)
        {
            if (_hasPendingMute && _pendingMute == requested)
            {
                _hasPendingMute = false;
                _hasUncommittedMuteEdit = false;
                SetMuteFromState(_lastKnownIsMuted);
            }
        }
        catch
        {
            if (_hasPendingMute && _pendingMute == requested)
            {
                _hasPendingMute = false;
                SetMuteFromState(_lastKnownIsMuted);
            }
        }
    }

    private void SetVolumeFromState(int value)
    {
        int normalized = Math.Clamp(value, 0, 100);
        if (_volumePercent == normalized)
        {
            return;
        }

        _volumePercent = normalized;
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeLabel));
    }

    private void SetMuteFromState(bool value)
    {
        if (_isMuted == value)
        {
            return;
        }

        _isMuted = value;
        OnPropertyChanged(nameof(IsMuted));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
