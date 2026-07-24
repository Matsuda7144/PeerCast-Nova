namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshotTraceTests
{
    [Fact]
    public void Positional_snapshot_keeps_absent_trace_by_default()
    {
        var snapshot = CreateSnapshot();

        Assert.Equal(
            default(PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot),
            snapshot.DeliveryTrace);
    }

    [Fact]
    public void Init_only_property_retains_fixed_delivery_trace()
    {
        var expected = Trace(sequence: 3);
        var snapshot = CreateSnapshot() with
        {
            DeliveryTrace = expected,
        };

        Assert.Equal(expected, snapshot.DeliveryTrace);
        Assert.Equal(1, snapshot.DeliveryTrace.Count);
        Assert.Equal(3L, snapshot.DeliveryTrace.First.Sequence);
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot CreateSnapshot() =>
        new(
            State: PlaybackApprovedExternalRuntimeWindowsLifecycleState.Created,
            Result: PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None,
            DecoderGeneration: -1,
            DiscontinuityGeneration: 0,
            OutputReadyGeneration: -1,
            VideoWidth: 0,
            VideoHeight: 0,
            AudioSampleRate: 0,
            AudioChannelCount: 0,
            HasActiveOperation: false,
            ProbeResult: PlaybackWindowsOutputProbeResult.Failure,
            ActivationResult: PlaybackWindowsOutputActivationResult.Failure,
            Binding: default,
            Streams: default,
            Buffers: default);

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
}
