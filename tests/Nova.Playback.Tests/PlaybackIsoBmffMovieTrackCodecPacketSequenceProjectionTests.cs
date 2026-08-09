using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionTests
{
    [Fact]
    public void Project_RetainsExactMovieBindingAndAscendingPackets()
    {
        var source =
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionTests
                .Projection();

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Same(source.MovieTiming, projection.Sequence.MovieTiming);
        Assert.Same(source.Source.TrackIdentity,
            projection.Sequence.TrackIdentity);
        Assert.Equal(source.Entries.Count, projection.Sequence.Entries.Count);
        for (var index = 0; index < projection.Sequence.Entries.Count; index++)
        {
            var entry = projection.Sequence.Entries[index];
            Assert.Equal(index, entry.SequenceIndex);
            Assert.Same(source.Entries[index].MovieTrackPacket, entry.Packet);
        }
    }

    [Fact]
    public void Project_RejectsMissingSource()
    {
        Assert.Equal(
            Result.MissingMoviePacketBindingProjection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjector.Project(
                null,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection
        Project(
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjector.Project(
                source,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection>(projection);
    }
}
