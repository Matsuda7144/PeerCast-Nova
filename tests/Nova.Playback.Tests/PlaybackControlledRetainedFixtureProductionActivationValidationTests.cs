using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;
using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

#pragma warning disable CA1861 // Local order assertions are part of each finite ownership proof.

public sealed class PlaybackControlledRetainedFixtureProductionActivationValidationTests
{
    [Fact]
    public async Task Controlled_fixture_reaches_ready_and_retains_then_delivers_one_frame()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new ControlledOperations(dispatcher);
        await using var lifecycle = CreateLifecycle(operations, dispatcher);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized,
            await lifecycle.InitializeAsync());
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            await lifecycle.ConsumeAsync(CreatePacket(0, BuildBootstrap())));

        var generation = Assert.Single(operations.Creator.Generations);
        Assert.Equal(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            lifecycle.Snapshot.State);
        Assert.Equal(0, lifecycle.Snapshot.OutputReadyGeneration);
        Assert.Equal(320, lifecycle.Snapshot.VideoWidth);
        Assert.Equal(240, lifecycle.Snapshot.VideoHeight);
        Assert.Equal(48_000, lifecycle.Snapshot.AudioSampleRate);
        Assert.Equal(2, lifecycle.Snapshot.AudioChannelCount);
        Assert.Equal(new[] { "configure:initial:0" }, operations.ConfigurationLog);

        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Audio)));
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            await lifecycle.ConsumeAsync(CreatePacket(
                1,
                Concat(BuildVideoNaluTag(
                    timestamp: 100,
                    compositionTime: -20,
                    keyframe: true,
                    payload: [0, 0, 0, 2, 0x65, 0x88]),
                    BuildAudioRawTag(90, [0x11, 0x22])))));

        Assert.Equal(2, generation.PacketOperations.SubmitCount);
        Assert.Equal(1, generation.PacketOperations.RetryCount);
        Assert.Equal(1, generation.Root.TransferCount);
        Assert.Equal(1, generation.Root.RetainedFrameCount);
        Assert.Equal(1, operations.ProductionSnapshot.TransferredFrameCount);

        generation.Root.DeliveryResults.Enqueue(
            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock);
        generation.Root.DeliveryResults.Enqueue(
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
            await lifecycle.DeliverOneAsync());
        Assert.Equal(1, generation.Root.RetainedFrameCount);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
            await lifecycle.DeliverOneAsync());
        Assert.Equal(0, generation.Root.RetainedFrameCount);
        Assert.Equal(1, operations.ProductionSnapshot.DeliveredFrameCount);
        Assert.All(operations.DispatcherAccess, Assert.True);
    }

    [Fact]
    public async Task Controlled_discontinuity_stops_old_generation_and_reconfigures_output()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new ControlledOperations(dispatcher);
        await using var lifecycle = CreateLifecycle(operations, dispatcher);
        await lifecycle.InitializeAsync();
        await lifecycle.ConsumeAsync(CreatePacket(0, BuildBootstrap()));
        var first = Assert.Single(operations.Creator.Generations);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            await lifecycle.ConsumeAsync(
                CreatePacket(1, BuildBootstrap(), discontinuity: true)));

        Assert.Equal(2, operations.Creator.Generations.Count);
        var second = operations.Creator.Generations[1];
        Assert.True(first.Root.IsDisposed);
        Assert.Equal(1, second.Generation.DecoderGeneration);
        Assert.Equal(1, second.Generation.DiscontinuityGeneration);
        Assert.True(
            operations.Log.IndexOf("stop:end:0") <
            operations.Log.IndexOf("create:1:1"));
        Assert.Equal(
            new[] { "configure:initial:0", "configure:replacement:1" },
            operations.ConfigurationLog);
        Assert.Equal(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            lifecycle.Snapshot.State);
        Assert.Equal(1, lifecycle.Snapshot.OutputReadyGeneration);

        await lifecycle.StopAsync();

        Assert.True(second.Root.IsDisposed);
        Assert.True(
            operations.Log.IndexOf("production:stop") <
            operations.Log.IndexOf("output:dispose"));
        Assert.False(dispatcher.IsDisposed);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary CreateLifecycle(
        ControlledOperations operations,
        RecordingDispatcher dispatcher) =>
        new(
            ChannelId,
            SessionId,
            "C:\\verified-runtime",
            PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate,
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowNativeCode = true,
                AllowBundled = false,
            },
            dispatcher,
            windowToken: 7144,
            new PlaybackApprovedExternalRuntimeWindowsLifecycleOptions(),
            new StopwatchPlaybackMonotonicClock(),
            operations);

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(static value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result.AsSpan(offset));
            offset += value.Length;
        }

        return result;
    }

    private sealed class RecordingDispatcher : IPlaybackWindowsOutputDispatcher
    {
        private bool _hasAccess;

        public bool HasAccess => _hasAccess;

        public bool IsDisposed { get; private set; }

        public int InvocationCount { get; private set; }

        public async ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> InvokeAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Shutdown, default!);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Cancelled, default!);
            }

            InvocationCount++;
            _hasAccess = true;
            try
            {
                return new(
                    PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                    await operation(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Cancelled, default!);
            }
            catch
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Faulted, default!);
            }
            finally
            {
                _hasAccess = false;
            }
        }
    }

    private sealed class ControlledOperations :
        IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations
    {
        private readonly RecordingDispatcher _dispatcher;
        private readonly OutputHarness _output = new();
        private PlaybackProductionPcpFlvCompositionBoundary? _production;
        private PlaybackWindowsOutputProbeResult _probe =
            PlaybackWindowsOutputProbeResult.Failure;
        private PlaybackWindowsOutputActivationResult _activation =
            PlaybackWindowsOutputActivationResult.Failure;
        private PlaybackWindowsOutputBindingSnapshot _binding;
        private PlaybackWindowsOutputStreamSnapshot _streams;
        private PlaybackWindowsOutputBufferSnapshot _buffers;
        private int _outputDisposed;

        public ControlledOperations(RecordingDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Creator = new ControlledCreator(Log);
        }

        public ControlledCreator Creator { get; }

        public List<string> Log { get; } = [];

        public List<string> ConfigurationLog { get; } = [];

        public List<bool> DispatcherAccess { get; } = [];

        public PlaybackProductionPcpFlvCompositionSnapshot ProductionSnapshot =>
            _production?.Snapshot ?? throw new InvalidOperationException("Production is unavailable.");

        public PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot Snapshot => new(
            _probe,
            _activation,
            _binding,
            _streams,
            _buffers,
            _production?.Snapshot);

        public PlaybackWindowsOutputProbeReport Probe(
            PlaybackDecodedVideoPixelFormat videoFormat,
            PlaybackDecodedAudioSampleFormat audioFormat)
        {
            Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, videoFormat);
            Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, audioFormat);
            _probe = PlaybackWindowsOutputProbeResult.Available;
            Log.Add("probe");
            return new(_probe, PlaybackWindowsOutputAdapterSelection.Windows11Default);
        }

        public async ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
            CancellationToken cancellationToken)
        {
            var invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(_dispatcher.HasAccess);
                    Log.Add("activation");
                    return ValueTask.FromResult(
                        PlaybackWindowsOutputActivationResult.Activated);
                },
                cancellationToken);
            _activation = invocation.Result ==
                PlaybackWindowsOutputDispatcherInvocationResult.Completed
                ? invocation.Value
                : PlaybackWindowsOutputActivationResult.Failure;
            return _activation;
        }

        public void CreateProduction()
        {
            Log.Add("production:create");
            _production = new(
                ChannelId,
                SessionId,
                "C:\\verified-runtime",
                _output.Output,
                Creator);
        }

        public async ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
            PeerCast.Nova.Network.Pcp.PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            var production = GetProduction();
            await production.ConsumeAsync(packet, cancellationToken);
            return production.Snapshot.Result;
        }

        public async ValueTask<PlaybackProductionPcpFlvCompositionResult> CompleteInputAsync(
            CancellationToken cancellationToken)
        {
            var production = GetProduction();
            await production.CompleteInputAsync(cancellationToken);
            return production.Snapshot.Result;
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            GetProduction().ReceiveDecodedAsync(mediaKind, cancellationToken);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            GetProduction().DrainDecodedAsync(mediaKind, cancellationToken);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
            CancellationToken cancellationToken) =>
            GetProduction().DeliverOneAsync(cancellationToken);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
            CancellationToken cancellationToken) =>
            GetProduction().ResetAsync(cancellationToken);

        public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ConfigureOutputAsync(
                PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
                bool replacement,
                CancellationToken cancellationToken)
        {
            var invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(_dispatcher.HasAccess);
                    ConfigurationLog.Add(
                        $"configure:{(replacement ? "replacement" : "initial")}:{configuration.Generation}");
                    _binding = new(
                        PlaybackWindowsOutputBindingState.Bound,
                        PlaybackWindowsOutputBindingState.Bound,
                        configuration.Generation,
                        configuration.Generation);
                    _streams = new(
                        PlaybackWindowsOutputStreamState.Initialized,
                        PlaybackWindowsOutputStreamState.Initialized,
                        configuration.Generation,
                        configuration.Generation);
                    _buffers = new(
                        PlaybackWindowsOutputBufferState.Ready,
                        PlaybackWindowsOutputBufferState.Ready,
                        configuration.Generation,
                        configuration.Generation,
                        AudioStarted: false);
                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
                },
                cancellationToken);
            return invocation.Result switch
            {
                PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
                PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                    PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
                PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                    PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
                _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
            };
        }

        public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ResizeVideoAsync(
                long windowToken,
                int width,
                int height,
                PlaybackDecodedVideoPixelFormat format,
                long generation,
                CancellationToken cancellationToken)
        {
            var invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(_dispatcher.HasAccess);
                    Assert.Equal(7144, windowToken);
                    Assert.True(width > 0);
                    Assert.True(height > 0);
                    Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, format);
                    Assert.Equal(_streams.VideoGeneration, generation);
                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
                },
                cancellationToken);
            return invocation.Result == PlaybackWindowsOutputDispatcherInvocationResult.Completed
                ? invocation.Value
                : PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        public async ValueTask StopProductionAsync()
        {
            var production = _production;
            _production = null;
            if (production is not null)
            {
                Log.Add("production:stop");
                await production.StopAsync();
                await production.DisposeAsync();
            }
        }

        public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            DisposeOutputAsync()
        {
            if (Interlocked.Exchange(ref _outputDisposed, 1) != 0)
            {
                return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured;
            }

            var invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(_dispatcher.HasAccess);
                    Log.Add("output:dispose");
                    _output.Dispose();
                    _binding = new(
                        PlaybackWindowsOutputBindingState.Disposed,
                        PlaybackWindowsOutputBindingState.Disposed,
                        _binding.VideoGeneration,
                        _binding.AudioGeneration);
                    _streams = new(
                        PlaybackWindowsOutputStreamState.Disposed,
                        PlaybackWindowsOutputStreamState.Disposed,
                        _streams.VideoGeneration,
                        _streams.AudioGeneration);
                    _buffers = new(
                        PlaybackWindowsOutputBufferState.Disposed,
                        PlaybackWindowsOutputBufferState.Disposed,
                        _buffers.VideoGeneration,
                        _buffers.AudioGeneration,
                        AudioStarted: false);
                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
                });
            return invocation.Result == PlaybackWindowsOutputDispatcherInvocationResult.Completed
                ? invocation.Value
                : PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        public async ValueTask DisposeAsync()
        {
            await StopProductionAsync();
            await DisposeOutputAsync();
        }

        private PlaybackProductionPcpFlvCompositionBoundary GetProduction() =>
            _production ?? throw new InvalidOperationException("Production is unavailable.");
    }

    private sealed class ControlledCreator(List<string> log) :
        IPlaybackProductionDecoderGenerationCreator
    {
        public List<GenerationHarness> Generations { get; } = [];

        public bool IsDisposed { get; private set; }

        public ValueTask<PlaybackProductionDecoderGenerationCreation> CreateAsync(
            string runtimeDirectory,
            PlaybackBoundedDemuxSession? sourceSession,
            PlaybackWindowsOutputBufferProcessingBoundary? output,
            int decoderGeneration,
            int discontinuityGeneration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("C:\\verified-runtime", runtimeDirectory);
            Assert.NotNull(output);
            var source = Assert.IsType<PlaybackBoundedDemuxSession>(sourceSession);
            log.Add($"create:{decoderGeneration}:{discontinuityGeneration}");
            var packetOperations = new FakePacketOperations(log);
            var receiveOperations = new FakeReceiveOperations();
            var nativeSession = new PlaybackBoundedFlvNativeDecoderSession(
                source,
                source.Video.StreamId,
                source.Audio.StreamId,
                source.Video.TimeBase,
                source.Audio.TimeBase,
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264, "video", log),
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac, "audio", log));
            var decode = new PlaybackSourceBoundOneFrameDecodeBoundary(
                source,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration,
                options: null,
                packetOperations,
                receiveOperations);
            var root = new ControlledRoot(
                source,
                decoderGeneration,
                discontinuityGeneration,
                decode,
                log);
            var generation = new PlaybackProductionDecoderGeneration(
                source,
                decoderGeneration,
                discontinuityGeneration,
                root);
            Generations.Add(new(
                source,
                packetOperations,
                receiveOperations,
                root,
                generation));
            return ValueTask.FromResult(new PlaybackProductionDecoderGenerationCreation(
                PlaybackProductionDecoderGenerationFactoryResult.Created,
                generation));
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                log.Add("factory");
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record GenerationHarness(
        PlaybackBoundedDemuxSession Source,
        FakePacketOperations PacketOperations,
        FakeReceiveOperations ReceiveOperations,
        ControlledRoot Root,
        PlaybackProductionDecoderGeneration Generation);

    private sealed class ControlledRoot :
        IPlaybackProductionDecoderGenerationRoot,
        IPlaybackProductionDecoderGenerationIngressForwarder
    {
        private readonly PlaybackSourceBoundOneFrameDecodeBoundary _decode;
        private readonly List<string> _log;
        private readonly List<(PlaybackDecoderFfmpegManagedFrame Frame,
            PlaybackBoundedDemuxMediaKind MediaKind)> _retained = [];
        private long _nextVideoSequence;
        private long _nextAudioSequence;
        private long _nextSchedulerSequence;
        private long _transferCount;
        private bool _stopping;

        public ControlledRoot(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            int discontinuityGeneration,
            PlaybackSourceBoundOneFrameDecodeBoundary decode,
            List<string> log)
        {
            SourceSession = sourceSession;
            DecoderGeneration = decoderGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
            _decode = decode;
            _log = log;
        }

        public PlaybackBoundedDemuxSession SourceSession { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration { get; }

        public bool IsDisposed { get; private set; }

        public int TransferCount => checked((int)_transferCount);

        public int RetainedFrameCount => _retained.Count;

        public Queue<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliveryResults { get; } = [];

        public PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshot =>
            IsDisposed ? null : _decode.Snapshot;

        public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
        {
            get
            {
                var ingress = new PlaybackDecodedCompletionSchedulerIngressSnapshot(
                    IsDisposed
                        ? PlaybackDecodedCompletionSchedulerIngressState.Disposed
                        : PlaybackDecodedCompletionSchedulerIngressState.Accepting,
                    IsDisposed
                        ? PlaybackDecodedCompletionSchedulerIngressResult.Disposed
                        : PlaybackDecodedCompletionSchedulerIngressResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    _nextVideoSequence,
                    _nextAudioSequence,
                    _nextSchedulerSequence,
                    _transferCount,
                    DiscontinuityGeneration > 0 && _transferCount == 0,
                    new(
                        _retained.Count,
                        0,
                        0,
                        _retained.Sum(static item => ManagedBytes(item.Frame))));
                return new(
                    IsDisposed
                        ? PlaybackGenerationMatchedSchedulerDeliveryState.Disposed
                        : PlaybackGenerationMatchedSchedulerDeliveryState.Accepting,
                    IsDisposed
                        ? PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed
                        : PlaybackGenerationMatchedSchedulerDeliveryResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    true,
                    DiscontinuityGeneration > 0 && _transferCount == 0,
                    false,
                    0,
                    false,
                    ingress);
            }
        }

        public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
            ReferenceEquals(SourceSession, sourceSession);

        public PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
            PlaybackBoundedDemuxReadCompletion? sourceCompletion,
            CancellationToken cancellationToken) =>
            IsDisposed
                ? PlaybackFiniteOneEventPacketSubmissionResult.Disposed
                : _decode.BeginSubmission(sourceCompletion, cancellationToken);

        public PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
            PlaybackBoundedDemuxMediaKind mediaKind,
            int streamId,
            PlaybackSourceBoundOneFrameDecodeMode mode,
            CancellationToken cancellationToken,
            PlaybackSourceBoundOneFrameDecodeBudget budget) =>
            IsDisposed
                ? new(PlaybackSourceBoundOneFrameDecodeResult.Disposed, null, default)
                : _decode.ReceiveOne(
                    SourceSession,
                    DecoderGeneration,
                    mediaKind,
                    streamId,
                    mode,
                    cancellationToken,
                    budget);

        public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
            PlaybackSourceBoundDecodedFrameCompletion? completion,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                completion?.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PlaybackDecodedCompletionSchedulerIngressResult.Cancelled;
            }

            if (completion is null)
            {
                return PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion;
            }

            var mediaKind = completion.MediaKind;
            var sequence = completion.Sequence;
            if (!completion.TryDetachFrame(out var frame) || frame is null)
            {
                completion.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion;
            }

            completion.Dispose();
            _retained.Add((frame, mediaKind));
            _transferCount++;
            _nextSchedulerSequence++;
            if (mediaKind == PlaybackBoundedDemuxMediaKind.Video)
            {
                _nextVideoSequence = checked(sequence + 1);
            }
            else
            {
                _nextAudioSequence = checked(sequence + 1);
            }

            return PlaybackDecodedCompletionSchedulerIngressResult.Transferred;
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(
                    PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
            }

            var result = DeliveryResults.Count == 0
                ? PlaybackGenerationMatchedSchedulerDeliveryResult.Empty
                : DeliveryResults.Dequeue();
            if (result is PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered or
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered or
                PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped)
            {
                Assert.NotEmpty(_retained);
                var item = _retained[0];
                _retained.RemoveAt(0);
                item.Frame.Dispose();
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerResetResult.Disposed
                : cancellationToken.IsCancellationRequested
                    ? PlaybackGenerationMatchedSchedulerResetResult.Cancelled
                    : PlaybackGenerationMatchedSchedulerResetResult.Reset);

        public ValueTask StopAsync()
        {
            if (IsDisposed || _stopping)
            {
                return ValueTask.CompletedTask;
            }

            _stopping = true;
            _log.Add($"stop:start:{DecoderGeneration}");
            DisposeCore();
            _log.Add($"stop:end:{DecoderGeneration}");
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            GC.SuppressFinalize(this);
        }

        private void DisposeCore()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            foreach (var item in _retained)
            {
                item.Frame.Dispose();
            }

            _retained.Clear();
            _decode.Dispose();
        }

        private static long ManagedBytes(PlaybackDecoderFfmpegManagedFrame frame) =>
            frame.Planes.Sum(static plane => (long)plane.Buffer.Length);
    }

    private sealed class FakeContextOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        string name,
        List<string> log) : IPlaybackBoundedFlvNativeContextOwner
    {
        private int _isDisposed;

        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add(name);
            }
        }
    }

    private sealed class OutputHarness : IDisposable
    {
        public OutputHarness()
        {
            Activation = new PlaybackWindowsOutputActivationBoundary();
            Binding = new PlaybackWindowsOutputResourceBindingBoundary(Activation);
            Initialization = new PlaybackWindowsOutputStreamInitializationBoundary(
                Activation,
                Binding);
            Output = new PlaybackWindowsOutputBufferProcessingBoundary(
                Activation,
                Binding,
                Initialization);
        }

        public PlaybackWindowsOutputActivationBoundary Activation { get; }

        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; }

        public PlaybackWindowsOutputStreamInitializationBoundary Initialization { get; }

        public PlaybackWindowsOutputBufferProcessingBoundary Output { get; }

        public void Dispose()
        {
            Output.Dispose();
            Initialization.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }
}
