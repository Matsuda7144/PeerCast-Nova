using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackProductionGenerationDeliveryTraceProjectionTests
{
    [Fact]
    public void Null_generation_returns_absent_trace()
    {
        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            PlaybackProductionGenerationDeliveryTraceProjection.Capture(generation: null));
    }

    [Fact]
    public async Task Live_generation_returns_only_the_fixed_delivery_trace()
    {
        using var source = CreateSource();
        var expected = Trace(sequence: 3);
        var root = new FakeRoot(source, decoderGeneration: 7, expected);
        await using var generation = new PlaybackProductionDecoderGeneration(
            source,
            decoderGeneration: 7,
            discontinuityGeneration: 0,
            root);

        var actual = PlaybackProductionGenerationDeliveryTraceProjection.Capture(generation);

        Assert.Equal(expected, actual);
        Assert.Equal(1, actual.Count);
        Assert.Equal(3L, actual.First.Sequence);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public async Task Disposed_generation_returns_absent_trace_without_touching_source()
    {
        using var source = CreateSource();
        var root = new FakeRoot(source, decoderGeneration: 7, Trace(sequence: 3));
        await using var generation = new PlaybackProductionDecoderGeneration(
            source,
            decoderGeneration: 7,
            discontinuityGeneration: 0,
            root);
        await generation.DisposeAsync();

        var actual = PlaybackProductionGenerationDeliveryTraceProjection.Capture(generation);

        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            actual);
        Assert.True(root.IsDisposed);
        Assert.False(source.IsDisposed);
    }

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
            Result: PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            Completion: PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);
        return new(
            Count: 1,
            Overflowed: false,
            First: observation,
            Second: default,
            Third: default,
            Fourth: default);
    }

    private sealed class FakeRoot : IPlaybackProductionDecoderGenerationRoot
    {
        private readonly PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot _trace;

        public FakeRoot(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot trace)
        {
            SourceSession = sourceSession;
            DecoderGeneration = decoderGeneration;
            _trace = trace;
        }

        public PlaybackBoundedDemuxSession SourceSession { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration => 0;

        public bool IsDisposed { get; private set; }

        public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                var ingress = new PlaybackDecodedCompletionSchedulerIngressSnapshot(
                    State: PlaybackDecodedCompletionSchedulerIngressState.Accepting,
                    Result: PlaybackDecodedCompletionSchedulerIngressResult.None,
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
                    : PlaybackGenerationMatchedSchedulerDeliveryResult.Empty);

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

        public void Dispose()
        {
            IsDisposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
