namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionTests
{
    [Fact]
    public void Project_RetainsExactBindingPacketPayloadAndDeadline()
    {
        var source = BindingProjection();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjector
                .Project(source, out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection>(
                projection);
        Assert.Same(source, projected.Source);
        Assert.Equal(source.Bindings.Count, projected.Requests.Count);
        for (var index = 0; index < projected.Requests.Count; index++)
        {
            var request = projected.Requests[index];
            var binding = source.Bindings[index];
            Assert.Same(binding, request.Binding);
            Assert.Equal(binding.Target.TargetId, request.TargetId);
            Assert.Equal(binding.Target.MediaKind, request.MediaKind);
            Assert.Same(binding.Candidate.Entry.Event.Packet, request.Packet);
            Assert.True(request.Payload.Span.SequenceEqual(request.Packet.Payload.Span));
            Assert.Same(
                binding.Candidate.Eligibility.Observation.Deadline,
                request.Deadline);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .MissingTargetBindingProjection,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjector
                .Project(null, out var projection));
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsMismatchedBindingOrder()
    {
        var source = BindingProjection();
        var bindings = source.Bindings.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection(
                source.Selection,
                source.TargetSet,
                Array.AsReadOnly(bindings));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .SourceMismatch,
            mismatched);
    }

    [Fact]
    public void Project_RejectsForeignTargetBinding()
    {
        var source = BindingProjection();
        var foreignTarget = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget(
                Guid.NewGuid(),
                source.Bindings[0].Target.MediaKind);
        var bindings = source.Bindings.ToArray();
        bindings[0] = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding(
                bindings[0].Candidate,
                foreignTarget);
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection(
                source.Selection,
                source.TargetSet,
                Array.AsReadOnly(bindings));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .TargetMismatch,
            mismatched);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection
        BindingProjection()
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
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
                .TryCreate(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    out var targetSet));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .Bound,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinder.Bind(
                selection,
                targetSet,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection>(
                projection);
    }
}
