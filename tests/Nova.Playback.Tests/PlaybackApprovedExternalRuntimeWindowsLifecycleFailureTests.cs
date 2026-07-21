using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleFailureTests
{
    [Fact]
    public async Task Stop_cancels_active_operation_before_production_first_cleanup()
    {
        var dispatcher = new InlineDispatcher();
        var operations = new BlockingOperations();
        var boundary = CreateBoundary(operations, dispatcher);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized,
            await boundary.InitializeAsync());

        var consume = boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap())).AsTask();
        await operations.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await boundary.StopAsync();

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed,
            await consume);
        Assert.Equal(1, operations.StopCount);
        Assert.Equal(1, operations.OutputDisposeCount);
        Assert.True(operations.StopOrder < operations.OutputDisposeOrder);
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            boundary.Snapshot.State);
        Assert.False(dispatcher.IsDisposed);

        await boundary.DisposeAsync();
    }

    [Fact]
    public async Task Activation_failure_unwinds_only_acquired_output_ownership()
    {
        var dispatcher = new InlineDispatcher();
        var operations = new BlockingOperations
        {
            ActivationResult = PlaybackWindowsOutputActivationResult.DeviceUnavailable,
        };
        var boundary = CreateBoundary(operations, dispatcher);

        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ActivationRejected,
            await boundary.InitializeAsync());
        Assert.Equal(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Faulted,
            boundary.Snapshot.State);
        Assert.Equal(0, operations.CreateProductionCount);
        Assert.Equal(0, operations.StopCount);
        Assert.Equal(1, operations.OutputDisposeCount);
        Assert.Equal(1, operations.ProbeCount);
        Assert.Equal(1, operations.ActivationCount);

        await boundary.DisposeAsync();
        Assert.Equal(1, operations.OutputDisposeCount);
        Assert.False(dispatcher.IsDisposed);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary CreateBoundary(
        BlockingOperations operations,
        InlineDispatcher dispatcher) =>
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

    private sealed class InlineDispatcher : IPlaybackWindowsOutputDispatcher
    {
        private bool _hasAccess;

        public bool HasAccess => _hasAccess;

        public bool IsDisposed { get; private set; }

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
            finally
            {
                _hasAccess = false;
            }
        }
    }

    private sealed class BlockingOperations :
        IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations
    {
        private PlaybackProductionPcpFlvCompositionSnapshot? _production;
        private bool _productionStopped;
        private bool _outputDisposed;
        private int _order;

        public PlaybackWindowsOutputActivationResult ActivationResult { get; init; } =
            PlaybackWindowsOutputActivationResult.Activated;

        public int ProbeCount { get; private set; }

        public int ActivationCount { get; private set; }

        public int CreateProductionCount { get; private set; }

        public int StopCount { get; private set; }

        public int OutputDisposeCount { get; private set; }

        public int StopOrder { get; private set; }

        public int OutputDisposeOrder { get; private set; }

        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot Snapshot => new(
            PlaybackWindowsOutputProbeResult.Available,
            ActivationResult,
            default,
            default,
            default,
            _production);

        public PlaybackWindowsOutputProbeReport Probe(
            PlaybackDecodedVideoPixelFormat videoFormat,
            PlaybackDecodedAudioSampleFormat audioFormat)
        {
            ProbeCount++;
            return new(
                PlaybackWindowsOutputProbeResult.Available,
                PlaybackWindowsOutputAdapterSelection.Windows11Default);
        }

        public ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
            CancellationToken cancellationToken)
        {
            ActivationCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ActivationResult);
        }

        public void CreateProduction()
        {
            CreateProductionCount++;
            _production = new(
                PlaybackProductionPcpFlvCompositionState.Accepting,
                PlaybackProductionPcpFlvCompositionResult.None,
                AcceptedPacketCount: 0,
                AcceptedPayloadByteCount: 0,
                ParserOperationCount: 0,
                DecodeOperationCount: 0,
                TransferredFrameCount: 0,
                DeliveredFrameCount: 0,
                DroppedFrameCount: 0,
                ParserGeneration: 0,
                DecoderGeneration: -1,
                HasDecoderGeneration: false,
                HasActiveOperation: false,
                HasActiveDecodedCompletion: false,
                HasActiveSubmission: false,
                HasReliefRetry: false,
                VideoFlushAccepted: false,
                AudioFlushAccepted: false,
                VideoEndOfStream: false,
                AudioEndOfStream: false,
                VideoWidth: 0,
                VideoHeight: 0,
                AudioSampleRate: 0,
                AudioChannelCount: 0);
        }

        public async ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PlaybackProductionPcpFlvCompositionResult.PacketAccepted;
        }

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> CompleteInputAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.EncodedInputFinalized);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.NeedMoreInput);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
            PlaybackBoundedDemuxMediaKind mediaKind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.Empty);

        public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackProductionPcpFlvCompositionResult.Reset);

        public ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
            ConfigureOutputAsync(
                PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
                bool replacement,
                CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);

        public ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult> ResizeVideoAsync(
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
            if (!_productionStopped)
            {
                _productionStopped = true;
                StopCount++;
                StopOrder = ++_order;
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
                OutputDisposeCount++;
                OutputDisposeOrder = ++_order;
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
}
