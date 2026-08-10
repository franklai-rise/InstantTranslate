namespace InstantTranslate.Services;

internal sealed class RequestVersionGate : IDisposable
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _activeCancellation;
    private long _version;
    private bool _disposed;

    public RequestLease BeginRequest()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelActiveUnsafe();
            _activeCancellation = new CancellationTokenSource();
            _version++;
            return new RequestLease(_version, _activeCancellation.Token);
        }
    }

    public void CancelActive()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            CancelActiveUnsafe();
            _version++;
        }
    }

    public bool IsCurrent(long version)
    {
        lock (_syncRoot)
        {
            return !_disposed && version == _version && _activeCancellation?.IsCancellationRequested == false;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelActiveUnsafe();
        }
    }

    private void CancelActiveUnsafe()
    {
        if (_activeCancellation is null)
        {
            return;
        }

        _activeCancellation.Cancel();
        _activeCancellation.Dispose();
        _activeCancellation = null;
    }
}

internal readonly record struct RequestLease(long Version, CancellationToken CancellationToken);
