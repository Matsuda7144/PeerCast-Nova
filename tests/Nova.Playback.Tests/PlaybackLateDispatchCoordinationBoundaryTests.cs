namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateDispatchCoordinationBoundaryTests
{
    [Theory]
    [InlineData(PlaybackTimedSchedulerDispatchResult.VideoLate,
        PlaybackPolicyMediaKind.Video,
        PlaybackLateDispatchCoordinationResult.RetainedVideoLate)]
    [InlineData(PlaybackTimedSchedulerDispatchResult.AudioLate,
        PlaybackPolicyMediaKind.Audio,
        PlaybackLateDispatchCoordinationResult.RetainedAudioLate)]
    public void Coordinate_DefaultPolicy_RetainsLateItem(
        PlaybackTimedSchedulerDispatchResult dispatchResult,
        PlaybackPolicyMediaKind mediaKind,
        PlaybackLateDispatchCoordinationResult expected)
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        Assert.Equal(expected, boundary.Coordinate(Request(dispatchResult, mediaKind)));
        Assert.Equal(PlaybackLateDispatchCoordinationState.Ready, boundary.Snapshot.State);
    }

    [Fact]
    public void Coordinate_ConfiguredVideoPolicy_ReturnsDropCandidateWithoutConsuming()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions
        {
            VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
        });
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        Assert.Equal(PlaybackLateDispatchCoordinationResult.VideoDropCandidate,
            boundary.Coordinate(Request(
                PlaybackTimedSchedulerDispatchResult.VideoLate,
                PlaybackPolicyMediaKind.Video)));
        Assert.Equal(PlaybackLateDispatchCoordinationState.Ready, boundary.Snapshot.State);
    }

    [Fact]
    public void Coordinate_ConfiguredAudioPolicy_ReturnsRecoveryRequired()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions
        {
            AudioLatePolicy = PlaybackAudioLatePolicy.RequireRecovery,
        });
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        Assert.Equal(PlaybackLateDispatchCoordinationResult.AudioRecoveryRequired,
            boundary.Coordinate(Request(
                PlaybackTimedSchedulerDispatchResult.AudioLate,
                PlaybackPolicyMediaKind.Audio)));
    }

    [Theory]
    [InlineData(PlaybackTimedSchedulerDispatchResult.VideoLate, PlaybackPolicyMediaKind.Audio)]
    [InlineData(PlaybackTimedSchedulerDispatchResult.AudioLate, PlaybackPolicyMediaKind.Video)]
    [InlineData(PlaybackTimedSchedulerDispatchResult.VideoProcessed, PlaybackPolicyMediaKind.Video)]
    [InlineData(PlaybackTimedSchedulerDispatchResult.WaitLimitReached, PlaybackPolicyMediaKind.Audio)]
    public void Coordinate_InvalidDispatchMediaPair_FailsClosed(
        PlaybackTimedSchedulerDispatchResult dispatchResult,
        PlaybackPolicyMediaKind mediaKind)
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        Assert.Equal(PlaybackLateDispatchCoordinationResult.InvalidInput,
            boundary.Coordinate(Request(dispatchResult, mediaKind)));
    }

    [Fact]
    public void Coordinate_GenerationMismatch_LatchesResetRequired()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        var mismatch = Request(
            PlaybackTimedSchedulerDispatchResult.VideoLate,
            PlaybackPolicyMediaKind.Video) with
        { Generation = 2 };

        Assert.Equal(PlaybackLateDispatchCoordinationResult.ResetRequired,
            boundary.Coordinate(mismatch));
        Assert.Equal(PlaybackLateDispatchCoordinationState.ResetRequired,
            boundary.Snapshot.State);
        Assert.Equal(PlaybackLateDispatchCoordinationResult.ResetRequired,
            boundary.Coordinate(Request(
                PlaybackTimedSchedulerDispatchResult.AudioLate,
                PlaybackPolicyMediaKind.Audio)));
    }

    [Fact]
    public void Coordinate_Discontinuity_LatchesResetRequired()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);
        var request = Request(
            PlaybackTimedSchedulerDispatchResult.VideoLate,
            PlaybackPolicyMediaKind.Video) with
        {
            Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
        };

        Assert.Equal(PlaybackLateDispatchCoordinationResult.ResetRequired,
            boundary.Coordinate(request));
    }

    [Fact]
    public void Reset_UpdatesBothBoundaryAndPolicyGenerations()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        using var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        Assert.True(boundary.Reset(3, 4));
        Assert.Equal(new PlaybackLateDispatchCoordinationSnapshot(
            3, 4, PlaybackLateDispatchCoordinationState.Ready), boundary.Snapshot);
        Assert.Equal(PlaybackLateDispatchCoordinationResult.RetainedAudioLate,
            boundary.Coordinate(Request(
                PlaybackTimedSchedulerDispatchResult.AudioLate,
                PlaybackPolicyMediaKind.Audio) with
            { Generation = 4 }));
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedPolicy()
    {
        using var policy = CreatePolicy(new PlaybackLateItemDispositionOptions());
        var boundary = new PlaybackLateDispatchCoordinationBoundary(policy, 1, 1);

        boundary.Dispose();
        boundary.Dispose();

        Assert.Equal(PlaybackLateDispatchCoordinationResult.Disposed,
            boundary.Coordinate(Request(
                PlaybackTimedSchedulerDispatchResult.VideoLate,
                PlaybackPolicyMediaKind.Video)));
        Assert.Equal(PlaybackLateItemDisposition.Retain,
            policy.Decide(new PlaybackLateItemDispositionRequest(
                PlaybackPolicyMediaKind.Video,
                PlaybackAvTimingDecisionResult.VideoLate,
                PlaybackDecodedFrameDiscontinuityKind.None,
                1,
                1)).Disposition);
    }

    private static PlaybackLateItemDispositionPolicyBoundary CreatePolicy(
        PlaybackLateItemDispositionOptions options) => new(options, 1, 1);

    private static PlaybackLateDispatchCoordinationRequest Request(
        PlaybackTimedSchedulerDispatchResult dispatchResult,
        PlaybackPolicyMediaKind mediaKind) => new(
            dispatchResult,
            mediaKind,
            PlaybackDecodedFrameDiscontinuityKind.None,
            1,
            1);
}
