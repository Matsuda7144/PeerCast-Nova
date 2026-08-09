namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionTests
{
    [Fact]
    public void Resolve_RetainsExactRequestsAndDeclaredCapabilities()
    {
        var source = RequestProjection();
        var capabilitySet = CapabilitySet(source);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .Resolved,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolver
                .Resolve(source, capabilitySet, out var projection));
        var resolved = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection>(
                projection);
        Assert.Same(source, resolved.Source);
        Assert.Same(capabilitySet, resolved.CapabilitySet);
        Assert.Equal(source.Requests.Count, resolved.Resolutions.Count);
        foreach (var resolution in resolved.Resolutions)
        {
            Assert.Same(
                source.Requests.Single(
                    request => request.TargetId ==
                        resolution.Capability.TargetId),
                resolution.Request);
            Assert.Equal(resolution.Request.MediaKind,
                resolution.Capability.MediaKind);
            Assert.True(resolution.Request.Payload.Length <=
                resolution.Capability.MaximumPayloadByteCount);
        }
    }

    [Fact]
    public void Resolve_RejectsMissingInputs()
    {
        var source = RequestProjection();
        var capabilitySet = CapabilitySet(source);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .MissingRequestProjection,
            null,
            capabilitySet);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .MissingCapabilitySet,
            source,
            null);
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

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .MissingCapability,
            source,
            capabilitySet);
    }

    [Fact]
    public void Resolve_RejectsPayloadBeyondDeclaredLimit()
    {
        var source = RequestProjection();
        var declarations = source.Requests.Select(request => new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration(
                request.TargetId,
                request.MediaKind,
                request.Payload.Length)).ToArray();
        declarations[0] = declarations[0] with
        {
            MaximumPayloadByteCount = declarations[0].MaximumPayloadByteCount -
                1,
        };

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .PayloadLimitExceeded,
            source,
            CapabilitySet(declarations));
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection?
            source,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet?
            capabilitySet)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolver
                .Resolve(source, capabilitySet, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection
        RequestProjection()
    {
        var bindings =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionTests
                .BindingProjection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjector
                .Project(bindings, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet
        CapabilitySet(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection
                source) =>
        CapabilitySet(source.Requests.Select(request => new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration(
                request.TargetId,
                request.MediaKind,
                request.Payload.Length)).ToArray());

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet
        CapabilitySet(
            IReadOnlyList<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration>
                declarations)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetFactory
                .TryCreate(declarations, out var capabilitySet));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet>(
                capabilitySet);
    }
}
