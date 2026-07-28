namespace WinQuickSwitch.Features.Widget;

internal interface IStartupRegistrationService
{
    bool IsEnabled { get; }

    StartupRegistrationResult SetEnabled(bool enabled);
}

internal sealed record StartupRegistrationResult(
    bool Succeeded,
    string Message);
