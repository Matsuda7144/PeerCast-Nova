using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceRecordValidationTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public void Serializer_rejects_all_overflow_and_configuration_conflicts()
    {
        var source = CreateRecord();
        var deliveryOverflow = source.HoldStartDeliveryTrace with { Overflowed = true };
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    HoldStartDeliveryTrace = deliveryOverflow,
                    PreCleanupDeliveryTrace = deliveryOverflow,
                }));

        var holdAudioOverflow = source.HoldStartAudioRenderTrace with { Overflowed = true };
        var preAudioOverflow = source.PreCleanupAudioRenderTrace with { Overflowed = true };
        var postAudioOverflow = source.PostStopAudioRenderTrace with { Overflowed = true };
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    HoldStartAudioRenderTrace = holdAudioOverflow,
                    HoldStartNativeEvidence = source.HoldStartNativeEvidence with
                    {
                        AudioRenderTrace = holdAudioOverflow,
                    },
                    PreCleanupAudioRenderTrace = preAudioOverflow,
                    PreCleanupNativeEvidence = source.PreCleanupNativeEvidence with
                    {
                        AudioRenderTrace = preAudioOverflow,
                    },
                    PostStopAudioRenderTrace = postAudioOverflow,
                    PostStopNativeEvidence = source.PostStopNativeEvidence with
                    {
                        AudioRenderTrace = postAudioOverflow,
                    },
                }));

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                WithAllNativeEvidence(
                    source,
                    static value => value with { CounterOverflowed = true })));
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                WithAllNativeEvidence(
                    source,
                    static value => value with { AudioConfigurationConflict = true })));
    }

    [Fact]
    public void Serializer_rejects_delivery_and_native_total_contradictions()
    {
        var source = CreateRecord();
        var invalidDelivery = source.HoldStartDeliveryTrace with
        {
            First = source.HoldStartDeliveryTrace.First with
            {
                Result = PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock,
                Completion =
                    PlaybackGenerationMatchedSchedulerReservationCompletion.Retained,
            },
        };
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    HoldStartDeliveryTrace = invalidDelivery,
                    PreCleanupDeliveryTrace = invalidDelivery,
                }));

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                WithAllNativeEvidence(
                    source,
                    static value => value with
                    {
                        AudioAcceptedFrameCount = value.AudioAcceptedFrameCount + 1,
                    })));
    }

    [Fact]
    public void Serializer_rejects_run_and_owner_state_contradictions()
    {
        var source = CreateRecord();

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with { PhysicalRunsExecutedByThisCommand = false }));
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with { BorrowedWindowPreserved = false }));
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    Result = PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected,
                }));
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with { DeliveredCount = 3 }));
    }

    [Fact]
    public void Serializer_rejects_write_acknowledgement_contradictions()
    {
        var source = CreateRecord();
        var holdAudio = source.HoldStartAudioRenderTrace with
        {
            First = source.HoldStartAudioRenderTrace.First with
            {
                StartAcknowledged = false,
            },
        };
        var preAudio = source.PreCleanupAudioRenderTrace with
        {
            First = source.PreCleanupAudioRenderTrace.First with
            {
                StartAcknowledged = false,
            },
        };
        var postAudio = source.PostStopAudioRenderTrace with
        {
            First = source.PostStopAudioRenderTrace.First with
            {
                StartAcknowledged = false,
            },
        };

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    HoldStartAudioRenderTrace = holdAudio,
                    HoldStartNativeEvidence = source.HoldStartNativeEvidence with
                    {
                        AudioRenderTrace = holdAudio,
                    },
                    PreCleanupAudioRenderTrace = preAudio,
                    PreCleanupNativeEvidence = source.PreCleanupNativeEvidence with
                    {
                        AudioRenderTrace = preAudio,
                    },
                    PostStopAudioRenderTrace = postAudio,
                    PostStopNativeEvidence = source.PostStopNativeEvidence with
                    {
                        AudioRenderTrace = postAudio,
                    },
                }));
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceRecord WithAllNativeEvidence(
        PlaybackControlledWindowsAudioRenderEvidenceRecord source,
        Func<PlaybackWindowsOutputNativeEvidenceSnapshot,
            PlaybackWindowsOutputNativeEvidenceSnapshot> update) =>
        source with
        {
            HoldStartNativeEvidence = update(source.HoldStartNativeEvidence),
            PreCleanupNativeEvidence = update(source.PreCleanupNativeEvidence),
            PostStopNativeEvidence = update(source.PostStopNativeEvidence),
        };

    private static PlaybackControlledWindowsAudioRenderEvidenceRecord CreateRecord()
    {
        var holdStart = CreateCheckpoint(includePreStop: false);
        var preCleanup = CreateCheckpoint(includePreStop: false);
        var postStop = CreateCheckpoint(includePreStop: true);
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Completed,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 4,
            DeliveryAttemptCount: 4,
            DeliveredCount: 4,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true)
        {
            HoldStartAudioRenderEvidence = holdStart,
            PreCleanupAudioRenderEvidence = preCleanup,
            PostStopAudioRenderEvidence = postStop,
        };
        return PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
            snapshot,
            FixtureSha256,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            physicalRunsExecutedByThisCommand: true);
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceSnapshot CreateCheckpoint(
        bool includePreStop)
    {
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery = includePreStop
            ? default
            : new(
                Count: 4,
                Overflowed: false,
                First: Delivery(sequence: 0, presentationMilliseconds: 0),
                Second: Delivery(sequence: 1, presentationMilliseconds: 21),
                Third: Delivery(sequence: 2, presentationMilliseconds: 42),
                Fourth: Delivery(sequence: 3, presentationMilliseconds: 64));
        var audio = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 4,
            Overflowed: false,
            First: Write(paddingFrames: 0, startRequested: true),
            Second: Write(paddingFrames: 1024, startRequested: false),
            Third: Write(paddingFrames: 2048, startRequested: false),
            Fourth: Write(paddingFrames: 3072, startRequested: false),
            PreStop: includePreStop
                ? new(
                    QueryCount: 1,
                    Available: true,
                    PaddingFrameCount: 0,
                    Overflowed: false)
                : default);
        return new(delivery, audio, CreateNativeEvidence(audio));
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        int presentationMilliseconds) =>
        new(
            sequence,
            PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            TimeSpan.FromMilliseconds(presentationMilliseconds).Ticks,
            HasTimingDecision: true,
            PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);

    private static PlaybackWindowsAudioRenderWriteObservation Write(
        int paddingFrames,
        bool startRequested) =>
        new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: 4800,
            HasPaddingFrameCount: true,
            PaddingFrameCount: paddingFrames,
            HasAvailableFrameCount: true,
            AvailableFrameCount: 4800 - paddingFrames,
            StartRequested: startRequested,
            GetBufferAcknowledged: true,
            ReleaseBufferAcknowledged: true,
            StartAcknowledged: startRequested,
            PlaybackWindowsAudioRenderWriteResult.Processed);

    private static PlaybackWindowsOutputNativeEvidenceSnapshot CreateNativeEvidence(
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 0,
            VideoHeight: 0,
            VideoStride: 0,
            VideoGeneration: -1,
            VideoPresentAttemptCount: 0,
            VideoPresentAcknowledgementCount: 0,
            VideoAcceptedByteCount: 0,
            VideoWouldBlockCount: 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            AudioConfigurationConflict: false,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 7,
            AudioWriteAttemptCount: 4,
            AudioWriteAcknowledgementCount: 4,
            AudioAcceptedFrameCount: 4_096,
            AudioAcceptedByteCount: 16_384,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = audioTrace,
        };
}
