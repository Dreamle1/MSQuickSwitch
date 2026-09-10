using System.Windows.Input;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Overlay;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class DisplayFeatureViewModel : SharedFeatureViewModel
{
    private DisplayQuickSwitchState _state = DisplayQuickSwitchState.Empty;

    public DisplayFeatureViewModel(IQuickSwitchCoordinator coordinator)
        : base(coordinator, QuickSwitchSurfaceDemand.Display)
    {
        ApplyPcScreenOnlyCommand = CreateDisplayCommand(DisplayMode.PcScreenOnly);
        ApplyDuplicateCommand = CreateDisplayCommand(DisplayMode.Duplicate);
        ApplyExtendCommand = CreateDisplayCommand(DisplayMode.Extend);
        ApplySecondScreenOnlyCommand = CreateDisplayCommand(DisplayMode.SecondScreenOnly);
        RefreshCommand = new AsyncRelayCommand(
            token => RefreshAsync(QuickSwitchRefreshScope.Display, token));
        OpenDisplaySettingsCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenSystemSettingsCommand(OverlaySystemSettingsTarget.Display), token));
        OpenFloatingDisplayCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenFloatingWidgetCommand(OverlayFeatureId.Display), token));
        OpenOverlayCommand = new AsyncRelayCommand(
            token => ExecuteAsync(new OpenOverlayWorkspaceCommand(), token));
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.Display;

    public string StatusMessage => _state.StatusMessage;

    public string TopologySummary => _state.IsReliable
        ? $"{_state.ActiveDisplayCount} active · {_state.AvailableDisplayCount} available"
        : "Display topology is still being verified.";

    public bool IsBusy => _state.IsBusy;

    public bool IsPcScreenOnly => _state.CurrentMode == DisplayMode.PcScreenOnly;

    public bool IsDuplicate => _state.CurrentMode == DisplayMode.Duplicate;

    public bool IsExtend => _state.CurrentMode == DisplayMode.Extend;

    public bool IsSecondScreenOnly => _state.CurrentMode == DisplayMode.SecondScreenOnly;

    public ICommand ApplyPcScreenOnlyCommand { get; }

    public ICommand ApplyDuplicateCommand { get; }

    public ICommand ApplyExtendCommand { get; }

    public ICommand ApplySecondScreenOnlyCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenDisplaySettingsCommand { get; }

    public ICommand OpenFloatingDisplayCommand { get; }

    public ICommand OpenOverlayCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        _state = state.Display;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(TopologySummary));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsPcScreenOnly));
        OnPropertyChanged(nameof(IsDuplicate));
        OnPropertyChanged(nameof(IsExtend));
        OnPropertyChanged(nameof(IsSecondScreenOnly));
    }

    private ICommand CreateDisplayCommand(DisplayMode mode) =>
        new AsyncRelayCommand(
            token => ExecuteAsync(new ApplyDisplayModeCommand(mode), token),
            () => !_state.IsBusy);
}
