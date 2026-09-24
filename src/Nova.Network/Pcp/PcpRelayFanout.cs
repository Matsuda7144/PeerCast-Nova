using System.Diagnostics.CodeAnalysis;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Copies each upstream payload once, then offers the shared owned payload to
/// independently bounded relay-child queues. A slow or failing child is
/// stopped without failing the upstream packet consumer or another child.
/// </summary>
public sealed class PcpRelayFanout : IPcpChannelPacketConsumer, IAsyncDisposable
{
    private const uint HeadPacketType = 0x64616568;
    private const int MaximumRetainedHeadBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly HashSet<PcpRelayChildWorker> _activeChildren = [];
    private readonly HashSet<PcpRelayChildWorker> _knownChildren = [];
    private readonly PcpRelayFanoutOptions _options;
    private readonly bool _replayHeadForNewChildren;
    private Task? _stopTask;
    private RetainedHead? _retainedHead;
    private int _consumeActive;
    private bool _stopping;

    public PcpRelayFanout(
        PcpRelayFanoutOptions? options = null,
        bool replayHeadForNewChildren = false)
    {
        _options = options is null
            ? new PcpRelayFanoutOptions()
            : options with { };
        _options.Validate();
        _replayHeadForNewChildren = replayHeadForNewChildren;
    }

    public int ActiveChildCount
    {
        get
        {
            lock (_gate)
            {
                return _activeChildren.Count;
            }
        }
    }

    public PcpRelayChildRegistration AddChild(IPcpRelayChildSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (!TryAddChild(sink, out var registration) ||
            registration is null)
        {
            throw new InvalidOperationException(
                "The bounded relay-child limit has been reached.");
        }

        return registration;
    }

    /// <summary>
    /// Atomically reserves a bounded child slot. A false result means only
    /// that the finite child limit is currently full; a stopping fan-out still
    /// throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    public bool TryAddChild(
        IPcpRelayChildSink sink,
        [NotNullWhen(true)] out PcpRelayChildRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);

            if (_knownChildren.Count >= _options.MaxChildren)
            {
                registration = null;
                return false;
            }

            var child = new PcpRelayChildWorker(
                sink,
                _options,
                OnChildStoppedAccepting,
                OnChildCompleted);

            if (_replayHeadForNewChildren &&
                _retainedHead is { } retainedHead &&
                !child.TryEnqueue(retainedHead.Packet, retainedHead.Payload))
            {
                child.RequestStop(PcpRelayChildEndReason.OwnerRemoved);
                registration = null;
                return false;
            }

            _activeChildren.Add(child);
            _knownChildren.Add(child);
            registration = new PcpRelayChildRegistration(child);
            return true;
        }
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _consumeActive, 1) != 0)
        {
            throw new InvalidOperationException(
                "Relay fan-out consumption must remain sequential.");
        }

        PcpRelaySharedPayload? payload = null;
        RetainedHead? replacedHead = null;

        try
        {
            if (_replayHeadForNewChildren && packet.Type == HeadPacketType)
            {
                var headByteLimit = (int)Math.Min(
                    MaximumRetainedHeadBytes,
                    _options.MaxPendingBytesPerChild);
                if (packet.Payload.Length > headByteLimit)
                {
                    throw new InvalidDataException(
                        $"The PCP HEAD packet exceeds the bounded {headByteLimit}-byte relay bootstrap limit.");
                }

                payload = PcpRelaySharedPayload.CopyFrom(packet.Payload);
            }

            PcpRelayChildWorker[] children;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);

                if (payload is not null)
                {
                    var retainedPacket = new PcpChannelPacket(
                        packet.ChannelId,
                        packet.SessionId,
                        packet.Sequence,
                        packet.Type,
                        packet.WirePosition,
                        packet.StreamPosition,
                        payload.Memory,
                        packet.Discontinuity,
                        packet.Continuation);
                    payload.AddReference();
                    replacedHead = _retainedHead;
                    _retainedHead = new RetainedHead(retainedPacket, payload);
                }

                children = [.. _activeChildren];
            }

            replacedHead?.Payload.Dispose();

            if (children.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            payload ??= PcpRelaySharedPayload.CopyFrom(packet.Payload);

            foreach (var child in children)
            {
                child.TryEnqueue(packet, payload);
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
            payload?.Dispose();
            Volatile.Write(ref _consumeActive, 0);
        }
    }

    /// <summary>
    /// Waits until every currently known child has completed its in-flight
    /// packet and emptied its bounded queue. The caller must stop upstream
    /// consumption before calling this method so no new packets can race the
    /// idle observation.
    /// </summary>
    public async Task<bool> WaitForIdleAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The relay drain timeout must be positive and no greater than one minute.");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            while (true)
            {
                PcpRelayChildWorker[] children;

                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_stopping, this);
                    children = [.. _knownChildren];
                }

                if (children.All(
                    child => child.Snapshot.PendingPacketCount == 0))
                {
                    return true;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_stopTask is null)
            {
                _stopping = true;
                var children = _knownChildren.ToArray();
                _activeChildren.Clear();
                var retainedHead = _retainedHead;
                _retainedHead = null;
                _stopTask = StopChildrenAsync(children, retainedHead);
            }

            return new ValueTask(_stopTask);
        }
    }

    private static async Task StopChildrenAsync(
        PcpRelayChildWorker[] children,
        RetainedHead? retainedHead)
    {
        try
        {
            foreach (var child in children)
            {
                child.RequestStop(PcpRelayChildEndReason.FanoutStopped);
            }

            var waits = children.Select(
                child => child.WaitForCompletionAsync(CancellationToken.None));
            await Task.WhenAll(waits).ConfigureAwait(false);
        }
        finally
        {
            retainedHead?.Payload.Dispose();
        }
    }

    private void OnChildStoppedAccepting(PcpRelayChildWorker child)
    {
        lock (_gate)
        {
            _activeChildren.Remove(child);
        }
    }

    private void OnChildCompleted(PcpRelayChildWorker child)
    {
        lock (_gate)
        {
            _activeChildren.Remove(child);
            _knownChildren.Remove(child);
        }
    }

    private sealed record RetainedHead(
        PcpChannelPacket Packet,
        PcpRelaySharedPayload Payload);
}
