using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpHttpFlvPlaybackSession :
    IOwnedPcpChannelPacketConsumer
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private readonly object _gate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpFlvBootstrapRetention _bootstrap;
    private readonly Action<PcpHttpFlvPlaybackSession> _onDisposed;
    private readonly Action _onHeaderReady;
    private TaskCompletionSource<bool> _headerReady = NewHeaderReadySource();
    private Task? _disposeTask;
    private Task<bool>? _completionTask;
    private bool _completing;
    private bool _disposed;
    private bool _headerReadyReported;

    public Guid ChannelIdValue => _channelId.Value;

    public int ActiveClientCount => _fanout.ActiveChildCount;

    public PcpHttpFlvPlaybackSession(
        ChannelId channelId,
        ChannelSessionId sessionId,
        PcpHttpFlvPlaybackOptions options,
        Action<PcpHttpFlvPlaybackSession> onDisposed,
        Action onHeaderReady)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onDisposed);
        ArgumentNullException.ThrowIfNull(onHeaderReady);

        _channelId = channelId;
        _sessionId = sessionId;
        _fanout = new PcpRelayFanout(
            options.Fanout with
            {
                MaxChildren = options.MaximumConcurrentStreamClients,
            });
        _bootstrap = new PcpFlvBootstrapRetention(options.Bootstrap);
        _onDisposed = onDisposed;
        _onHeaderReady = onHeaderReady;
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (packet.ChannelId != _channelId || packet.SessionId != _sessionId)
        {
            throw new InvalidDataException(
                "The FLV packet does not belong to this channel session.");
        }

        if (packet.Type is not HeadPacketType and not DataPacketType)
        {
            return ValueTask.CompletedTask;
        }

        ValueTask fanoutResult;
        var notifyHeaderReady = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _completing, this);

            if (packet.Discontinuity is not null)
            {
                _bootstrap.Reset();
                _headerReady = NewHeaderReadySource();
            }

            // Observe synchronously while callback-scoped packet memory is valid.
            _bootstrap.Observe(packet.Payload);
            if (_bootstrap.StatusSnapshot.HeaderRetained)
            {
                _headerReady.TrySetResult(true);
                if (!_headerReadyReported)
                {
                    _headerReadyReported = true;
                    notifyHeaderReady = true;
                }
            }

            // The fan-out copies packet.Payload into bounded owned child queues.
            // Its completion is synchronous; child I/O never backpressures PCP.
            fanoutResult = _fanout.ConsumeAsync(packet, cancellationToken);
        }

        if (notifyHeaderReady)
        {
            try
            {
                _onHeaderReady();
            }
            catch
            {
                // Optional readiness reporting must not affect PCP consumption.
            }
        }

        return fanoutResult;
    }

    public async Task<bool> WaitForHeaderAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<bool> readyTask;
        lock (_gate)
        {
            if (_disposed || _completing)
            {
                return false;
            }

            if (_bootstrap.StatusSnapshot.HeaderRetained)
            {
                return true;
            }

            readyTask = _headerReady.Task;
        }

        try
        {
            return await readyTask.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public bool TryAddClient(
        IPcpRelayChildSink sink,
        out PcpRelayChildRegistration? registration,
        out PcpFlvBootstrapPayloadSnapshot? bootstrap)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _completing, this);
            if (!_bootstrap.StatusSnapshot.HeaderRetained ||
                !_fanout.TryAddChild(sink, out registration))
            {
                registration = null;
                bootstrap = null;
                return false;
            }

            // Snapshot and child registration share this lock with ConsumeAsync,
            // so no packet can fall between bootstrap and live delivery.
            bootstrap = _bootstrap.Snapshot();
            return true;
        }
    }

    /// <summary>
    /// After upstream has stopped, close admission and drain existing bounded
    /// client queues before removing this source. Cancellation never reports
    /// a successful drain. Fault/abort callers use DisposeAsync directly.
    /// </summary>
    internal Task<bool> CompleteAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            _completing = true;
            _headerReady.TrySetResult(false);
            return _completionTask ??= CompleteCoreAsync(timeout, cancellationToken);
        }
    }

    private async Task<bool> CompleteCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await _fanout.WaitForIdleAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new(_disposeTask);
            }

            _disposed = true;
            _headerReady.TrySetResult(false);
            _disposeTask = DisposeCoreAsync();
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _fanout.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _bootstrap.Reset();
            _onDisposed(this);
        }
    }

    private static TaskCompletionSource<bool> NewHeaderReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
