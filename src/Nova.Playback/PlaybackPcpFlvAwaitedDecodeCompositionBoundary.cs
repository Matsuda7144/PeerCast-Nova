using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

public enum PlaybackPcpFlvAwaitedDecodeGenerationResult
{
    Created,
    InvalidSource,
    Rejected,
    Faulted,
}

public enum PlaybackPcpFlvAwaitedDecodeState
{
    Accepting,
    EncodedInputFinalized,
    DecodersDrained,
    Faulted,
    Disposed,
}

public enum PlaybackPcpFlvAwaitedDecodeResult
{
    None,
    PacketAccepted,
    EncodedInputFinalized,
    DecodedFrameConsumed,
    NeedMoreInput,
    StreamEndOfStream,
    StreamRequiresDrain,
    StreamNotFlushed,
    StreamAlreadyEnded,
    InvalidIdentity,
    SequenceRejected,
    ParserRejected,
    GenerationRejected,
    SubmissionRejected,
    SettlementRejected,
    DecoderNoProgress,
    WorkLimitExceeded,
    Cancelled,
    Faulted,
    Disposed,
}

public enum PlaybackPcpFlvAwaitedDecodeOperationResult
{
    FrameConsumed,
    NeedMoreInput,
    EndOfStream,
    StreamRequiresDrain,
    StreamNotFlushed,
    StreamAlreadyEnded,
}

public enum PlaybackPcpFlvDecodedSettlementResult
{
    Consumed,
    Rejected,
    Cancelled,
    Faulted,
}

public enum PlaybackPcpFlvAwaitedDecodeMediaSelection
{
    AudioAndVideo,
    AudioOnly,
    VideoOnly,
}

public sealed record PlaybackPcpFlvAwaitedDecodeOptions
{
    public const int DefaultMaximumParserOperationsPerCall = 4_096;
    public const int MaximumParserOperationsPerCallHardLimit = 65_536;
    public const int DefaultMaximumDecodeOperationsPerCall = 256;
    public const int MaximumDecodeOperationsPerCallHardLimit = 4_096;
    public const int DefaultMaximumDecodedFramesPerCall = 256;
    public const long DefaultMaximumDecodedManagedBytesPerCall = 64L * 1024 * 1024;

    public int MaximumParserOperationsPerCall { get; init; } =
        DefaultMaximumParserOperationsPerCall;

    public int MaximumDecodeOperationsPerCall { get; init; } =
        DefaultMaximumDecodeOperationsPerCall;

    public int MaximumDecodedFramesPerCall { get; init; } =
        DefaultMaximumDecodedFramesPerCall;

    public long MaximumDecodedManagedBytesPerCall { get; init; } =
        DefaultMaximumDecodedManagedBytesPerCall;

    public PlaybackPcpFlvAwaitedDecodeMediaSelection MediaSelection { get; init; } =
        PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioAndVideo;
}

public sealed record PlaybackPcpFlvAwaitedDecodeSnapshot(
    PlaybackPcpFlvAwaitedDecodeState State,
    PlaybackPcpFlvAwaitedDecodeResult Result,
    long AcceptedPacketCount,
    long AcceptedPayloadByteCount,
    long ParserOperationCount,
    long DecodeOperationCount,
    long SettledFrameCount,
    int ParserGeneration,
    int DecoderGeneration,
    bool HasDecoderGeneration,
    bool HasActiveDecodedCompletion,
    bool HasActiveSubmission,
    bool HasReliefRetry,
    bool VideoFlushAccepted,
    bool AudioFlushAccepted,
    bool VideoEndOfStream,
    bool AudioEndOfStream);

public sealed class PlaybackPcpFlvAwaitedDecodeException : Exception
{
    private const string DefaultMessage =
        "The awaited PCP-to-FLV decode composition boundary failed.";

    public PlaybackPcpFlvAwaitedDecodeException()
        : this(DefaultMessage)
    {
    }

    public PlaybackPcpFlvAwaitedDecodeException(string message)
        : this(message, innerException: null)
    {
    }

    public PlaybackPcpFlvAwaitedDecodeException(
        string message,
        Exception? innerException)
        : base(message, innerException)
    {
    }

    internal PlaybackPcpFlvAwaitedDecodeException(
        PlaybackPcpFlvAwaitedDecodeResult result)
        : base(DefaultMessage)
    {
        Result = result;
    }

    public PlaybackPcpFlvAwaitedDecodeResult Result { get; }
}

public interface IPlaybackPcpFlvDecodedCompletionSettlement : IDisposable
{
    ValueTask<PlaybackPcpFlvDecodedSettlementResult> SettleAsync(
        PlaybackSourceBoundDecodedFrameCompletion completion,
        CancellationToken cancellationToken);
}

public interface IPlaybackPcpFlvAwaitedDecodeGenerationFactory : IDisposable
{
    PlaybackPcpFlvAwaitedDecodeGenerationCreation Create(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration);
}

public sealed record PlaybackPcpFlvAwaitedDecodeGenerationCreation(
    PlaybackPcpFlvAwaitedDecodeGenerationResult Result,
    PlaybackPcpFlvAwaitedDecodeGeneration? Generation);

public sealed class PlaybackPcpFlvAwaitedDecodeGeneration : IDisposable, IAsyncDisposable
{
    private PlaybackSourceBoundOneFrameDecodeBoundary? _decodeBoundary;
    private IPlaybackPcpFlvDecodedCompletionSettlement? _settlement;

    public PlaybackPcpFlvAwaitedDecodeGeneration(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackSourceBoundOneFrameDecodeBoundary decodeBoundary,
        IPlaybackPcpFlvDecodedCompletionSettlement settlement,
        int decoderGeneration,
        int discontinuityGeneration)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(decodeBoundary);
        ArgumentNullException.ThrowIfNull(settlement);

        var snapshot = decodeBoundary.Snapshot;
        if (sourceSession.IsDisposed ||
            decodeBoundary.IsDisposed ||
            !decodeBoundary.ReferencesSourceSession(sourceSession) ||
            decoderGeneration < 0 ||
            discontinuityGeneration < 0 ||
            snapshot.DecoderGeneration != decoderGeneration ||
            snapshot.DiscontinuityGeneration != discontinuityGeneration)
        {
            decodeBoundary.Dispose();
            settlement.Dispose();
            throw new ArgumentException(
                "The decode generation must reference the same live source and exact generation identifiers.",
                nameof(decodeBoundary));
        }

        SourceSession = sourceSession;
        DecoderGeneration = decoderGeneration;
        DiscontinuityGeneration = discontinuityGeneration;
        _decodeBoundary = decodeBoundary;
        _settlement = settlement;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public int DecoderGeneration { get; }

    public int DiscontinuityGeneration { get; }

    public bool IsDisposed =>
        Volatile.Read(ref _decodeBoundary) is null ||
        Volatile.Read(ref _settlement) is null;

    internal PlaybackSourceBoundOneFrameDecodeBoundary DecodeBoundary
    {
        get
        {
            var value = Volatile.Read(ref _decodeBoundary);
            ObjectDisposedException.ThrowIf(value is null, this);
            return value;
        }
    }

    internal IPlaybackPcpFlvDecodedCompletionSettlement Settlement
    {
        get
        {
            var value = Volatile.Read(ref _settlement);
            ObjectDisposedException.ThrowIf(value is null, this);
            return value;
        }
    }

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(SourceSession, sourceSession);

    public void Dispose()
    {
        var settlement = Interlocked.Exchange(ref _settlement, null);
        var decodeBoundary = Interlocked.Exchange(ref _decodeBoundary, null);
        try
        {
            settlement?.Dispose();
        }
        finally
        {
            decodeBoundary?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Finite awaited composition from callback-scoped PCP payload through the bounded
/// FLV parser and one source-bound decode generation. Retained packet or flush
/// relief is settled before the callback returns. Ordinary receive and flush drain
/// remain explicit caller-driven one-frame operations. No decoded queue is owned.
/// </summary>
public sealed class PlaybackPcpFlvAwaitedDecodeCompositionBoundary :
    IPcpChannelPacketConsumer,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _channelSessionId;
    private readonly PlaybackPcpFlvAwaitedDecodeOptions _options;
    private readonly PlaybackOneEventFinalEosFlvParser _parser;
    private readonly IPlaybackPcpFlvAwaitedDecodeGenerationFactory _generationFactory;
    private readonly Func<bool>? _stopAfterSettlement;

    private PlaybackPcpFlvAwaitedDecodeGeneration? _generation;
    private PlaybackPcpFlvAwaitedDecodeState _state =
        PlaybackPcpFlvAwaitedDecodeState.Accepting;
    private PlaybackPcpFlvAwaitedDecodeResult _result =
        PlaybackPcpFlvAwaitedDecodeResult.None;
    private long _nextPacketSequence;
    private long _acceptedPacketCount;
    private long _acceptedPayloadByteCount;
    private long _parserOperationCount;
    private long _decodeOperationCount;
    private long _settledFrameCount;
    private int _parserGeneration;
    private int _nextDecoderGeneration;
    private int _decoderGeneration = -1;
    private bool _hasDecoderGeneration;
    private int _isDisposed;

    public PlaybackPcpFlvAwaitedDecodeCompositionBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        IPlaybackPcpFlvAwaitedDecodeGenerationFactory generationFactory,
        long initialPacketSequence = 0,
        PlaybackPcpFlvAwaitedDecodeOptions? options = null,
        PlaybackBoundedFlvParserOptions? parserOptions = null)
        : this(
            channelId,
            channelSessionId,
            generationFactory,
            initialPacketSequence,
            options,
            parserOptions,
            stopAfterSettlement: null)
    {
    }

    internal PlaybackPcpFlvAwaitedDecodeCompositionBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        IPlaybackPcpFlvAwaitedDecodeGenerationFactory generationFactory,
        long initialPacketSequence,
        PlaybackPcpFlvAwaitedDecodeOptions? options,
        PlaybackBoundedFlvParserOptions? parserOptions,
        Func<bool>? stopAfterSettlement)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(channelSessionId);
        ArgumentNullException.ThrowIfNull(generationFactory);

        if (initialPacketSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPacketSequence),
                initialPacketSequence,
                "The initial PCP packet sequence cannot be negative.");
        }

        _options = options is null
            ? new PlaybackPcpFlvAwaitedDecodeOptions()
            : options with { };
        ValidateOptions(_options);

        _channelId = channelId;
        _channelSessionId = channelSessionId;
        _generationFactory = generationFactory;
        _stopAfterSettlement = stopAfterSettlement;
        _nextPacketSequence = initialPacketSequence;
        _parser = new PlaybackOneEventFinalEosFlvParser(parserOptions);
    }

    public PlaybackPcpFlvAwaitedDecodeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                PlaybackSourceBoundOneFrameDecodeSnapshot? decode =
                    _generation is { IsDisposed: false }
                        ? _generation.DecodeBoundary.Snapshot
                        : null;
                return new(
                    _state,
                    _result,
                    _acceptedPacketCount,
                    _acceptedPayloadByteCount,
                    _parserOperationCount,
                    _decodeOperationCount,
                    _settledFrameCount,
                    _parserGeneration,
                    _decoderGeneration,
                    _hasDecoderGeneration,
                    decode?.HasActiveDecodedCompletion == true,
                    decode?.HasActiveSubmission == true,
                    decode?.HasReliefRetry == true,
                    decode?.VideoFlushAccepted == true,
                    decode?.AudioFlushAccepted == true,
                    decode?.VideoEndOfStream == true,
                    decode?.AudioEndOfStream == true);
            }
        }
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return ConsumeCoreAsync(packet, cancellationToken);
    }

    public ValueTask CompleteInputAsync(
        CancellationToken cancellationToken = default) =>
        CompleteInputCoreAsync(cancellationToken);

    public ValueTask<PlaybackPcpFlvAwaitedDecodeOperationResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        DriveDecodedCoreAsync(
            mediaKind,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            cancellationToken);

    public ValueTask<PlaybackPcpFlvAwaitedDecodeOperationResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        DriveDecodedCoreAsync(
            mediaKind,
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain,
            cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _stopSource.Cancel();
        _operationGate.Wait();
        try
        {
            DisposeOwnedSafely();
            lock (_sync)
            {
                _state = PlaybackPcpFlvAwaitedDecodeState.Disposed;
                _result = PlaybackPcpFlvAwaitedDecodeResult.Disposed;
                _hasDecoderGeneration = false;
                _decoderGeneration = -1;
            }
        }
        finally
        {
            _operationGate.Dispose();
            _stopSource.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask ConsumeCoreAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            EnsureAcceptingInput();
            linkedSource.Token.ThrowIfCancellationRequested();
            ValidatePacket(packet);

            var parserOperations = 0;
            var decodeOperations = new DecodeOperationCounter();
            var decodeBudget = CreateDecodeBudget();
            var offset = 0;
            var applyDiscontinuity = packet.Discontinuity is not null;
            if (applyDiscontinuity)
            {
                DisposeGenerationForReplacement();
            }

            while (true)
            {
                linkedSource.Token.ThrowIfCancellationRequested();
                var remaining = packet.Payload.Slice(offset);
                var operation = ParseOne(
                    remaining,
                    isFinal: false,
                    applyDiscontinuity,
                    ref parserOperations,
                    linkedSource.Token);
                applyDiscontinuity = false;
                ValidateParserAccounting(operation, remaining.Length);
                offset = checked(offset + operation.ConsumedByteCount);

                await ProcessParserOperationAsync(
                    operation,
                    allowCompletionFreeEndOfStream: false,
                    decodeBudget,
                    decodeOperations,
                    linkedSource.Token).ConfigureAwait(false);

                if (operation.Result == PlaybackBoundedFlvParserResult.NeedMoreInput)
                {
                    if (offset != packet.Payload.Length)
                    {
                        throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
                    }

                    break;
                }

                if (offset == packet.Payload.Length)
                {
                    break;
                }
            }

            EnsureNoActiveDecodeWork();
            CommitPacket(packet.Payload.Length);
        }
        catch (PlaybackPcpFlvAwaitedDecodeStopException)
        {
            DisposeGenerationForReplacement();
            CommitPacket(packet.Payload.Length);
        }
        catch (OperationCanceledException)
            when (linkedSource.IsCancellationRequested)
        {
            ThrowIfDisposed();
            _ = Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackPcpFlvAwaitedDecodeException)
        {
            throw;
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Faulted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask CompleteInputCoreAsync(
        CancellationToken cancellationToken)
    {
        await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            if (IsEncodedInputFinalized())
            {
                return;
            }

            EnsureAcceptingInput();
            linkedSource.Token.ThrowIfCancellationRequested();
            _ = EnsureGenerationAvailable();

            var parserOperations = 0;
            var decodeOperations = new DecodeOperationCounter();
            var decodeBudget = CreateDecodeBudget();
            var signalFinal = true;
            while (true)
            {
                linkedSource.Token.ThrowIfCancellationRequested();
                var operation = ParseOne(
                    ReadOnlyMemory<byte>.Empty,
                    signalFinal,
                    discontinuity: false,
                    ref parserOperations,
                    linkedSource.Token);
                signalFinal = false;
                ValidateParserAccounting(operation, remainingByteCount: 0);

                if (operation.Result == PlaybackBoundedFlvParserResult.EndOfStream &&
                    operation.Completion is null)
                {
                    EnsureFinalStreamsSubmitted();
                    CommitEncodedInputFinalized();
                    return;
                }

                if (operation.Result == PlaybackBoundedFlvParserResult.NeedMoreInput)
                {
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
                }

                await ProcessParserOperationAsync(
                    operation,
                    allowCompletionFreeEndOfStream: true,
                    decodeBudget,
                    decodeOperations,
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (PlaybackPcpFlvAwaitedDecodeStopException)
        {
            DisposeGenerationForReplacement();
            lock (_sync)
            {
                _state = PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized;
                _result = PlaybackPcpFlvAwaitedDecodeResult.DecodedFrameConsumed;
            }
        }
        catch (OperationCanceledException)
            when (linkedSource.IsCancellationRequested)
        {
            ThrowIfDisposed();
            _ = Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackPcpFlvAwaitedDecodeException)
        {
            throw;
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Faulted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<PlaybackPcpFlvAwaitedDecodeOperationResult> DriveDecodedCoreAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        CancellationToken cancellationToken)
    {
        await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            EnsureOperational();
            linkedSource.Token.ThrowIfCancellationRequested();
            var generation = EnsureGenerationAvailable();
            var streamId = StreamId(generation.SourceSession, mediaKind);
            var decodeOperations = new DecodeOperationCounter();
            var budget = CreateDecodeBudget();

            var operation = ReceiveOne(
                generation,
                mediaKind,
                streamId,
                mode,
                budget,
                decodeOperations,
                linkedSource.Token);
            switch (operation.Result)
            {
                case PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable:
                    if (operation.Completion is null)
                    {
                        throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
                    }

                    await SettleAndReleaseAsync(
                        generation,
                        operation.Completion,
                        mediaKind,
                        streamId,
                        linkedSource.Token).ConfigureAwait(false);
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.DecodedFrameConsumed);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.FrameConsumed;

                case PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput:
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.NeedMoreInput);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.NeedMoreInput;

                case PlaybackSourceBoundOneFrameDecodeResult.EndOfStream:
                    CommitDecoderEndOfStreamIfComplete(generation);
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.StreamEndOfStream);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.EndOfStream;

                case PlaybackSourceBoundOneFrameDecodeResult.StreamRequiresDrain:
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.StreamRequiresDrain);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.StreamRequiresDrain;

                case PlaybackSourceBoundOneFrameDecodeResult.StreamNotFlushed:
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.StreamNotFlushed);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.StreamNotFlushed;

                case PlaybackSourceBoundOneFrameDecodeResult.StreamAlreadyEnded:
                    CommitDecoderEndOfStreamIfComplete(generation);
                    SetResult(PlaybackPcpFlvAwaitedDecodeResult.StreamAlreadyEnded);
                    return PlaybackPcpFlvAwaitedDecodeOperationResult.StreamAlreadyEnded;

                case PlaybackSourceBoundOneFrameDecodeResult.Cancelled:
                    if (linkedSource.Token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(linkedSource.Token);
                    }

                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);

                default:
                    throw Fail(MapDecodeFailure(operation.Result));
            }
        }
        catch (PlaybackPcpFlvAwaitedDecodeStopException)
        {
            DisposeGenerationForReplacement();
            SetResult(PlaybackPcpFlvAwaitedDecodeResult.DecodedFrameConsumed);
            return PlaybackPcpFlvAwaitedDecodeOperationResult.FrameConsumed;
        }
        catch (OperationCanceledException)
            when (linkedSource.IsCancellationRequested)
        {
            ThrowIfDisposed();
            _ = Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackPcpFlvAwaitedDecodeException)
        {
            throw;
        }
        catch (ObjectDisposedException)
            when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Faulted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask EnterOperationAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            await _operationGate.WaitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            ThrowIfDisposed();
            PlaybackPcpFlvAwaitedDecodeResult result;
            lock (_sync)
            {
                result = _result;
            }

            throw new PlaybackPcpFlvAwaitedDecodeException(result);
        }
    }

    private PlaybackBoundedFlvParserOperation ParseOne(
        ReadOnlyMemory<byte> input,
        bool isFinal,
        bool discontinuity,
        ref int parserOperations,
        CancellationToken cancellationToken)
    {
        parserOperations = checked(parserOperations + 1);
        if (parserOperations > _options.MaximumParserOperationsPerCall)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.WorkLimitExceeded);
        }

        var operation = _parser.Parse(
            input,
            isFinal,
            discontinuity,
            cancellationToken);
        lock (_sync)
        {
            _parserOperationCount = checked(_parserOperationCount + 1);
            _parserGeneration = operation.Generation;
        }

        return operation;
    }

    private async ValueTask ProcessParserOperationAsync(
        PlaybackBoundedFlvParserOperation operation,
        bool allowCompletionFreeEndOfStream,
        PlaybackSourceBoundOneFrameDecodeBudget decodeBudget,
        DecodeOperationCounter decodeOperations,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(operation.Result))
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
        }

        switch (operation.Result)
        {
            case PlaybackBoundedFlvParserResult.StreamDescriptionAvailable:
                if (operation.Completion is not null)
                {
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
                }

                CreateGeneration();
                return;

            case PlaybackBoundedFlvParserResult.PacketsAvailable:
                if (operation.Completion is null)
                {
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
                }

                await SubmitCompletionAsync(
                    operation.Completion,
                    decodeBudget,
                    decodeOperations,
                    cancellationToken).ConfigureAwait(false);
                return;

            case PlaybackBoundedFlvParserResult.EndOfStream:
                if (operation.Completion is not null)
                {
                    await SubmitCompletionAsync(
                        operation.Completion,
                        decodeBudget,
                        decodeOperations,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (allowCompletionFreeEndOfStream)
                {
                    return;
                }

                throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);

            case PlaybackBoundedFlvParserResult.NeedMoreInput:
                if (operation.Completion is not null)
                {
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
                }

                return;

            case PlaybackBoundedFlvParserResult.Cancelled:
                throw new OperationCanceledException(cancellationToken);

            default:
                throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
        }
    }

    private async ValueTask SubmitCompletionAsync(
        PlaybackBoundedDemuxReadCompletion completion,
        PlaybackSourceBoundOneFrameDecodeBudget decodeBudget,
        DecodeOperationCounter decodeOperations,
        CancellationToken cancellationToken)
    {
        var generation = EnsureGenerationAvailable();
        var source = _parser.Session;
        if (source is null ||
            source.IsDisposed ||
            !generation.ReferencesSourceSession(source) ||
            completion.IsDisposed ||
            completion.Events.Count != 1)
        {
            completion.Dispose();
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        var sourceEvent = completion.Events[0];
        if (!IsSelectedMedia(sourceEvent.MediaKind))
        {
            completion.Dispose();
            return;
        }

        var result = generation.DecodeBoundary.BeginSubmission(
            completion,
            cancellationToken);
        if (!Enum.IsDefined(result))
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SubmissionRejected);
        }

        if (result is
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted or
            PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted or
            PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream)
        {
            EnsureNoActiveDecodeWork();
            return;
        }

        if (result == PlaybackFiniteOneEventPacketSubmissionResult.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result != PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SubmissionRejected);
        }

        await RelievePendingSubmissionAsync(
            generation,
            sourceEvent.MediaKind,
            sourceEvent.StreamId,
            decodeBudget,
            decodeOperations,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RelievePendingSubmissionAsync(
        PlaybackPcpFlvAwaitedDecodeGeneration generation,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeBudget decodeBudget,
        DecodeOperationCounter decodeOperations,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receive = ReceiveOne(
                generation,
                mediaKind,
                streamId,
                PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief,
                decodeBudget,
                decodeOperations,
                cancellationToken);

            if (receive.Result == PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable)
            {
                if (receive.Completion is null)
                {
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
                }

                await SettleAndReleaseAsync(
                    generation,
                    receive.Completion,
                    mediaKind,
                    streamId,
                    cancellationToken).ConfigureAwait(false);

                var retry = ReceiveOne(
                    generation,
                    mediaKind,
                    streamId,
                    PlaybackSourceBoundOneFrameDecodeMode.PendingSubmissionRelief,
                    decodeBudget,
                    decodeOperations,
                    cancellationToken);
                switch (retry.Result)
                {
                    case PlaybackSourceBoundOneFrameDecodeResult.SubmissionAccepted:
                    case PlaybackSourceBoundOneFrameDecodeResult.EndOfStream:
                        EnsureNoActiveDecodeWork();
                        return;
                    case PlaybackSourceBoundOneFrameDecodeResult.SubmissionStillPending:
                        continue;
                    case PlaybackSourceBoundOneFrameDecodeResult.Cancelled:
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);
                    default:
                        throw Fail(MapDecodeFailure(retry.Result));
                }
            }

            if (receive.Result == PlaybackSourceBoundOneFrameDecodeResult.EndOfStream)
            {
                EnsureNoActiveDecodeWork();
                return;
            }

            if (receive.Result == PlaybackSourceBoundOneFrameDecodeResult.Cancelled)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);
            }

            throw Fail(MapDecodeFailure(receive.Result));
        }
    }

    private PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
        PlaybackPcpFlvAwaitedDecodeGeneration generation,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        PlaybackSourceBoundOneFrameDecodeBudget budget,
        DecodeOperationCounter decodeOperations,
        CancellationToken cancellationToken)
    {
        decodeOperations.Count = checked(decodeOperations.Count + 1);
        if (decodeOperations.Count > _options.MaximumDecodeOperationsPerCall)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.WorkLimitExceeded);
        }

        lock (_sync)
        {
            _decodeOperationCount = checked(_decodeOperationCount + 1);
        }

        return generation.DecodeBoundary.ReceiveOne(
            generation.SourceSession,
            generation.DecoderGeneration,
            mediaKind,
            streamId,
            mode,
            cancellationToken,
            budget);
    }

    private async ValueTask SettleAndReleaseAsync(
        PlaybackPcpFlvAwaitedDecodeGeneration generation,
        PlaybackSourceBoundDecodedFrameCompletion completion,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        CancellationToken cancellationToken)
    {
        if (!IsValidCompletion(generation, completion, mediaKind, streamId))
        {
            completion.Dispose();
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
        }

        PlaybackPcpFlvDecodedSettlementResult settlement;
        try
        {
            settlement = await generation.Settlement.SettleAsync(
                completion,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.Dispose();
            throw;
        }
        catch
        {
            completion.Dispose();
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
        }

        if (!Enum.IsDefined(settlement))
        {
            completion.Dispose();
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
        }

        switch (settlement)
        {
            case PlaybackPcpFlvDecodedSettlementResult.Consumed:
                if (!IsValidCompletion(generation, completion, mediaKind, streamId))
                {
                    completion.Dispose();
                    throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
                }

                completion.Dispose();
                lock (_sync)
                {
                    _settledFrameCount = checked(_settledFrameCount + 1);
                }

                if (_stopAfterSettlement?.Invoke() == true)
                {
                    throw new PlaybackPcpFlvAwaitedDecodeStopException();
                }

                return;

            case PlaybackPcpFlvDecodedSettlementResult.Cancelled:
                completion.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw Fail(PlaybackPcpFlvAwaitedDecodeResult.Cancelled);

            case PlaybackPcpFlvDecodedSettlementResult.Rejected:
            case PlaybackPcpFlvDecodedSettlementResult.Faulted:
            default:
                completion.Dispose();
                throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected);
        }
    }

    private static bool IsValidCompletion(
        PlaybackPcpFlvAwaitedDecodeGeneration generation,
        PlaybackSourceBoundDecodedFrameCompletion completion,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId)
    {
        if (completion.IsDisposed ||
            !completion.ReferencesSourceSession(generation.SourceSession) ||
            completion.DecoderGeneration != generation.DecoderGeneration ||
            completion.DiscontinuityGeneration != generation.DiscontinuityGeneration ||
            completion.MediaKind != mediaKind ||
            completion.StreamId != streamId ||
            completion.Sequence < 0 ||
            completion.ManagedByteCount < 1)
        {
            return false;
        }

        try
        {
            var frame = completion.Frame;
            return !frame.IsDisposed &&
                frame.Metadata.MediaKind == MapFrameMediaKind(mediaKind);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void CreateGeneration()
    {
        if (_generation is not null || _nextDecoderGeneration == int.MaxValue)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        var source = _parser.Session;
        if (source is null || source.IsDisposed)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        var decoderGeneration = _nextDecoderGeneration;
        var discontinuityGeneration = _parserGeneration;
        PlaybackPcpFlvAwaitedDecodeGenerationCreation creation;
        try
        {
            creation = _generationFactory.Create(
                source,
                decoderGeneration,
                discontinuityGeneration);
        }
        catch
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        if (!Enum.IsDefined(creation.Result) ||
            creation.Result != PlaybackPcpFlvAwaitedDecodeGenerationResult.Created ||
            creation.Generation is null ||
            creation.Generation.IsDisposed ||
            !creation.Generation.ReferencesSourceSession(source) ||
            creation.Generation.DecoderGeneration != decoderGeneration ||
            creation.Generation.DiscontinuityGeneration != discontinuityGeneration)
        {
            creation.Generation?.Dispose();
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        _generation = creation.Generation;
        _nextDecoderGeneration = checked(decoderGeneration + 1);
        lock (_sync)
        {
            _hasDecoderGeneration = true;
            _decoderGeneration = decoderGeneration;
        }
    }

    private void ValidatePacket(PcpChannelPacket packet)
    {
        if (packet.ChannelId != _channelId ||
            packet.SessionId != _channelSessionId)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.InvalidIdentity);
        }

        if (packet.Sequence != _nextPacketSequence || packet.Sequence == long.MaxValue)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SequenceRejected);
        }
    }

    private bool IsSelectedMedia(PlaybackBoundedDemuxMediaKind mediaKind) =>
        _options.MediaSelection switch
        {
            PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioAndVideo => true,
            PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioOnly =>
                mediaKind == PlaybackBoundedDemuxMediaKind.Audio,
            PlaybackPcpFlvAwaitedDecodeMediaSelection.VideoOnly =>
                mediaKind == PlaybackBoundedDemuxMediaKind.Video,
            _ => false,
        };

    private void ValidateParserAccounting(
        PlaybackBoundedFlvParserOperation operation,
        int remainingByteCount)
    {
        if (operation.ConsumedByteCount < 0 ||
            operation.ConsumedByteCount > remainingByteCount ||
            operation.StreamBytePosition < 0 ||
            operation.CarryByteCount < 0 ||
            operation.SkippedTagCount < 0 ||
            operation.Generation < 0)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
        }

        var madeProgress = operation.ConsumedByteCount > 0 ||
            operation.Completion is not null ||
            operation.Result == PlaybackBoundedFlvParserResult.StreamDescriptionAvailable ||
            operation.Result == PlaybackBoundedFlvParserResult.EndOfStream;
        if (remainingByteCount > 0 && !madeProgress)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
        }
    }

    private PlaybackPcpFlvAwaitedDecodeGeneration EnsureGenerationAvailable()
    {
        var generation = _generation;
        if (generation is null || generation.IsDisposed)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.GenerationRejected);
        }

        return generation;
    }

    private void EnsureNoActiveDecodeWork()
    {
        if (_generation is not { IsDisposed: false } generation)
        {
            return;
        }

        var snapshot = generation.DecodeBoundary.Snapshot;
        if (snapshot.HasActiveDecodedCompletion ||
            snapshot.HasActiveSubmission ||
            snapshot.HasReliefRetry)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SubmissionRejected);
        }
    }

    private void EnsureFinalStreamsSubmitted()
    {
        var generation = EnsureGenerationAvailable();
        var snapshot = generation.DecodeBoundary.Snapshot;
        var videoSubmitted = snapshot.VideoFlushAccepted || snapshot.VideoEndOfStream;
        var audioSubmitted = snapshot.AudioFlushAccepted || snapshot.AudioEndOfStream;
        if (!videoSubmitted ||
            !audioSubmitted ||
            snapshot.HasActiveDecodedCompletion ||
            snapshot.HasActiveSubmission ||
            snapshot.HasReliefRetry)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.SubmissionRejected);
        }
    }

    private void EnsureAcceptingInput()
    {
        PlaybackPcpFlvAwaitedDecodeState state;
        PlaybackPcpFlvAwaitedDecodeResult result;
        lock (_sync)
        {
            state = _state;
            result = _result;
        }

        if (state == PlaybackPcpFlvAwaitedDecodeState.Accepting)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(
            state == PlaybackPcpFlvAwaitedDecodeState.Disposed,
            this);

        if (state is
            PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized or
            PlaybackPcpFlvAwaitedDecodeState.DecodersDrained)
        {
            throw Fail(PlaybackPcpFlvAwaitedDecodeResult.ParserRejected);
        }

        throw new PlaybackPcpFlvAwaitedDecodeException(result);
    }

    private void EnsureOperational()
    {
        PlaybackPcpFlvAwaitedDecodeState state;
        PlaybackPcpFlvAwaitedDecodeResult result;
        lock (_sync)
        {
            state = _state;
            result = _result;
        }

        ObjectDisposedException.ThrowIf(
            state == PlaybackPcpFlvAwaitedDecodeState.Disposed,
            this);
        if (state == PlaybackPcpFlvAwaitedDecodeState.Faulted)
        {
            throw new PlaybackPcpFlvAwaitedDecodeException(result);
        }
    }

    private bool IsEncodedInputFinalized()
    {
        lock (_sync)
        {
            return _state is
                PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized or
                PlaybackPcpFlvAwaitedDecodeState.DecodersDrained;
        }
    }

    private void CommitPacket(int payloadByteCount)
    {
        _nextPacketSequence = checked(_nextPacketSequence + 1);
        lock (_sync)
        {
            _acceptedPacketCount = checked(_acceptedPacketCount + 1);
            _acceptedPayloadByteCount = checked(
                _acceptedPayloadByteCount + payloadByteCount);
            _state = PlaybackPcpFlvAwaitedDecodeState.Accepting;
            _result = PlaybackPcpFlvAwaitedDecodeResult.PacketAccepted;
        }
    }

    private void CommitEncodedInputFinalized()
    {
        var generation = EnsureGenerationAvailable();
        var snapshot = generation.DecodeBoundary.Snapshot;
        lock (_sync)
        {
            _state = snapshot.VideoEndOfStream && snapshot.AudioEndOfStream
                ? PlaybackPcpFlvAwaitedDecodeState.DecodersDrained
                : PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized;
            _result = PlaybackPcpFlvAwaitedDecodeResult.EncodedInputFinalized;
        }
    }

    private void CommitDecoderEndOfStreamIfComplete(
        PlaybackPcpFlvAwaitedDecodeGeneration generation)
    {
        var snapshot = generation.DecodeBoundary.Snapshot;
        if (!snapshot.VideoEndOfStream || !snapshot.AudioEndOfStream)
        {
            return;
        }

        lock (_sync)
        {
            if (_state == PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized)
            {
                _state = PlaybackPcpFlvAwaitedDecodeState.DecodersDrained;
            }
        }
    }

    private void SetResult(PlaybackPcpFlvAwaitedDecodeResult result)
    {
        lock (_sync)
        {
            _result = result;
        }
    }

    private void DisposeGenerationForReplacement()
    {
        var generation = _generation;
        _generation = null;
        lock (_sync)
        {
            _hasDecoderGeneration = false;
            _decoderGeneration = -1;
        }

        generation?.Dispose();
    }

    private PlaybackPcpFlvAwaitedDecodeException Fail(
        PlaybackPcpFlvAwaitedDecodeResult result)
    {
        lock (_sync)
        {
            if (_state != PlaybackPcpFlvAwaitedDecodeState.Disposed)
            {
                _state = PlaybackPcpFlvAwaitedDecodeState.Faulted;
                _result = result;
                _hasDecoderGeneration = false;
                _decoderGeneration = -1;
            }
        }

        _stopSource.Cancel();
        DisposeOwnedSafely();
        return new PlaybackPcpFlvAwaitedDecodeException(result);
    }

    private void DisposeOwnedSafely()
    {
        var generation = _generation;
        _generation = null;
        try
        {
            generation?.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal composition result.
        }

        try
        {
            _parser.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal composition result.
        }

        try
        {
            _generationFactory.Dispose();
        }
        catch
        {
            // Preserve the fixed terminal composition result.
        }
    }

    private PlaybackSourceBoundOneFrameDecodeBudget CreateDecodeBudget() =>
        new(
            _options.MaximumDecodedFramesPerCall,
            _options.MaximumDecodedManagedBytesPerCall);

    private static int StreamId(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => source.Video.StreamId,
            PlaybackBoundedDemuxMediaKind.Audio => source.Audio.StreamId,
            _ => throw new InvalidOperationException("The media kind is undefined."),
        };

    private static PlaybackDecoderFfmpegFrameMediaKind MapFrameMediaKind(
        PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                PlaybackDecoderFfmpegFrameMediaKind.Video,
            PlaybackBoundedDemuxMediaKind.Audio =>
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
            _ => throw new InvalidOperationException("The media kind is undefined."),
        };

    private static PlaybackPcpFlvAwaitedDecodeResult MapDecodeFailure(
        PlaybackSourceBoundOneFrameDecodeResult result) => result switch
        {
            PlaybackSourceBoundOneFrameDecodeResult.DecoderNoProgress =>
                PlaybackPcpFlvAwaitedDecodeResult.DecoderNoProgress,
            PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded =>
                PlaybackPcpFlvAwaitedDecodeResult.WorkLimitExceeded,
            PlaybackSourceBoundOneFrameDecodeResult.Cancelled =>
                PlaybackPcpFlvAwaitedDecodeResult.Cancelled,
            PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable =>
                PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected,
            _ => PlaybackPcpFlvAwaitedDecodeResult.SubmissionRejected,
        };

    private sealed class DecodeOperationCounter
    {
        public int Count { get; set; }
    }

    private sealed class PlaybackPcpFlvAwaitedDecodeStopException : Exception;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _isDisposed) != 0,
            this);
    }

    private static void ValidateOptions(PlaybackPcpFlvAwaitedDecodeOptions options)
    {
        if (options.MaximumParserOperationsPerCall < 1 ||
            options.MaximumParserOperationsPerCall >
                PlaybackPcpFlvAwaitedDecodeOptions.MaximumParserOperationsPerCallHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumParserOperationsPerCall)} is outside the supported range.");
        }

        if (options.MaximumDecodeOperationsPerCall < 1 ||
            options.MaximumDecodeOperationsPerCall >
                PlaybackPcpFlvAwaitedDecodeOptions.MaximumDecodeOperationsPerCallHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumDecodeOperationsPerCall)} is outside the supported range.");
        }

        if (options.MaximumDecodedFramesPerCall < 1 ||
            options.MaximumDecodedFramesPerCall >
                PlaybackSourceBoundOneFrameDecodeBudget.MaximumFrameCountHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumDecodedFramesPerCall)} is outside the supported range.");
        }

        if (options.MaximumDecodedManagedBytesPerCall < 1 ||
            options.MaximumDecodedManagedBytesPerCall >
                PlaybackSourceBoundOneFrameDecodeBudget.MaximumManagedBytesHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumDecodedManagedBytesPerCall)} is outside the supported range.");
        }

        if (!Enum.IsDefined(options.MediaSelection))
        {
            throw new InvalidOperationException(
                $"{nameof(options.MediaSelection)} is not defined.");
        }
    }
}
