using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaFragmentIdentityProjectionTests
{
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;

    [Fact]
    public void Project_SequenceNumberAndTrackIdentitiesAreProjected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(17),
            TrackFragment(1),
            TrackFragment(2));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentIdentityProjection>(projection);
        Assert.Equal((uint)17, projected.SequenceNumber);
        Assert.Collection(
            projected.Tracks,
            track => Assert.Equal((uint)1, track.TrackId),
            track => Assert.Equal((uint)2, track.TrackId));
    }

    [Fact]
    public void Project_MissingMovieFragmentHeaderIsRejected()
    {
        var movieFragment = Box(Moof, TrackFragment(1));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .MissingMovieFragmentHeader,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateMovieFragmentHeaderIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(17),
            MovieFragmentHeader(18),
            TrackFragment(1));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .DuplicateMovieFragmentHeader,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MissingTrackFragmentHeaderIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(17),
            Box(Traf));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .MissingTrackFragmentHeader,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackFragmentHeaderIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(17),
            Box(
                Traf,
                TrackFragmentHeader(1),
                TrackFragmentHeader(2)));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .DuplicateTrackFragmentHeader,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackIdIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(17),
            TrackFragment(1),
            TrackFragment(1));

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            movieFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .DuplicateTrackId,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_TrackFragmentCountIsBounded()
    {
        var children = new List<byte[]> { MovieFragmentHeader(17) };
        for (var trackId = 1;
             trackId <= PlaybackFmp4LiveMediaFragmentIdentityProjector
                 .MaximumTrackFragmentCount;
             trackId++)
        {
            children.Add(TrackFragment((uint)trackId));
        }

        var maximumFragment = Box(Moof, children.ToArray());
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                maximumFragment,
                out var maximumProjection));
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjector
                .MaximumTrackFragmentCount,
            maximumProjection!.Tracks.Count);

        children.Add(TrackFragment(
            (uint)PlaybackFmp4LiveMediaFragmentIdentityProjector
                .MaximumTrackFragmentCount + 1));
        var overBoundFragment = Box(Moof, children.ToArray());

        var result = PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
            overBoundFragment,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .TrackFragmentLimitExceeded,
            result);
        Assert.Null(projection);
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(uint trackId) =>
        Box(Traf, TrackFragmentHeader(trackId));

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        return box;
    }

    private static byte[] Box(uint type, params byte[][] children)
    {
        var box = new byte[8 + children.Sum(child => child.Length)];
        WriteHeader(box, type);
        var offset = 8;
        foreach (var child in children)
        {
            child.CopyTo(box, offset);
            offset += child.Length;
        }

        return box;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            checked((uint)box.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
