using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioTerminalRenderRecordTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public void Round_trip_preserves_canonical_bounded_terminal_record()
    {
        var expected = CreateValidRecord();

        var text = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(expected);
        Assert.True(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                text,
                out var parsed));

        Assert.Equal(expected, parsed);
        Assert.True(
            text.Length <
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec
                .MaximumRecordCharacterCount);
        Assert.Equal(3, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"PhysicalRunCompleted\":false", text, StringComparison.Ordinal);
        Assert.Contains(
            "\"CompletedRenderEvidenceEstablished\":false",
            text,
            StringComparison.Ordinal);
        Assert.Contains("\"AudioAcceptanceClaimed\":false", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_normalizes_video_fields_and_never_claims_acceptance()
    {
        var expected = CreateValidRecord();
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Faulted,
            expected.Result,
            expected.Kind,
            FixtureSha256,
            expected.FixtureByteCount,
            expected.FixturePacketCount,
            expected.DecodeOperationCount,
            expected.DeliveryAttemptCount,
            expected.DeliveredCount,
            expected.RetainedWouldBlockCount,
            expected.RunnerState,
            expected.LifecycleState,
            expected.EvidenceResult,
            expected.BorrowedWindowPreserved,
            expected.BorrowedDispatcherPreserved)
        {
            PreCleanupAudioRenderEvidence = new(
                expected.PreCleanupDeliveryTrace,
                expected.PreCleanupAudioRenderTrace,
                WithVideoEvidence(expected.PreCleanupNativeEvidence)),
            PostStopAudioRenderEvidence = new(
                expected.PostStopDeliveryTrace,
                expected.PostStopAudioRenderTrace,
                WithVideoEvidence(expected.PostStopNativeEvidence)),
        };

        var actual = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Create(
            snapshot,
            FixtureSha256,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100),
            physicalRunStarted: true);

        Assert.Equal(expected, actual);
        Assert.Equal(0, actual.PreCleanupNativeEvidence.VideoPresentAttemptCount);
        Assert.Equal(-1, actual.PreCleanupNativeEvidence.VideoGeneration);
        Assert.False(actual.PhysicalRunCompleted);
        Assert.False(actual.CompletedRenderEvidenceEstablished);
        Assert.False(actual.AudioAcceptanceClaimed);
        Assert.False(actual.ExternalBinaryOrPayloadPublished);
    }

    [Fact]
    public void Retained_wait_resume_keeps_sequence_and_can_exceed_outer_attempt_count()
    {
        var delivery = new PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot(
            Count: 4,
            Overflowed: false,
            First: Delivery(
                10,
                TimeSpan.Zero,
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Retained,
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(3)),
            Second: Delivery(
                10,
                TimeSpan.Zero,
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered),
            Third: Delivery(
                11,
                TimeSpan.FromMilliseconds(21),
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered),
            Fourth: Delivery(
                12,
                TimeSpan.FromMilliseconds(42),
                PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Retained));
        var audio = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 3,
            Overflowed: false,
            First: Write(0, startRequested: true, PlaybackWindowsAudioRenderWriteResult.Processed),
            Second: Write(
                1_024,
                startRequested: false,
                PlaybackWindowsAudioRenderWriteResult.Processed),
            Third: Write(
                2_048,
                startRequested: false,
                PlaybackWindowsAudioRenderWriteResult.Failure),
            Fourth: default,
            PreStop: default);
        var expected = WithCheckpoints(CreateValidRecord(), delivery, audio) with
        {
            DeliveryAttemptCount = 3,
            DeliveredCount = 2,
            RetainedWouldBlockCount = 0,
        };

        var text = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(expected);
        Assert.True(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                text,
                out var parsed));
        var actual = Assert.IsType<PlaybackControlledWindowsAudioTerminalRenderRecord>(parsed);

        Assert.Equal(expected, actual);
        Assert.Equal(4, actual.PreCleanupDeliveryTrace.Count);
        Assert.Equal(3, actual.DeliveryAttemptCount);
        Assert.Equal(10L, actual.PreCleanupDeliveryTrace.First.Sequence);
        Assert.Equal(10L, actual.PreCleanupDeliveryTrace.Second.Sequence);
    }

    [Theory]
    [InlineData(PlaybackControlledWindowsPhysicalRunResult.Completed)]
    [InlineData(PlaybackControlledWindowsPhysicalRunResult.RunnerRejected)]
    [InlineData(PlaybackControlledWindowsPhysicalRunResult.LifecycleRejected)]
    [InlineData(PlaybackControlledWindowsPhysicalRunResult.FixtureRejected)]
    public void Serialize_rejects_unreviewed_terminal_results(
        PlaybackControlledWindowsPhysicalRunResult result)
    {
        Assert.Throws<ArgumentException>(
            () => PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(
                CreateValidRecord() with { Result = result }));
    }

    [Fact]
    public void Serialize_rejects_acceptance_completion_and_publication_claims()
    {
        var valid = CreateValidRecord();

        AssertInvalid(valid with
        {
            EvidenceResult = PlaybackControlledWindowsOutputEvidenceResult.Accepted,
        });
        AssertInvalid(valid with { PhysicalRunCompleted = true });
        AssertInvalid(valid with { CompletedRenderEvidenceEstablished = true });
        AssertInvalid(valid with { AudioAcceptanceClaimed = true });
        AssertInvalid(valid with { ExternalBinaryOrPayloadPublished = true });
    }

    [Fact]
    public void Serialize_rejects_contradictory_or_unsanitized_evidence()
    {
        var valid = CreateValidRecord();

        AssertInvalid(valid with { DeliveredCount = valid.DeliveredCount + 1 });
        AssertInvalid(valid with
        {
            PreCleanupNativeEvidence = valid.PreCleanupNativeEvidence with
            {
                AudioWriteAcknowledgementCount = 3,
            },
        });
        AssertInvalid(valid with { PostStopDeliveryTrace = valid.PreCleanupDeliveryTrace });
        AssertInvalid(valid with
        {
            PostStopAudioRenderTrace = valid.PostStopAudioRenderTrace with
            {
                Second = valid.PostStopAudioRenderTrace.Second with
                {
                    RequestedFrameCount = 512,
                },
            },
        });
        AssertInvalid(valid with
        {
            PreCleanupNativeEvidence = WithVideoEvidence(valid.PreCleanupNativeEvidence),
        });
    }

    [Fact]
    public void Serialize_rejects_abandoned_retention_and_nonterminal_failed_write()
    {
        var valid = CreateValidRecord();
        var abandoned = valid.PreCleanupDeliveryTrace with
        {
            First = valid.PreCleanupDeliveryTrace.First with
            {
                Result = PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait,
                Completion =
                    PlaybackGenerationMatchedSchedulerReservationCompletion.Retained,
            },
        };
        AssertInvalid(valid with { PreCleanupDeliveryTrace = abandoned });

        var failedThenContinued = valid.PreCleanupAudioRenderTrace with
        {
            First = Write(
                0,
                startRequested: true,
                PlaybackWindowsAudioRenderWriteResult.Failure),
        };
        AssertInvalid(valid with
        {
            PreCleanupAudioRenderTrace = failedThenContinued,
            PreCleanupNativeEvidence = Native(failedThenContinued),
        });
    }

    [Fact]
    public void Parser_rejects_noncanonical_extended_and_oversized_text()
    {
        var text = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(
            CreateValidRecord());

        Assert.False(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                text.Replace(
                    "\"PhysicalRunStarted\":true",
                    "\"PhysicalRunStarted\":True",
                    StringComparison.Ordinal),
                out _));
        Assert.False(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                text + "unexpected\n",
                out _));
        Assert.False(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                new string(
                    'X',
                    PlaybackControlledWindowsAudioTerminalRenderRecordCodec
                        .MaximumRecordCharacterCount + 1),
                out _));
    }

    private static void AssertInvalid(
        PlaybackControlledWindowsAudioTerminalRenderRecord record) =>
        Assert.Throws<ArgumentException>(
            () => PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(record));

    private static PlaybackControlledWindowsAudioTerminalRenderRecord CreateValidRecord()
    {
        var delivery = new PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot(
            Count: 3,
            Overflowed: false,
            First: Delivery(
                10,
                TimeSpan.Zero,
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered),
            Second: Delivery(
                11,
                TimeSpan.FromMilliseconds(21),
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered),
            Third: Delivery(
                12,
                TimeSpan.FromMilliseconds(42),
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock,
                PlaybackGenerationMatchedSchedulerReservationCompletion.Retained),
            Fourth: default);
        var audio = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 3,
            Overflowed: false,
            First: Write(0, startRequested: true, PlaybackWindowsAudioRenderWriteResult.Processed),
            Second: Write(
                1_024,
                startRequested: false,
                PlaybackWindowsAudioRenderWriteResult.Processed),
            Third: Write(
                4_096,
                startRequested: false,
                PlaybackWindowsAudioRenderWriteResult.WouldBlock),
            Fourth: default,
            PreStop: default);
        var empty = new PlaybackControlledWindowsAudioTerminalRenderRecord(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.CurrentMilestone,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            PlaybackFfmpegBackendCandidate.BuildId,
            FixtureSha256,
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
            EvidenceResult: default,
            FixtureByteCount: 32_768,
            FixturePacketCount: 1,
            DecodeOperationCount: 4,
            DeliveryAttemptCount: 3,
            DeliveredCount: 2,
            RetainedWouldBlockCount: 1,
            ObservationHoldMilliseconds: 0,
            AudioBufferMilliseconds: 100,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            BorrowedWindowPreserved: false,
            BorrowedDispatcherPreserved: false,
            PreCleanupDeliveryTrace: default,
            PreCleanupAudioRenderTrace: default,
            PreCleanupNativeEvidence: default,
            PostStopDeliveryTrace: default,
            PostStopAudioRenderTrace: default,
            PostStopNativeEvidence: default,
            PhysicalRunStarted: true,
            PhysicalRunCompleted: false,
            CompletedRenderEvidenceEstablished: false,
            AudioAcceptanceClaimed: false,
            ExternalBinaryOrPayloadPublished: false);
        return WithCheckpoints(empty, delivery, audio);
    }

    private static PlaybackControlledWindowsAudioTerminalRenderRecord WithCheckpoints(
        PlaybackControlledWindowsAudioTerminalRenderRecord record,
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot preCleanupAudio)
    {
        var postStopAudio = preCleanupAudio with
        {
            PreStop = new(
                QueryCount: 1,
                Available: true,
                PaddingFrameCount: 2_048,
                Overflowed: false),
        };
        return record with
        {
            PreCleanupDeliveryTrace = delivery,
            PreCleanupAudioRenderTrace = preCleanupAudio,
            PreCleanupNativeEvidence = Native(preCleanupAudio),
            PostStopDeliveryTrace = default,
            PostStopAudioRenderTrace = postStopAudio,
            PostStopNativeEvidence = Native(postStopAudio),
        };
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        TimeSpan presentationOffset,
        PlaybackGenerationMatchedSchedulerDeliveryResult result,
        PlaybackGenerationMatchedSchedulerReservationCompletion completion,
        PlaybackAvTimingDecisionResult timingResult =
            PlaybackAvTimingDecisionResult.ProcessNow,
        TimeSpan timingWait = default) =>
        new(
            sequence,
            PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            presentationOffset.Ticks,
            HasTimingDecision: true,
            timingResult,
            timingWait.Ticks,
            result,
            completion);

    private static PlaybackWindowsAudioRenderWriteObservation Write(
        int padding,
        bool startRequested,
        PlaybackWindowsAudioRenderWriteResult result)
    {
        const int capacity = 4_800;
        var processed = result == PlaybackWindowsAudioRenderWriteResult.Processed;
        return new(
            RequestedFrameCount: 1_024,
            RequestedByteCount: 4_096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: capacity,
            HasPaddingFrameCount: true,
            PaddingFrameCount: padding,
            HasAvailableFrameCount: true,
            AvailableFrameCount: capacity - padding,
            StartRequested: startRequested,
            GetBufferAcknowledged: processed,
            ReleaseBufferAcknowledged: processed,
            StartAcknowledged: processed && startRequested,
            result);
    }

    private static PlaybackWindowsOutputNativeEvidenceSnapshot Native(
        PlaybackWindowsAudioRenderTraceSnapshot trace)
    {
        long acknowledgements = 0;
        long acceptedFrames = 0;
        long acceptedBytes = 0;
        long wouldBlock = 0;
        long starts = 0;
        long deviceLost = 0;
        for (var index = 0; index < trace.Count; index++)
        {
            var observation = trace.GetObservation(index);
            switch (observation.Result)
            {
                case PlaybackWindowsAudioRenderWriteResult.Processed:
                    acknowledgements++;
                    acceptedFrames += observation.RequestedFrameCount;
                    acceptedBytes += observation.RequestedByteCount;
                    if (observation.StartAcknowledged)
                    {
                        starts++;
                    }
                    break;
                case PlaybackWindowsAudioRenderWriteResult.WouldBlock:
                    wouldBlock++;
                    break;
                case PlaybackWindowsAudioRenderWriteResult.DeviceLost:
                    deviceLost++;
                    break;
            }
        }

        return new(
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
            acknowledgements > 0
                ? PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved
                : default,
            AudioSampleRate: acknowledgements > 0 ? 48_000 : 0,
            AudioChannelCount: acknowledgements > 0 ? 2 : 0,
            AudioGeneration: acknowledgements > 0 ? 7 : -1,
            AudioWriteAttemptCount: trace.Count,
            AudioWriteAcknowledgementCount: acknowledgements,
            AudioAcceptedFrameCount: acceptedFrames,
            AudioAcceptedByteCount: acceptedBytes,
            AudioWouldBlockCount: wouldBlock,
            AudioStartAcknowledgementCount: starts,
            AudioDeviceLostCount: deviceLost)
        {
            AudioRenderTrace = trace,
        };
    }

    private static PlaybackWindowsOutputNativeEvidenceSnapshot WithVideoEvidence(
        PlaybackWindowsOutputNativeEvidenceSnapshot value) =>
        new(
            value.Source,
            value.CounterOverflowed,
            VideoConfigurationConflict: true,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoStride: 7680,
            VideoGeneration: 99,
            VideoPresentAttemptCount: 3,
            VideoPresentAcknowledgementCount: 2,
            VideoAcceptedByteCount: 128,
            VideoWouldBlockCount: 1,
            VideoResizeCount: 1,
            VideoDeviceLostCount: 1,
            value.AudioConfigurationConflict,
            value.AudioSampleFormat,
            value.AudioSampleRate,
            value.AudioChannelCount,
            value.AudioGeneration,
            value.AudioWriteAttemptCount,
            value.AudioWriteAcknowledgementCount,
            value.AudioAcceptedFrameCount,
            value.AudioAcceptedByteCount,
            value.AudioWouldBlockCount,
            value.AudioStartAcknowledgementCount,
            value.AudioDeviceLostCount)
        {
            AudioRenderTrace = value.AudioRenderTrace,
        };
}
