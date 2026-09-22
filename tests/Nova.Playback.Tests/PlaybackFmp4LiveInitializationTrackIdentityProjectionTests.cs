using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackIdentityProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Project_TrackHeaderIdsPreserveInitializationOrder()
    {
        var segment = CreateInitializationSegment(
            CreateTrackHeader(7),
            CreateTrackHeader(9));

        var result = PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackIdentityProjection>(projection);
        Assert.Equal(2, projected.Tracks.Count);
        Assert.Same(projected.Source.Tracks[0], projected.Tracks[0].Track);
        Assert.Equal(7U, projected.Tracks[0].TrackId);
        Assert.Same(projected.Source.Tracks[1], projected.Tracks[1].Track);
        Assert.Equal(9U, projected.Tracks[1].TrackId);
    }

    [Fact]
    public void Project_MissingTrackHeaderIsRejected()
    {
        var segment = CreateInitializationSegment(Array.Empty<byte>());

        var result = PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .MissingTrackHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackHeaderIsRejected()
    {
        var segment = CreateInitializationSegment(
            Concat(CreateTrackHeader(7), CreateTrackHeader(8)));

        var result = PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .DuplicateTrackHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidTrackHeaderIsRejected()
    {
        var segment = CreateInitializationSegment(CreateTrackHeader(0));

        var result = PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .InvalidTrackHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackIdsAreRejected()
    {
        var segment = CreateInitializationSegment(
            CreateTrackHeader(7),
            CreateTrackHeader(7));

        var result = PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .DuplicateTrackId,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidInitializationEnvelopeIsRejected()
    {
        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .InvalidTrackInventory,
            PlaybackFmp4LiveInitializationTrackIdentityProjector.Project(
                ReadOnlySpan<byte>.Empty,
                1,
                out var projection));
        Assert.Null(projection);
    }

    private static byte[] CreateInitializationSegment(params byte[][] trackPayloads)
    {
        var trackBytes = 0;
        for (var index = 0; index < trackPayloads.Length; index++)
        {
            trackBytes = checked(trackBytes + 8 + trackPayloads[index].Length);
        }

        var movieLength = checked(8 + trackBytes);
        var segment = new byte[checked(8 + movieLength)];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), checked((uint)movieLength), Moov);

        var offset = 16;
        for (var index = 0; index < trackPayloads.Length; index++)
        {
            var payload = trackPayloads[index];
            var trackLength = checked(8 + payload.Length);
            WriteHeader(
                segment.AsSpan(offset, 8),
                checked((uint)trackLength),
                Trak);
            payload.AsSpan().CopyTo(segment.AsSpan(offset + 8, payload.Length));
            offset += trackLength;
        }

        return segment;
    }

    private static byte[] CreateTrackHeader(uint trackId)
    {
        var box = new byte[TkhdBoxLength];
        WriteHeader(box.AsSpan(0, 8), TkhdBoxLength, Tkhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), trackId);
        return box;
    }

    private static byte[] Concat(params byte[][] boxes)
    {
        var length = 0;
        for (var index = 0; index < boxes.Length; index++)
        {
            length = checked(length + boxes[index].Length);
        }

        var result = new byte[length];
        var offset = 0;
        for (var index = 0; index < boxes.Length; index++)
        {
            boxes[index].AsSpan().CopyTo(result.AsSpan(offset));
            offset += boxes[index].Length;
        }

        return result;
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
