using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsPhysicalRunRecordTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public void Canonical_record_round_trips_without_private_execution_details()
    {
        var source = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video);

        var text = PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(source);

        Assert.True(
            PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(text, out var parsed));
        Assert.Equal(source, parsed);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handle", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD\n",
            text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\nUnexpected: value")]
    [InlineData("\n")]
    public void Parser_rejects_noncanonical_trailing_content(string suffix)
    {
        var text = PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
            ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video));

        Assert.False(
            PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(
                text + suffix,
                out _));
    }

    [Fact]
    public void Independent_video_then_audio_records_complete_one_set()
    {
        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.VideoAccepted,
            boundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetState.AwaitingAudio,
            boundary.Snapshot.State);
        Assert.False(boundary.Snapshot.IsComplete);

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.Completed,
            boundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio)));
        Assert.True(boundary.Snapshot.HasVideoRecord);
        Assert.True(boundary.Snapshot.HasAudioRecord);
        Assert.True(boundary.Snapshot.IsComplete);
    }

    [Fact]
    public void Independent_audio_then_video_records_complete_one_set()
    {
        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.AudioAccepted,
            boundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio)));
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetState.AwaitingVideo,
            boundary.Snapshot.State);
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.Completed,
            boundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
    }

    [Fact]
    public void Duplicate_kind_rejects_without_replacing_first_record()
    {
        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);
        var video = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video);

        boundary.ValidateAndConsume(video);

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.DuplicateKind,
            boundary.ValidateAndConsume(video));
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetState.Rejected,
            boundary.Snapshot.State);
        Assert.True(boundary.Snapshot.HasVideoRecord);
        Assert.False(boundary.Snapshot.HasAudioRecord);
    }

    [Fact]
    public void Runtime_or_fixture_identity_mismatch_is_terminal()
    {
        using var runtimeBoundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);
        using var fixtureBoundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.RuntimeRejected,
            runtimeBoundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video) with
                {
                    RuntimeBuildId = "unexpected-runtime",
                }));
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.FixtureRejected,
            fixtureBoundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video) with
                {
                    FixtureSha256 = new string('B', 64),
                }));
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.AlreadyRejected,
            runtimeBoundary.ValidateAndConsume(
                ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Cleanup_contradiction_rejects_record(
        bool windowPreserved,
        bool dispatcherPreserved,
        bool published)
    {
        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);
        var record = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio) with
        {
            BorrowedWindowPreserved = windowPreserved,
            BorrowedDispatcherPreserved = dispatcherPreserved,
            ExternalBinaryOrPayloadPublished = published,
        };

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.CleanupRejected,
            boundary.ValidateAndConsume(record));
    }

    [Fact]
    public void Failed_or_unacknowledged_run_cannot_enter_set()
    {
        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            FixtureSha256);
        var record = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video) with
        {
            Result = PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected,
            EvidenceResult =
                PlaybackControlledWindowsOutputEvidenceResult.PhysicalAcknowledgementRequired,
            DeliveredCount = 0,
        };

        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.EvidenceRejected,
            boundary.ValidateAndConsume(record));
        Assert.False(boundary.Snapshot.HasVideoRecord);
    }

    private static PlaybackControlledWindowsPhysicalRunRecord ValidRecord(
        PlaybackControlledWindowsOutputValidationKind kind) => new(
            PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentMilestone,
            kind,
            PlaybackFfmpegBackendCandidate.BuildId,
            FixtureSha256,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 1,
            DeliveryAttemptCount: 1,
            DeliveredCount: 1,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true,
            ExternalBinaryOrPayloadPublished: false);
}
