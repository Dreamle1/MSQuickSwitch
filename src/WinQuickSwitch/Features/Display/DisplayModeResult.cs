namespace WinQuickSwitch.Features.Display;

public sealed record DisplayModeResult(
    bool Succeeded,
    string Message,
    int? ErrorCode = null);
