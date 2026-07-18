namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackLateItemDispositionPolicyBoundaryTests
{
    private static readonly PlaybackLateItemDispositionOptions CandidateOptions = new()
    {
        VideoLatePolicy = PlaybackVideoLatePolicy.MarkDropCandidate,
        AudioLatePolicy = PlaybackAudioLatePolicy.RequireRecovery,
    };

    [Theory]
    [InlineData(PlaybackPolicyMediaKind.Video, PlaybackAvTimingDecisionResult.VideoLate)]
    [InlineData(PlaybackPolicyMediaKind.Audio, PlaybackAvTimingDecisionResult.AudioLate)]
    public void Decide_DefaultPolicy_Retains(
        PlaybackPolicyMediaKind kind,
        PlaybackAvTimingDecisionResult timingResult)
    {
        using var boundary = new PlaybackLateItemDispositionPolicyBoundary(
            new PlaybackLateItemDispositionOptions(), 1, 1);

        Assert.Equal(PlaybackLateItemDisposition.Retain,
            boundary.Decide(Request(kind, timingResult)).Disposition);
    }

    [Fact]
    public void Decide_VideoLate_CanMarkDropCandidateWithoutConsuming()
    {
        using var boundary = Create();
        var decision = boundary.Decide(Request(
            PlaybackPolicyMediaKind.Video,
            PlaybackAvTimingDecisionResult.VideoLate));

        Assert.Equal(PlaybackLateItemDisposition.VideoDropCandidate,
            decision.Disposition);
        Assert.Equal(PlaybackAvTimingDecisionResult.VideoLate,
            decision.TimingResult);
        Assert.Equal(PlaybackLateItemDispositionState.Ready,
            boundary.Snapshot.State);
    }

    [Fact]
    public void Decide_AudioLate_CanRequireRecovery()
    {
        using var boundary = Create();

        Assert.Equal(PlaybackLateItemDisposition.AudioRecoveryRequired,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Audio,
                PlaybackAvTimingDecisionResult.AudioLate)).Disposition);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void Decide_InvalidMediaKind_FailsClosed(int value)
    {
        using var boundary = Create();

        Assert.Equal(PlaybackLateItemDisposition.InvalidInput,
            boundary.Decide(Request(
                (PlaybackPolicyMediaKind)value,
                PlaybackAvTimingDecisionResult.VideoLate)).Disposition);
    }

    [Theory]
    [InlineData(PlaybackPolicyMediaKind.Video, PlaybackAvTimingDecisionResult.AudioLate)]
    [InlineData(PlaybackPolicyMediaKind.Audio, PlaybackAvTimingDecisionResult.VideoLate)]
    [InlineData(PlaybackPolicyMediaKind.Video, PlaybackAvTimingDecisionResult.ProcessNow)]
    [InlineData(PlaybackPolicyMediaKind.Audio, PlaybackAvTimingDecisionResult.Wait)]
    public void Decide_ResultThatDoesNotMatchLateMedia_FailsClosed(
        PlaybackPolicyMediaKind kind,
        PlaybackAvTimingDecisionResult timingResult)
    {
        using var boundary = Create();

        Assert.Equal(PlaybackLateItemDisposition.InvalidInput,
            boundary.Decide(Request(kind, timingResult)).Disposition);
    }

    [Fact]
    public void Decide_UndefinedTimingResult_FailsClosed()
    {
        using var boundary = Create();

        Assert.Equal(PlaybackLateItemDisposition.InvalidInput,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Video,
                (PlaybackAvTimingDecisionResult)99)).Disposition);
    }

    [Fact]
    public void Decide_GenerationMismatch_LatchesResetRequired()
    {
        using var boundary = Create();
        var mismatch = Request(
            PlaybackPolicyMediaKind.Video,
            PlaybackAvTimingDecisionResult.VideoLate) with
        {
            Generation = 2,
        };

        Assert.Equal(PlaybackLateItemDisposition.ResetRequired,
            boundary.Decide(mismatch).Disposition);
        Assert.Equal(PlaybackLateItemDispositionState.ResetRequired,
            boundary.Snapshot.State);
        Assert.Equal(PlaybackLateItemDisposition.ResetRequired,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Audio,
                PlaybackAvTimingDecisionResult.AudioLate)).Disposition);
    }

    [Fact]
    public void Decide_Discontinuity_LatchesResetRequired()
    {
        using var boundary = Create();
        var request = Request(
            PlaybackPolicyMediaKind.Video,
            PlaybackAvTimingDecisionResult.VideoLate) with
        {
            Discontinuity = PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
        };

        Assert.Equal(PlaybackLateItemDisposition.ResetRequired,
            boundary.Decide(request).Disposition);
    }

    [Fact]
    public void Reset_UpdatesIndependentGenerationsAndRestoresReady()
    {
        using var boundary = Create();
        boundary.Decide(Request(
            PlaybackPolicyMediaKind.Video,
            PlaybackAvTimingDecisionResult.VideoLate) with
        {
            Generation = 2,
        });

        Assert.True(boundary.Reset(3, 4));
        Assert.Equal(new PlaybackLateItemDispositionSnapshot(
            3, 4, PlaybackLateItemDispositionState.Ready), boundary.Snapshot);
        Assert.Equal(PlaybackLateItemDisposition.AudioRecoveryRequired,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Audio,
                PlaybackAvTimingDecisionResult.AudioLate) with
            {
                Generation = 4,
            }).Disposition);
    }

    [Fact]
    public void Fault_FailsClosedUntilReset()
    {
        using var boundary = Create();
        boundary.Fault();

        Assert.Equal(PlaybackLateItemDisposition.Failure,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Video,
                PlaybackAvTimingDecisionResult.VideoLate)).Disposition);
        Assert.True(boundary.Reset(1, 1));
        Assert.Equal(PlaybackLateItemDisposition.VideoDropCandidate,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Video,
                PlaybackAvTimingDecisionResult.VideoLate)).Disposition);
    }

    [Fact]
    public void Dispose_IsIdempotentAndRejectsFurtherUse()
    {
        var boundary = Create();
        boundary.Dispose();
        boundary.Dispose();

        Assert.Equal(PlaybackLateItemDisposition.Disposed,
            boundary.Decide(Request(
                PlaybackPolicyMediaKind.Video,
                PlaybackAvTimingDecisionResult.VideoLate)).Disposition);
        Assert.False(boundary.Reset(2, 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_UndefinedPolicy_IsRejected(bool video)
    {
        var invalid = video
            ? CandidateOptions with { VideoLatePolicy = (PlaybackVideoLatePolicy)99 }
            : CandidateOptions with { AudioLatePolicy = (PlaybackAudioLatePolicy)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlaybackLateItemDispositionPolicyBoundary(invalid, 1, 1));
    }

    private static PlaybackLateItemDispositionPolicyBoundary Create() =>
        new(CandidateOptions, 1, 1);

    private static PlaybackLateItemDispositionRequest Request(
        PlaybackPolicyMediaKind kind,
        PlaybackAvTimingDecisionResult timingResult) =>
        new(kind, timingResult,
            PlaybackDecodedFrameDiscontinuityKind.None, 1, 1);
}
