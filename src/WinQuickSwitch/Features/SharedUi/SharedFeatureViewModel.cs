using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.SharedUi;

internal abstract class SharedFeatureViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly QuickSwitchSurfaceDemand _demand;
    private IDisposable? _demandLease;
    private CancellationTokenSource? _refreshCancellation;
    private bool _attached;
    private long _latestStateRevision;
    private string _refreshErrorMessage = string.Empty;

    protected SharedFeatureViewModel(
        IQuickSwitchCoordinator coordinator,
        QuickSwitchSurfaceDemand demand)
    {
        Coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _demand = demand;
        RetryRefreshCommand = new AsyncRelayCommand(RefreshSafelyAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RetryRefreshCommand { get; }

    public string RefreshErrorMessage
    {
        get => _refreshErrorMessage;
        private set
        {
            if (SetProperty(ref _refreshErrorMessage, value))
            {
                OnPropertyChanged(nameof(RefreshErrorVisibility));
            }
        }
    }

    public Visibility RefreshErrorVisibility =>
        string.IsNullOrWhiteSpace(RefreshErrorMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;

    protected IQuickSwitchCoordinator Coordinator { get; }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Coordinator.StateChanged += Coordinator_StateChanged;
        _demandLease = Coordinator.AcquireSurfaceDemand(_demand);
        _refreshCancellation = new CancellationTokenSource();
        ApplyState(Coordinator.State);
        _ = RefreshSafelyAsync(_refreshCancellation.Token);
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        Coordinator.StateChanged -= Coordinator_StateChanged;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        _demandLease?.Dispose();
        _demandLease = null;
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    protected abstract QuickSwitchRefreshScope RefreshScope { get; }

    protected abstract void ApplyState(QuickSwitchState state);

    protected Task ExecuteAsync(
        QuickSwitchCommand command,
        CancellationToken cancellationToken) =>
        Coordinator.ExecuteAsync(command, cancellationToken);

    protected Task RefreshAsync(
        QuickSwitchRefreshScope scope,
        CancellationToken cancellationToken) =>
        RefreshSafelyAsync(scope, cancellationToken);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        await RefreshSafelyAsync(RefreshScope, cancellationToken);
    }

    private async Task RefreshSafelyAsync(
        QuickSwitchRefreshScope scope,
        CancellationToken cancellationToken)
    {
        RefreshErrorMessage = string.Empty;
        try
        {
            await Coordinator.RefreshAsync(scope, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RefreshErrorMessage =
                $"This panel could not refresh: {exception.Message}";
        }
    }

    private void Coordinator_StateChanged(
        object? sender,
        QuickSwitchStateChangedEventArgs e)
    {
        if ((e.ChangedScopes & RefreshScope) == 0 &&
            e.ChangedScopes != QuickSwitchRefreshScope.None)
        {
            return;
        }

        if (!_attached || !TryAcceptRevision(e.Revision))
        {
            return;
        }

        if (Application.Current?.Dispatcher is { } dispatcher &&
            !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() =>
            {
                if (_attached &&
                    (e.Revision == 0 ||
                     e.Revision == Volatile.Read(ref _latestStateRevision)))
                {
                    ApplyState(e.State);
                }
            });
            return;
        }

        ApplyState(e.State);
    }

    private bool TryAcceptRevision(long revision)
    {
        if (revision == 0)
        {
            return true;
        }

        while (true)
        {
            long current = Volatile.Read(ref _latestStateRevision);
            if (revision <= current)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _latestStateRevision,
                    revision,
                    current) == current)
            {
                return true;
            }
        }
    }
}

internal static class SharedFeatureViewLifetime
{
    public static void Attach(FrameworkElement view)
    {
        WidgetTheme.AttachResourceScope(view);
        if (view.DataContext is SharedFeatureViewModel viewModel)
        {
            viewModel.Attach();
        }
    }

    public static void Detach(FrameworkElement view)
    {
        if (view.DataContext is SharedFeatureViewModel viewModel)
        {
            viewModel.Detach();
        }
    }
}
