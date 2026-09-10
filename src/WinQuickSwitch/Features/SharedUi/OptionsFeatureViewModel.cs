using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class OptionsFeatureViewModel : SharedFeatureViewModel
{
    private OptionsQuickSwitchState _state = OptionsQuickSwitchState.Empty;
    private IReadOnlyList<FavoriteShortcutRowViewModel> _outputFavoriteRows = [];
    private IReadOnlyList<FavoriteShortcutRowViewModel> _inputFavoriteRows = [];
    private bool _isDarkTheme;
    private bool _isOverlayEnabled;
    private bool _isMainWindowPinned;
    private bool _isStartWithWindowsEnabled;
    private double _overlayDimmingOpacity = WidgetSettings.DefaultOverlayDimmingOpacity;
    private double _inspectorWidth = 720;
    private string _shortcutSearchText = string.Empty;

    public OptionsFeatureViewModel(
        IQuickSwitchCoordinator coordinator,
        ProductConfiguration configuration)
        : base(coordinator, QuickSwitchSurfaceDemand.Audio)
    {
        ShortcutRows = QuickSwitchCommandCatalog.GetShortcutCommands(configuration)
            .Where(descriptor =>
                descriptor.Category != QuickSwitchCommandCategory.Favorites)
            .Select(descriptor =>
                new OptionsShortcutRowViewModel(coordinator, descriptor))
            .ToArray();
        ShortcutSections = ShortcutRows.GroupBy(row => row.Category)
            .OrderBy(group => group.Key)
            .Select(group => new ShortcutSectionViewModel(group.Key, group.ToArray()))
            .ToArray();
        SetDarkThemeCommand = CreateOptionCommand(OverlayOption.DarkTheme);
        SetOverlayEnabledCommand = CreateOptionCommand(OverlayOption.OverlayEnabled);
        SetMainWindowPinnedCommand = CreateOptionCommand(OverlayOption.MainWindowPinned);
        SetStartWithWindowsCommand = CreateOptionCommand(OverlayOption.StartWithWindows);
        ShowTaskbarCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new SetTaskbarAutoHideCommand(false), token));
        AutoHideTaskbarCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new SetTaskbarAutoHideCommand(true), token));
        OpenTaskbarSettingsCommand = OpenSettings(OverlaySystemSettingsTarget.Taskbar);
        OpenDisplaySettingsCommand = OpenSettings(OverlaySystemSettingsTarget.Display);
        OpenNotificationsSettingsCommand = OpenSettings(OverlaySystemSettingsTarget.Notifications);
        ResetShortcutsCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new ResetAllShortcutsCommand(), token));
        RestoreDefaultsCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new RestoreOptionsDefaultsCommand(), token));
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.Options | QuickSwitchRefreshScope.Audio |
        QuickSwitchRefreshScope.Favorites;

    public string Title => "Options";
    public string Description =>
        "Manage shortcuts, appearance, startup, overlay, and taskbar behavior.";
    public string StatusMessage => _state.StatusMessage;
    public string TaskbarDescription => "Show the taskbar or enable Windows auto-hide.";
    public string TaskbarStatusMessage => "Taskbar changes are applied through Windows.";
    public IReadOnlyList<OptionsShortcutRowViewModel> ShortcutRows { get; }
    public IReadOnlyList<ShortcutSectionViewModel> ShortcutSections { get; }
    public string ShortcutSearchText
    {
        get => _shortcutSearchText;
        set
        {
            if (SetProperty(ref _shortcutSearchText, value ?? string.Empty))
            {
                FilterShortcuts();
            }
        }
    }
    public string ShortcutSearchSummary =>
        $"{ShortcutSections.Sum(section => section.FilteredRows.Count)} of {ShortcutRows.Count} command shortcuts";

    private void FilterShortcuts()
    {
        foreach (ShortcutSectionViewModel section in ShortcutSections)
        {
            section.Filter(ShortcutSearchText);
        }
        OnPropertyChanged(nameof(ShortcutSearchSummary));
    }
    public IReadOnlyList<FavoriteShortcutRowViewModel> OutputFavoriteRows => _outputFavoriteRows;
    public IReadOnlyList<FavoriteShortcutRowViewModel> InputFavoriteRows => _inputFavoriteRows;
    public string OutputFavoritesEmptyMessage => _outputFavoriteRows.Count == 0
        ? "In Audio, select an output and choose Add favorite."
        : string.Empty;
    public string InputFavoritesEmptyMessage => _inputFavoriteRows.Count == 0
        ? "In Audio, select a microphone and choose Add favorite."
        : string.Empty;
    public bool AreOutputFavoritesExpanded { get; set; }
    public bool AreInputFavoritesExpanded { get; set; }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }

    public bool IsOverlayEnabled
    {
        get => _isOverlayEnabled;
        set => SetProperty(ref _isOverlayEnabled, value);
    }

    public bool IsMainWindowPinned
    {
        get => _isMainWindowPinned;
        set => SetProperty(ref _isMainWindowPinned, value);
    }

    public bool IsStartWithWindowsEnabled
    {
        get => _isStartWithWindowsEnabled;
        set => SetProperty(ref _isStartWithWindowsEnabled, value);
    }

    public double OverlayDimmingOpacity
    {
        get => _overlayDimmingOpacity;
        set
        {
            double normalized = Math.Clamp(value, 0, 0.85);
            if (SetProperty(ref _overlayDimmingOpacity, normalized))
            {
                OnPropertyChanged(nameof(OverlayDimmingPercent));
                _ = Coordinator.ExecuteAsync(new SetOverlayDimmingOpacityCommand(normalized));
            }
        }
    }

    public string OverlayDimmingPercent => $"{OverlayDimmingOpacity:P0}";

    public double InspectorWidth
    {
        get => _inspectorWidth;
        set
        {
            double normalized = Math.Clamp(value, 520, 900);
            if (SetProperty(ref _inspectorWidth, normalized))
            {
                OnPropertyChanged(nameof(InspectorWidthText));
                _ = Coordinator.ExecuteAsync(new SetOverlayInspectorWidthCommand(normalized));
            }
        }
    }

    public string InspectorWidthText => $"{InspectorWidth:0} DIP";
    public IReadOnlyList<int> AudioVolumeSteps { get; } = Enumerable.Range(1, 20).ToArray();
    public int AudioVolumeStepPercent
    {
        get => _state.AudioVolumeStepPercent;
        set
        {
            if (value != _state.AudioVolumeStepPercent && value is >= 1 and <= 20)
                _ = Coordinator.ExecuteAsync(new SetAudioShortcutStepCommand(value));
        }
    }
    public ICommand SetDarkThemeCommand { get; }
    public ICommand SetOverlayEnabledCommand { get; }
    public ICommand SetMainWindowPinnedCommand { get; }
    public ICommand SetStartWithWindowsCommand { get; }
    public ICommand ShowTaskbarCommand { get; }
    public ICommand AutoHideTaskbarCommand { get; }
    public ICommand OpenTaskbarSettingsCommand { get; }
    public ICommand OpenDisplaySettingsCommand { get; }
    public ICommand OpenNotificationsSettingsCommand { get; }
    public ICommand ResetShortcutsCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        _state = state.Options;
        IsDarkTheme = _state.UseDarkTheme;
        IsOverlayEnabled = _state.OverlayEnabled;
        IsMainWindowPinned = _state.MainWindowPinned;
        IsStartWithWindowsEnabled = _state.StartWithWindows;
        _overlayDimmingOpacity = _state.OverlayDimmingOpacity;
        _inspectorWidth = _state.InspectorWidth;
        _outputFavoriteRows = state.Audio.FavoriteOutputs
            .Select(item => new FavoriteShortcutRowViewModel(Coordinator, item))
            .ToArray();
        _inputFavoriteRows = state.Audio.FavoriteInputs
            .Select(item => new FavoriteShortcutRowViewModel(Coordinator, item))
            .ToArray();
        foreach (OptionsShortcutRowViewModel row in ShortcutRows)
        {
            string? shortcutText = null;
            if (row.Action is WidgetHotkeyAction action)
                _state.ShortcutTexts.TryGetValue(action, out shortcutText);
            else
                _state.CommandShortcutTexts?.TryGetValue(row.CommandKey, out shortcutText);
            row.SetShortcutText(shortcutText ?? "Not set");
        }
        FilterShortcuts();
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(OutputFavoriteRows));
        OnPropertyChanged(nameof(InputFavoriteRows));
        OnPropertyChanged(nameof(OutputFavoritesEmptyMessage));
        OnPropertyChanged(nameof(InputFavoritesEmptyMessage));
        OnPropertyChanged(nameof(OverlayDimmingOpacity));
        OnPropertyChanged(nameof(OverlayDimmingPercent));
        OnPropertyChanged(nameof(InspectorWidth));
        OnPropertyChanged(nameof(InspectorWidthText));
        OnPropertyChanged(nameof(AudioVolumeStepPercent));
    }

    private ICommand CreateOptionCommand(OverlayOption option) =>
        new AsyncRelayCommand<bool>(
            (enabled, token) => ExecuteAsync(
                new SetOverlayOptionCommand(option, enabled), token));

    private ICommand OpenSettings(OverlaySystemSettingsTarget target) =>
        new AsyncRelayCommand(
            token => ExecuteAsync(new OpenSystemSettingsCommand(target), token));

}

internal sealed class ShortcutSectionViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<OptionsShortcutRowViewModel> _allRows;
    private bool _isExpanded = true;
    private bool _searchActive;

    public ShortcutSectionViewModel(
        QuickSwitchCommandCategory category,
        IReadOnlyList<OptionsShortcutRowViewModel> rows)
    {
        Title = category.ToString();
        _allRows = rows;
        Rows = rows;
        FilteredRows = rows;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title { get; }
    public string AccessibleName => $"Show or hide {Title.ToLowerInvariant()} command shortcuts";
    public IReadOnlyList<OptionsShortcutRowViewModel> Rows { get; }
    public IReadOnlyList<OptionsShortcutRowViewModel> FilteredRows { get; private set; }
    public Visibility Visibility => FilteredRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public bool IsExpanded
    {
        get => _searchActive || _isExpanded;
        set
        {
            if (_searchActive || _isExpanded == value) return;
            _isExpanded = value;
            Notify(nameof(IsExpanded));
        }
    }

    public void Filter(string query)
    {
        string normalized = query.Trim();
        bool wasSearching = _searchActive;
        _searchActive = normalized.Length > 0;
        OptionsShortcutRowViewModel[] matches = _allRows.Where(row =>
            normalized.Length == 0 ||
            row.Label.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            row.ShortcutText.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            Title.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (OptionsShortcutRowViewModel row in _allRows)
        {
            row.SetSearchMatch(matches.Contains(row));
        }
        if (!FilteredRows.SequenceEqual(matches))
        {
            // Keep ItemsSource stable: replacing it can leave WPF's nested
            // ItemsControl automation peers detached from their new containers.
            // Visibility filtering preserves both visual and accessibility trees.
            FilteredRows = matches;
            Notify(nameof(FilteredRows));
            Notify(nameof(Visibility));
        }
        if (wasSearching != _searchActive) Notify(nameof(IsExpanded));
    }

    private void Notify(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

internal sealed class OptionsShortcutRowViewModel : INotifyPropertyChanged
{
    private string _shortcutText;
    private bool _matchesSearch = true;

    public OptionsShortcutRowViewModel(
        IQuickSwitchCoordinator coordinator,
        QuickSwitchCommandDescriptor descriptor)
    {
        Action = descriptor.LegacyHotkeyAction;
        CommandKey = descriptor.Key;
        Label = descriptor.Label;
        Category = descriptor.Category;
        _shortcutText = descriptor.DefaultShortcut?.DisplayText ?? "Not set";
        EditorName = $"Shortcut for {Label}";
        EditorHelpText = "Choose Set, then press a modifier and a letter, number, or F-key.";
        CaptureCommand = new AsyncRelayCommand(
            token => coordinator.ExecuteAsync(Action is WidgetHotkeyAction action
                ? new CaptureWidgetShortcutCommand(action)
                : new CaptureCommandShortcutCommand(CommandKey), token));
        ClearCommand = new AsyncRelayCommand(
            token => coordinator.ExecuteAsync(Action is WidgetHotkeyAction action
                ? new ClearWidgetShortcutCommand(action)
                : new ClearCommandShortcutCommand(CommandKey), token));
    }

    public WidgetHotkeyAction? Action { get; }
    public string CommandKey { get; }
    public string Label { get; }
    public QuickSwitchCommandCategory Category { get; }
    public string EditorName { get; }
    public string EditorHelpText { get; }
    public string ShortcutText => _shortcutText;
    public Visibility SearchVisibility => _matchesSearch ? Visibility.Visible : Visibility.Collapsed;
    public ICommand CaptureCommand { get; }
    public ICommand ClearCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetSearchMatch(bool matches)
    {
        if (_matchesSearch == matches) return;
        _matchesSearch = matches;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchVisibility)));
    }

    public void SetShortcutText(string shortcutText)
    {
        string normalized = string.IsNullOrWhiteSpace(shortcutText)
            ? "Not set"
            : shortcutText;
        if (string.Equals(_shortcutText, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _shortcutText = normalized;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(ShortcutText)));
    }

}

internal sealed class FavoriteShortcutRowViewModel : INotifyPropertyChanged
{
    private readonly IQuickSwitchCoordinator _coordinator;
    private string _alias;

    public FavoriteShortcutRowViewModel(
        IQuickSwitchCoordinator coordinator,
        FavoriteAudioEndpointState favorite)
    {
        _coordinator = coordinator;
        Kind = favorite.Kind;
        Role = favorite.Role;
        Slot = favorite.Slot;
        Name = favorite.DisplayName;
        _alias = favorite.DisplayName;
        ShortcutText = string.IsNullOrWhiteSpace(favorite.ShortcutText)
            ? "Set"
            : favorite.ShortcutText;
        MoveUpCommand = Execute(new MoveFavoriteCommand(Kind, Slot, -1));
        MoveDownCommand = Execute(new MoveFavoriteCommand(Kind, Slot, 1));
        CaptureShortcutCommand = Execute(new CaptureFavoriteShortcutCommand(Kind, Slot));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public FavoriteEndpointRole Role { get; }
    public AudioEndpointKind Kind { get; }
    public int Slot { get; }
    public string Name { get; }
    public string ShortcutText { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand CaptureShortcutCommand { get; }

    public string Alias
    {
        get => _alias;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_alias, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _alias = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alias)));
            _ = _coordinator.ExecuteAsync(
                new UpdateFavoriteAliasCommand(Kind, Slot, normalized));
        }
    }

    private ICommand Execute(QuickSwitchCommand command) =>
        new AsyncRelayCommand(token => _coordinator.ExecuteAsync(command, token));
}
