namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackControlledWindowsOutputEvidenceBoundaryTests
{
    private static readonly string FixtureSha256 = new('A', 64);

    [Fact]
    public void SystemWindowsVideoEvidenceIsAcceptedOnce()
    {
        using var boundary = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        var result = boundary.ValidateAndConsume(VideoEvidence());

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted, result);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceState.Accepted, boundary.Snapshot.State);
        Assert.Equal(PlaybackControlledWindowsOutputValidationKind.Video, boundary.Snapshot.Kind);
        Assert.Equal(PlaybackFfmpegBackendCandidate.BuildId, boundary.Snapshot.RuntimeBuildId);
        Assert.Equal(FixtureSha256, boundary.Snapshot.FixtureSha256);
        Assert.Equal(7, boundary.Snapshot.DecoderGeneration);
        Assert.Equal(7, boundary.Snapshot.OutputGeneration);
        Assert.True(boundary.Snapshot.HasAcceptedEvidence);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.AlreadyCompleted,
            boundary.ValidateAndConsume(VideoEvidence()));
    }

    [Fact]
    public void SystemWindowsAudioEvidenceAcceptsOneStartAndExactBufferProgress()
    {
        using var boundary = Create(PlaybackControlledWindowsOutputValidationKind.Audio);

        var result = boundary.ValidateAndConsume(AudioEvidence());

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Accepted, result);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceState.Accepted, boundary.Snapshot.State);
        Assert.True(boundary.Snapshot.HasAcceptedEvidence);
    }

    [Fact]
    public void InjectedEvidenceCannotClaimPhysicalOutput()
    {
        using var boundary = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        var result = boundary.ValidateAndConsume(VideoEvidence() with
        {
            Source = PlaybackControlledWindowsOutputEvidenceSource.Injected,
        });

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.SourceRejected, result);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceState.Rejected, boundary.Snapshot.State);
        Assert.False(boundary.Snapshot.HasAcceptedEvidence);
    }

    [Fact]
    public void RuntimeFixtureAndGenerationMustMatchExactly()
    {
        using var runtime = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var fixture = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var generation = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.RuntimeRejected,
            runtime.ValidateAndConsume(VideoEvidence() with { RuntimeBuildId = "other" }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.FixtureRejected,
            fixture.ValidateAndConsume(VideoEvidence() with
            {
                FixtureSha256 = new string('B', 64),
            }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.GenerationMismatch,
            generation.ValidateAndConsume(VideoEvidence() with { OutputGeneration = 8 }));
    }

    [Fact]
    public void HiddenRetryFallbackAndRecreationAreRejected()
    {
        using var hiddenRetry = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var fallback = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var recreation = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.RetryPolicyRejected,
            hiddenRetry.ValidateAndConsume(VideoEvidence() with { HiddenRetryCount = 1 }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.RetryPolicyRejected,
            fallback.ValidateAndConsume(VideoEvidence() with { DeviceFallbackCount = 1 }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.RetryPolicyRejected,
            recreation.ValidateAndConsume(VideoEvidence() with { DeviceRecreationCount = 1 }));
    }

    [Fact]
    public void DeterministicPhysicalAcknowledgementIsRequired()
    {
        using var video = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var audio = Create(PlaybackControlledWindowsOutputValidationKind.Audio);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.PhysicalAcknowledgementRequired,
            video.ValidateAndConsume(VideoEvidence() with
            {
                Video = VideoEvidence().Video! with { AcceptedByteCount = 15 },
            }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.PhysicalAcknowledgementRequired,
            audio.ValidateAndConsume(AudioEvidence() with
            {
                Audio = AudioEvidence().Audio! with { AcceptedByteCount = 4_095 },
            }));
    }

    [Fact]
    public void ProductionFirstOwnerThreadCleanupIsRequired()
    {
        using var wrongOrder = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var wrongThread = Create(PlaybackControlledWindowsOutputValidationKind.Video);
        using var borrowedOwnerLost = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.CleanupRejected,
            wrongOrder.ValidateAndConsume(VideoEvidence() with
            {
                Cleanup = Cleanup() with { BufferDisposeOrder = 5 },
            }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.CleanupRejected,
            wrongThread.ValidateAndConsume(VideoEvidence() with
            {
                Cleanup = Cleanup() with { OwnerThreadTeardown = false },
            }));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.CleanupRejected,
            borrowedOwnerLost.ValidateAndConsume(VideoEvidence() with
            {
                Cleanup = Cleanup() with { BorrowedDispatcherPreserved = false },
            }));
    }

    [Fact]
    public void InvalidExpectedDigestFailsClosedAndDisposeClearsAcceptedEvidence()
    {
        using var invalid = new PlaybackControlledWindowsOutputEvidenceBoundary(
            PlaybackControlledWindowsOutputValidationKind.Video,
            "invalid");
        var accepted = Create(PlaybackControlledWindowsOutputValidationKind.Video);

        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.InvalidEvidence,
            invalid.ValidateAndConsume(VideoEvidence()));
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            accepted.ValidateAndConsume(VideoEvidence()));

        accepted.Dispose();

        Assert.Equal(PlaybackControlledWindowsOutputEvidenceState.Disposed, accepted.Snapshot.State);
        Assert.Equal(PlaybackControlledWindowsOutputEvidenceResult.Disposed, accepted.Snapshot.Result);
        Assert.False(accepted.Snapshot.HasAcceptedEvidence);
        Assert.Equal(
            PlaybackControlledWindowsOutputEvidenceResult.Disposed,
            accepted.ValidateAndConsume(VideoEvidence()));
    }

    private static PlaybackControlledWindowsOutputEvidenceBoundary Create(
        PlaybackControlledWindowsOutputValidationKind kind) => new(kind, FixtureSha256);

    private static PlaybackControlledWindowsOutputEvidence VideoEvidence() => new(
        PlaybackControlledWindowsOutputValidationKind.Video,
        PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
        PlaybackFfmpegBackendCandidate.BuildId,
        FixtureSha256.ToLowerInvariant(),
        DecoderGeneration: 7,
        OutputGeneration: 7,
        DeliveryAttemptCount: 2,
        DeliveredCount: 1,
        RetainedWouldBlockCount: 1,
        HiddenRetryCount: 0,
        DeviceFallbackCount: 0,
        DeviceRecreationCount: 0,
        new(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            Width: 2,
            Height: 2,
            Stride: 8,
            PresentAttemptCount: 1,
            PresentAcknowledgementCount: 1,
            AcceptedByteCount: 16,
            ResizeCount: 0,
            DeviceLostCount: 0),
        Audio: null,
        Cleanup());

    private static PlaybackControlledWindowsOutputEvidence AudioEvidence() => new(
        PlaybackControlledWindowsOutputValidationKind.Audio,
        PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
        PlaybackFfmpegBackendCandidate.BuildId,
        FixtureSha256,
        DecoderGeneration: 7,
        OutputGeneration: 7,
        DeliveryAttemptCount: 2,
        DeliveredCount: 1,
        RetainedWouldBlockCount: 1,
        HiddenRetryCount: 0,
        DeviceFallbackCount: 0,
        DeviceRecreationCount: 0,
        Video: null,
        new(
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            SampleRate: 48_000,
            ChannelCount: 2,
            BufferDuration: TimeSpan.FromMilliseconds(100),
            WriteAttemptCount: 1,
            WriteAcknowledgementCount: 1,
            AcceptedFrameCount: 1_024,
            AcceptedByteCount: 4_096,
            StartAcknowledgementCount: 1,
            DeviceLostCount: 0),
        Cleanup());

    private static PlaybackControlledWindowsOutputCleanupEvidence Cleanup() => new(
        ProductionStopOrder: 1,
        BufferDisposeOrder: 2,
        StreamDisposeOrder: 3,
        BindingDisposeOrder: 4,
        ActivationDisposeOrder: 5,
        OwnerThreadTeardown: true,
        BorrowedWindowPreserved: true,
        BorrowedDispatcherPreserved: true);
}
