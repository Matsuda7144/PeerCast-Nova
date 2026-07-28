using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackProductionPcpFlvCompositionDeliveryTraceSnapshotTests
{
    [Fact]
    public async Task Snapshot_is_absent_before_generation_then_exposes_fixed_trace()
    {
        using var output = new OutputHarness();
        var expected = Trace(sequence: 3);
        var creator = new FakeCreator(expected);
        await using var boundary = new PlaybackProductionPcpFlvCompositionBoundary(
            ChannelId,
            SessionId,
            "C:\\verified-runtime",
            output.Output,
            creator);

        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            boundary.Snapshot.DeliveryTrace);

        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);

        Assert.Equal(expected, boundary.Snapshot.DeliveryTrace);
        Assert.Equal(1, boundary.Snapshot.DeliveryTrace.Count);
        Assert.Equal(3L, boundary.Snapshot.DeliveryTrace.First.Sequence);
    }

    [Fact]
    public async Task Delivery_rejection_retains_trace_after_terminal_generation_cleanup()
    {
        using var output = new OutputHarness();
        var expected = Trace(
            sequence: 7,
            PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure,
            PlaybackGenerationMatchedSchedulerReservationCompletion.Retained);
        var creator = new FakeCreator(
            expected,
            PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure);
        await using var boundary = new PlaybackProductionPcpFlvCompositionBoundary(
            ChannelId,
            SessionId,
            "C:\\verified-runtime",
            output.Output,
            creator);
        await boundary.ConsumeAsync(CreatePacket(0, BuildBootstrap()), default);

        var exception = await Assert.ThrowsAsync<PlaybackProductionPcpFlvCompositionException>(
            () => boundary.DeliverOneAsync().AsTask());

        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.DeliveryRejected,
            exception.Result);
        var terminal = boundary.Snapshot;
        Assert.Equal(PlaybackProductionPcpFlvCompositionState.Faulted, terminal.State);
        Assert.Equal(
            PlaybackProductionPcpFlvCompositionResult.DeliveryRejected,
            terminal.Result);
        Assert.False(terminal.HasDecoderGeneration);
        Assert.Equal(-1, terminal.DecoderGeneration);
        Assert.Equal(expected, terminal.DeliveryTrace);
        Assert.True(creator.IsDisposed);
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot Trace(
        long sequence,
        PlaybackGenerationMatchedSchedulerDeliveryResult result =
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
        PlaybackGenerationMatchedSchedulerReservationCompletion completion =
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered)
    {
        var observation = new PlaybackGenerationMatchedSchedulerDeliveryObservation(
            Sequence: sequence,
            MediaKind: PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            PresentationTimestampOffsetTicks: TimeSpan.FromMilliseconds(20).Ticks,
            HasTimingDecision: true,
            TimingResult: PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            Result: result,
            Completion: completion);
        return new(
            Count: 1,
            Overflowed: false,
            First: observation,
            Second: default,
            Third: default,
            Fourth: default);
    }

    private sealed class FakeCreator(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot trace,
        PlaybackGenerationMatchedSchedulerDeliveryResult deliveryResult =
            PlaybackGenerationMatchedSchedulerDeliveryResult.Empty) :
        IPlaybackProductionDecoderGenerationCreator
    {
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
            var root = new FakeRoot(
                source,
                decoderGeneration,
                discontinuityGeneration,
                trace,
                deliveryResult);
            return ValueTask.FromResult(
                new PlaybackProductionDecoderGenerationCreation(
                    PlaybackProductionDecoderGenerationFactoryResult.Created,
                    new PlaybackProductionDecoderGeneration(
                        source,
                        decoderGeneration,
                        discontinuityGeneration,
                        root)));
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRoot :
        IPlaybackProductionDecoderGenerationRoot,
        IPlaybackProductionDecoderGenerationIngressForwarder
    {
        private readonly PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot _trace;
        private readonly PlaybackGenerationMatchedSchedulerDeliveryResult _deliveryResult;

        public FakeRoot(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            int discontinuityGeneration,
            PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot trace,
            PlaybackGenerationMatchedSchedulerDeliveryResult deliveryResult)
        {
            SourceSession = sourceSession;
            DecoderGeneration = decoderGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
            _trace = trace;
            _deliveryResult = deliveryResult;
        }

        public PlaybackBoundedDemuxSession SourceSession { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration { get; }

        public bool IsDisposed { get; private set; }

        public PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshot =>
            IsDisposed
                ? null
                : new(
                    PlaybackSourceBoundOneFrameDecodeState.Accepting,
                    PlaybackSourceBoundOneFrameDecodeResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    NextVideoSequence: 0,
                    NextAudioSequence: 0,
                    VideoFlushAccepted: false,
                    AudioFlushAccepted: false,
                    VideoEndOfStream: false,
                    AudioEndOfStream: false,
                    HasActiveDecodedCompletion: false,
                    HasActiveSubmission: false,
                    HasReliefRetry: false);

        public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                var ingress = new PlaybackDecodedCompletionSchedulerIngressSnapshot(
                    PlaybackDecodedCompletionSchedulerIngressState.Accepting,
                    PlaybackDecodedCompletionSchedulerIngressResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    NextVideoSourceSequence: 0,
                    NextAudioSourceSequence: 0,
                    NextSchedulerSequence: 0,
                    AcceptedTransferCount: 0,
                    DecoderResetPending: false,
                    Scheduler: default);
                return new(
                    PlaybackGenerationMatchedSchedulerDeliveryState.Accepting,
                    PlaybackGenerationMatchedSchedulerDeliveryResult.None,
                    DecoderGeneration,
                    DiscontinuityGeneration,
                    PreferVideoNext: true,
                    DecoderResetPending: false,
                    HasCompletedSequence: false,
                    LastCompletedSequence: 0,
                    HasActiveDelivery: false,
                    Ingress: ingress)
                {
                    DeliveryTrace = _trace,
                };
            }
        }

        public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
            ReferenceEquals(SourceSession, sourceSession);

        public PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
            PlaybackBoundedDemuxReadCompletion? sourceCompletion,
            CancellationToken cancellationToken)
        {
            sourceCompletion?.Dispose();
            return cancellationToken.IsCancellationRequested
                ? PlaybackFiniteOneEventPacketSubmissionResult.Cancelled
                : PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted;
        }

        public PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
            PlaybackBoundedDemuxMediaKind mediaKind,
            int streamId,
            PlaybackSourceBoundOneFrameDecodeMode mode,
            CancellationToken cancellationToken,
            PlaybackSourceBoundOneFrameDecodeBudget budget) =>
            new(
                cancellationToken.IsCancellationRequested
                    ? PlaybackSourceBoundOneFrameDecodeResult.Cancelled
                    : PlaybackSourceBoundOneFrameDecodeResult.NeedMoreInput,
                null,
                default);

        public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
            PlaybackSourceBoundDecodedFrameCompletion? completion,
            CancellationToken cancellationToken)
        {
            completion?.Dispose();
            return IsDisposed
                ? PlaybackDecodedCompletionSchedulerIngressResult.Disposed
                : cancellationToken.IsCancellationRequested
                    ? PlaybackDecodedCompletionSchedulerIngressResult.Cancelled
                    : PlaybackDecodedCompletionSchedulerIngressResult.Transferred;
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed
                : cancellationToken.IsCancellationRequested
                    ? PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled
                    : _deliveryResult);

        public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerResetResult.Disposed
                : cancellationToken.IsCancellationRequested
                    ? PlaybackGenerationMatchedSchedulerResetResult.Cancelled
                    : PlaybackGenerationMatchedSchedulerResetResult.Reset);

        public ValueTask StopAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
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
