namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Keeps only the newest refresh for a state domain active. Starting a newer
/// refresh cancels the previous lease, while disposing an older lease cannot
/// clear or cancel the newer one.
/// </summary>
internal sealed class LatestRefreshGate : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private long _revision;
    private bool _disposed;

    public LatestRefreshLease Begin(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _current?.Cancel();
            CancellationTokenSource source =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            long revision = checked(++_revision);
            _current = source;
            return new LatestRefreshLease(this, source, revision);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            _current = null;
        }

        current?.Cancel();
    }

    internal bool IsCurrent(CancellationTokenSource source, long revision)
    {
        lock (_sync)
        {
            return !_disposed &&
                ReferenceEquals(_current, source) &&
                revision == _revision;
        }
    }

    internal void Release(CancellationTokenSource source)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_current, source))
            {
                _current = null;
            }
        }

        source.Dispose();
    }
}

internal sealed class LatestRefreshLease : IDisposable
{
    private LatestRefreshGate? _owner;
    private CancellationTokenSource? _source;

    internal LatestRefreshLease(
        LatestRefreshGate owner,
        CancellationTokenSource source,
        long revision)
    {
        _owner = owner;
        _source = source;
        Revision = revision;
    }

    public CancellationToken Token =>
        _source?.Token ?? new CancellationToken(canceled: true);

    public long Revision { get; }

    public bool IsCurrent =>
        _owner is { } owner &&
        _source is { } source &&
        owner.IsCurrent(source, Revision);

    public void ThrowIfSuperseded()
    {
        Token.ThrowIfCancellationRequested();
        if (!IsCurrent)
        {
            throw new OperationCanceledException(
                "A newer refresh superseded this request.",
                innerException: null,
                Token);
        }
    }

    public void Dispose()
    {
        LatestRefreshGate? owner = Interlocked.Exchange(ref _owner, null);
        CancellationTokenSource? source = Interlocked.Exchange(ref _source, null);
        if (owner is not null && source is not null)
        {
            owner.Release(source);
        }
    }
}
