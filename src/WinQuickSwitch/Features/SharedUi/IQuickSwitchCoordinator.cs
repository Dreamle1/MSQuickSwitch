using WinQuickSwitch.Features.Overlay;

namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Application-scoped state and action boundary shared by every presentation
/// surface. A surface never owns Windows services and never mutates another
/// surface's controls.
/// </summary>
internal interface IQuickSwitchCoordinator
{
    QuickSwitchState State { get; }

    event EventHandler<QuickSwitchStateChangedEventArgs>? StateChanged;

    Task<QuickSwitchResult> ExecuteAsync(
        QuickSwitchCommand command,
        CancellationToken cancellationToken = default);

    Task RefreshAsync(
        QuickSwitchRefreshScope scope,
        CancellationToken cancellationToken = default);

    IDisposable AcquireSurfaceDemand(QuickSwitchSurfaceDemand demand);
}

[Flags]
internal enum QuickSwitchSurfaceDemand
{
    None = 0,
    Display = 1 << 0,
    Audio = 1 << 1,
    Devices = 1 << 2,
    All = Display | Audio | Devices,
}

internal sealed class QuickSwitchStateChangedEventArgs(
    QuickSwitchState state,
    QuickSwitchRefreshScope changedScopes,
    long revision = 0) : EventArgs
{
    public QuickSwitchState State { get; } = state;

    public QuickSwitchRefreshScope ChangedScopes { get; } = changedScopes;

    /// <summary>
    /// Monotonically increasing coordinator revision. A value of zero is kept
    /// for compatibility with simple test or extension coordinators that do
    /// not yet publish ordered revisions.
    /// </summary>
    public long Revision { get; } = Math.Max(0, revision);
}
