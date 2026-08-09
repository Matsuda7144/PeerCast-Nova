using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionTests
{
    [Fact]
    public void TryProject_RetainsExactBindingPayloadAndDeadline()
    {
        var source = BindingProjection();

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Equal(source.Bindings.Count, projection.Requests.Count);
        for (var index = 0; index < projection.Requests.Count; index++)
        {
            var request = projection.Requests[index];
            var binding = source.Bindings[index];
            Assert.Same(binding, request.Binding);
            Assert.Equal(binding.Target.TargetId, request.TargetId);
            Assert.Equal(binding.Target.MediaKind, request.MediaKind);
            Assert.Same(
                binding.Candidate.Event.Packet.TrackPacket.Packet,
                request.Packet);
            Assert.True(request.Payload.Span.SequenceEqual(
                request.Packet.Payload.Span));
            Assert.Same(
                binding.Candidate.Eligibility.Observation.Deadline,
                request.Deadline);
        }
    }

    [Fact]
    public void TryProject_RejectsMissingAndMismatchedBindingOrder()
    {
        AssertResult(Result.MissingTargetBindingProjection, null);
        var source = BindingProjection();
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection(
                source.Selection,
                source.TargetSet,
                Array.AsReadOnly(source.Bindings.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, mismatched);
    }

    [Fact]
    public void TryProject_RejectsForeignTarget()
    {
        var source = BindingProjection();
        var bindings = source.Bindings.ToArray();
        bindings[0] = new(
            bindings[0].Candidate,
            new(Guid.NewGuid(), bindings[0].Target.MediaKind));
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection(
                source.Selection,
                source.TargetSet,
                Array.AsReadOnly(bindings));
        AssertResult(Result.TargetMismatch, mismatched);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjector
                .TryProject(source, out var projection));
        Assert.Null(projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjector
                .TryProject(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
            BindingProjection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
            .Bind(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
                    .Selection(),
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
                    .TargetSet(Guid.NewGuid(), Guid.NewGuid()));
}
