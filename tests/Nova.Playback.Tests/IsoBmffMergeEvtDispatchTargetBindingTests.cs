using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
{
    [Fact]
    public void Bind_RetainsExactCandidatesAndOpaqueMediaTargets()
    {
        var selection = Selection();
        var targetSet = TargetSet(Guid.NewGuid(), Guid.NewGuid());

        var projection = Bind(selection, targetSet);
        Assert.Same(selection, projection.Selection);
        Assert.Same(targetSet, projection.TargetSet);
        Assert.Equal(selection.Candidates.Count, projection.Bindings.Count);
        foreach (var binding in projection.Bindings)
        {
            Assert.Equal(
                binding.Candidate.Event.Packet.TrackPacket.MediaKind,
                binding.Target.MediaKind);
        }
    }

    [Fact]
    public void Bind_RejectsMissingInputsAndMediaTarget()
    {
        var selection = Selection();
        var targetSet = TargetSet(Guid.NewGuid(), Guid.NewGuid());
        AssertResult(Result.MissingCandidateSelection, null, targetSet);
        AssertResult(Result.MissingTargetSet, selection, null);
        AssertResult(
            Result.MissingMediaTarget,
            selection,
            TargetSet(Guid.NewGuid(), null));
    }

    [Fact]
    public void Bind_RejectsForeignCandidateEligibility()
    {
        var selection = Selection();
        var foreign = Selection();
        var candidates = selection.Candidates.ToArray();
        candidates[0] = new(0, foreign.Candidates[0].Eligibility);
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection(
                selection.Source,
                selection.MaximumCandidateCount,
                selection.EligibleCount,
                Array.AsReadOnly(candidates));

        AssertResult(
            Result.SourceMismatch,
            mismatched,
            TargetSet(Guid.NewGuid(), Guid.NewGuid()));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection?
            selection,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet?
            targetSet)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinder
                .Bind(selection, targetSet, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
            Bind(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
                    selection,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
                    targetSet)
    {
        Assert.Equal(
            Result.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinder
                .Bind(selection, targetSet, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
            Selection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionTests
            .Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionTests
                    .Eligibility(1_000, Policy.ReadyOrExpired),
                16);

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
            TargetSet(Guid? videoTargetId, Guid? audioTargetId) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetTests
            .TargetSet(videoTargetId, audioTargetId);
}
