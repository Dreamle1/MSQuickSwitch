namespace WinQuickSwitch.Features.Audio;

internal sealed class DebouncedActionScheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _delay;
    private readonly Func<CancellationToken, Task> _action;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    public DebouncedActionScheduler(
        TimeSpan delay,
        Func<CancellationToken, Task> action,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(action);

        _delay = delay;
        _action = action;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public void Schedule()
    {
        CancellationTokenSource current;
        CancellationTokenSource? previous;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _pending;
            current = new CancellationTokenSource();
            _pending = current;
        }

        previous?.Cancel();
        _ = RunAsync(current);
    }

    public void Dispose()
    {
        CancellationTokenSource? pending;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        pending?.Cancel();
    }

    private async Task RunAsync(CancellationTokenSource current)
    {
        try
        {
            await _delayAsync(_delay, current.Token).ConfigureAwait(false);
            await _action(current.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // A newer notification superseded this one or the scheduler closed.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, current))
                {
                    _pending = null;
                }
            }

            current.Dispose();
        }
    }
}
