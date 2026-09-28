using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpFmp4PublishingBoundaryCreateResult
{
    Created,
    InvalidConfiguration,
    InvalidIdentity,
    InvalidConsumer,
}

public enum PcpFmp4PublishingSegmentRole
{
    Initialization,
    Media,
}

public enum PcpFmp4PublishingBoundaryState
{
    AwaitingInitialization,
    Ready,
    Faulted,
    Disposed,
}

public enum PcpFmp4PublishingResult
{
    None,
    Published,
    InvalidSegmentRole,
    InvalidSegment,
    InitializationRequired,
    InitializationAlreadyPublished,
    InitializationSegmentTooLarge,
    MediaSegmentTooLarge,
    PacketSequenceOverflow,
    AccountingOverflow,
    PositionOverflow,
    ConcurrentPublishRejected,
    ConsumerRejected,
    Cancelled,
    Faulted,
    Disposed,
}

public sealed record PcpFmp4PublishingBoundaryOptions
{
    public const int MaximumInitializationBytesHardLimit = 1024 * 1024;
    public const int MaximumMediaSegmentBytesHardLimit = 64 * 1024 * 1024;
    public const int MaximumPacketPayloadBytesHardLimit = 1024 * 1024;

    public int MaximumInitializationBytes { get; init; } = 64 * 1024;

    public int MaximumMediaSegmentBytes { get; init; } = 64 * 1024 * 1024;

    public int MaximumPacketPayloadBytes { get; init; } = 64 * 1024;

    internal bool IsValid =>
        MaximumInitializationBytes is >= 1 and <= MaximumInitializationBytesHardLimit &&
        MaximumMediaSegmentBytes is >= 1 and <= MaximumMediaSegmentBytesHardLimit &&
        MaximumPacketPayloadBytes is >= 1 and <= MaximumPacketPayloadBytesHardLimit &&
        MaximumInitializationBytes <= MaximumPacketPayloadBytes;
}

public readonly record struct PcpFmp4PublishingBoundarySnapshot(
    PcpFmp4PublishingBoundaryState State,
    PcpFmp4PublishingResult Result,
    long InitializationSegmentCount,
    long MediaSegmentCount,
    long AcceptedPacketCount,
    long AcceptedByteCount,
    long HeadPacketCount,
    long DataPacketCount,
    long ConsumerFailureCount,
    long NextSequence,
    uint NextWirePosition,
    long NextStreamPosition);

/// <summary>
/// Turns caller-owned, pre-muxed fMP4 segments into bounded PCP HEAD and DATA
/// packets. This boundary treats segment contents as opaque bytes and awaits
/// each consumer callback before it advances accounting or reads the next
/// packet slice. Callers must keep each segment stable until publication
/// completes; the boundary does not retain its memory afterward.
/// </summary>
public sealed class PcpFmp4PublishingBoundary : IAsyncDisposable
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private readonly object _gate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly IPcpChannelPacketConsumer _consumer;
    private readonly PcpFmp4PublishingBoundaryOptions _options;
    private PcpFmp4PublishingBoundarySnapshot _snapshot = new(
        PcpFmp4PublishingBoundaryState.AwaitingInitialization,
        PcpFmp4PublishingResult.None,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
    private bool _operationActive;
    private bool _disposeRequested;
    private TaskCompletionSource? _operationDrained;
    private Task? _disposeTask;

    internal PcpFmp4PublishingBoundary(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPcpChannelPacketConsumer consumer,
        PcpFmp4PublishingBoundaryOptions options,
        uint initialWirePosition,
        long initialStreamPosition,
        long initialSequence)
    {
        _channelId = channelId;
        _sessionId = sessionId;
        _consumer = consumer;
        _options = options;
        _snapshot = _snapshot with
        {
            NextSequence = initialSequence,
            NextWirePosition = initialWirePosition,
            NextStreamPosition = initialStreamPosition,
        };
    }

    public PcpFmp4PublishingBoundarySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public static PcpFmp4PublishingBoundaryCreateResult Create(
        ChannelId? channelId,
        ChannelSessionId? sessionId,
        IPcpChannelPacketConsumer? consumer,
        out PcpFmp4PublishingBoundary? boundary,
        PcpFmp4PublishingBoundaryOptions? options = null)
    {
        boundary = null;
        if (channelId is null || sessionId is null)
        {
            return PcpFmp4PublishingBoundaryCreateResult.InvalidIdentity;
        }

        if (consumer is null)
        {
            return PcpFmp4PublishingBoundaryCreateResult.InvalidConsumer;
        }

        var copiedOptions = options is null
            ? new PcpFmp4PublishingBoundaryOptions()
            : options with { };
        if (!copiedOptions.IsValid)
        {
            return PcpFmp4PublishingBoundaryCreateResult.InvalidConfiguration;
        }

        boundary = new PcpFmp4PublishingBoundary(
            channelId,
            sessionId,
            consumer,
            copiedOptions,
            initialWirePosition: 0,
            initialStreamPosition: 0,
            initialSequence: 0);
        return PcpFmp4PublishingBoundaryCreateResult.Created;
    }

    /// <summary>
    /// Publishes one complete initialization or media segment. A media segment
    /// may become several sequential DATA packets; packet callbacks are awaited
    /// before publication advances.
    /// </summary>
    public ValueTask<PcpFmp4PublishingResult> PublishSegmentAsync(
        ReadOnlyMemory<byte> segment,
        PcpFmp4PublishingSegmentRole role,
        CancellationToken cancellationToken = default)
    {
        if (!TryStartOperation(out var immediateResult))
        {
            return ValueTask.FromResult(immediateResult);
        }

        return PublishSegmentCoreAsync(segment, role, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeRequested = true;
            if (_operationActive)
            {
                _operationDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = DisposeAfterOperationAsync(_operationDrained.Task);
            }
            else
            {
                _snapshot = _snapshot with
                {
                    State = PcpFmp4PublishingBoundaryState.Disposed,
                    Result = PcpFmp4PublishingResult.Disposed,
                };
                _disposeTask = Task.CompletedTask;
            }

            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    internal static bool TryAdvancePositions(
        uint wirePosition,
        long streamPosition,
        long byteCount,
        out uint nextWirePosition,
        out long nextStreamPosition)
    {
        nextWirePosition = wirePosition;
        nextStreamPosition = streamPosition;
        if (streamPosition < 0 || byteCount < 0)
        {
            return false;
        }

        var wireEnd = (ulong)wirePosition + (ulong)byteCount;
        if (wireEnd > uint.MaxValue || byteCount > long.MaxValue - streamPosition)
        {
            return false;
        }

        nextWirePosition = (uint)wireEnd;
        nextStreamPosition = streamPosition + byteCount;
        return true;
    }

    private bool TryStartOperation(out PcpFmp4PublishingResult result)
    {
        lock (_gate)
        {
            if (_disposeRequested ||
                _snapshot.State == PcpFmp4PublishingBoundaryState.Disposed)
            {
                result = PcpFmp4PublishingResult.Disposed;
                return false;
            }

            if (_snapshot.State == PcpFmp4PublishingBoundaryState.Faulted)
            {
                result = PcpFmp4PublishingResult.Faulted;
                return false;
            }

            if (_operationActive)
            {
                result = PcpFmp4PublishingResult.ConcurrentPublishRejected;
                return false;
            }

            _operationActive = true;
            _operationDrained = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            result = PcpFmp4PublishingResult.None;
            return true;
        }
    }

    private async ValueTask<PcpFmp4PublishingResult> PublishSegmentCoreAsync(
        ReadOnlyMemory<byte> segment,
        PcpFmp4PublishingSegmentRole role,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return FinishTerminal(PcpFmp4PublishingResult.Cancelled);
            }

            if (role is not PcpFmp4PublishingSegmentRole.Initialization and
                not PcpFmp4PublishingSegmentRole.Media)
            {
                return FinishNonterminal(PcpFmp4PublishingResult.InvalidSegmentRole);
            }

            if (segment.IsEmpty)
            {
                return FinishNonterminal(PcpFmp4PublishingResult.InvalidSegment);
            }

            var snapshot = Snapshot;
            if (role == PcpFmp4PublishingSegmentRole.Initialization &&
                snapshot.InitializationSegmentCount != 0)
            {
                return FinishNonterminal(
                    PcpFmp4PublishingResult.InitializationAlreadyPublished);
            }

            if (role == PcpFmp4PublishingSegmentRole.Media &&
                snapshot.InitializationSegmentCount == 0)
            {
                return FinishNonterminal(PcpFmp4PublishingResult.InitializationRequired);
            }

            if (role == PcpFmp4PublishingSegmentRole.Initialization &&
                segment.Length > _options.MaximumInitializationBytes)
            {
                return FinishNonterminal(
                    PcpFmp4PublishingResult.InitializationSegmentTooLarge);
            }

            if (role == PcpFmp4PublishingSegmentRole.Media &&
                segment.Length > _options.MaximumMediaSegmentBytes)
            {
                return FinishNonterminal(PcpFmp4PublishingResult.MediaSegmentTooLarge);
            }

            var packetCount = role == PcpFmp4PublishingSegmentRole.Initialization
                ? 1
                : ((long)segment.Length + _options.MaximumPacketPayloadBytes - 1) /
                    _options.MaximumPacketPayloadBytes;
            if (!TryAdvanceSequence(snapshot.NextSequence, packetCount, out _))
            {
                return FinishTerminal(PcpFmp4PublishingResult.PacketSequenceOverflow);
            }

            if (!HasRemainingCapacity(snapshot, role, segment.Length, packetCount))
            {
                return FinishTerminal(PcpFmp4PublishingResult.AccountingOverflow);
            }

            if (!TryAdvancePositions(
                    snapshot.NextWirePosition,
                    snapshot.NextStreamPosition,
                    segment.Length,
                    out _,
                    out _))
            {
                return FinishTerminal(PcpFmp4PublishingResult.PositionOverflow);
            }

            var packetType = role == PcpFmp4PublishingSegmentRole.Initialization
                ? HeadPacketType
                : DataPacketType;
            var payloadOffset = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var payloadLength = role == PcpFmp4PublishingSegmentRole.Initialization
                    ? segment.Length
                    : Math.Min(
                        _options.MaximumPacketPayloadBytes,
                        segment.Length - payloadOffset);
                var packet = new PcpChannelPacket(
                    _channelId,
                    _sessionId,
                    snapshot.NextSequence,
                    packetType,
                    snapshot.NextWirePosition,
                    snapshot.NextStreamPosition,
                    segment.Slice(payloadOffset, payloadLength),
                    discontinuity: null);

                try
                {
                    await _consumer.ConsumeAsync(packet, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return FinishTerminal(PcpFmp4PublishingResult.Cancelled);
                }
                catch (Exception)
                {
                    return FinishConsumerFailure();
                }

                if (!TryAdvancePositions(
                        snapshot.NextWirePosition,
                        snapshot.NextStreamPosition,
                        payloadLength,
                        out var nextWirePosition,
                        out var nextStreamPosition))
                {
                    return FinishTerminal(PcpFmp4PublishingResult.PositionOverflow);
                }

                if (!TryAdvanceSequence(snapshot.NextSequence, 1, out var nextSequence))
                {
                    return FinishTerminal(PcpFmp4PublishingResult.PacketSequenceOverflow);
                }

                snapshot = RecordAcceptedPacket(
                    snapshot,
                    role,
                    payloadLength,
                    nextSequence,
                    nextWirePosition,
                    nextStreamPosition);
                payloadOffset = checked(payloadOffset + payloadLength);
            }
            while (payloadOffset < segment.Length);

            RecordAcceptedSegment(role);
            return FinishSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FinishTerminal(PcpFmp4PublishingResult.Cancelled);
        }
        finally
        {
            ExitOperation();
        }
    }

    private static bool HasRemainingCapacity(
        PcpFmp4PublishingBoundarySnapshot snapshot,
        PcpFmp4PublishingSegmentRole role,
        int segmentLength,
        long packetCount) =>
        snapshot.AcceptedPacketCount <= long.MaxValue - packetCount &&
        snapshot.AcceptedByteCount <= long.MaxValue - segmentLength &&
        (role == PcpFmp4PublishingSegmentRole.Initialization
            ? snapshot.HeadPacketCount <= long.MaxValue - packetCount &&
                snapshot.InitializationSegmentCount < long.MaxValue
            : snapshot.DataPacketCount <= long.MaxValue - packetCount &&
                snapshot.MediaSegmentCount < long.MaxValue);

    private static bool TryAdvanceSequence(
        long sequence,
        long packetCount,
        out long nextSequence)
    {
        nextSequence = sequence;
        if (sequence < 0 || packetCount < 1 || sequence > long.MaxValue - packetCount)
        {
            return false;
        }

        nextSequence = sequence + packetCount;
        return true;
    }

    private PcpFmp4PublishingBoundarySnapshot RecordAcceptedPacket(
        PcpFmp4PublishingBoundarySnapshot prior,
        PcpFmp4PublishingSegmentRole role,
        int payloadLength,
        long nextSequence,
        uint nextWirePosition,
        long nextStreamPosition)
    {
        lock (_gate)
        {
            var isHead = role == PcpFmp4PublishingSegmentRole.Initialization;
            _snapshot = prior with
            {
                AcceptedPacketCount = checked(prior.AcceptedPacketCount + 1),
                AcceptedByteCount = checked(prior.AcceptedByteCount + payloadLength),
                HeadPacketCount = checked(prior.HeadPacketCount + (isHead ? 1 : 0)),
                DataPacketCount = checked(prior.DataPacketCount + (isHead ? 0 : 1)),
                NextSequence = nextSequence,
                NextWirePosition = nextWirePosition,
                NextStreamPosition = nextStreamPosition,
            };
            return _snapshot;
        }
    }

    private void RecordAcceptedSegment(PcpFmp4PublishingSegmentRole role)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = role == PcpFmp4PublishingSegmentRole.Initialization
                    ? PcpFmp4PublishingBoundaryState.Ready
                    : _snapshot.State,
                InitializationSegmentCount = checked(
                    _snapshot.InitializationSegmentCount +
                    (role == PcpFmp4PublishingSegmentRole.Initialization ? 1 : 0)),
                MediaSegmentCount = checked(
                    _snapshot.MediaSegmentCount +
                    (role == PcpFmp4PublishingSegmentRole.Media ? 1 : 0)),
            };
        }
    }

    private PcpFmp4PublishingResult FinishSuccess()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { Result = PcpFmp4PublishingResult.Published };
        }

        return PcpFmp4PublishingResult.Published;
    }

    private PcpFmp4PublishingResult FinishNonterminal(
        PcpFmp4PublishingResult result)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { Result = result };
        }

        return result;
    }

    private PcpFmp4PublishingResult FinishTerminal(PcpFmp4PublishingResult result)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = PcpFmp4PublishingBoundaryState.Faulted,
                Result = result,
            };
        }

        return result;
    }

    private PcpFmp4PublishingResult FinishConsumerFailure()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = PcpFmp4PublishingBoundaryState.Faulted,
                Result = PcpFmp4PublishingResult.ConsumerRejected,
                ConsumerFailureCount = checked(_snapshot.ConsumerFailureCount + 1),
            };
        }

        return PcpFmp4PublishingResult.ConsumerRejected;
    }

    private async Task DisposeAfterOperationAsync(Task operationDrained)
    {
        await operationDrained.ConfigureAwait(false);
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = PcpFmp4PublishingBoundaryState.Disposed,
                Result = PcpFmp4PublishingResult.Disposed,
            };
        }
    }

    private void ExitOperation()
    {
        lock (_gate)
        {
            _operationActive = false;
            if (_disposeRequested)
            {
                _operationDrained?.TrySetResult();
            }
        }
    }
}
