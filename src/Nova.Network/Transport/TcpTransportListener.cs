using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Transport;

/// <summary>
/// A single-use TCP listener with bounded accepted-connection concurrency.
/// Stop waits until pending accept operations have observed cancellation.
/// Accepted connections remain owned by their callers after the listener stops.
/// </summary>
public sealed class TcpTransportListener : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IPEndPoint _requestedEndPoint;
    private readonly TcpTransportOptions _options;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly SemaphoreSlim _connectionSlots;

    private TcpListener? _listener;
    private TaskCompletionSource<bool> _acceptsDrained =
        CreateCompletedTaskSource();

    private int _activeAcceptOperations;
    private bool _started;
    private bool _stopped;
    private int _disposed;

    public TcpTransportListener(
        IPEndPoint localEndPoint,
        TcpTransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(localEndPoint);

        _requestedEndPoint = localEndPoint;
        _options = options is null ? new TcpTransportOptions() : options with { };
        _options.Validate();

        _connectionSlots = new SemaphoreSlim(
            _options.MaxConcurrentConnections,
            _options.MaxConcurrentConnections);
    }

    public IPEndPoint LocalEndPoint
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (!_started || _listener is null)
                {
                    throw new InvalidOperationException("The TCP listener has not started.");
                }

                return (IPEndPoint)_listener.LocalEndpoint;
            }
        }
    }

    public int MaxConcurrentConnections => _options.MaxConcurrentConnections;

    public void Start()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_started)
            {
                return;
            }

            if (_stopped)
            {
                throw new InvalidOperationException(
                    "A stopped TCP listener cannot be restarted.");
            }

            var listener = new TcpListener(_requestedEndPoint);

            try
            {
                if (_requestedEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                    && _options.EnableDualMode)
                {
                    listener.Server.DualMode = true;
                }

                listener.Start(_options.ListenBacklog);
                _listener = listener;
                _started = true;
            }
            catch
            {
                listener.Stop();
                throw;
            }
        }
    }

    public async ValueTask<TcpTransportConnection> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        TcpListener listener;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_started || _listener is null)
            {
                throw new InvalidOperationException("The TCP listener has not started.");
            }

            if (_stopped)
            {
                throw new InvalidOperationException("The TCP listener has stopped.");
            }

            listener = _listener;
            TrackAcceptStarted();
        }

        var slotAcquired = false;
        var connectionOwnsSlot = false;

        try
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeSource.Token);

            await _connectionSlots.WaitAsync(linkedSource.Token).ConfigureAwait(false);
            slotAcquired = true;

            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(
                    linkedSource.Token).ConfigureAwait(false);
            }
            catch (SocketException exception) when (linkedSource.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The TCP accept operation was canceled.",
                    exception,
                    linkedSource.Token);
            }

            try
            {
                TcpSocketConfigurator.Apply(client, _options);

                var connection = new TcpTransportConnection(
                    client,
                    ReleaseConnectionSlot);

                connectionOwnsSlot = true;
                return connection;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            if (slotAcquired && !connectionOwnsSlot)
            {
                _connectionSlots.Release();
            }

            TrackAcceptFinished();
        }
    }

    public ValueTask StopAsync()
    {
        return StopCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopCoreAsync().ConfigureAwait(false);
        _lifetimeSource.Dispose();
    }

    private async ValueTask StopCoreAsync()
    {
        TcpListener? listener;
        Task acceptsDrained;

        lock (_sync)
        {
            if (_stopped)
            {
                acceptsDrained = _acceptsDrained.Task;
                listener = null;
            }
            else
            {
                _stopped = true;
                listener = _listener;
                acceptsDrained = _acceptsDrained.Task;
            }
        }

        _lifetimeSource.Cancel();
        listener?.Stop();

        await acceptsDrained.ConfigureAwait(false);
    }

    private void TrackAcceptStarted()
    {
        if (_activeAcceptOperations == 0)
        {
            _acceptsDrained = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _activeAcceptOperations++;
    }

    private void TrackAcceptFinished()
    {
        lock (_sync)
        {
            _activeAcceptOperations--;

            if (_activeAcceptOperations == 0)
            {
                _acceptsDrained.TrySetResult(true);
            }
        }
    }

    private void ReleaseConnectionSlot()
    {
        _connectionSlots.Release();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static TaskCompletionSource<bool> CreateCompletedTaskSource()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        source.SetResult(true);
        return source;
    }
}
