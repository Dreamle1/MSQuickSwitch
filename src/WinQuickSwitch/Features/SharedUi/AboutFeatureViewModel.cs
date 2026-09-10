using System.Windows.Input;
using WinQuickSwitch.Features.Legal;
using WinQuickSwitch.Features.Overlay;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class AboutFeatureViewModel : SharedFeatureViewModel
{
    private AboutQuickSwitchState _state = AboutQuickSwitchState.Empty;

    public AboutFeatureViewModel(IQuickSwitchCoordinator coordinator)
        : base(coordinator, QuickSwitchSurfaceDemand.None)
    {
        OpenEulaCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenLegalDocumentCommand(LegalDocumentKind.Eula), token));
        OpenPrivacyCommand = new AsyncRelayCommand(
            token => ExecuteAsync(
                new OpenLegalDocumentCommand(LegalDocumentKind.PrivacyPolicy), token));
    }

    protected override QuickSwitchRefreshScope RefreshScope =>
        QuickSwitchRefreshScope.None;

    public string Title => string.IsNullOrWhiteSpace(_state.Edition)
        ? "About WinQuickSwitch"
        : $"About {_state.Edition}";
    public string Description =>
        "A local Windows utility for quick display, audio, device, taskbar, favorite, and profile controls.";
    public string EditionName => _state.Edition;
    public string Version => _state.Version;
    public string PublisherName => _state.Publisher;
    public string LicenseHeading => "Licensed software";
    public string LicenseText =>
        "A Microsoft Store purchase grants permission to use WinQuickSwitch; it does not transfer ownership of the app or its source code.";
    public string SharingText =>
        "Install it on devices allowed by Microsoft Store. Do not copy, upload, redistribute, resell, sublicense, or share the app package or executable with another person.";
    public string LocalDataText =>
        "Keep local settings, saved profiles, device names, and shortcuts private.";
    public string PrivacySummary => string.IsNullOrWhiteSpace(_state.PrivacySummary)
        ? "WinQuickSwitch stores preferences locally and does not sell personal information."
        : _state.PrivacySummary;
    public string ResponsibleUseText =>
        "Display, audio, radio, taskbar, and profile actions change Windows settings for the current user. Review the selected action before applying it.";
    public string StatusMessage => string.Empty;
    public ICommand OpenEulaCommand { get; }
    public ICommand OpenPrivacyCommand { get; }

    protected override void ApplyState(QuickSwitchState state)
    {
        _state = state.About;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EditionName));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(PublisherName));
        OnPropertyChanged(nameof(PrivacySummary));
    }
}
