using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

public enum PlaybackProductionPcpFlvCompositionState
{
    Accepting,
    EncodedInputFinalized,
    DecodersDrained,
    Faulted,
    Disposed,
}

public enum PlaybackProductionPcpFlvCompositionResult
{
    None,
    PacketAccepted,
    EncodedInputFinalized,
    FrameTransferred,
    NeedMoreInput,
    StreamEndOfStream,
    StreamRequiresDrain,
    StreamNotFlushed,
    StreamAlreadyEnded,
    VideoDelivered,
    AudioDelivered,
    VideoDropped,
    Empty,
    RetainedWait,
    RetainedWouldBlock,
    RetainedInitializationRequired,
    RetainedBindingRequired,
    RetainedResizeRequired,
    RetainedAudioRecoveryRequired,
    ClockNotStarted,
    ResetRequired,
    Reset,
    InvalidIdentity,
    SequenceRejected,
    ParserRejected,
    LicenseReviewPending,
    PolicyRejected,
    InvalidConfiguration,
    SourceDisposed,
    OutputUnavailable,
    BindingRejected,
    RuntimeRejected,
    AbiGateRejected,
    OwnershipConflict,
    NativeSessionRejected,
    GenerationRejected,
    SubmissionRejected,
    TransferRejected,
    SchedulerAdmissionRejected,
    DeliveryRejected,
    ResetRejected,
    DecoderNoProgress,
    WorkLimitExceeded,
    Cancelled,
    Faulted,
    Disposed,
}

public sealed record PlaybackProductionPcpFlvCompositionSnapshot(
    PlaybackProductionPcpFlvCompositionState State,
    PlaybackProductionPcpFlvCompositionResult Result,
    long AcceptedPacketCount,
    long AcceptedPayloadByteCount,
    long ParserOperationCount,
    long DecodeOperationCount,
    long TransferredFrameCount,
    long DeliveredFrameCount,
    long DroppedFrameCount,
    int ParserGeneration,
    int DecoderGeneration,
    bool HasDecoderGeneration,
    bool HasActiveOperation,
    bool HasActiveDecodedCompletion,
    bool HasActiveSubmission,
    bool HasReliefRetry,
    bool VideoFlushAccepted,
    bool AudioFlushAccepted,
    bool VideoEndOfStream,
    bool AudioEndOfStream,
    int VideoWidth,
    int VideoHeight,
    int AudioSampleRate,
    int AudioChannelCount)
{
    public PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot DeliveryTrace { get; init; }
}

public sealed class PlaybackProductionPcpFlvCompositionException : Exception
{
    private const string DefaultMessage =
        "The production PCP-to-FLV generation composition boundary failed.";

    public PlaybackProductionPcpFlvCompositionException()
        : this(DefaultMessage)
    {
    }

    public PlaybackProductionPcpFlvCompositionException(string message)
        : this(message, innerException: null)
    {
    }

    public PlaybackProductionPcpFlvCompositionException(
        string message,
        Exception? innerException)
        : base(message, innerException)
    {
    }

    internal PlaybackProductionPcpFlvCompositionException(
        PlaybackProductionPcpFlvCompositionResult result)
        : base(DefaultMessage)
    {
        Result = result;
    }

    public PlaybackProductionPcpFlvCompositionResult Result { get; }
}

internal interface IPlaybackProductionDecoderGenerationCreator : IDisposable, IAsyncDisposable
{
    ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
        string runtimeDirectory,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        int decoderGeneration,
        int discontinuityGeneration,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackProductionDecoderGenerationCreator(
    PlaybackProductionDecoderGenerationFactory factory) :
    IPlaybackProductionDecoderGenerationCreator
{
    public ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
        string runtimeDirectory,
        PlaybackBoundedDemuxSession? sourceSession,
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        int decoderGeneration,
        int discontinuityGeneration,
        CancellationToken cancellationToken) =>
        factory.CreateAsync(
            runtimeDirectory,
            sourceSession,
            output,
            decoderGeneration,
            discontinuityGeneration,
            cancellationToken);

    public void Dispose() => factory.Dispose();

    public ValueTask DisposeAsync() => factory.DisposeAsync();
}

/// <summary>
/// Serialized finite production composition from callback-scoped PCP payload through
/// bounded FLV parsing, source-bound decode, one-shot scheduler transfer, caller-driven
/// one-item delivery, explicit reset, and stop-old-before-parser-reset replacement.
/// The Windows output boundary is borrowed and no resident pump or second queue is owned.
/// </summary>
public sealed class PlaybackProductionPcpFlvCompositionBoundary :
    IPcpChannelPacketConsumer,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _channelSessionId;
    private readonly string _runtimeDirectory;
    private readonly PlaybackWindowsOutputBufferProcessingBoundary _output;
    private readonly PlaybackPcpFlvAwaitedDecodeOptions _options;
    private readonly PlaybackOneEventFinalEosFlvParser _parser;
    private readonly IPlaybackProductionDecoderGenerationCreator _factory;
    private readonly IPlaybackWindowsAudioNativeStageReporter _audioNativeStageReporter;
    private readonly PlaybackProductionGenerationDeliveryTraceRetention _deliveryTraceRetention =
        new();

    private PlaybackProductionDecoderGeneration? _generation;
    private PlaybackProductionPcpFlvCompositionState _state =
        PlaybackProductionPcpFlvCompositionState.Accepting;
    private PlaybackProductionPcpFlvCompositionResult _result =
        PlaybackProductionPcpFlvCompositionResult.None;
    private long _nextPacketSequence;
    private long _acceptedPacketCount;
    private long _acceptedPayloadByteCount;
    private long _parserOperationCount;
    private long _decodeOperationCount;
    private long _transferredFrameCount;
    private long _deliveredFrameCount;
    private long _droppedFrameCount;
    private int _parserGeneration;
    private int _nextDecoderGeneration;
    private int _decoderGeneration = -1;
    private bool _hasDecoderGeneration;
    private bool _hasActiveOperation;
    private bool _ownedDisposed;
    private int _admissionClosed;
    private int _isDisposed;
    private Task? _stopTask;

    public PlaybackProductionPcpFlvCompositionBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackDecoderBackendPolicy policy,
        long initialPacketSequence = 0,
        PlaybackPcpFlvAwaitedDecodeOptions? options = null,
        PlaybackBoundedFlvParserOptions? parserOptions = null,
        PlaybackProductionDecoderGenerationFactoryOptions? generationOptions = null,
        IPlaybackMonotonicClock? clock = null)
        : this(
            channelId,
            channelSessionId,
            runtimeDirectory,
            output,
            new PlaybackProductionDecoderGenerationCreator(
                new PlaybackProductionDecoderGenerationFactory(
                    policy,
                    generationOptions,
                    clock)),
            NullPlaybackWindowsAudioNativeStageReporter.Instance,
            initialPacketSequence,
            options,
            parserOptions)
    {
    }

    internal PlaybackProductionPcpFlvCompositionBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        IPlaybackProductionDecoderGenerationCreator factory,
        IPlaybackWindowsAudioNativeStageReporter? audioNativeStageReporter = null,
        long initialPacketSequence = 0,
        PlaybackPcpFlvAwaitedDecodeOptions? options = null,
        PlaybackBoundedFlvParserOptions? parserOptions = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(channelSessionId);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            factory.Dispose();
            throw new ArgumentException(
                "The production runtime directory cannot be empty.",
                nameof(runtimeDirectory));
        }

        if (initialPacketSequence < 0)
        {
            factory.Dispose();
            throw new ArgumentOutOfRangeException(
                nameof(initialPacketSequence),
                initialPacketSequence,
                "The initial PCP packet sequence cannot be negative.");
        }

        _options = options is null
            ? new PlaybackPcpFlvAwaitedDecodeOptions()
            : options with { };
        try
        {
            ValidateOptions(_options);
        }
        catch
        {
            factory.Dispose();
            throw;
        }

        _channelId = channelId;
        _channelSessionId = channelSessionId;
        _runtimeDirectory = runtimeDirectory;
        _output = output;
        _factory = factory;
        _audioNativeStageReporter = audioNativeStageReporter ??
            NullPlaybackWindowsAudioNativeStageReporter.Instance;
        _nextPacketSequence = initialPacketSequence;
        _parser = new PlaybackOneEventFinalEosFlvParser(parserOptions);
    }

    public PlaybackProductionPcpFlvCompositionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var generation = _generation;
                var decode = DecodeSnapshotOrNull(generation);
                var deliveryTrace = _deliveryTraceRetention.Select(generation);
                var source = _parser.Session;
                var liveSource = source is { IsDisposed: false };
                return new(
                    _state,
                    _result,
                    _acceptedPacketCount,
                    _acceptedPayloadByteCount,
                    _parserOperationCount,
                    _decodeOperationCount,
                    _transferredFrameCount,
                    _deliveredFrameCount,
                    _droppedFrameCount,
                    _parserGeneration,
                    _decoderGeneration,
                    _hasDecoderGeneration,
                    _hasActiveOperation,
                    decode?.HasActiveDecodedCompletion == true,
                    decode?.HasActiveSubmission == true,
                    decode?.HasReliefRetry == true,
                    decode?.VideoFlushAccepted == true,
                    decode?.AudioFlushAccepted == true,
                    decode?.VideoEndOfStream == true,
                    decode?.AudioEndOfStream == true,
                    liveSource ? source!.Video.Width : 0,
                    liveSource ? source!.Video.Height : 0,
                    liveSource ? source!.Audio.SampleRate : 0,
                    liveSource ? source!.Audio.ChannelCount : 0)
                {
                    DeliveryTrace = deliveryTrace,
                };
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

    public ValueTask CompleteInputAsync(CancellationToken cancellationToken = default) =>
        CompleteInputCoreAsync(cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        DriveDecodedCoreAsync(
            mediaKind,
            PlaybackSourceBoundOneFrameDecodeMode.Ordinary,
            cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        DriveDecodedCoreAsync(
            mediaKind,
            PlaybackSourceBoundOneFrameDecodeMode.FlushDrain,
            cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
        CancellationToken cancellationToken = default) =>
        DeliverOneCoreAsync(cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
        CancellationToken cancellationToken = default) =>
        ResetCoreAsync(cancellationToken);

    public ValueTask StopAsync()
    {
        var existing = Volatile.Read(ref _stopTask);
        if (existing is not null)
        {
            return new(existing);
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        existing = Interlocked.CompareExchange(ref _stopTask, completion.Task, null);
        if (existing is not null)
        {
            return new(existing);
        }

        _ = CompleteStopAsync(completion);
        return new(completion.Task);
    }

    public void Dispose()
    {
        StopAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
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
                await StopGenerationForReplacementAsync().ConfigureAwait(false);
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
                        throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
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
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await HandleCancellationAsync(cancellationToken).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            var exception = Error(PlaybackProductionPcpFlvCompositionResult.Faulted);
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            ExitOperation();
        }
    }

    private async ValueTask CompleteInputCoreAsync(CancellationToken cancellationToken)
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
                    throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
                }

                await ProcessParserOperationAsync(
                    operation,
                    allowCompletionFreeEndOfStream: true,
                    decodeBudget,
                    decodeOperations,
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await HandleCancellationAsync(cancellationToken).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            var exception = Error(PlaybackProductionPcpFlvCompositionResult.Faulted);
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            ExitOperation();
        }
    }

    private async ValueTask<PlaybackProductionPcpFlvCompositionResult> DriveDecodedCoreAsync(
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
                        throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);
                    }

                    TransferCompletion(
                        generation,
                        operation.Completion,
                        mediaKind,
                        streamId,
                        linkedSource.Token);
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.FrameTransferred);

                case PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput:
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.NeedMoreInput);

                case PlaybackSourceBoundOneFrameDecodeResult.EndOfStream:
                    CommitDecoderEndOfStreamIfComplete(generation);
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream);

                case PlaybackSourceBoundOneFrameDecodeResult.StreamRequiresDrain:
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.StreamRequiresDrain);

                case PlaybackSourceBoundOneFrameDecodeResult.StreamNotFlushed:
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.StreamNotFlushed);

                case PlaybackSourceBoundOneFrameDecodeResult.StreamAlreadyEnded:
                    CommitDecoderEndOfStreamIfComplete(generation);
                    return SetResult(PlaybackProductionPcpFlvCompositionResult.StreamAlreadyEnded);

                case PlaybackSourceBoundOneFrameDecodeResult.Cancelled:
                    if (linkedSource.Token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(linkedSource.Token);
                    }

                    throw Error(PlaybackProductionPcpFlvCompositionResult.Cancelled);

                default:
                    throw Error(MapDecodeFailure(operation.Result));
            }
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await HandleCancellationAsync(cancellationToken).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            var exception = Error(PlaybackProductionPcpFlvCompositionResult.Faulted);
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            ExitOperation();
        }
    }

    private async ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneCoreAsync(
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
            var result = await generation.DeliverOneAsync(linkedSource.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(result))
            {
                throw Error(PlaybackProductionPcpFlvCompositionResult.DeliveryRejected);
            }

            return result switch
            {
                PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered =>
                    CommitDelivery(PlaybackProductionPcpFlvCompositionResult.VideoDelivered),
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered =>
                    CommitDelivery(PlaybackProductionPcpFlvCompositionResult.AudioDelivered),
                PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped =>
                    CommitDrop(),
                PlaybackGenerationMatchedSchedulerDeliveryResult.Empty =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.Empty),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedWait),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedWouldBlock),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedInitializationRequired =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedInitializationRequired),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedBindingRequired =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedBindingRequired),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedResizeRequired =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedResizeRequired),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedAudioRecoveryRequired =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.RetainedAudioRecoveryRequired),
                PlaybackGenerationMatchedSchedulerDeliveryResult.ClockNotStarted =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.ClockNotStarted),
                PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.ResetRequired),
                PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled
                    when linkedSource.Token.IsCancellationRequested =>
                    throw new OperationCanceledException(linkedSource.Token),
                _ => throw Error(PlaybackProductionPcpFlvCompositionResult.DeliveryRejected),
            };
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await HandleCancellationAsync(cancellationToken).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            var exception = Error(PlaybackProductionPcpFlvCompositionResult.Faulted);
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            ExitOperation();
        }
    }

    private async ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetCoreAsync(
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
            var result = await generation.ResetAsync(linkedSource.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(result))
            {
                throw Error(PlaybackProductionPcpFlvCompositionResult.ResetRejected);
            }

            return result switch
            {
                PlaybackGenerationMatchedSchedulerResetResult.Reset =>
                    SetResult(PlaybackProductionPcpFlvCompositionResult.Reset),
                PlaybackGenerationMatchedSchedulerResetResult.Cancelled
                    when linkedSource.Token.IsCancellationRequested =>
                    throw new OperationCanceledException(linkedSource.Token),
                _ => throw Error(PlaybackProductionPcpFlvCompositionResult.ResetRejected),
            };
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await HandleCancellationAsync(cancellationToken).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _isDisposed) != 0)
        {
            throw;
        }
        catch
        {
            var exception = Error(PlaybackProductionPcpFlvCompositionResult.Faulted);
            await EnterTerminalAsync(exception.Result).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            ExitOperation();
        }
    }

    private async ValueTask EnterOperationAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _admissionClosed) != 0)
        {
            throw Error(CurrentResult());
        }

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
            throw Error(CurrentResult());
        }

        lock (_sync)
        {
            _hasActiveOperation = true;
        }

        if (Volatile.Read(ref _admissionClosed) != 0)
        {
            ExitOperation();
            throw Error(CurrentResult());
        }
    }

    private void ExitOperation()
    {
        lock (_sync)
        {
            _hasActiveOperation = false;
        }

        _operationGate.Release();
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
            throw Error(PlaybackProductionPcpFlvCompositionResult.WorkLimitExceeded);
        }

        var operation = _parser.Parse(input, isFinal, discontinuity, cancellationToken);
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
            throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
        }

        switch (operation.Result)
        {
            case PlaybackBoundedFlvParserResult.StreamDescriptionAvailable:
                if (operation.Completion is not null)
                {
                    throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
                }

                await CreateGenerationAsync(cancellationToken).ConfigureAwait(false);
                return;

            case PlaybackBoundedFlvParserResult.PacketsAvailable:
                if (operation.Completion is null)
                {
                    throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
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

                throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);

            case PlaybackBoundedFlvParserResult.NeedMoreInput:
                if (operation.Completion is not null)
                {
                    throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
                }

                return;

            case PlaybackBoundedFlvParserResult.Cancelled:
                throw new OperationCanceledException(cancellationToken);

            default:
                throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
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
        if (source is null || source.IsDisposed ||
            !generation.ReferencesSourceSession(source) ||
            completion.IsDisposed || completion.Events.Count != 1)
        {
            completion.Dispose();
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        var sourceEvent = completion.Events[0];
        var result = generation.BeginSubmission(completion, cancellationToken);
        if (!Enum.IsDefined(result))
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.SubmissionRejected);
        }

        if (result is PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted or
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
            throw Error(PlaybackProductionPcpFlvCompositionResult.SubmissionRejected);
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
        PlaybackProductionDecoderGeneration generation,
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
                    throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);
                }

                TransferCompletion(
                    generation,
                    receive.Completion,
                    mediaKind,
                    streamId,
                    cancellationToken);

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

                        throw Error(PlaybackProductionPcpFlvCompositionResult.Cancelled);
                    default:
                        throw Error(MapDecodeFailure(retry.Result));
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

                throw Error(PlaybackProductionPcpFlvCompositionResult.Cancelled);
            }

            throw Error(MapDecodeFailure(receive.Result));
        }
    }

    private PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
        PlaybackProductionDecoderGeneration generation,
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
            throw Error(PlaybackProductionPcpFlvCompositionResult.WorkLimitExceeded);
        }

        lock (_sync)
        {
            _decodeOperationCount = checked(_decodeOperationCount + 1);
        }

        return generation.ReceiveOne(mediaKind, streamId, mode, cancellationToken, budget);
    }

    private void TransferCompletion(
        PlaybackProductionDecoderGeneration generation,
        PlaybackSourceBoundDecodedFrameCompletion completion,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        CancellationToken cancellationToken)
    {
        if (!IsValidCompletion(generation, completion, mediaKind, streamId))
        {
            completion.Dispose();
            throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);
        }

        var transfer = generation.Transfer(completion, cancellationToken);
        if (!Enum.IsDefined(transfer))
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);
        }

        switch (transfer)
        {
            case PlaybackDecodedCompletionSchedulerIngressResult.Transferred:
                lock (_sync)
                {
                    _transferredFrameCount = SaturatingIncrement(_transferredFrameCount);
                }

                // Transfer consumed the completion and detached its frame. Do not touch it.
                return;

            case PlaybackDecodedCompletionSchedulerIngressResult.Cancelled:
                if (cancellationToken.IsCancellationRequested || _stopSource.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);

            case PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected:
                throw Error(PlaybackProductionPcpFlvCompositionResult.SchedulerAdmissionRejected);

            case PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion:
            case PlaybackDecodedCompletionSchedulerIngressResult.SequenceExhausted:
            case PlaybackDecodedCompletionSchedulerIngressResult.Faulted:
            case PlaybackDecodedCompletionSchedulerIngressResult.Disposed:
            default:
                throw Error(PlaybackProductionPcpFlvCompositionResult.TransferRejected);
        }
    }

    private static bool IsValidCompletion(
        PlaybackProductionDecoderGeneration generation,
        PlaybackSourceBoundDecodedFrameCompletion completion,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId)
    {
        var snapshot = generation.Snapshot;
        if (snapshot is null || completion.IsDisposed || !completion.HasFrameOwnership ||
            generation.IsDisposed || generation.SourceSession.IsDisposed ||
            !completion.ReferencesSourceSession(generation.SourceSession) ||
            completion.DecoderGeneration != generation.DecoderGeneration ||
            completion.DiscontinuityGeneration != generation.DiscontinuityGeneration ||
            completion.MediaKind != mediaKind || completion.StreamId != streamId ||
            completion.ManagedByteCount < 1)
        {
            return false;
        }

        var expectedSequence = mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video =>
                snapshot.Ingress.NextVideoSourceSequence,
            PlaybackBoundedDemuxMediaKind.Audio =>
                snapshot.Ingress.NextAudioSourceSequence,
            _ => -1,
        };
        if (completion.Sequence != expectedSequence)
        {
            return false;
        }

        try
        {
            var frame = completion.Frame;
            var expectedFrameKind = mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackDecoderFfmpegFrameMediaKind.Video
                : PlaybackDecoderFfmpegFrameMediaKind.Audio;
            return !frame.IsDisposed && frame.Metadata.MediaKind == expectedFrameKind &&
                TryGetManagedByteCount(frame, out var managedByteCount) &&
                managedByteCount == completion.ManagedByteCount;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async ValueTask CreateGenerationAsync(CancellationToken cancellationToken)
    {
        if (_generation is not null || _nextDecoderGeneration == int.MaxValue)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        var source = _parser.Session;
        if (source is null || source.IsDisposed)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.SourceDisposed);
        }

        var decoderGeneration = _nextDecoderGeneration;
        var discontinuityGeneration = _parserGeneration;
        PlaybackProductionDecoderGenerationCreation creation;
        try
        {
            creation = await _factory.CreateAsync(
                _runtimeDirectory,
                source,
                _output,
                decoderGeneration,
                discontinuityGeneration,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        if (!Enum.IsDefined(creation.Result))
        {
            await StopUnexpectedGenerationAsync(creation.Generation).ConfigureAwait(false);
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        if (creation.Result != PlaybackProductionDecoderGenerationFactoryResult.Created)
        {
            await StopUnexpectedGenerationAsync(creation.Generation).ConfigureAwait(false);
            if (creation.Result == PlaybackProductionDecoderGenerationFactoryResult.Cancelled &&
                cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw Error(MapCreationFailure(creation.Result));
        }

        var generation = creation.Generation;
        if (generation is null || generation.IsDisposed || source.IsDisposed ||
            !generation.ReferencesSourceSession(source) ||
            generation.DecoderGeneration != decoderGeneration ||
            generation.DiscontinuityGeneration != discontinuityGeneration ||
            generation.DecodeSnapshot is null)
        {
            await StopUnexpectedGenerationAsync(generation).ConfigureAwait(false);
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        _generation = generation;
        _deliveryTraceRetention.Reset();
        _nextDecoderGeneration = checked(decoderGeneration + 1);
        lock (_sync)
        {
            _hasDecoderGeneration = true;
            _decoderGeneration = decoderGeneration;
        }
    }

    private async ValueTask StopGenerationForReplacementAsync()
    {
        var generation = _generation;
        _deliveryTraceRetention.Capture(generation);
        _generation = null;
        lock (_sync)
        {
            _hasDecoderGeneration = false;
            _decoderGeneration = -1;
        }

        if (generation is null)
        {
            return;
        }

        try
        {
            await generation.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        if (!generation.IsDisposed)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }
    }

    private async ValueTask HandleCancellationAsync(CancellationToken callerToken)
    {
        var result = Volatile.Read(ref _admissionClosed) != 0 && !callerToken.IsCancellationRequested
            ? PlaybackProductionPcpFlvCompositionResult.Disposed
            : PlaybackProductionPcpFlvCompositionResult.Cancelled;
        await EnterTerminalAsync(result).ConfigureAwait(false);
        if (result == PlaybackProductionPcpFlvCompositionResult.Disposed)
        {
            ThrowIfDisposed();
        }
    }

    private async ValueTask EnterTerminalAsync(
        PlaybackProductionPcpFlvCompositionResult result)
    {
        Interlocked.Exchange(ref _admissionClosed, 1);
        try
        {
            _stopSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Stop already completed.
        }

        lock (_sync)
        {
            if (_state != PlaybackProductionPcpFlvCompositionState.Disposed &&
                _state != PlaybackProductionPcpFlvCompositionState.Faulted)
            {
                _state = PlaybackProductionPcpFlvCompositionState.Faulted;
                _result = result;
                _hasDecoderGeneration = false;
                _decoderGeneration = -1;
            }
        }

        await DisposeOwnedAsync().ConfigureAwait(false);
    }

    private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        catch
        {
            // Stop exposes no cleanup exception.
        }
        finally
        {
            completion.TrySetResult(true);
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _admissionClosed, 1);
        try
        {
            _stopSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeOwnedAsync().ConfigureAwait(false);
            lock (_sync)
            {
                _state = PlaybackProductionPcpFlvCompositionState.Disposed;
                _result = PlaybackProductionPcpFlvCompositionResult.Disposed;
                _hasDecoderGeneration = false;
                _decoderGeneration = -1;
                _hasActiveOperation = false;
            }

            Volatile.Write(ref _isDisposed, 1);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _stopSource.Dispose();
        }
    }

    private async ValueTask DisposeOwnedAsync()
    {
        if (_ownedDisposed)
        {
            return;
        }

        _ownedDisposed = true;
        var generation = _generation;
        _deliveryTraceRetention.Capture(generation);
        _generation = null;
        if (generation is not null)
        {
            _audioNativeStageReporter.Report(
                PlaybackWindowsAudioNativeStage.ProductionGenerationCleanupEntering);
            try
            {
                await generation.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the first fixed terminal result.
            }
            _audioNativeStageReporter.Report(
                PlaybackWindowsAudioNativeStage.ProductionGenerationCleanupCompleted);
        }

        _audioNativeStageReporter.Report(
            PlaybackWindowsAudioNativeStage.ProductionParserCleanupEntering);
        try
        {
            _parser.Dispose();
        }
        catch
        {
            // Preserve the first fixed terminal result.
        }
        _audioNativeStageReporter.Report(
            PlaybackWindowsAudioNativeStage.ProductionParserCleanupCompleted);

        _audioNativeStageReporter.Report(
            PlaybackWindowsAudioNativeStage.ProductionFactoryCleanupEntering);
        try
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preserve the first fixed terminal result.
        }
        _audioNativeStageReporter.Report(
            PlaybackWindowsAudioNativeStage.ProductionFactoryCleanupCompleted);
    }

    private void ValidatePacket(PcpChannelPacket packet)
    {
        if (packet.ChannelId != _channelId || packet.SessionId != _channelSessionId)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.InvalidIdentity);
        }

        if (packet.Sequence != _nextPacketSequence || packet.Sequence == long.MaxValue)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.SequenceRejected);
        }
    }

    private void ValidateParserAccounting(
        PlaybackBoundedFlvParserOperation operation,
        int remainingByteCount)
    {
        if (operation.ConsumedByteCount < 0 ||
            operation.ConsumedByteCount > remainingByteCount ||
            operation.StreamBytePosition < 0 || operation.CarryByteCount < 0 ||
            operation.SkippedTagCount < 0 || operation.Generation < 0)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
        }

        var madeProgress = operation.ConsumedByteCount > 0 || operation.Completion is not null ||
            operation.Result == PlaybackBoundedFlvParserResult.StreamDescriptionAvailable ||
            operation.Result == PlaybackBoundedFlvParserResult.EndOfStream;
        if (remainingByteCount > 0 && !madeProgress)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
        }
    }

    private PlaybackProductionDecoderGeneration EnsureGenerationAvailable()
    {
        var generation = _generation;
        if (generation is null || generation.IsDisposed || generation.DecodeSnapshot is null)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        return generation;
    }

    private void EnsureNoActiveDecodeWork()
    {
        if (_generation is not { IsDisposed: false } generation)
        {
            return;
        }

        var snapshot = generation.DecodeSnapshot;
        if (snapshot is null || snapshot.HasActiveDecodedCompletion ||
            snapshot.HasActiveSubmission || snapshot.HasReliefRetry)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.SubmissionRejected);
        }
    }

    private void EnsureFinalStreamsSubmitted()
    {
        var generation = EnsureGenerationAvailable();
        var snapshot = generation.DecodeSnapshot;
        if (snapshot is null)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        }

        var videoSubmitted = snapshot.VideoFlushAccepted || snapshot.VideoEndOfStream;
        var audioSubmitted = snapshot.AudioFlushAccepted || snapshot.AudioEndOfStream;
        if (!videoSubmitted || !audioSubmitted || snapshot.HasActiveDecodedCompletion ||
            snapshot.HasActiveSubmission || snapshot.HasReliefRetry)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.SubmissionRejected);
        }
    }

    private void EnsureAcceptingInput()
    {
        PlaybackProductionPcpFlvCompositionState state;
        PlaybackProductionPcpFlvCompositionResult result;
        lock (_sync)
        {
            state = _state;
            result = _result;
        }

        if (state == PlaybackProductionPcpFlvCompositionState.Accepting)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(
            state == PlaybackProductionPcpFlvCompositionState.Disposed,
            this);
        if (state is PlaybackProductionPcpFlvCompositionState.EncodedInputFinalized or
            PlaybackProductionPcpFlvCompositionState.DecodersDrained)
        {
            throw Error(PlaybackProductionPcpFlvCompositionResult.ParserRejected);
        }

        throw Error(result);
    }

    private void EnsureOperational()
    {
        PlaybackProductionPcpFlvCompositionState state;
        PlaybackProductionPcpFlvCompositionResult result;
        lock (_sync)
        {
            state = _state;
            result = _result;
        }

        ObjectDisposedException.ThrowIf(
            state == PlaybackProductionPcpFlvCompositionState.Disposed,
            this);
        if (state == PlaybackProductionPcpFlvCompositionState.Faulted)
        {
            throw Error(result);
        }
    }

    private bool IsEncodedInputFinalized()
    {
        lock (_sync)
        {
            return _state is PlaybackProductionPcpFlvCompositionState.EncodedInputFinalized or
                PlaybackProductionPcpFlvCompositionState.DecodersDrained;
        }
    }

    private void CommitPacket(int payloadByteCount)
    {
        _nextPacketSequence = checked(_nextPacketSequence + 1);
        lock (_sync)
        {
            _acceptedPacketCount = checked(_acceptedPacketCount + 1);
            _acceptedPayloadByteCount = checked(_acceptedPayloadByteCount + payloadByteCount);
            _state = PlaybackProductionPcpFlvCompositionState.Accepting;
            _result = PlaybackProductionPcpFlvCompositionResult.PacketAccepted;
        }
    }

    private void CommitEncodedInputFinalized()
    {
        var generation = EnsureGenerationAvailable();
        var snapshot = generation.DecodeSnapshot ??
            throw Error(PlaybackProductionPcpFlvCompositionResult.GenerationRejected);
        lock (_sync)
        {
            _state = snapshot.VideoEndOfStream && snapshot.AudioEndOfStream
                ? PlaybackProductionPcpFlvCompositionState.DecodersDrained
                : PlaybackProductionPcpFlvCompositionState.EncodedInputFinalized;
            _result = PlaybackProductionPcpFlvCompositionResult.EncodedInputFinalized;
        }
    }

    private void CommitDecoderEndOfStreamIfComplete(
        PlaybackProductionDecoderGeneration generation)
    {
        var snapshot = generation.DecodeSnapshot;
        if (snapshot is null || !snapshot.VideoEndOfStream || !snapshot.AudioEndOfStream)
        {
            return;
        }

        lock (_sync)
        {
            if (_state == PlaybackProductionPcpFlvCompositionState.EncodedInputFinalized)
            {
                _state = PlaybackProductionPcpFlvCompositionState.DecodersDrained;
            }
        }
    }

    private PlaybackProductionPcpFlvCompositionResult CommitDelivery(
        PlaybackProductionPcpFlvCompositionResult result)
    {
        lock (_sync)
        {
            _deliveredFrameCount = SaturatingIncrement(_deliveredFrameCount);
            _result = result;
            return result;
        }
    }

    private PlaybackProductionPcpFlvCompositionResult CommitDrop()
    {
        lock (_sync)
        {
            _droppedFrameCount = SaturatingIncrement(_droppedFrameCount);
            _result = PlaybackProductionPcpFlvCompositionResult.VideoDropped;
            return _result;
        }
    }

    private PlaybackProductionPcpFlvCompositionResult SetResult(
        PlaybackProductionPcpFlvCompositionResult result)
    {
        lock (_sync)
        {
            _result = result;
            return result;
        }
    }

    private PlaybackProductionPcpFlvCompositionResult CurrentResult()
    {
        lock (_sync)
        {
            return _result == PlaybackProductionPcpFlvCompositionResult.None
                ? PlaybackProductionPcpFlvCompositionResult.Disposed
                : _result;
        }
    }

    private PlaybackSourceBoundOneFrameDecodeBudget CreateDecodeBudget() =>
        new(
            _options.MaximumDecodedFramesPerCall,
            _options.MaximumDecodedManagedBytesPerCall);

    private static PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshotOrNull(
        PlaybackProductionDecoderGeneration? generation)
    {
        try
        {
            return generation is { IsDisposed: false } ? generation.DecodeSnapshot : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static int StreamId(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind) => mediaKind switch
        {
            PlaybackBoundedDemuxMediaKind.Video => source.Video.StreamId,
            PlaybackBoundedDemuxMediaKind.Audio => source.Audio.StreamId,
            _ => throw new InvalidOperationException("The media kind is undefined."),
        };

    private static PlaybackProductionPcpFlvCompositionResult MapDecodeFailure(
        PlaybackSourceBoundOneFrameDecodeResult result) => result switch
        {
            PlaybackSourceBoundOneFrameDecodeResult.DecoderNoProgress =>
                PlaybackProductionPcpFlvCompositionResult.DecoderNoProgress,
            PlaybackSourceBoundOneFrameDecodeResult.WorkLimitExceeded =>
                PlaybackProductionPcpFlvCompositionResult.WorkLimitExceeded,
            PlaybackSourceBoundOneFrameDecodeResult.Cancelled =>
                PlaybackProductionPcpFlvCompositionResult.Cancelled,
            PlaybackSourceBoundOneFrameDecodeResult.FrameAvailable =>
                PlaybackProductionPcpFlvCompositionResult.TransferRejected,
            _ => PlaybackProductionPcpFlvCompositionResult.SubmissionRejected,
        };

    private static PlaybackProductionPcpFlvCompositionResult MapCreationFailure(
        PlaybackProductionDecoderGenerationFactoryResult result) => result switch
        {
            PlaybackProductionDecoderGenerationFactoryResult.LicenseReviewPending =>
                PlaybackProductionPcpFlvCompositionResult.LicenseReviewPending,
            PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected =>
                PlaybackProductionPcpFlvCompositionResult.PolicyRejected,
            PlaybackProductionDecoderGenerationFactoryResult.InvalidConfiguration =>
                PlaybackProductionPcpFlvCompositionResult.InvalidConfiguration,
            PlaybackProductionDecoderGenerationFactoryResult.SourceDisposed =>
                PlaybackProductionPcpFlvCompositionResult.SourceDisposed,
            PlaybackProductionDecoderGenerationFactoryResult.OutputUnavailable =>
                PlaybackProductionPcpFlvCompositionResult.OutputUnavailable,
            PlaybackProductionDecoderGenerationFactoryResult.BindingRejected =>
                PlaybackProductionPcpFlvCompositionResult.BindingRejected,
            PlaybackProductionDecoderGenerationFactoryResult.VideoRuntimeRejected or
            PlaybackProductionDecoderGenerationFactoryResult.AudioRuntimeRejected =>
                PlaybackProductionPcpFlvCompositionResult.RuntimeRejected,
            PlaybackProductionDecoderGenerationFactoryResult.VideoAbiGateRejected or
            PlaybackProductionDecoderGenerationFactoryResult.AudioAbiGateRejected =>
                PlaybackProductionPcpFlvCompositionResult.AbiGateRejected,
            PlaybackProductionDecoderGenerationFactoryResult.OwnershipConflict =>
                PlaybackProductionPcpFlvCompositionResult.OwnershipConflict,
            PlaybackProductionDecoderGenerationFactoryResult.NativeSessionRejected =>
                PlaybackProductionPcpFlvCompositionResult.NativeSessionRejected,
            PlaybackProductionDecoderGenerationFactoryResult.GenerationRejected =>
                PlaybackProductionPcpFlvCompositionResult.GenerationRejected,
            PlaybackProductionDecoderGenerationFactoryResult.Cancelled =>
                PlaybackProductionPcpFlvCompositionResult.Cancelled,
            PlaybackProductionDecoderGenerationFactoryResult.Disposed =>
                PlaybackProductionPcpFlvCompositionResult.Disposed,
            _ => PlaybackProductionPcpFlvCompositionResult.Faulted,
        };

    private static bool TryGetManagedByteCount(
        PlaybackDecoderFfmpegManagedFrame frame,
        out long managedByteCount)
    {
        managedByteCount = 0;
        if (frame.Planes.Count < 1)
        {
            return false;
        }

        try
        {
            foreach (var plane in frame.Planes)
            {
                if (plane.Buffer.IsEmpty)
                {
                    return false;
                }

                managedByteCount = checked(managedByteCount + plane.Buffer.Length);
            }

            return managedByteCount > 0 && managedByteCount <=
                PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit;
        }
        catch (OverflowException)
        {
            managedByteCount = 0;
            return false;
        }
    }

    private static async ValueTask StopUnexpectedGenerationAsync(
        PlaybackProductionDecoderGeneration? generation)
    {
        if (generation is null)
        {
            return;
        }

        try
        {
            await generation.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // The composition still returns one fixed generation-creation failure.
        }
    }

    private PlaybackProductionPcpFlvCompositionException Error(
        PlaybackProductionPcpFlvCompositionResult result) => new(result);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _isDisposed) != 0,
            this);
    }

    private static long SaturatingIncrement(long value) =>
        value == long.MaxValue ? long.MaxValue : value + 1;

    private static void ValidateOptions(PlaybackPcpFlvAwaitedDecodeOptions options)
    {
        if (options.MaximumParserOperationsPerCall < 1 ||
            options.MaximumParserOperationsPerCall >
                PlaybackPcpFlvAwaitedDecodeOptions.MaximumParserOperationsPerCallHardLimit ||
            options.MaximumDecodeOperationsPerCall < 1 ||
            options.MaximumDecodeOperationsPerCall >
                PlaybackPcpFlvAwaitedDecodeOptions.MaximumDecodeOperationsPerCallHardLimit ||
            options.MaximumDecodedFramesPerCall < 1 ||
            options.MaximumDecodedFramesPerCall >
                PlaybackSourceBoundOneFrameDecodeBudget.MaximumFrameCountHardLimit ||
            options.MaximumDecodedManagedBytesPerCall < 1 ||
            options.MaximumDecodedManagedBytesPerCall >
                PlaybackSourceBoundOneFrameDecodeBudget.MaximumManagedBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private sealed class DecodeOperationCounter
    {
        public int Count { get; set; }
    }
}
