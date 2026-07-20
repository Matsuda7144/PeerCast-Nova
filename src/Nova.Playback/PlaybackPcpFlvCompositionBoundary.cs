using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

public enum PlaybackPcpFlvCompositionGenerationResult
{
    Created,
    InvalidSource,
    Rejected,
    Faulted,
}

public enum PlaybackPcpFlvCompositionState
{
    Accepting,
    Finalized,
    Faulted,
    Disposed,
}

public enum PlaybackPcpFlvCompositionResult
{
    None,
    PacketAccepted,
    Finalized,
    InvalidIdentity,
    SequenceRejected,
    ParserRejected,
    GenerationRejected,
    SubmissionRejected,
    DecoderNoProgress,
    WorkLimitExceeded,
    Cancelled,
    Faulted,
    Disposed,
}

public sealed record PlaybackPcpFlvCompositionOptions
{
    public const int DefaultMaximumParserOperationsPerCall = 4_096;
    public const int MaximumParserOperationsPerCallHardLimit = 65_536;
    public const int DefaultMaximumDecoderAdvancesPerCall = 256;
    public const int MaximumDecoderAdvancesPerCallHardLimit = 4_096;

    public int MaximumParserOperationsPerCall { get; init; } =
        DefaultMaximumParserOperationsPerCall;

    public int MaximumDecoderAdvancesPerCall { get; init; } =
        DefaultMaximumDecoderAdvancesPerCall;
}

public sealed record PlaybackPcpFlvCompositionSnapshot(
    PlaybackPcpFlvCompositionState State,
    PlaybackPcpFlvCompositionResult Result,
    long AcceptedPacketCount,
    long AcceptedPayloadByteCount,
    long ParserOperationCount,
    long DecoderAdvanceCount,
    int ParserGeneration,
    bool HasDecoderGeneration);

public sealed class PlaybackPcpFlvCompositionException : Exception
{
    private const string DefaultMessage =
        "The PCP-to-FLV composition boundary failed.";

    public PlaybackPcpFlvCompositionException()
        : this(DefaultMessage)
    {
    }

    public PlaybackPcpFlvCompositionException(string message)
        : this(message, innerException: null)
    {
    }

    public PlaybackPcpFlvCompositionException(
        string message,
        Exception? innerException)
        : base(message, innerException)
    {
    }

    internal PlaybackPcpFlvCompositionException(
        PlaybackPcpFlvCompositionResult result)
        : base(DefaultMessage)
    {
        Result = result;
    }

    public PlaybackPcpFlvCompositionResult Result { get; }
}

public interface IPlaybackPcpFlvCompositionGenerationFactory : IDisposable
{
    PlaybackPcpFlvCompositionGenerationCreation Create(
        PlaybackBoundedDemuxSession sourceSession);
}

public sealed record PlaybackPcpFlvCompositionGenerationCreation(
    PlaybackPcpFlvCompositionGenerationResult Result,
    PlaybackPcpFlvCompositionGeneration? Generation);

public sealed class PlaybackPcpFlvCompositionGeneration : IDisposable, IAsyncDisposable
{
    private PlaybackFiniteOneEventPacketSubmissionStateMachine? _submission;
    private PlaybackBoundedFlvNativeDecoderSession? _nativeSession;

    public PlaybackPcpFlvCompositionGeneration(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackFiniteOneEventPacketSubmissionStateMachine submission)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(nativeSession);
        ArgumentNullException.ThrowIfNull(submission);

        if (sourceSession.IsDisposed ||
            nativeSession.IsDisposed ||
            submission.IsDisposed ||
            !nativeSession.ReferencesSourceSession(sourceSession))
        {
            throw new ArgumentException(
                "The native decoder session must reference the same live source session.",
                nameof(nativeSession));
        }

        SourceSession = sourceSession;
        _nativeSession = nativeSession;
        _submission = submission;
    }

    public PlaybackBoundedDemuxSession SourceSession { get; }

    public bool IsDisposed =>
        Volatile.Read(ref _submission) is null ||
        Volatile.Read(ref _nativeSession) is null;

    internal PlaybackFiniteOneEventPacketSubmissionStateMachine Submission
    {
        get
        {
            var value = Volatile.Read(ref _submission);
            ObjectDisposedException.ThrowIf(value is null, this);
            return value;
        }
    }

    internal PlaybackBoundedFlvNativeDecoderSession NativeSession
    {
        get
        {
            var value = Volatile.Read(ref _nativeSession);
            ObjectDisposedException.ThrowIf(value is null, this);
            return value;
        }
    }

    public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
        ReferenceEquals(SourceSession, sourceSession);

    public void Dispose()
    {
        var submission = Interlocked.Exchange(ref _submission, null);
        var nativeSession = Interlocked.Exchange(ref _nativeSession, null);
        try
        {
            submission?.Dispose();
        }
        finally
        {
            nativeSession?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class PlaybackPcpFlvCompositionBoundary :
    IPcpChannelPacketConsumer,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _channelSessionId;
    private readonly PlaybackPcpFlvCompositionOptions _options;
    private readonly PlaybackOneEventFinalEosFlvParser _parser;
    private readonly IPlaybackPcpFlvCompositionGenerationFactory _generationFactory;

    private PlaybackPcpFlvCompositionGeneration? _generation;
    private PlaybackPcpFlvCompositionState _state =
        PlaybackPcpFlvCompositionState.Accepting;
    private PlaybackPcpFlvCompositionResult _result =
        PlaybackPcpFlvCompositionResult.None;
    private long _nextPacketSequence;
    private long _acceptedPacketCount;
    private long _acceptedPayloadByteCount;
    private long _parserOperationCount;
    private long _decoderAdvanceCount;
    private int _parserGeneration;
    private bool _hasDecoderGeneration;
    private int _isDisposed;

    public PlaybackPcpFlvCompositionBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        IPlaybackPcpFlvCompositionGenerationFactory generationFactory,
        long initialPacketSequence = 0,
        PlaybackPcpFlvCompositionOptions? options = null,
        PlaybackBoundedFlvParserOptions? parserOptions = null)
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
            ? new PlaybackPcpFlvCompositionOptions()
            : options with { };
        ValidateOptions(_options);

        _channelId = channelId;
        _channelSessionId = channelSessionId;
        _generationFactory = generationFactory;
        _nextPacketSequence = initialPacketSequence;
        _parser = new PlaybackOneEventFinalEosFlvParser(parserOptions);
    }

    public PlaybackPcpFlvCompositionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _acceptedPacketCount,
                    _acceptedPayloadByteCount,
                    _parserOperationCount,
                    _decoderAdvanceCount,
                    _parserGeneration,
                    _hasDecoderGeneration);
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
                _state = PlaybackPcpFlvCompositionState.Disposed;
                _result = PlaybackPcpFlvCompositionResult.Disposed;
                _hasDecoderGeneration = false;
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
        await EnterOperationAsync().ConfigureAwait(false);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            EnsureAcceptingInput();
            linkedSource.Token.ThrowIfCancellationRequested();
            ValidatePacket(packet);

            var parserOperations = 0;
            var decoderAdvances = 0;
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
                    linkedSource.Token,
                    ref parserOperations);
                applyDiscontinuity = false;
                ValidateParserAccounting(operation, remaining.Length);
                offset = checked(offset + operation.ConsumedByteCount);

                ProcessParserOperation(
                    operation,
                    allowCompletionFreeEndOfStream: false,
                    linkedSource.Token,
                    ref decoderAdvances);

                if (operation.Result == PlaybackBoundedFlvParserResult.NeedMoreInput)
                {
                    if (offset != packet.Payload.Length)
                    {
                        throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
                    }

                    break;
                }

                if (offset == packet.Payload.Length)
                {
                    break;
                }
            }

            EnsureNoActiveSubmission();
            CommitPacket(packet.Payload.Length);
        }
        catch (OperationCanceledException)
            when (linkedSource.IsCancellationRequested)
        {
            ThrowIfDisposed();
            _ = Fail(PlaybackPcpFlvCompositionResult.Cancelled);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackPcpFlvCompositionException)
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
            throw Fail(PlaybackPcpFlvCompositionResult.Faulted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask CompleteInputCoreAsync(
        CancellationToken cancellationToken)
    {
        await EnterOperationAsync().ConfigureAwait(false);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        try
        {
            if (IsFinalized())
            {
                return;
            }

            EnsureAcceptingInput();
            linkedSource.Token.ThrowIfCancellationRequested();
            _ = EnsureGenerationAvailable();

            var parserOperations = 0;
            var decoderAdvances = 0;
            var signalFinal = true;
            while (true)
            {
                linkedSource.Token.ThrowIfCancellationRequested();
                var operation = ParseOne(
                    ReadOnlyMemory<byte>.Empty,
                    signalFinal,
                    discontinuity: false,
                    linkedSource.Token,
                    ref parserOperations);
                signalFinal = false;
                ValidateParserAccounting(operation, remainingByteCount: 0);

                if (operation.Result == PlaybackBoundedFlvParserResult.EndOfStream &&
                    operation.Completion is null)
                {
                    EnsureFinalStreamsComplete();
                    CommitFinalized();
                    return;
                }

                if (operation.Result == PlaybackBoundedFlvParserResult.NeedMoreInput)
                {
                    throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
                }

                ProcessParserOperation(
                    operation,
                    allowCompletionFreeEndOfStream: true,
                    linkedSource.Token,
                    ref decoderAdvances);
            }
        }
        catch (OperationCanceledException)
            when (linkedSource.IsCancellationRequested)
        {
            ThrowIfDisposed();
            _ = Fail(PlaybackPcpFlvCompositionResult.Cancelled);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackPcpFlvCompositionException)
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
            throw Fail(PlaybackPcpFlvCompositionResult.Faulted);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask EnterOperationAsync()
    {
        ThrowIfDisposed();
        try
        {
            await _operationGate.WaitAsync(_stopSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ThrowIfDisposed();
            PlaybackPcpFlvCompositionResult result;
            lock (_sync)
            {
                result = _result;
            }

            throw new PlaybackPcpFlvCompositionException(result);
        }
    }

    private PlaybackBoundedFlvParserOperation ParseOne(
        ReadOnlyMemory<byte> input,
        bool isFinal,
        bool discontinuity,
        CancellationToken cancellationToken,
        ref int parserOperations)
    {
        parserOperations = checked(parserOperations + 1);
        if (parserOperations > _options.MaximumParserOperationsPerCall)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.WorkLimitExceeded);
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

    private void ProcessParserOperation(
        PlaybackBoundedFlvParserOperation operation,
        bool allowCompletionFreeEndOfStream,
        CancellationToken cancellationToken,
        ref int decoderAdvances)
    {
        if (!Enum.IsDefined(operation.Result))
        {
            throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
        }

        switch (operation.Result)
        {
            case PlaybackBoundedFlvParserResult.StreamDescriptionAvailable:
                if (operation.Completion is not null)
                {
                    throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
                }

                CreateGeneration();
                return;

            case PlaybackBoundedFlvParserResult.PacketsAvailable:
                if (operation.Completion is null)
                {
                    throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
                }

                SubmitCompletion(
                    operation.Completion,
                    cancellationToken,
                    ref decoderAdvances);
                return;

            case PlaybackBoundedFlvParserResult.EndOfStream:
                if (operation.Completion is not null)
                {
                    SubmitCompletion(
                        operation.Completion,
                        cancellationToken,
                        ref decoderAdvances);
                    return;
                }

                if (allowCompletionFreeEndOfStream)
                {
                    return;
                }

                throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);

            case PlaybackBoundedFlvParserResult.NeedMoreInput:
                if (operation.Completion is not null)
                {
                    throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
                }

                return;

            case PlaybackBoundedFlvParserResult.Cancelled:
                throw new OperationCanceledException(cancellationToken);

            default:
                throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
        }
    }

    private void SubmitCompletion(
        PlaybackBoundedDemuxReadCompletion completion,
        CancellationToken cancellationToken,
        ref int decoderAdvances)
    {
        var generation = EnsureGenerationAvailable();
        var source = _parser.Session;
        if (source is null ||
            source.IsDisposed ||
            !generation.ReferencesSourceSession(source))
        {
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        var result = generation.Submission.Begin(
            generation.NativeSession,
            source,
            completion,
            cancellationToken);
        if (!Enum.IsDefined(result))
        {
            throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
        }

        if (result is
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted or
            PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted or
            PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream)
        {
            EnsureNoActiveSubmission();
            return;
        }

        if (result == PlaybackFiniteOneEventPacketSubmissionResult.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result != PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decoderAdvances = checked(decoderAdvances + 1);
            if (decoderAdvances > _options.MaximumDecoderAdvancesPerCall)
            {
                throw Fail(PlaybackPcpFlvCompositionResult.WorkLimitExceeded);
            }

            lock (_sync)
            {
                _decoderAdvanceCount = checked(_decoderAdvanceCount + 1);
            }

            var advance = generation.Submission.Advance(cancellationToken);
            if (!Enum.IsDefined(advance))
            {
                throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
            }

            switch (advance)
            {
                case PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndStillPending:
                    if (!generation.Submission.HasActiveSubmission)
                    {
                        throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
                    }

                    continue;

                case PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted:
                case PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndEndOfStream:
                case PlaybackFiniteOneEventPacketSubmissionResult.StreamEndOfStream:
                    EnsureNoActiveSubmission();
                    return;

                case PlaybackFiniteOneEventPacketSubmissionResult.NoReceiveProgress:
                    throw Fail(PlaybackPcpFlvCompositionResult.DecoderNoProgress);

                case PlaybackFiniteOneEventPacketSubmissionResult.Cancelled:
                    throw new OperationCanceledException(cancellationToken);

                default:
                    throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
            }
        }
    }

    private void CreateGeneration()
    {
        if (_generation is not null)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        var source = _parser.Session;
        if (source is null || source.IsDisposed)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        PlaybackPcpFlvCompositionGenerationCreation creation;
        try
        {
            creation = _generationFactory.Create(source);
        }
        catch
        {
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        if (!Enum.IsDefined(creation.Result) ||
            creation.Result != PlaybackPcpFlvCompositionGenerationResult.Created ||
            creation.Generation is null ||
            creation.Generation.IsDisposed ||
            !creation.Generation.ReferencesSourceSession(source))
        {
            creation.Generation?.Dispose();
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        _generation = creation.Generation;
        lock (_sync)
        {
            _hasDecoderGeneration = true;
        }
    }

    private void ValidatePacket(PcpChannelPacket packet)
    {
        if (packet.ChannelId != _channelId ||
            packet.SessionId != _channelSessionId)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.InvalidIdentity);
        }

        if (packet.Sequence != _nextPacketSequence || packet.Sequence == long.MaxValue)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.SequenceRejected);
        }
    }

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
            throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
        }

        var madeProgress = operation.ConsumedByteCount > 0 ||
            operation.Completion is not null ||
            operation.Result == PlaybackBoundedFlvParserResult.StreamDescriptionAvailable ||
            operation.Result == PlaybackBoundedFlvParserResult.EndOfStream;
        if (remainingByteCount > 0 && !madeProgress)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
        }
    }

    private PlaybackPcpFlvCompositionGeneration EnsureGenerationAvailable()
    {
        var generation = _generation;
        if (generation is null || generation.IsDisposed)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.GenerationRejected);
        }

        return generation;
    }

    private void EnsureNoActiveSubmission()
    {
        if (_generation?.Submission.HasActiveSubmission == true)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
        }
    }

    private void EnsureFinalStreamsComplete()
    {
        var generation = EnsureGenerationAvailable();
        var submission = generation.Submission;
        var videoComplete = submission.VideoFlushAccepted || submission.VideoEndOfStream;
        var audioComplete = submission.AudioFlushAccepted || submission.AudioEndOfStream;
        if (!videoComplete || !audioComplete || submission.HasActiveSubmission)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.SubmissionRejected);
        }
    }

    private void EnsureAcceptingInput()
    {
        PlaybackPcpFlvCompositionState state;
        PlaybackPcpFlvCompositionResult result;
        lock (_sync)
        {
            state = _state;
            result = _result;
        }

        if (state == PlaybackPcpFlvCompositionState.Accepting)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(
            state == PlaybackPcpFlvCompositionState.Disposed,
            this);

        if (state == PlaybackPcpFlvCompositionState.Finalized)
        {
            throw Fail(PlaybackPcpFlvCompositionResult.ParserRejected);
        }

        throw new PlaybackPcpFlvCompositionException(result);
    }

    private bool IsFinalized()
    {
        lock (_sync)
        {
            return _state == PlaybackPcpFlvCompositionState.Finalized;
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
            _state = PlaybackPcpFlvCompositionState.Accepting;
            _result = PlaybackPcpFlvCompositionResult.PacketAccepted;
        }
    }

    private void CommitFinalized()
    {
        lock (_sync)
        {
            _state = PlaybackPcpFlvCompositionState.Finalized;
            _result = PlaybackPcpFlvCompositionResult.Finalized;
        }
    }

    private void DisposeGenerationForReplacement()
    {
        var generation = _generation;
        _generation = null;
        lock (_sync)
        {
            _hasDecoderGeneration = false;
        }

        generation?.Dispose();
    }

    private PlaybackPcpFlvCompositionException Fail(
        PlaybackPcpFlvCompositionResult result)
    {
        lock (_sync)
        {
            if (_state != PlaybackPcpFlvCompositionState.Disposed)
            {
                _state = PlaybackPcpFlvCompositionState.Faulted;
                _result = result;
                _hasDecoderGeneration = false;
            }
        }

        _stopSource.Cancel();
        DisposeOwnedSafely();
        return new PlaybackPcpFlvCompositionException(result);
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _isDisposed) != 0,
            this);
    }

    private static void ValidateOptions(PlaybackPcpFlvCompositionOptions options)
    {
        if (options.MaximumParserOperationsPerCall < 1 ||
            options.MaximumParserOperationsPerCall >
            PlaybackPcpFlvCompositionOptions.MaximumParserOperationsPerCallHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumParserOperationsPerCall)} is outside the supported range.");
        }

        if (options.MaximumDecoderAdvancesPerCall < 1 ||
            options.MaximumDecoderAdvancesPerCall >
            PlaybackPcpFlvCompositionOptions.MaximumDecoderAdvancesPerCallHardLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(options.MaximumDecoderAdvancesPerCall)} is outside the supported range.");
        }
    }
}
