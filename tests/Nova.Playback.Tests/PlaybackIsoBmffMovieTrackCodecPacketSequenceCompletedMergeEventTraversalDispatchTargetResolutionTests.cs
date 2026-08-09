using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionTests
{
    [Fact]
    public void Resolve_RetainsExactRequestsAndDeclaredCapabilities()
    {
        var source = RequestProjection();
        var capabilitySet = CapabilitySet(source);

        Assert.Equal(
            Result.Resolved,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolver
                .Resolve(source, capabilitySet, out var projection));
        var resolved = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection>(
                projection);
        Assert.Same(source, resolved.Source);
        Assert.Same(capabilitySet, resolved.CapabilitySet);
        Assert.Equal(source.Requests.Count, resolved.Resolutions.Count);
        for (var index = 0; index < resolved.Resolutions.Count; index++)
        {
            var resolution = resolved.Resolutions[index];
            Assert.Same(source.Requests[index], resolution.Request);
            Assert.Equal(
                resolution.Request.TargetId,
                resolution.Capability.TargetId);
            Assert.Equal(
                resolution.Request.MediaKind,
                resolution.Capability.MediaKind);
            Assert.True(
                resolution.Request.Payload.Length <=
                resolution.Capability.MaximumPayloadByteCount);
        }
    }

    [Fact]
    public void Resolve_RejectsMissingInputs()
    {
        var source = RequestProjection();
        var capabilitySet = CapabilitySet(source);
        AssertResult(Result.MissingRequestProjection, null, capabilitySet);
        AssertResult(Result.MissingCapabilitySet, source, null);
    }

    [Fact]
    public void Resolve_RejectsMissingTargetCapability()
    {
        var source = RequestProjection();
        var first = source.Requests[0];
        var capabilitySet = CapabilitySet([
            new(
                first.TargetId,
                first.MediaKind,
                first.Payload.Length),
        ]);

        AssertResult(Result.MissingCapability, source, capabilitySet);
    }

    [Fact]
    public void Resolve_RejectsPayloadBeyondDeclaredLimit()
    {
        var source = RequestProjection();
        var declarations = CapabilityDeclarations(source);
        var firstRequest = source.Requests[0];
        var declarationIndex = Array.FindIndex(
            declarations,
            declaration => declaration.TargetId == firstRequest.TargetId);
        declarations[declarationIndex] = declarations[declarationIndex] with
        {
            MaximumPayloadByteCount = firstRequest.Payload.Length - 1,
        };

        AssertResult(
            Result.PayloadLimitExceeded,
            source,
            CapabilitySet(declarations));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection?
            source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet?
            capabilitySet)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolver
                .Resolve(source, capabilitySet, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
            RequestProjection()
    {
        var bindings =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
                .Bind(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
                        .Selection(),
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingTests
                        .TargetSet(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                .Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjector
                .TryProject(bindings, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet
            CapabilitySet(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
                    source) =>
        CapabilitySet(CapabilityDeclarations(source));

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration[]
            CapabilityDeclarations(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
                    source) =>
        source.Requests
            .GroupBy(request => new { request.TargetId, request.MediaKind })
            .Select(group => new
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration(
                    group.Key.TargetId,
                    group.Key.MediaKind,
                    group.Max(request => request.Payload.Length)))
            .ToArray();

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet
            CapabilitySet(
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration>
                    declarations)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetFactory
                .TryCreate(declarations, out var capabilitySet));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet>(
                capabilitySet);
    }
}
