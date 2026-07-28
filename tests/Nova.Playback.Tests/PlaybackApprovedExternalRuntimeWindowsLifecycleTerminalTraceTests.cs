using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleTerminalTraceTests
{
    [Fact]
    public async Task Rejected_delivery_retains_trace_after_cleanup_clears_production()
    {
        var dispatcher = new InlineDispatcher();
        var expected = Trace(sequence: 7);
        var operations = new RejectingOperations(expected);
        await using var boundary = new PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary(
            new ChannelId(Guid.ParseExact("71440000000000000000000000000001", "N")),
            new ChannelSessionId(Guid.ParseExact("71440000000000000000000000000002", "N")),
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

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized,
            await boundary.InitializeAsync());
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected,
            await boundary.DeliverOneAsync());

        var terminal = boundary.Snapshot;
        Assert.Equal(1, operations.StopCount);
        Assert.False(operations.HasProduction);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Faulted,
            terminal.State);
        Assert.Equal(-1, terminal.DecoderGeneration);
        Assert.Equal(expected, terminal.DeliveryTrace);
    }

    private sealed class InlineDispatcher : IPlaybackWindowsOutputDispatcher
    {
        private bool _hasAccess;

        public bool HasAccess => _hasAccess;

        public async ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> InvokeAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hasAccess = true;
            try
            {
                return new(
                    PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                    await operation(cancellationToken));
            }
            finally
            {
                _hasAccess = false;
            }
        }
    }

    private sealed class RejectingOperations(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot deliveryTrace) :
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
        private bool _outputDisposed;

        public int StopCount { get; private set; }

        public bool HasProduction => _production is not null;

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
            _probe = PlaybackWindowsOutputProbeResult.Available;
            return new(_probe, PlaybackWindowsOutputAdapterSelection.Windows11Default);
        }

        public ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _activation = PlaybackWindowsOutputActivationResult.Activated;
            return ValueTask.FromResult(_activation);
        }

        public void CreateProduction()
        {
            _production = Production(default);
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.PacketAccepted);

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
            _production = Production(deliveryTrace);
            return ValueTask.FromResult(
                PlaybackProductionPcpFlvCompositionResult.DeliveryRejected);
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.Reset);

        public ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ConfigureOutputAsync(
                PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
                bool replacement,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                AudioStarted: true);
            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
        }

        public ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ResizeVideoAsync(
                long windowToken,
                int width,
                int height,
                PlaybackDecodedVideoPixelFormat format,
                long generation,
                CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);

        public ValueTask StopProductionAsync()
        {
            if (_production is not null)
            {
                StopCount++;
                _production = null;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            DisposeOutputAsync()
        {
            if (!_outputDisposed)
            {
                _outputDisposed = true;
                _buffers = _buffers with
                {
                    VideoState = PlaybackWindowsOutputBufferState.Disposed,
                    AudioState = PlaybackWindowsOutputBufferState.Disposed,
                };
            }

            return ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
        }

        public async ValueTask DisposeAsync()
        {
            await StopProductionAsync();
            await DisposeOutputAsync();
        }
    }

    private static PlaybackProductionPcpFlvCompositionSnapshot Production(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot trace) =>
        new(
            State: PlaybackProductionPcpFlvCompositionState.Accepting,
            Result: PlaybackProductionPcpFlvCompositionResult.PacketAccepted,
            AcceptedPacketCount: 0,
            AcceptedPayloadByteCount: 0,
            ParserOperationCount: 0,
            DecodeOperationCount: 0,
            TransferredFrameCount: 1,
            DeliveredFrameCount: 0,
            DroppedFrameCount: 0,
            ParserGeneration: 0,
            DecoderGeneration: 0,
            HasDecoderGeneration: true,
            HasActiveOperation: false,
            HasActiveDecodedCompletion: false,
            HasActiveSubmission: false,
            HasReliefRetry: false,
            VideoFlushAccepted: false,
            AudioFlushAccepted: false,
            VideoEndOfStream: false,
            AudioEndOfStream: false,
            VideoWidth: 1920,
            VideoHeight: 1080,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2)
        {
            DeliveryTrace = trace,
        };

    private static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot Trace(long sequence)
    {
        var observation = new PlaybackGenerationMatchedSchedulerDeliveryObservation(
            Sequence: sequence,
            MediaKind: PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            PresentationTimestampOffsetTicks: TimeSpan.FromMilliseconds(20).Ticks,
            HasTimingDecision: true,
            TimingResult: PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            Result: PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure,
            Completion: PlaybackGenerationMatchedSchedulerReservationCompletion.Retained);
        return new(
            Count: 1,
            Overflowed: false,
            First: observation,
            Second: default,
            Third: default,
            Fourth: default);
    }
}
