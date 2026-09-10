using System.Windows.Input;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Taskbar;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class ProfilesFeatureViewModel : SharedFeatureViewModel
{
    private ProfilesQuickSwitchState _state = ProfilesQuickSwitchState.Empty;
    private IReadOnlyList<ProfileRowViewModel> _profiles = [];
    private IReadOnlyList<FavoriteChoiceViewModel> _playbackFavoriteChoices = [];
    private IReadOnlyList<FavoriteChoiceViewModel> _recordingFavoriteChoices = [];
    private FavoriteChoiceViewModel? _selectedPlaybackFavorite;
    private FavoriteChoiceViewModel? _selectedRecordingFavorite;
    private string? _editingProfileId;
    private string _editName = "New profile";
    private bool _includeDisplay = true;
    private bool _includeTaskbar = true;
    private bool _includePlayback = true;
    private bool _includeRecording = true;
    private bool _includeMasterVolume;
    private bool _includeMicrophoneMute;
    private bool _pinAfterSaving;
    private bool _canUndo;

    public ProfilesFeatureViewModel(IQuickSwitchCoordinator coordinator)
        : base(coordinator, QuickSwitchSurfaceDemand.Audio)
    {
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !_state.IsBusy);
        CancelEditCommand = new AsyncRelayCommand(
            _ =>
            {
                ResetEditor();
                return Task.CompletedTask;
            });
        UndoCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new UndoLastProfileCommand(), token),
            () => CanUndo && !_state.IsBusy);
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.Profiles | QuickSwitchRefreshScope.Audio |
        QuickSwitchRefreshScope.Favorites;

    public string Title => "Profiles";
    public string Description =>
        "Save the settings that define a desk mode. Unchecked items stay unchanged when you apply it.";
    public string StatusMessage => _state.StatusMessage;
    public string EmptyMessage => "No profiles saved yet.";
    public string EditorHeading => IsEditing ? "Edit profile" : "Capture current setup";
    public string SaveButtonText => IsEditing ? "Save changes" : "Save current setup";
    public bool HasProfiles => _profiles.Count > 0;
    public bool IsEditing => !string.IsNullOrWhiteSpace(_editingProfileId);
    public IReadOnlyList<ProfileRowViewModel> Profiles => _profiles;
    public IReadOnlyList<FavoriteChoiceViewModel> PlaybackFavoriteChoices => _playbackFavoriteChoices;
    public IReadOnlyList<FavoriteChoiceViewModel> RecordingFavoriteChoices => _recordingFavoriteChoices;

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value ?? string.Empty);
    }

    public bool IncludeDisplay
    {
        get => _includeDisplay;
        set => SetProperty(ref _includeDisplay, value);
    }

    public bool IncludeTaskbar
    {
        get => _includeTaskbar;
        set => SetProperty(ref _includeTaskbar, value);
    }

    public bool IncludePlayback
    {
        get => _includePlayback;
        set => SetProperty(ref _includePlayback, value);
    }

    public bool IncludeRecording
    {
        get => _includeRecording;
        set => SetProperty(ref _includeRecording, value);
    }

    public bool IncludeMasterVolume
    {
        get => _includeMasterVolume;
        set => SetProperty(ref _includeMasterVolume, value);
    }

    public bool IncludeMicrophoneMute
    {
        get => _includeMicrophoneMute;
        set => SetProperty(ref _includeMicrophoneMute, value);
    }

    public bool PinAfterSaving
    {
        get => _pinAfterSaving;
        set => SetProperty(ref _pinAfterSaving, value);
    }

    public FavoriteChoiceViewModel? SelectedPlaybackFavorite
    {
        get => _selectedPlaybackFavorite;
        set => SetProperty(ref _selectedPlaybackFavorite, value);
    }

    public FavoriteChoiceViewModel? SelectedRecordingFavorite
    {
        get => _selectedRecordingFavorite;
        set => SetProperty(ref _selectedRecordingFavorite, value);
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (SetProperty(ref _canUndo, value))
            {
                (UndoCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand UndoCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        _state = state.Profiles;
        _profiles = _state.Profiles
            .Select(profile => new ProfileRowViewModel(
                Coordinator,
                profile,
                BeginEdit,
                () => CanUndo = true))
            .ToArray();
        string? playbackId = SelectedPlaybackFavorite?.Id;
        string? recordingId = SelectedRecordingFavorite?.Id;
        _playbackFavoriteChoices = BuildFavoriteChoices(state.Audio.FavoriteOutputs);
        _recordingFavoriteChoices = BuildFavoriteChoices(state.Audio.FavoriteInputs);
        SelectedPlaybackFavorite = FindChoice(_playbackFavoriteChoices, playbackId);
        SelectedRecordingFavorite = FindChoice(_recordingFavoriteChoices, recordingId);

        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(PlaybackFavoriteChoices));
        OnPropertyChanged(nameof(RecordingFavoriteChoices));
        (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (UndoCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ProfileDraft draft = new(
            _editingProfileId,
            EditName.Trim(),
            IncludeDisplay,
            IncludeTaskbar,
            IncludePlayback,
            IncludeRecording,
            IncludeMasterVolume,
            IncludeMicrophoneMute,
            NormalizeChoiceId(SelectedPlaybackFavorite),
            NormalizeChoiceId(SelectedRecordingFavorite),
            PinAfterSaving);
        QuickSwitchResult result = await Coordinator.ExecuteAsync(
            new SaveProfileDraftCommand(draft), cancellationToken);
        if (result.Succeeded)
        {
            ResetEditor();
        }
    }

    private void BeginEdit(ProfileDefinition profile)
    {
        _editingProfileId = profile.Id;
        EditName = profile.Name;
        IncludeDisplay = profile.DisplayMode is not null;
        IncludeTaskbar = profile.TaskbarState is not null;
        IncludePlayback = profile.PlaybackGeneral is not null ||
            profile.PlaybackCommunications is not null;
        IncludeRecording = profile.RecordingGeneral is not null ||
            profile.RecordingCommunications is not null;
        IncludeMasterVolume = profile.MasterVolume is not null;
        IncludeMicrophoneMute = profile.MicrophoneMuted is not null;
        PinAfterSaving = profile.IsPinned;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorHeading));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private void ResetEditor()
    {
        _editingProfileId = null;
        EditName = "New profile";
        IncludeDisplay = true;
        IncludeTaskbar = true;
        IncludePlayback = true;
        IncludeRecording = true;
        IncludeMasterVolume = false;
        IncludeMicrophoneMute = false;
        PinAfterSaving = false;
        SelectedPlaybackFavorite = _playbackFavoriteChoices.FirstOrDefault();
        SelectedRecordingFavorite = _recordingFavoriteChoices.FirstOrDefault();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorHeading));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private static IReadOnlyList<FavoriteChoiceViewModel> BuildFavoriteChoices(
        IReadOnlyList<FavoriteAudioEndpointState> favorites) =>
        new[] { new FavoriteChoiceViewModel(string.Empty, "Use current endpoint") }
            .Concat(favorites.Select(favorite =>
                new FavoriteChoiceViewModel(favorite.EndpointId, favorite.DisplayName)))
            .ToArray();

    private static FavoriteChoiceViewModel? FindChoice(
        IReadOnlyList<FavoriteChoiceViewModel> choices,
        string? id) =>
        choices.FirstOrDefault(choice => choice.Id == id) ?? choices.FirstOrDefault();

    private static string? NormalizeChoiceId(FavoriteChoiceViewModel? choice) =>
        string.IsNullOrWhiteSpace(choice?.Id) ? null : choice.Id;
}

internal sealed record FavoriteChoiceViewModel(string Id, string Name);

internal sealed class ProfileRowViewModel
{
    public ProfileRowViewModel(
        IQuickSwitchCoordinator coordinator,
        ProfileDefinition profile,
        Action<ProfileDefinition> beginEdit,
        Action markUndoAvailable)
    {
        Id = profile.Id;
        Name = profile.Name;
        IsPinned = profile.IsPinned;
        Summary = Describe(profile);
        ShortcutText = profile.Shortcut?.DisplayText ?? "Set shortcut";
        PinButtonText = profile.IsPinned ? "Unpin" : "Pin";
        QuickSwitchCommandDescriptor applyDescriptor =
            QuickSwitchCommandCatalog.CreateProfile(profile);
        ApplyCommand = new AsyncRelayCommand(async token =>
        {
            QuickSwitchResult result = await coordinator.ExecuteAsync(
                applyDescriptor.CreateCommand(), token);
            if (result.Succeeded)
            {
                markUndoAvailable();
            }
        });
        EditCommand = new AsyncRelayCommand(
            _ =>
            {
                beginEdit(profile);
                return Task.CompletedTask;
            });
        DuplicateCommand = Execute(new DuplicateProfileCommand(profile.Id));
        TogglePinCommand = Execute(new ToggleProfilePinCommand(profile.Id));
        CaptureShortcutCommand = Execute(new CaptureProfileShortcutCommand(profile.Id));
        ClearShortcutCommand = Execute(new ClearProfileShortcutCommand(profile.Id));
        DeleteCommand = Execute(new DeleteProfileCommand(profile.Id));

        ICommand Execute(QuickSwitchCommand command) =>
            new AsyncRelayCommand(token => coordinator.ExecuteAsync(command, token));
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }
    public string ShortcutText { get; }
    public string PinButtonText { get; }
    public string LastResultMessage => string.Empty;
    public bool IsPinned { get; }
    public ICommand ApplyCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand CaptureShortcutCommand { get; }
    public ICommand ClearShortcutCommand { get; }
    public ICommand DeleteCommand { get; }

    private static string Describe(ProfileDefinition profile)
    {
        List<string> parts = [];
        if (profile.DisplayMode is not null)
        {
            parts.Add($"Display: {profile.DisplayMode}");
        }

        if (profile.PlaybackGeneral is not null || profile.PlaybackCommunications is not null)
        {
            parts.Add("Playback");
        }

        if (profile.RecordingGeneral is not null || profile.RecordingCommunications is not null)
        {
            parts.Add("Recording");
        }

        if (profile.TaskbarState is TaskbarState state)
        {
            parts.Add(state == TaskbarState.AutoHidden ? "Taskbar: auto-hide" : "Taskbar: visible");
        }

        if (profile.MasterVolume is not null)
        {
            parts.Add("Volume");
        }

        if (profile.MicrophoneMuted is not null)
        {
            parts.Add("Microphone mute");
        }

        return parts.Count == 0 ? "No actions selected" : string.Join(" · ", parts);
    }
}
