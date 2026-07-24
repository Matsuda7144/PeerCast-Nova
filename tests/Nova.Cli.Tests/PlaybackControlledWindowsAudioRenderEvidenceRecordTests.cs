using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceRecordTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public void Canonical_record_round_trips_without_private_execution_details()
    {
        var source = CreateRecord();

        var text = PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(source);

        Assert.True(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(
                text,
                out var parsed));
        Assert.Equal(source, parsed);
        Assert.True(
            text.Length <=
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec
                .MaximumRecordCharacterCount);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device-id", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handle", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HRESULT", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_copies_all_three_checkpoints_and_fixed_run_metadata()
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

        var record = PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
            snapshot,
            FixtureSha256.ToLowerInvariant(),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            physicalRunsExecutedByThisCommand: false);

        Assert.Equal(FixtureSha256, record.FixtureSha256);
        Assert.Equal(1000, record.ObservationHoldMilliseconds);
        Assert.Equal(100, record.AudioBufferMilliseconds);
        Assert.Equal(holdStart.DeliveryTrace, record.HoldStartDeliveryTrace);
        Assert.Equal(preCleanup.AudioRenderTrace, record.PreCleanupAudioRenderTrace);
        Assert.Equal(postStop.NativeEvidence, record.PostStopNativeEvidence);
        Assert.False(record.PhysicalRunsExecutedByThisCommand);
        Assert.False(record.AudioAcceptanceClaimed);
        Assert.False(record.ExternalBinaryOrPayloadPublished);
    }

    [Theory]
    [InlineData("\nUnexpected: value")]
    [InlineData("\n")]
    public void Parser_rejects_noncanonical_trailing_content(string suffix)
    {
        var text = PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
            CreateRecord());

        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(
                text + suffix,
                out _));
    }

    [Fact]
    public void Parser_rejects_record_larger_than_fixed_limit()
    {
        var text = new string(
            'X',
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec
                .MaximumRecordCharacterCount + 1);

        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(text, out _));
    }

    [Fact]
    public void Serializer_rejects_acceptance_or_publication_claims()
    {
        var source = CreateRecord();

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with { AudioAcceptanceClaimed = true }));
        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with { ExternalBinaryOrPayloadPublished = true }));
    }

    [Fact]
    public void Serializer_rejects_missing_post_stop_padding_query()
    {
        var source = CreateRecord();
        var postStopAudio = source.PostStopAudioRenderTrace with
        {
            PreStop = default,
        };
        var postStopNative = source.PostStopNativeEvidence with
        {
            AudioRenderTrace = postStopAudio,
        };

        Assert.Throws<ArgumentException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
                source with
                {
                    PostStopAudioRenderTrace = postStopAudio,
                    PostStopNativeEvidence = postStopNative,
                }));
    }

    [Fact]
    public void Create_rejects_non_integral_milliseconds()
    {
        var snapshot = CreateSnapshot();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
                snapshot,
                FixtureSha256,
                TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond + 1),
                TimeSpan.FromMilliseconds(100),
                physicalRunsExecutedByThisCommand: false));
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceRecord CreateRecord()
    {
        var snapshot = CreateSnapshot();
        return PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
            snapshot,
            FixtureSha256,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            physicalRunsExecutedByThisCommand: true);
    }

    private static PlaybackControlledWindowsPhysicalRunSnapshot CreateSnapshot()
    {
        var holdStart = CreateCheckpoint(includePreStop: false);
        var preCleanup = CreateCheckpoint(includePreStop: false);
        var postStop = CreateCheckpoint(includePreStop: true);
        return new(
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
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceSnapshot CreateCheckpoint(
        bool includePreStop)
    {
        var delivery = includePreStop ? default : CreateDeliveryTrace();
        var audio = CreateAudioTrace(includePreStop);
        return new(
            delivery,
            audio,
            CreateNativeEvidence(audio));
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot CreateDeliveryTrace() =>
        new(
            Count: 4,
            Overflowed: false,
            First: Delivery(sequence: 0, presentationMilliseconds: 0),
            Second: Delivery(sequence: 1, presentationMilliseconds: 21),
            Third: Delivery(sequence: 2, presentationMilliseconds: 42),
            Fourth: Delivery(sequence: 3, presentationMilliseconds: 64));

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        int presentationMilliseconds) =>
        new(
            Sequence: sequence,
            MediaKind: PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            PresentationTimestampOffsetTicks:
                TimeSpan.FromMilliseconds(presentationMilliseconds).Ticks,
            HasTimingDecision: true,
            TimingResult: PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            Result: PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            Completion: PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);

    private static PlaybackWindowsAudioRenderTraceSnapshot CreateAudioTrace(bool includePreStop) =>
        new(
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
            Result: PlaybackWindowsAudioRenderWriteResult.Processed);

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
