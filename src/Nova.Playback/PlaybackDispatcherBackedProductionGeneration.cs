namespace PeerCast.Nova.Playback;

internal sealed class PlaybackDispatcherBackedProductionGenerationFactoryOperations :
    IPlaybackProductionDecoderGenerationFactoryOperations
{
    private readonly IPlaybackMonotonicClock _clock;
    private readonly IPlaybackWindowsOutputDispatcher _dispatcher;
    private readonly PlaybackProductionDecoderGenerationFactoryOperations _standard;

    internal PlaybackDispatcherBackedProductionGenerationFactoryOperations(
        IPlaybackMonotonicClock clock,
        IPlaybackWindowsOutputDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _clock = clock;
        _dispatcher = dispatcher;
        _standard = new(clock);
    }

    public PlaybackDecoderBackendProvenance Provenance => _standard.Provenance;

    public PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
        PlaybackBoundedDemuxSession sourceSession) =>
        _standard.PrepareBinding(sourceSession);

    public ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
        string runtimeDirectory,
        CancellationToken cancellationToken) =>
        _standard.LocateRuntimeAsync(runtimeDirectory, cancellationToken);

    public PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(
        IDisposable runtimeOwner) =>
        _standard.AcquireGate(runtimeOwner);

    public PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
        IDisposable bindingOwner,
        IDisposable videoGateOwner,
        IDisposable audioGateOwner) =>
        _standard.OpenNativeSession(bindingOwner, videoGateOwner, audioGateOwner);

    public PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
        PlaybackBoundedDemuxSession sourceSession,
        IDisposable nativeSessionOwner,
        int decoderGeneration,
        int discontinuityGeneration,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackProductionDecoderGenerationFactoryOptions options)
    {
        if (nativeSessionOwner is not PlaybackBoundedFlvNativeDecoderSession nativeSession)
        {
            nativeSessionOwner.Dispose();
            return Faulted();
        }

        PlaybackBoundedFlvNativeDecoderSession? ownedNative = nativeSession;
        PlaybackSourceBoundOneFrameDecodeBoundary? decode = null;
        BoundedPlaybackScheduler? scheduler = null;
        PlaybackAvTimingDecisionBoundary? timing = null;
        IPlaybackGenerationMatchedSchedulerDeliveryOperations? deliveryOperations = null;
        try
        {
            decode = new(
                sourceSession,
                ownedNative,
                decoderGeneration,
                discontinuityGeneration,
                options.Decode);
            ownedNative = null;

            var schedulerPolicy = new PlaybackPolicyClockBoundary(
                _clock,
                options.SchedulerPolicy);
            scheduler = new(options.Scheduler, schedulerPolicy);
            timing = new(
                _clock,
                options.Timing,
                decoderGeneration,
                decoderGeneration);
            deliveryOperations = new PlaybackDispatcherBackedSchedulerDeliveryOperations(
                timing,
                output,
                _dispatcher);
            timing = null;
            var delivery = new PlaybackGenerationMatchedSchedulerDeliveryBoundary(
                sourceSession,
                decode,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration,
                scheduler,
                initialSchedulerSequence: 0,
                deliveryOperations,
                captureDeliveryObservations: options.CaptureDeliveryObservations);
            decode = null;
            scheduler = null;
            deliveryOperations = null;
            return new(
                PlaybackProductionDecoderGenerationStageResult.Created,
                new PlaybackProductionDecoderGenerationRoot(
                    sourceSession,
                    decoderGeneration,
                    discontinuityGeneration,
                    delivery));
        }
        catch
        {
            try
            {
                deliveryOperations?.Dispose();
                timing?.Dispose();
            }
            finally
            {
                try
                {
                    scheduler?.Dispose();
                }
                finally
                {
                    try
                    {
                        decode?.Dispose();
                    }
                    finally
                    {
                        ownedNative?.Dispose();
                    }
                }
            }

            return Faulted();
        }
    }

    private static PlaybackProductionDecoderGenerationRootCreation Faulted() =>
        new(PlaybackProductionDecoderGenerationStageResult.Faulted, null);
}

internal sealed class PlaybackDispatcherBackedSchedulerDeliveryOperations :
    IPlaybackGenerationMatchedSchedulerDeliveryOperations
{
    private readonly PlaybackAvTimingDecisionBoundary _timing;
    private readonly PlaybackWindowsOutputBufferProcessingBoundary _output;
    private readonly IPlaybackWindowsOutputDispatcher _dispatcher;
    private readonly PlaybackDecoderFfmpegDecodedOutputAdapter _adapter = new();
    private readonly PlaybackDecoderFfmpegManagedFormatConversionBoundary _conversion = new();
    private int _isDisposed;

    internal PlaybackDispatcherBackedSchedulerDeliveryOperations(
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        IPlaybackWindowsOutputDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _timing = timing;
        _output = output;
        _dispatcher = dispatcher;
    }

    public bool ValidateContextRole(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        if (Volatile.Read(ref _isDisposed) != 0 || nativeSession.IsDisposed ||
            !Enum.IsDefined(mediaKind))
        {
            return false;
        }

        try
        {
            var context = mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? nativeSession.VideoContext
                : nativeSession.AudioContext;
            var expectedCodec = mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackDecoderFfmpegCodecId.H264
                : PlaybackDecoderFfmpegCodecId.Aac;
            return !context.IsDisposed && context.CodecId == expectedCodec;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken) =>
        _timing.DecideAsync(item, cancellationToken);

    public ValueTask<PlaybackAvTimingResetResult> ResetTimingAsync(
        long generation,
        CancellationToken cancellationToken) =>
        _timing.ResetAsync(generation, generation, cancellationToken);

    public async ValueTask<PlaybackGenerationMatchedOutputResetResult> ResetOutputAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        var audio = await InvokeResetAsync(video: false, generation, cancellationToken)
            .ConfigureAwait(false);
        if (!IsReset(audio))
        {
            return MapReset(audio);
        }

        var video = await InvokeResetAsync(video: true, generation, cancellationToken)
            .ConfigureAwait(false);
        return IsReset(video)
            ? PlaybackGenerationMatchedOutputResetResult.Reset
            : PlaybackGenerationMatchedOutputResetResult.PartialResetFault;
    }

    public async ValueTask<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliverAsync(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        long generation,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed;
        }

        PlaybackDecoderFfmpegContextLease context;
        try
        {
            context = frame.Metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video
                ? nativeSession.VideoContext
                : nativeSession.AudioContext;
        }
        catch (ObjectDisposedException)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable;
        }

        if (context.IsDisposed)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable;
        }

        var consumer = new OutputConsumer(_output, _dispatcher, generation);
        try
        {
            var adapted = await _adapter.AdaptAsync(
                context,
                frame,
                sequence,
                discontinuity,
                consumer,
                cancellationToken).ConfigureAwait(false);
            if (adapted == PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConversionRequired)
            {
                var converted = await _conversion.ConvertAsync(
                    context,
                    frame,
                    sequence,
                    discontinuity,
                    consumer,
                    cancellationToken).ConfigureAwait(false);
                return Map(converted, consumer);
            }

            return Map(adapted, consumer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled;
        }
        catch
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _timing.Dispose();
        }
    }

    private async ValueTask<PlaybackWindowsOutputBufferResetResult> InvokeResetAsync(
        bool video,
        long generation,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackWindowsOutputBufferResetResult.Disposed;
        }

        PlaybackWindowsOutputDispatcherInvocation<PlaybackWindowsOutputBufferResetResult> invocation;
        try
        {
            invocation = await _dispatcher.InvokeAsync(
                async token =>
                {
                    if (!_dispatcher.HasAccess)
                    {
                        return PlaybackWindowsOutputBufferResetResult.Failure;
                    }

                    return video
                        ? await _output.ResetVideoAsync(generation, token).ConfigureAwait(false)
                        : await _output.ResetAudioAsync(generation, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackWindowsOutputBufferResetResult.Cancelled;
        }
        catch
        {
            return PlaybackWindowsOutputBufferResetResult.Failure;
        }

        return invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                PlaybackWindowsOutputBufferResetResult.Cancelled,
            PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                PlaybackWindowsOutputBufferResetResult.Disposed,
            _ => PlaybackWindowsOutputBufferResetResult.Failure,
        };
    }

    private static bool IsReset(PlaybackWindowsOutputBufferResetResult result) =>
        result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;

    private static PlaybackGenerationMatchedOutputResetResult MapReset(
        PlaybackWindowsOutputBufferResetResult result) => result switch
        {
            PlaybackWindowsOutputBufferResetResult.Cancelled =>
                PlaybackGenerationMatchedOutputResetResult.Cancelled,
            PlaybackWindowsOutputBufferResetResult.Disposed =>
                PlaybackGenerationMatchedOutputResetResult.Disposed,
            _ => PlaybackGenerationMatchedOutputResetResult.Failure,
        };

    private static PlaybackGenerationMatchedCanonicalDeliveryResult Map(
        PlaybackDecoderFfmpegDecodedOutputAdapterResult result,
        OutputConsumer consumer) => result switch
        {
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConsumerRejected when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ContextUnavailable =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.DisposedFrame or
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame or
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
            _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
        };

    private static PlaybackGenerationMatchedCanonicalDeliveryResult Map(
        PlaybackDecoderFfmpegManagedFormatConversionResult result,
        OutputConsumer consumer) => result switch
        {
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConsumerRejected when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ContextUnavailable =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegManagedFormatConversionResult.DisposedFrame or
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame or
            PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
            PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed or
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConversionFailed =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ConversionFailed,
            _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
        };

    private sealed class OutputConsumer(
        PlaybackWindowsOutputBufferProcessingBoundary output,
        IPlaybackWindowsOutputDispatcher dispatcher,
        long generation) : IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer
    {
        public bool WasInvoked { get; private set; }

        public PlaybackGenerationMatchedCanonicalDeliveryResult Result { get; private set; } =
            PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;

        public async ValueTask<bool> ConsumeAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            PlaybackWindowsOutputDispatcherInvocation<PlaybackWindowsOutputBufferProcessingResult>
                invocation;
            try
            {
                invocation = await dispatcher.InvokeAsync(
                    async token =>
                    {
                        if (!dispatcher.HasAccess)
                        {
                            return PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment;
                        }

                        return await output.ProcessAudioAsync(
                            new(
                                frame.SampleFormat,
                                frame.SampleRate,
                                frame.ChannelCount,
                                frame.SampleCount,
                                frame.Payload,
                                generation),
                            token).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Result = PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled;
                return false;
            }
            catch
            {
                Result = PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;
                return false;
            }

            Result = MapInvocation(invocation, video: false);
            return Result == PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed;
        }

        public async ValueTask<bool> ConsumeVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            PlaybackWindowsOutputDispatcherInvocation<PlaybackWindowsOutputBufferProcessingResult>
                invocation;
            try
            {
                invocation = await dispatcher.InvokeAsync(
                    async token =>
                    {
                        if (!dispatcher.HasAccess)
                        {
                            return PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment;
                        }

                        return await output.ProcessVideoAsync(
                            new(
                                frame.PixelFormat,
                                frame.Width,
                                frame.Height,
                                frame.Stride,
                                frame.Payload,
                                generation),
                            token).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Result = PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled;
                return false;
            }
            catch
            {
                Result = PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;
                return false;
            }

            Result = MapInvocation(invocation, video: true);
            return Result == PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed;
        }

        private static PlaybackGenerationMatchedCanonicalDeliveryResult MapInvocation(
            PlaybackWindowsOutputDispatcherInvocation<PlaybackWindowsOutputBufferProcessingResult>
                invocation,
            bool video) => invocation.Result switch
            {
                PlaybackWindowsOutputDispatcherInvocationResult.Completed =>
                    MapOutput(invocation.Value, video),
                PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled,
                PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed,
                _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
            };

        private static PlaybackGenerationMatchedCanonicalDeliveryResult MapOutput(
            PlaybackWindowsOutputBufferProcessingResult result,
            bool video) => result switch
            {
                PlaybackWindowsOutputBufferProcessingResult.Processed => video
                    ? PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed
                    : PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed,
                PlaybackWindowsOutputBufferProcessingResult.WouldBlock =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.WouldBlock,
                PlaybackWindowsOutputBufferProcessingResult.InitializationRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.InitializationRequired,
                PlaybackWindowsOutputBufferProcessingResult.BindingRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.BindingRequired,
                PlaybackWindowsOutputBufferProcessingResult.ResizeRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.ResizeRequired,
                PlaybackWindowsOutputBufferProcessingResult.DeviceLost =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.DeviceLost,
                PlaybackWindowsOutputBufferProcessingResult.Cancelled =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled,
                PlaybackWindowsOutputBufferProcessingResult.Disposed =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed,
                PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported or
                PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration or
                PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
                _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
            };
    }
}
