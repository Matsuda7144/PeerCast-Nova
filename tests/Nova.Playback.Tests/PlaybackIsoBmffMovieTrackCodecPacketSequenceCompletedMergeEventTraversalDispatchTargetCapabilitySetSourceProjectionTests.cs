using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjectionTests
{
    [Fact]
    public void Project_RetainsRequestSourceAndCallerDeclarations()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjectionTests
                .Projection();
        var declarations = Declarations(source);

        var projection = Project(source, declarations);
        Assert.Same(source, projection.Source);
        Assert.Equal(
            declarations.Length,
            projection.CapabilitySet.Capabilities.Count);
        for (var index = 0; index < declarations.Length; index++)
        {
            Assert.Equal(
                declarations[index],
                projection.CapabilitySet.Capabilities[index].Declaration);
        }
    }

    [Fact]
    public void Project_RejectsMissingSourceAndDeclarations()
    {
        AssertResult(
            Result.MissingRequestSourceProjection,
            null,
            null);
        AssertResult(
            Result.MissingDeclaration,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjectionTests
                .Projection(),
            null);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjection
            Projection()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjectionTests
                .Projection();
        return Project(source, Declarations(source));
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection
                    source,
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration>
                    declarations)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjector
                .Project(source, declarations, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration[]
            Declarations(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection
                    source) =>
        source.RequestProjection.Requests
            .GroupBy(request => new { request.TargetId, request.MediaKind })
            .Select(group => new
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration(
                    group.Key.TargetId,
                    group.Key.MediaKind,
                    group.Max(request => request.Payload.Length)))
            .ToArray();

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestSourceProjection?
            source,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetSourceProjector
                .Project(source, declarations, out var projection));
        Assert.Null(projection);
    }
}
