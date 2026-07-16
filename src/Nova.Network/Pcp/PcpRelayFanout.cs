using System.Diagnostics.CodeAnalysis;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Copies each upstream payload once, then offers the shared owned payload to
/// independently bounded relay-child queues. A slow or failing child is
/// stopped without failing the upstream packet consumer or another child.
/// </summary>
public sealed class PcpRelayFanout : IPcpChannelPacketConsumer, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<PcpRelayChildWorker> _activeChildren = [];
    private readonly HashSet<PcpRelayChildWorker> _knownChildren = [];
    private readonly PcpRelayFanoutOptions _options;
    private Task? _stopTask;
    private int _consumeActive;
    private bool _stopping;

    public PcpRelayFanout(PcpRelayFanoutOptions? options = null)
    {
        _options = options is null
            ? new PcpRelayFanoutOptions()
            : options with { };
        _options.Validate();
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

        try
        {
            PcpRelayChildWorker[] children;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                children = [.. _activeChildren];
            }

            if (children.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            using var payload = PcpRelaySharedPayload.CopyFrom(packet.Payload);

            foreach (var child in children)
            {
                child.TryEnqueue(packet, payload);
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
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
                _stopTask = StopChildrenAsync(children);
            }

            return new ValueTask(_stopTask);
        }
    }

    private static async Task StopChildrenAsync(
        PcpRelayChildWorker[] children)
    {
        foreach (var child in children)
        {
            child.RequestStop(PcpRelayChildEndReason.FanoutStopped);
        }

        var waits = children.Select(
            child => child.WaitForCompletionAsync(CancellationToken.None));
        await Task.WhenAll(waits).ConfigureAwait(false);
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
}
