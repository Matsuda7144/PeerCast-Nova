using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Stsd = 0x73747364;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Avc1 = 0x61766331;
    private const uint Avc3 = 0x61766333;
    private const uint Mp4a = 0x6d703461;
    private const uint Unsupported = 0x78787878;

    [Fact]
    public void Project_Avc1VideoTrackIsAccepted()
    {
        var segment = new byte[105];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 97, Moov);
        WriteHeader(segment.AsSpan(16, 8), 89, Trak);
        WriteHeader(segment.AsSpan(24, 8), 81, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 40, Minf);
        WriteHeader(segment.AsSpan(73, 8), 32, Stbl);
        WriteSampleDescription(segment.AsSpan(81, 24), Avc1);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection>(
                projection);
        var track = Assert.Single(projected.Source.Tracks);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video, track.Kind);
        Assert.Equal(Vide, track.Handler.HandlerType);
    }

    [Fact]
    public void Project_Avc3VideoTrackIsAccepted()
    {
        var segment = new byte[105];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 97, Moov);
        WriteHeader(segment.AsSpan(16, 8), 89, Trak);
        WriteHeader(segment.AsSpan(24, 8), 81, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 40, Minf);
        WriteHeader(segment.AsSpan(73, 8), 32, Stbl);
        WriteSampleDescription(segment.AsSpan(81, 24), Avc3);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection>(
                projection);
        var track = Assert.Single(projected.Source.Tracks);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video, track.Kind);
        Assert.Equal(Vide, track.Handler.HandlerType);
    }

    [Fact]
    public void Project_Mp4aAudioTrackIsAccepted()
    {
        var segment = new byte[105];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 97, Moov);
        WriteHeader(segment.AsSpan(16, 8), 89, Trak);
        WriteHeader(segment.AsSpan(24, 8), 81, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Soun);
        WriteHeader(segment.AsSpan(65, 8), 40, Minf);
        WriteHeader(segment.AsSpan(73, 8), 32, Stbl);
        WriteSampleDescription(segment.AsSpan(81, 24), Mp4a);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection>(
                projection);
        var track = Assert.Single(projected.Source.Tracks);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, track.Kind);
        Assert.Equal(Soun, track.Handler.HandlerType);
    }

    [Fact]
    public void Project_UnsupportedSampleEntryIsRejected()
    {
        var segment = new byte[105];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 97, Moov);
        WriteHeader(segment.AsSpan(16, 8), 89, Trak);
        WriteHeader(segment.AsSpan(24, 8), 81, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 40, Minf);
        WriteHeader(segment.AsSpan(73, 8), 32, Stbl);
        WriteSampleDescription(segment.AsSpan(81, 24), Unsupported);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .UnsupportedSampleEntry,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1AudioHandlerMismatchIsRejected()
    {
        var segment = new byte[105];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 97, Moov);
        WriteHeader(segment.AsSpan(16, 8), 89, Trak);
        WriteHeader(segment.AsSpan(24, 8), 81, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Soun);
        WriteHeader(segment.AsSpan(65, 8), 40, Minf);
        WriteHeader(segment.AsSpan(73, 8), 32, Stbl);
        WriteSampleDescription(segment.AsSpan(81, 24), Avc1);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .HandlerMismatch,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_TwoSampleEntriesAreRejected()
    {
        var segment = new byte[113];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 105, Moov);
        WriteHeader(segment.AsSpan(16, 8), 97, Trak);
        WriteHeader(segment.AsSpan(24, 8), 89, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 48, Minf);
        WriteHeader(segment.AsSpan(73, 8), 40, Stbl);
        WriteHeader(segment.AsSpan(81, 8), 32, Stsd);
        BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(93, 4), 2);
        WriteHeader(segment.AsSpan(97, 8), 8, Avc1);
        WriteHeader(segment.AsSpan(105, 8), 8, Avc3);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .UnsupportedSampleDescriptionShape,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_ZeroSampleEntriesAreRejected()
    {
        var segment = new byte[97];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 89, Moov);
        WriteHeader(segment.AsSpan(16, 8), 81, Trak);
        WriteHeader(segment.AsSpan(24, 8), 73, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 32, Minf);
        WriteHeader(segment.AsSpan(73, 8), 24, Stbl);
        WriteHeader(segment.AsSpan(81, 8), 16, Stsd);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .UnsupportedSampleDescriptionShape,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MissingSampleDescriptionBoxIsRejected()
    {
        var segment = new byte[81];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 73, Moov);
        WriteHeader(segment.AsSpan(16, 8), 65, Trak);
        WriteHeader(segment.AsSpan(24, 8), 57, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 16, Minf);
        WriteHeader(segment.AsSpan(73, 8), 8, Stbl);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .MissingSampleDescriptionBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateSampleDescriptionBoxesAreRejected()
    {
        var segment = new byte[113];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 105, Moov);
        WriteHeader(segment.AsSpan(16, 8), 97, Trak);
        WriteHeader(segment.AsSpan(24, 8), 89, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 48, Minf);
        WriteHeader(segment.AsSpan(73, 8), 40, Stbl);
        WriteHeader(segment.AsSpan(81, 8), 16, Stsd);
        WriteHeader(segment.AsSpan(97, 8), 16, Stsd);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .DuplicateSampleDescriptionBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MalformedSampleDescriptionBoxIsRejected()
    {
        var segment = new byte[96];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 88, Moov);
        WriteHeader(segment.AsSpan(16, 8), 80, Trak);
        WriteHeader(segment.AsSpan(24, 8), 72, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 31, Minf);
        WriteHeader(segment.AsSpan(73, 8), 23, Stbl);
        WriteHeader(segment.AsSpan(81, 8), 15, Stsd);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .InvalidSampleDescriptionBox,
            result);
        Assert.Null(projection);
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), handlerType);
    }

    private static void WriteSampleDescription(
        Span<byte> destination,
        uint sampleEntryType)
    {
        WriteHeader(destination[..8], 24, Stsd);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(12, 4), 1);
        WriteHeader(destination.Slice(16, 8), 8, sampleEntryType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
