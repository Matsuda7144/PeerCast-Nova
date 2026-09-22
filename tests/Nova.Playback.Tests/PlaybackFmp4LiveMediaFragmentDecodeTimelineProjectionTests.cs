using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionTests
{
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;

    [Fact]
    public void Project_VersionZeroAndOneDecodeTimesAreProjected()
    {
        const uint versionZeroDecodeTime = 90_000;
        const ulong versionOneDecodeTime = 0x1_0000_0001UL;
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1, TrackDecodeTime(0, versionZeroDecodeTime)),
            TrackFragment(2, TrackDecodeTime(1, versionOneDecodeTime)));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(projection);
        Assert.Same(identity, projected.Source);
        Assert.Equal((uint)23, projected.SequenceNumber);
        Assert.Collection(
            projected.Tracks,
            track =>
            {
                Assert.Equal((uint)1, track.TrackId);
                Assert.Equal((byte)0, track.Version);
                Assert.Equal((ulong)versionZeroDecodeTime,
                    track.BaseMediaDecodeTime);
            },
            track =>
            {
                Assert.Equal((uint)2, track.TrackId);
                Assert.Equal((byte)1, track.Version);
                Assert.Equal(versionOneDecodeTime, track.BaseMediaDecodeTime);
            });
    }

    [Fact]
    public void Project_MissingTrackDecodeTimeIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .MissingTrackDecodeTime,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackDecodeTimeIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(
                1,
                TrackDecodeTime(0, 90_000),
                TrackDecodeTime(0, 90_001)));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .DuplicateTrackDecodeTime,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MalformedTrackDecodeTimeIsRejected()
    {
        var malformedDecodeTime = TrackDecodeTime(0, 90_000);
        malformedDecodeTime[8] = 2;
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1, malformedDecodeTime));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .InvalidTrackDecodeTime,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MissingIdentityProjectionIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1, TrackDecodeTime(0, 90_000)));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            null,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .MissingIdentityProjection,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_SequenceIdentityMismatchIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1, TrackDecodeTime(0, 90_000)));
        var differentFragment = Box(
            Moof,
            MovieFragmentHeader(24),
            TrackFragment(1, TrackDecodeTime(0, 90_000)));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                differentFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .IdentityProjectionMismatch,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_TrackIdentityMismatchIsRejected()
    {
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(1, TrackDecodeTime(0, 90_000)));
        var differentFragment = Box(
            Moof,
            MovieFragmentHeader(23),
            TrackFragment(2, TrackDecodeTime(0, 90_000)));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                differentFragment,
                out var identity));

        var result = PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
            movieFragment,
            identity,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .IdentityProjectionMismatch,
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

    private static byte[] TrackFragment(uint trackId, params byte[][] children)
    {
        var allChildren = new byte[children.Length + 1][];
        allChildren[0] = TrackFragmentHeader(trackId);
        children.CopyTo(allChildren, 1);
        return Box(Traf, allChildren);
    }

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        return box;
    }

    private static byte[] TrackDecodeTime(byte version, ulong value)
    {
        var box = new byte[version == 1 ? 20 : 16];
        WriteHeader(box, Tfdt);
        box[8] = version;
        if (version == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(12, 8), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
                checked((uint)value));
        }

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
