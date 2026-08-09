namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingTests
{
    [Fact]
    public void Bind_RetainsExactCandidatesAndMediaTargets()
    {
        var selection = Selection();
        var targetSet = TargetSet(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .Bound,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinder.Bind(
                selection,
                targetSet,
                out var projection));
        var bound = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection>(
                projection);
        Assert.Same(selection, bound.Selection);
        Assert.Same(targetSet, bound.TargetSet);
        Assert.Equal(selection.Candidates.Count, bound.Bindings.Count);
        for (var index = 0; index < bound.Bindings.Count; index++)
        {
            var binding = bound.Bindings[index];
            Assert.Same(selection.Candidates[index], binding.Candidate);
            Assert.Equal(
                binding.Candidate.Entry.Event.MediaKind,
                binding.Target.MediaKind);
        }
    }

    [Fact]
    public void Bind_RejectsMissingTargetForCandidateMediaKind()
    {
        var selection = Selection();
        var targetSet = TargetSet(Guid.NewGuid(), null);

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .MissingMediaTarget,
            selection,
            targetSet);
    }

    [Fact]
    public void Bind_RejectsMissingInputs()
    {
        var selection = Selection();
        var targetSet = TargetSet(Guid.NewGuid(), Guid.NewGuid());
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .MissingCandidateSelection,
            null,
            targetSet);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .MissingTargetSet,
            selection,
            null);
    }

    [Fact]
    public void Bind_RejectsForeignCandidateEligibility()
    {
        var selection = Selection();
        var foreign = Selection();
        var candidates = selection.Candidates.ToArray();
        candidates[0] = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate(
                0,
                foreign.Candidates[0].Eligibility);
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection(
                selection.Source,
                selection.MaximumCandidateCount,
                selection.EligibleCount,
                Array.AsReadOnly(candidates));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .SourceMismatch,
            mismatched,
            TargetSet(Guid.NewGuid(), Guid.NewGuid()));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection?
            selection,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet? targetSet)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinder.Bind(
                selection,
                targetSet,
                out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection
        Selection()
    {
        var observation =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityTests
                .Observation(1_000);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjector
                .Project(
                    observation,
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                        .ReadyOrExpired,
                    out var eligibility));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .Selected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelector
                .Select(eligibility, 2, out var selection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection>(
                selection);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet
        TargetSet(Guid? videoTargetId, Guid? audioTargetId)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
                .TryCreate(videoTargetId, audioTargetId, out var targetSet));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet>(
                targetSet);
    }
}
