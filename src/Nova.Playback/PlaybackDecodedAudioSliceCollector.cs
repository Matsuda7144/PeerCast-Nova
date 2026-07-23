namespace PeerCast.Nova.Playback;

public enum PlaybackDecodedAudioSliceCollectorResult
{
    Accepting,
    Completed,
    InvalidFrame,
    SequenceRejected,
    GenerationRejected,
    BudgetExceeded,
    Cancelled,
    AlreadyCompleted,
    Disposed,
}

public sealed record PlaybackDecodedAudioSliceCollectorOptions
{
    public const int TargetSampleFrames = 3_840;
    public const int ExpectedDecodedFrames = 4;
    public const int ExpectedFullFrames = 3;
    public const int ExpectedFinalFrameSampleFrames = 768;
    public const int PayloadBytes = TargetSampleFrames * 2 * sizeof(float);

    public long Generation { get; init; } = 1;

    public long MaximumOwnedBytes { get; init; } =
        PlaybackSourceBoundOneFrameDecodeOptions.DefaultMaximumManagedFrameBytes;
}

public readonly record struct PlaybackDecodedAudioSliceCollectorSnapshot(
    PlaybackDecodedAudioSliceCollectorResult Result,
    long Generation,
    int DecodedFramesConsumed,
    int FullDecodedFramesCopied,
    int FinalFrameSampleFramesCopied,
    int OutputSampleFrames,
    int PayloadBytes,
    bool IsComplete,
    bool PayloadPublished);

/// <summary>
/// Diagnostic-only bounded collector for the chronological first 80 ms of the
/// canonical decoded audio stream. Callback-scoped source payloads are copied
/// immediately. No incomplete aggregate can be obtained by callers.
/// </summary>
public sealed class PlaybackDecodedAudioSliceCollector :
    IPlaybackDecodedFrameSink,
    IDisposable
{
    public const PlaybackDecodedAudioSampleFormat SampleFormat =
        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved;
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int DurationMilliseconds = 80;
    public const int BytesPerSampleFrame = Channels * sizeof(float);

    private readonly object _sync = new();
    private readonly PlaybackDecodedAudioSliceCollectorOptions _options;
    private byte[]? _payload;
    private PlaybackDecodedAudioSliceCollectorResult _result =
        PlaybackDecodedAudioSliceCollectorResult.Accepting;
    private long _nextSequence;
    private int _decodedFramesConsumed;
    private int _fullDecodedFramesCopied;
    private int _finalFrameSampleFramesCopied;
    private int _outputSampleFrames;
    private bool _payloadPublished;
    private bool _isDisposed;

    public PlaybackDecodedAudioSliceCollector(
        PlaybackDecodedAudioSliceCollectorOptions? options = null)
    {
        _options = options is null
            ? new PlaybackDecodedAudioSliceCollectorOptions()
            : options with { };
        if (_options.Generation < 0 ||
            _options.MaximumOwnedBytes <
                PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes ||
            _options.MaximumOwnedBytes >
                PlaybackSourceBoundOneFrameDecodeOptions.MaximumManagedFrameBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _payload = new byte[PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes];
    }

    public PlaybackDecodedAudioSliceCollectorSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _result,
                    _options.Generation,
                    _decodedFramesConsumed,
                    _fullDecodedFramesCopied,
                    _finalFrameSampleFramesCopied,
                    _outputSampleFrames,
                    checked(_outputSampleFrames * BytesPerSampleFrame),
                    IsExpectedComplete(),
                    _payloadPublished);
            }
        }
    }

    public bool IsComplete
    {
        get
        {
            lock (_sync)
            {
                return IsExpectedComplete();
            }
        }
    }

    public ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryAppend(
            frame.Sequence,
            _options.Generation,
            frame.SampleFormat,
            frame.SampleRate,
            frame.ChannelCount,
            frame.SampleCount,
            frame.Payload,
            cancellationToken,
            out var result))
        {
            throw new PlaybackDecodedAudioSliceCollectorException(result);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlaybackDecodedAudioSliceCollectorException(
            PlaybackDecodedAudioSliceCollectorResult.InvalidFrame);
    }

    public bool TryTakeCompletedBuffer(
        long outputGeneration,
        out PlaybackWindowsAudioSampleBuffer buffer)
    {
        lock (_sync)
        {
            if (outputGeneration < 0 ||
                _isDisposed || !IsExpectedComplete() || _payloadPublished ||
                _payload is null)
            {
                buffer = default;
                return false;
            }

            var payload = _payload;
            _payload = null;
            _payloadPublished = true;
            buffer = new(
                SampleFormat,
                SampleRate,
                Channels,
                PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames,
                payload,
                outputGeneration);
            return true;
        }
    }

    internal bool TryAppend(
        long sequence,
        long generation,
        PlaybackDecodedAudioSampleFormat sampleFormat,
        int sampleRate,
        int channels,
        int sampleFrames,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken,
        out PlaybackDecodedAudioSliceCollectorResult result)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                result = PlaybackDecodedAudioSliceCollectorResult.Disposed;
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.Cancelled;
                result = _result;
                return false;
            }

            if (IsExpectedComplete())
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.AlreadyCompleted;
                result = _result;
                return false;
            }

            if (generation != _options.Generation)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.GenerationRejected;
                result = _result;
                return false;
            }

            if (sequence != _nextSequence || sequence < 0 ||
                _nextSequence == long.MaxValue)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.SequenceRejected;
                result = _result;
                return false;
            }

            int sourceBytes;
            try
            {
                sourceBytes = checked(sampleFrames * BytesPerSampleFrame);
            }
            catch (OverflowException)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.InvalidFrame;
                result = _result;
                return false;
            }

            if (sampleFormat != SampleFormat ||
                sampleRate != SampleRate ||
                channels != Channels ||
                sampleFrames <= 0 ||
                payload.Length != sourceBytes)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.InvalidFrame;
                result = _result;
                return false;
            }

            var remainingFrames =
                PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames -
                _outputSampleFrames;
            var copiedFrames = Math.Min(sampleFrames, remainingFrames);
            int copyBytes;
            int destinationOffset;
            try
            {
                copyBytes = checked(copiedFrames * BytesPerSampleFrame);
                destinationOffset = checked(_outputSampleFrames * BytesPerSampleFrame);
            }
            catch (OverflowException)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.BudgetExceeded;
                result = _result;
                return false;
            }

            if (copyBytes < 1 || destinationOffset < 0 ||
                checked((long)destinationOffset + copyBytes) > _options.MaximumOwnedBytes ||
                _payload is null ||
                destinationOffset > _payload.Length - copyBytes)
            {
                _result = PlaybackDecodedAudioSliceCollectorResult.BudgetExceeded;
                result = _result;
                return false;
            }

            payload.Span[..copyBytes].CopyTo(_payload.AsSpan(destinationOffset, copyBytes));
            _decodedFramesConsumed = checked(_decodedFramesConsumed + 1);
            if (copiedFrames == sampleFrames)
            {
                _fullDecodedFramesCopied = checked(_fullDecodedFramesCopied + 1);
            }
            else
            {
                _finalFrameSampleFramesCopied = copiedFrames;
            }

            _outputSampleFrames = checked(_outputSampleFrames + copiedFrames);
            _nextSequence = checked(_nextSequence + 1);
            _result = HasExpectedTotals()
                ? PlaybackDecodedAudioSliceCollectorResult.Completed
                : PlaybackDecodedAudioSliceCollectorResult.Accepting;
            result = _result;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _payload = null;
            _result = PlaybackDecodedAudioSliceCollectorResult.Disposed;
        }

        GC.SuppressFinalize(this);
    }

    private bool IsExpectedComplete() =>
        _result == PlaybackDecodedAudioSliceCollectorResult.Completed &&
        HasExpectedTotals();

    private bool HasExpectedTotals() =>
        _decodedFramesConsumed ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedDecodedFrames &&
        _fullDecodedFramesCopied ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedFullFrames &&
        _finalFrameSampleFramesCopied ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedFinalFrameSampleFrames &&
        _outputSampleFrames ==
            PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames;
}

public sealed class PlaybackDecodedAudioSliceCollectorException(
    PlaybackDecodedAudioSliceCollectorResult result) :
    Exception("The decoded audio slice collector rejected a frame.")
{
    public PlaybackDecodedAudioSliceCollectorResult Result { get; } = result;
}

internal sealed class PlaybackDecodedAudioSliceGenerationFactory :
    IPlaybackPcpFlvAwaitedDecodeGenerationFactory
{
    private readonly PlaybackProductionDecoderGenerationFactoryOperations _operations =
        new(new StopwatchPlaybackMonotonicClock());
    private readonly PlaybackDecodedAudioSliceCollector _collector;
    private IDisposable? _videoRuntime;
    private IDisposable? _videoGate;
    private IDisposable? _audioRuntime;
    private IDisposable? _audioGate;
    private bool _creationAttempted;
    private bool _isDisposed;

    private PlaybackDecodedAudioSliceGenerationFactory(
        PlaybackDecodedAudioSliceCollector collector,
        IDisposable videoRuntime,
        IDisposable videoGate,
        IDisposable audioRuntime,
        IDisposable audioGate)
    {
        _collector = collector;
        _videoRuntime = videoRuntime;
        _videoGate = videoGate;
        _audioRuntime = audioRuntime;
        _audioGate = audioGate;
    }

    internal static async ValueTask<PlaybackDecodedAudioSliceGenerationFactory?> PrepareAsync(
        string runtimeDirectory,
        PlaybackDecodedAudioSliceCollector collector,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(collector);

        var operations =
            new PlaybackProductionDecoderGenerationFactoryOperations(
                new StopwatchPlaybackMonotonicClock());
        IDisposable? videoRuntime = null;
        IDisposable? videoGate = null;
        IDisposable? audioRuntime = null;
        IDisposable? audioGate = null;
        try
        {
            var policy = new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowBundled = false,
                AllowNativeCode = true,
            };
            if (policy.Evaluate(operations.Provenance) !=
                PlaybackDecoderBackendPolicyRejection.None)
            {
                return null;
            }

            var videoRuntimeCreation = await operations.LocateRuntimeAsync(
                runtimeDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!TryTake(videoRuntimeCreation, out videoRuntime))
            {
                return null;
            }

            var videoGateCreation = operations.AcquireGate(videoRuntime!);
            if (!TryTake(videoGateCreation, out videoGate))
            {
                return null;
            }

            var audioRuntimeCreation = await operations.LocateRuntimeAsync(
                runtimeDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!TryTake(audioRuntimeCreation, out audioRuntime) ||
                ReferenceEquals(videoRuntime, audioRuntime))
            {
                return null;
            }

            var audioGateCreation = operations.AcquireGate(audioRuntime!);
            if (!TryTake(audioGateCreation, out audioGate) ||
                ReferenceEquals(videoGate, audioGate))
            {
                return null;
            }

            var factory = new PlaybackDecodedAudioSliceGenerationFactory(
                collector,
                videoRuntime!,
                videoGate!,
                audioRuntime!,
                audioGate!);
            videoRuntime = null;
            videoGate = null;
            audioRuntime = null;
            audioGate = null;
            return factory;
        }
        finally
        {
            DisposeReverse(audioGate, audioRuntime, videoGate, videoRuntime);
        }
    }

    public PlaybackPcpFlvAwaitedDecodeGenerationCreation Create(
        PlaybackBoundedDemuxSession sourceSession,
        int decoderGeneration,
        int discontinuityGeneration)
    {
        if (_isDisposed || _creationAttempted || sourceSession.IsDisposed ||
            _videoGate is null || _audioGate is null)
        {
            return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Rejected, null);
        }

        _creationAttempted = true;
        IDisposable? binding = null;
        IDisposable? nativeOwner = null;
        PlaybackSourceBoundOneFrameDecodeBoundary? decode = null;
        PlaybackDecodedAudioSliceSettlement? settlement = null;
        try
        {
            var bindingCreation = _operations.PrepareBinding(sourceSession);
            if (!TryTake(bindingCreation, out binding))
            {
                return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Rejected, null);
            }

            var nativeCreation = _operations.OpenNativeSession(
                binding!,
                _videoGate,
                _audioGate);
            binding = null;
            _videoGate = null;
            _audioGate = null;
            if (!TryTake(nativeCreation, out nativeOwner) ||
                nativeOwner is not PlaybackBoundedFlvNativeDecoderSession nativeSession)
            {
                return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Rejected, null);
            }

            _videoRuntime = null;
            _audioRuntime = null;
            settlement = new(
                nativeSession.AudioContext,
                _collector,
                decoderGeneration,
                discontinuityGeneration);
            decode = new(
                sourceSession,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration);
            nativeOwner = null;

            var generation = new PlaybackPcpFlvAwaitedDecodeGeneration(
                sourceSession,
                decode,
                settlement,
                decoderGeneration,
                discontinuityGeneration);
            decode = null;
            settlement = null;
            return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Created, generation);
        }
        catch
        {
            return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Faulted, null);
        }
        finally
        {
            settlement?.Dispose();
            decode?.Dispose();
            nativeOwner?.Dispose();
            binding?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisposeReverse(_audioGate, _audioRuntime, _videoGate, _videoRuntime);
        _audioGate = null;
        _audioRuntime = null;
        _videoGate = null;
        _videoRuntime = null;
    }

    private static bool TryTake(
        PlaybackProductionDecoderGenerationOwnedCreation creation,
        out IDisposable? owner)
    {
        owner = creation.Owner;
        return creation.Result == PlaybackProductionDecoderGenerationStageResult.Created &&
            owner is not null;
    }

    private static void DisposeReverse(params IDisposable?[] owners)
    {
        var seen = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (var owner in owners)
        {
            if (owner is not null && seen.Add(owner))
            {
                try
                {
                    owner.Dispose();
                }
                catch
                {
                    // Diagnostic construction cleanup remains fail-closed.
                }
            }
        }
    }
}

internal sealed class PlaybackDecodedAudioSliceSettlement :
    IPlaybackPcpFlvDecodedCompletionSettlement
{
    private readonly PlaybackDecoderFfmpegContextLease _audioContext;
    private readonly PlaybackDecodedAudioSliceCollector _collector;
    private readonly int _decoderGeneration;
    private readonly int _discontinuityGeneration;
    private PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary? _delivery = new();

    internal PlaybackDecodedAudioSliceSettlement(
        PlaybackDecoderFfmpegContextLease audioContext,
        PlaybackDecodedAudioSliceCollector collector,
        int decoderGeneration,
        int discontinuityGeneration)
    {
        _audioContext = audioContext;
        _collector = collector;
        _decoderGeneration = decoderGeneration;
        _discontinuityGeneration = discontinuityGeneration;
    }

    public async ValueTask<PlaybackPcpFlvDecodedSettlementResult> SettleAsync(
        PlaybackSourceBoundDecodedFrameCompletion completion,
        CancellationToken cancellationToken)
    {
        var delivery = Volatile.Read(ref _delivery);
        if (delivery is null ||
            completion.IsDisposed ||
            completion.MediaKind != PlaybackBoundedDemuxMediaKind.Audio ||
            completion.DecoderGeneration != _decoderGeneration ||
            completion.DiscontinuityGeneration != _discontinuityGeneration)
        {
            return PlaybackPcpFlvDecodedSettlementResult.Rejected;
        }

        var result = await delivery.DeliverAsync(
            _audioContext,
            completion.Frame,
            completion.Sequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            _collector,
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered =>
                PlaybackPcpFlvDecodedSettlementResult.Consumed,
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled =>
                PlaybackPcpFlvDecodedSettlementResult.Cancelled,
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed =>
                PlaybackPcpFlvDecodedSettlementResult.Rejected,
            _ => PlaybackPcpFlvDecodedSettlementResult.Faulted,
        };
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _delivery, null)?.Dispose();
}
