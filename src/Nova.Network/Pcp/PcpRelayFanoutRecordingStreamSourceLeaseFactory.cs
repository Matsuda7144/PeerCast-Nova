using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Creates recording readers as bounded children of an existing PCP relay
/// fan-out. It never creates or owns a network connection.
/// </summary>
public sealed class PcpRelayFanoutRecordingStreamSourceLeaseFactory :
    IRecordingStreamSourceLeaseFactory
{
    private readonly object _gate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpRecordingPacketStreamBridgeOptions _options;
    private readonly HashSet<RecordingLease> _leases = [];

    private bool _accepting;
    private bool _completed;
    private Task? _completionTask;

    public PcpRelayFanoutRecordingStreamSourceLeaseFactory(
        ChannelId channelId,
        ChannelSessionId sessionId,
        PcpRelayFanout fanout,
        PcpRecordingPacketStreamBridgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(fanout);

        var effectiveOptions = options is null
            ? new PcpRecordingPacketStreamBridgeOptions()
            : options with { };
        effectiveOptions.Validate();

        _channelId = channelId;
        _sessionId = sessionId;
        _fanout = fanout;
        _options = effectiveOptions;
    }

    public ChannelId ChannelId => _channelId;

    public int ActiveLeaseCount
    {
        get
        {
            lock (_gate)
            {
                return _leases.Count;
            }
        }
    }

    /// <summary>
    /// Opens acquisition after the channel owner has started its packet pump.
    /// </summary>
    public void StartAccepting()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _accepting = true;
        }
    }

    /// <summary>
    /// Closes acquisition without terminating readers that were already
    /// returned to recording sessions. The owner completes those readers
    /// after its fan-out drain has finished.
    /// </summary>
    internal void StopAccepting()
    {
        lock (_gate)
        {
            _accepting = false;
        }
    }

    public async ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Candidate.ChannelId != _channelId)
        {
            throw new ArgumentException(
                "The recording request channel does not match this fan-out.",
                nameof(request));
        }

        RecordingLease? rejectedLease = null;

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_accepting || _completed)
            {
                return null;
            }

            var lease = new RecordingLease(
                this,
                _channelId,
                _sessionId,
                _options);

            try
            {
                if (_fanout.TryAddChild(lease, out var registration))
                {
                    lease.Attach(registration);
                    _leases.Add(lease);
                    lease.ObserveCompletion();
                    return lease;
                }
            }
            catch (ObjectDisposedException)
            {
                // The owner may stop between acquisition and child reservation.
            }

            rejectedLease = lease;
        }

        await rejectedLease.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Closes future acquisitions and completes existing readers. A null
    /// terminal fault produces EOF; a fault is propagated as an exception.
    /// </summary>
    public ValueTask CompleteAsync(Exception? terminalFault = null)
    {
        RecordingLease[] leases;
        TaskCompletionSource completionSource;

        lock (_gate)
        {
            _accepting = false;
            _completed = true;

            if (_completionTask is not null)
            {
                return new ValueTask(_completionTask);
            }

            completionSource = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _completionTask = completionSource.Task;
            leases = [.. _leases];
        }

        _ = CompleteLeasesAsync(
            leases,
            terminalFault,
            completionSource);
        return new ValueTask(completionSource.Task);
    }

    private static async Task CompleteLeasesAsync(
        RecordingLease[] leases,
        Exception? terminalFault,
        TaskCompletionSource completionSource)
    {
        try
        {
            foreach (var lease in leases)
            {
                await lease.CompleteFromOwnerAsync(
                    terminalFault).ConfigureAwait(false);
            }

            completionSource.TrySetResult();
        }
        catch (Exception exception)
        {
            completionSource.TrySetException(exception);
        }
    }

    private void Remove(RecordingLease lease)
    {
        lock (_gate)
        {
            _leases.Remove(lease);
        }
    }

    private sealed class RecordingLease :
        IRecordingStreamSourceLease,
        IPcpRelayChildSink
    {
        private readonly object _gate = new();
        private readonly PcpRelayFanoutRecordingStreamSourceLeaseFactory _owner;
        private readonly ChannelId _channelId;
        private readonly ChannelSessionId _sessionId;
        private readonly PcpRecordingPacketStreamBridge _bridge;

        private PcpRelayChildRegistration? _registration;
        private Stream? _readStream;
        private Task? _terminalTask;
        private Task? _disposeTask;
        private long? _expectedSourceSequence;
        private long _recordingSequence;
        private int _terminalRequested;
        private int _disposed;

        public RecordingLease(
            PcpRelayFanoutRecordingStreamSourceLeaseFactory owner,
            ChannelId channelId,
            ChannelSessionId sessionId,
            PcpRecordingPacketStreamBridgeOptions options)
        {
            _owner = owner;
            _channelId = channelId;
            _sessionId = sessionId;
            _bridge = new PcpRecordingPacketStreamBridge(
                channelId,
                options);
        }

        public Stream ReadStream
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(
                        Volatile.Read(ref _disposed) != 0,
                        this);
                    return _readStream ??= _bridge.OpenReadStream();
                }
            }
        }

        public async ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    ObjectDisposedException.ThrowIf(true, this);
                }

                if (Volatile.Read(ref _terminalRequested) != 0)
                {
                    throw new IOException(
                        "The recording source owner has completed.");
                }

                if (!packet.IsRetainedHeadReplay &&
                    _expectedSourceSequence is { } expected &&
                    packet.Sequence != expected)
                {
                    throw new InvalidDataException(
                        $"Expected PCP packet sequence {expected} but observed {packet.Sequence}.");
                }

                var recordingPacket = new PcpChannelPacket(
                    _channelId,
                    _sessionId,
                    _recordingSequence,
                    packet.Type,
                    packet.WirePosition,
                    packet.StreamPosition,
                    packet.Payload,
                    packet.Discontinuity);

                await _bridge.ConsumeAsync(
                    recordingPacket,
                    cancellationToken).ConfigureAwait(false);

                if (!packet.IsRetainedHeadReplay)
                {
                    _expectedSourceSequence = checked(packet.Sequence + 1);
                }

                _recordingSequence = checked(_recordingSequence + 1);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await CompleteStreamAsync(
                    exception,
                    stopChild: false).ConfigureAwait(false);
                throw;
            }
        }

        public void Attach(PcpRelayChildRegistration registration)
        {
            _registration = registration;
        }

        public void ObserveCompletion()
        {
            _ = ObserveCompletionAsync(
                _registration ?? throw new InvalidOperationException(
                    "The fan-out registration has not been attached."));
        }

        public Task CompleteFromOwnerAsync(Exception? terminalFault)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            if (terminalFault is null &&
                _registration?.Snapshot.PendingPacketCount > 0)
            {
                terminalFault = new IOException(
                    "The channel owner stopped before recording packets drained.");
            }

            return CompleteStreamAsync(
                terminalFault,
                stopChild: terminalFault is not null);
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource completionSource;

            lock (_gate)
            {
                if (_disposeTask is not null)
                {
                    return new ValueTask(_disposeTask);
                }

                Volatile.Write(ref _disposed, 1);
                completionSource = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completionSource.Task;
            }

            _owner.Remove(this);
            _ = DisposeCoreAsync(completionSource);
            return new ValueTask(completionSource.Task);
        }

        private async Task ObserveCompletionAsync(
            PcpRelayChildRegistration registration)
        {
            try
            {
                await registration.Completion.ConfigureAwait(false);

                if (Volatile.Read(ref _disposed) == 0 &&
                    Volatile.Read(ref _terminalRequested) == 0)
                {
                    await CompleteStreamAsync(
                        new IOException(
                            $"The recording fan-out child stopped unexpectedly ({registration.Snapshot.EndReason})."),
                        stopChild: false).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    await CompleteStreamAsync(
                        new IOException(
                            "The recording fan-out child failed.",
                            exception),
                        stopChild: false).ConfigureAwait(false);
                }
            }
        }

        private Task CompleteStreamAsync(
            Exception? terminalFault,
            bool stopChild)
        {
            TaskCompletionSource completionSource;

            lock (_gate)
            {
                if (_terminalTask is not null)
                {
                    return _terminalTask;
                }

                Volatile.Write(ref _terminalRequested, 1);
                completionSource = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _terminalTask = completionSource.Task;
            }

            _ = CompleteStreamCoreAsync(
                terminalFault,
                stopChild,
                completionSource);
            return completionSource.Task;
        }

        private async Task CompleteStreamCoreAsync(
            Exception? terminalFault,
            bool stopChild,
            TaskCompletionSource completionSource)
        {
            try
            {
                if (stopChild && _registration is { } registration)
                {
                    _ = await registration.StopAsync().ConfigureAwait(false);
                    await registration.Completion.ConfigureAwait(false);
                }

                await _bridge.CompleteAsync(
                    terminalFault).ConfigureAwait(false);
                completionSource.TrySetResult();
            }
            catch (ObjectDisposedException)
                when (Volatile.Read(ref _disposed) != 0)
            {
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }

        private async Task DisposeCoreAsync(
            TaskCompletionSource completionSource)
        {
            try
            {
                if (_registration is { } registration)
                {
                    _ = await registration.StopAsync().ConfigureAwait(false);
                    await registration.Completion.ConfigureAwait(false);
                }

                await _bridge.DisposeAsync().ConfigureAwait(false);
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }
    }
}
