namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Serializes control-plane state changes and publishes immutable snapshots.
/// Media payloads and backpressure never pass through this type.
/// </summary>
public sealed class ChannelStatusTracker
{
    private readonly object _gate = new();
    private ChannelStatusSnapshot _current;

    public ChannelStatusTracker(
        ChannelId channelId,
        ChannelSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);

        _current = new ChannelStatusSnapshot(
            channelId,
            sessionId,
            ChannelSessionState.Created,
            ChannelMetadataSnapshot.Empty,
            streamPosition: null,
            fault: null,
            revision: 0);
    }

    public ChannelStatusSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public ChannelStatusSnapshot TransitionTo(ChannelSessionState nextState)
    {
        lock (_gate)
        {
            if (_current.State == nextState)
            {
                return _current;
            }

            if (nextState == ChannelSessionState.Faulted)
            {
                throw new InvalidOperationException(
                    "A faulted state requires a privacy-safe fault snapshot.");
            }

            if (!CanTransition(_current.State, nextState))
            {
                throw new InvalidOperationException(
                    $"A channel session cannot transition from {_current.State} to {nextState}.");
            }

            var nextFault = nextState is
                ChannelSessionState.Stopping or
                ChannelSessionState.Stopped
                    ? _current.Fault
                    : null;

            return ReplaceCurrent(
                nextState,
                _current.Metadata,
                _current.StreamPosition,
                nextFault);
        }
    }

    /// <summary>
    /// Atomically publishes a terminal state with a fixed privacy-safe fault.
    /// Exception objects and free-form messages are never accepted.
    /// </summary>
    public ChannelStatusSnapshot RecordFault(ChannelFaultSnapshot fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        lock (_gate)
        {
            if (_current.State == ChannelSessionState.Faulted)
            {
                if (_current.Fault == fault)
                {
                    return _current;
                }

                throw new InvalidOperationException(
                    "A terminal channel fault has already been recorded.");
            }

            if (!CanTransition(
                _current.State,
                ChannelSessionState.Faulted))
            {
                throw new InvalidOperationException(
                    $"A channel session cannot record a fault while it is {_current.State}.");
            }

            return ReplaceCurrent(
                ChannelSessionState.Faulted,
                _current.Metadata,
                _current.StreamPosition,
                fault);
        }
    }

    public ChannelStatusSnapshot UpdateMetadata(
        ChannelMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return ApplyObservation(metadata, streamPosition: null);
    }

    public ChannelStatusSnapshot AdvanceStreamPosition(long streamPosition)
    {
        if (streamPosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamPosition),
                streamPosition,
                "Stream position cannot be negative.");
        }

        return ApplyObservation(
            metadata: null,
            streamPosition: streamPosition);
    }

    /// <summary>
    /// Publishes one decoded control-plane observation as one immutable
    /// revision. A null argument means that part of the snapshot is unchanged.
    /// </summary>
    public ChannelStatusSnapshot ApplyObservation(
        ChannelMetadataSnapshot? metadata,
        long? streamPosition)
    {
        if (streamPosition is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamPosition),
                streamPosition,
                "Stream position cannot be negative.");
        }

        lock (_gate)
        {
            if (metadata is not null &&
                _current.State is not (
                    ChannelSessionState.Negotiating or
                    ChannelSessionState.Receiving or
                    ChannelSessionState.Reconnecting))
            {
                throw new InvalidOperationException(
                    $"Channel metadata cannot be updated while the session is {_current.State}.");
            }

            if (streamPosition is not null &&
                _current.State != ChannelSessionState.Receiving)
            {
                throw new InvalidOperationException(
                    $"Stream position cannot advance while the session is {_current.State}.");
            }

            if (streamPosition is not null &&
                _current.StreamPosition > streamPosition)
            {
                throw new InvalidOperationException(
                    "Stream position cannot move backwards without an explicit discontinuity.");
            }

            var nextMetadata = metadata ?? _current.Metadata;
            var nextStreamPosition = streamPosition ?? _current.StreamPosition;

            if (_current.Metadata == nextMetadata &&
                _current.StreamPosition == nextStreamPosition)
            {
                return _current;
            }

            return ReplaceCurrent(
                _current.State,
                nextMetadata,
                nextStreamPosition,
                _current.Fault);
        }
    }

    private ChannelStatusSnapshot ReplaceCurrent(
        ChannelSessionState state,
        ChannelMetadataSnapshot metadata,
        long? streamPosition,
        ChannelFaultSnapshot? fault)
    {
        _current = new ChannelStatusSnapshot(
            _current.ChannelId,
            _current.SessionId,
            state,
            metadata,
            streamPosition,
            fault,
            checked(_current.Revision + 1));

        return _current;
    }

    private static bool CanTransition(
        ChannelSessionState current,
        ChannelSessionState next)
    {
        return current switch
        {
            ChannelSessionState.Created => next is
                ChannelSessionState.Connecting or
                ChannelSessionState.Stopping,
            ChannelSessionState.Connecting => next is
                ChannelSessionState.Negotiating or
                ChannelSessionState.Reconnecting or
                ChannelSessionState.Faulted or
                ChannelSessionState.Stopping,
            ChannelSessionState.Negotiating => next is
                ChannelSessionState.Receiving or
                ChannelSessionState.Reconnecting or
                ChannelSessionState.Faulted or
                ChannelSessionState.Stopping,
            ChannelSessionState.Receiving => next is
                ChannelSessionState.Reconnecting or
                ChannelSessionState.Faulted or
                ChannelSessionState.Stopping,
            ChannelSessionState.Reconnecting => next is
                ChannelSessionState.Negotiating or
                ChannelSessionState.Faulted or
                ChannelSessionState.Stopping,
            ChannelSessionState.Faulted => next is
                ChannelSessionState.Reconnecting or
                ChannelSessionState.Stopping,
            ChannelSessionState.Stopping =>
                next == ChannelSessionState.Stopped,
            ChannelSessionState.Stopped => false,
            _ => false,
        };
    }
}
