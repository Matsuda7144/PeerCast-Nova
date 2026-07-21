using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleBoundaryTests
{
    [Fact]
    public async Task Policy_rejection_precedes_probe_and_dispatcher_access()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher);
        await using var boundary = CreateBoundary(
            operations,
            dispatcher,
            ApprovedPolicy() with { AllowThirdParty = false });

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PolicyRejected,
            await boundary.InitializeAsync());
        Assert.Empty(operations.Log);
        Assert.Equal(0, dispatcher.InvocationCount);
    }

    [Fact]
    public async Task Initialization_is_probe_then_owner_thread_activation_then_production()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher);
        await using var boundary = CreateBoundary(operations, dispatcher);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized,
            await boundary.InitializeAsync());

        Assert.Equal(new[] { "probe", "activation", "production:create" }, operations.Log);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.WaitingForStreamDescription,
            boundary.Snapshot.State);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.All(operations.DispatcherAccess, Assert.True);
    }

    [Fact]
    public async Task Delivery_is_rejected_until_exact_stream_description_is_configured()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher);
        await using var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigurationRequired,
            await boundary.DeliverOneAsync());
        Assert.Equal(0, operations.DeliveryCount);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.WaitingForStreamDescription,
            boundary.Snapshot.State);
    }

    [Fact]
    public async Task First_description_binds_and_initializes_before_delivery()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher)
        {
            PublishDescriptionOnConsume = true,
        };
        operations.DeliveryResults.Enqueue(
            PlaybackProductionPcpFlvCompositionResult.VideoDelivered);
        await using var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap())));
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            boundary.Snapshot.State);
        Assert.Equal(0, boundary.Snapshot.OutputReadyGeneration);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered,
            await boundary.DeliverOneAsync());

        Assert.True(operations.Log.IndexOf("bind:video:0") <
            operations.Log.IndexOf("initialize:video:0"));
        Assert.True(operations.Log.IndexOf("initialize:audio:0") <
            operations.Log.IndexOf("deliver"));
        Assert.All(operations.DispatcherAccess, Assert.True);
    }

    [Fact]
    public async Task Generation_replacement_uses_fixed_reverse_reset_then_forward_reconfigure_order()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher)
        {
            PublishDescriptionOnConsume = true,
        };
        await using var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()));
        operations.PublishGeneration = 1;
        operations.Log.Clear();

        await boundary.ConsumeAsync(CreatePacket(1, BuildBootstrap(), discontinuity: true));

        Assert.Equal(
            new[]
            {
                "consume",
                "buffer:reset:audio:1",
                "buffer:reset:video:1",
                "stream:reset:audio:1",
                "stream:reset:video:1",
                "binding:reset:1",
                "bind:video:1",
                "bind:audio:1",
                "initialize:video:1",
                "initialize:audio:1",
            },
            operations.Log);
        Assert.Equal(1, boundary.Snapshot.OutputReadyGeneration);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready,
            boundary.Snapshot.State);
    }

    [Fact]
    public async Task Retained_resize_requires_explicit_resize_and_does_not_retry_delivery()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher)
        {
            PublishDescriptionOnConsume = true,
        };
        operations.DeliveryResults.Enqueue(
            PlaybackProductionPcpFlvCompositionResult.RetainedResizeRequired);
        operations.DeliveryResults.Enqueue(
            PlaybackProductionPcpFlvCompositionResult.VideoDelivered);
        await using var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()));

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedResizeRequired,
            await boundary.DeliverOneAsync());
        Assert.Equal(1, operations.DeliveryCount);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.ResizeRequired,
            boundary.Snapshot.State);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Resized,
            await boundary.ResizeVideoAsync(1280, 720));
        Assert.Equal(1, operations.DeliveryCount);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered,
            await boundary.DeliverOneAsync());
        Assert.Equal(2, operations.DeliveryCount);
    }

    [Fact]
    public async Task Device_loss_is_terminal_and_stops_production_before_output_teardown()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher)
        {
            PublishDescriptionOnConsume = true,
            LoseDeviceOnDelivery = true,
        };
        await using var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()));
        operations.Log.Clear();

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DeviceLost,
            await boundary.DeliverOneAsync());
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.DeviceLost,
            boundary.Snapshot.State);
        Assert.Equal(
            new[]
            {
                "deliver",
                "production:stop",
                "port:dispose",
                "buffer:dispose",
                "stream:dispose",
                "binding:dispose",
                "activation:dispose",
            },
            operations.Log);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DeviceLost,
            await boundary.DeliverOneAsync());
    }

    [Fact]
    public async Task Stop_is_idempotent_and_preserves_borrowed_dispatcher_and_window()
    {
        var dispatcher = new RecordingDispatcher();
        var operations = new FakeOperations(dispatcher);
        var boundary = CreateBoundary(operations, dispatcher);
        await boundary.InitializeAsync();

        await boundary.StopAsync();
        await boundary.StopAsync();
        await boundary.DisposeAsync();

        Assert.Equal(1, operations.StopCount);
        Assert.Equal(1, operations.OutputDisposeCount);
        Assert.False(dispatcher.IsDisposed);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            boundary.Snapshot.State);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary CreateBoundary(
        FakeOperations operations,
        RecordingDispatcher dispatcher,
        PlaybackDecoderBackendPolicy? policy = null) =>
        new(
            ChannelId,
            SessionId,
            "C:\\verified-runtime",
            PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate,
            policy ?? ApprovedPolicy(),
            dispatcher,
            windowToken: 7144,
            new PlaybackApprovedExternalRuntimeWindowsLifecycleOptions(),
            new StopwatchPlaybackMonotonicClock(),
            operations);

    private static PlaybackDecoderBackendPolicy ApprovedPolicy() => new()
    {
        AllowThirdParty = true,
        AllowNativeCode = true,
        AllowBundled = false,
    };

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

            InvocationCount++;
            if (cancellationToken.IsCancellationRequested)
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Cancelled, default!);
            }

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

    private sealed class FakeOperations(RecordingDispatcher dispatcher) :
        IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations
    {
        private PlaybackWindowsOutputProbeResult _probe =
            PlaybackWindowsOutputProbeResult.Failure;
        private PlaybackWindowsOutputActivationResult _activation =
            PlaybackWindowsOutputActivationResult.Failure;
        private PlaybackWindowsOutputBindingSnapshot _binding;
        private PlaybackWindowsOutputStreamSnapshot _streams;
        private PlaybackWindowsOutputBufferSnapshot _buffers;
        private PlaybackProductionPcpFlvCompositionSnapshot? _production;
        private bool _productionStopped;
        private bool _outputDisposed;

        public bool PublishDescriptionOnConsume { get; init; }

        public int PublishGeneration { get; set; }

        public bool LoseDeviceOnDelivery { get; init; }

        public int DeliveryCount { get; private set; }

        public int StopCount { get; private set; }

        public int OutputDisposeCount { get; private set; }

        public List<string> Log { get; } = [];

        public List<bool> DispatcherAccess { get; } = [];

        public Queue<PlaybackProductionPcpFlvCompositionResult> DeliveryResults { get; } = [];

        public PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot Snapshot => new(
            _probe,
            _activation,
            _binding,
            _streams,
            _buffers,
            _production);

        public PlaybackWindowsOutputProbeReport Probe(
            PlaybackDecodedVideoPixelFormat videoFormat,
            PlaybackDecodedAudioSampleFormat audioFormat)
        {
            Log.Add("probe");
            _probe = PlaybackWindowsOutputProbeResult.Available;
            return new(_probe, PlaybackWindowsOutputAdapterSelection.Windows11Default);
        }

        public async ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
            CancellationToken cancellationToken)
        {
            var invocation = await dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(dispatcher.HasAccess);
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
            _production = Production(hasDescription: false, generation: 0);
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Add("consume");
            _production = Production(PublishDescriptionOnConsume, PublishGeneration);
            return ValueTask.FromResult(
                PlaybackProductionPcpFlvCompositionResult.PacketAccepted);
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> CompleteInputAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                PlaybackProductionPcpFlvCompositionResult.EncodedInputFinalized);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.FrameTransferred);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeliveryCount++;
            Log.Add("deliver");
            if (LoseDeviceOnDelivery)
            {
                _buffers = _buffers with
                {
                    VideoState = PlaybackWindowsOutputBufferState.DeviceLost,
                };
            }

            return ValueTask.FromResult(DeliveryResults.Count == 0
                ? PlaybackProductionPcpFlvCompositionResult.Empty
                : DeliveryResults.Dequeue());
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.Reset);

        public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ConfigureOutputAsync(
                PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
                bool replacement,
                CancellationToken cancellationToken)
        {
            var invocation = await dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(dispatcher.HasAccess);
                    if (replacement)
                    {
                        Log.Add($"buffer:reset:audio:{configuration.Generation}");
                        Log.Add($"buffer:reset:video:{configuration.Generation}");
                        Log.Add($"stream:reset:audio:{configuration.Generation}");
                        Log.Add($"stream:reset:video:{configuration.Generation}");
                        Log.Add($"binding:reset:{configuration.Generation}");
                    }

                    Log.Add($"bind:video:{configuration.Generation}");
                    Log.Add($"bind:audio:{configuration.Generation}");
                    Log.Add($"initialize:video:{configuration.Generation}");
                    Log.Add($"initialize:audio:{configuration.Generation}");
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
            var invocation = await dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(dispatcher.HasAccess);
                    Log.Add($"resize:{width}x{height}:{generation}");
                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
                },
                cancellationToken);
            return invocation.Result == PlaybackWindowsOutputDispatcherInvocationResult.Completed
                ? invocation.Value
                : PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        public ValueTask StopProductionAsync()
        {
            if (!_productionStopped)
            {
                _productionStopped = true;
                StopCount++;
                Log.Add("production:stop");
                Log.Add("port:dispose");
                _production = null;
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            DisposeOutputAsync()
        {
            if (_outputDisposed)
            {
                return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured;
            }

            _outputDisposed = true;
            OutputDisposeCount++;
            var invocation = await dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    DispatcherAccess.Add(dispatcher.HasAccess);
                    Log.Add("buffer:dispose");
                    Log.Add("stream:dispose");
                    Log.Add("binding:dispose");
                    Log.Add("activation:dispose");
                    _buffers = _buffers with
                    {
                        VideoState = PlaybackWindowsOutputBufferState.Disposed,
                        AudioState = PlaybackWindowsOutputBufferState.Disposed,
                    };
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

        private static PlaybackProductionPcpFlvCompositionSnapshot Production(
            bool hasDescription,
            int generation) => new(
                State: PlaybackProductionPcpFlvCompositionState.Accepting,
                Result: PlaybackProductionPcpFlvCompositionResult.PacketAccepted,
                AcceptedPacketCount: 0,
                AcceptedPayloadByteCount: 0,
                ParserOperationCount: 0,
                DecodeOperationCount: 0,
                TransferredFrameCount: 0,
                DeliveredFrameCount: 0,
                DroppedFrameCount: 0,
                ParserGeneration: generation,
                DecoderGeneration: generation,
                HasDecoderGeneration: hasDescription,
                HasActiveOperation: false,
                HasActiveDecodedCompletion: false,
                HasActiveSubmission: false,
                HasReliefRetry: false,
                VideoFlushAccepted: false,
                AudioFlushAccepted: false,
                VideoEndOfStream: false,
                AudioEndOfStream: false,
                VideoWidth: hasDescription ? 1920 : 0,
                VideoHeight: hasDescription ? 1080 : 0,
                AudioSampleRate: hasDescription ? 48_000 : 0,
                AudioChannelCount: hasDescription ? 2 : 0);
    }
}
