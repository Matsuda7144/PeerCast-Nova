using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationFragmentDefaultsProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Mvex = 0x6d766578;
    private const uint Trex = 0x74726578;
    private const uint Free = 0x66726565;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Project_BindsVideoAndAudioTrackExtendsByInitializationTrackId()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            Box(Free),
            MovieExtends(
                TrackExtends(7, 1, 1_001, 4_096, 0x02000000),
                Box(Free),
                TrackExtends(9, 1, 1_024, 1_024, 0)));

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationFragmentDefaultsProjection>(projection);
        Assert.Equal(7U, projected.Source.VideoTrackId);
        Assert.Equal(9U, projected.Source.AudioTrackId);
        Assert.Equal(7U, projected.VideoTrackId);
        Assert.Equal(9U, projected.AudioTrackId);
        Assert.Equal(1U, projected.VideoTrackExtends.DefaultSampleDescriptionIndex);
        Assert.Equal(1_001U, projected.VideoTrackExtends.DefaultSampleDuration);
        Assert.Equal(4_096U, projected.VideoTrackExtends.DefaultSampleSize);
        Assert.Equal(0x02000000U, projected.VideoTrackExtends.DefaultSampleFlags);
        Assert.Equal(1U, projected.AudioTrackExtends.DefaultSampleDescriptionIndex);
        Assert.Equal(1_024U, projected.AudioTrackExtends.DefaultSampleDuration);
        Assert.Equal(1_024U, projected.AudioTrackExtends.DefaultSampleSize);
        Assert.Equal(0U, projected.AudioTrackExtends.DefaultSampleFlags);
    }

    [Fact]
    public void Project_MovieExtendsCardinalityIsExact()
    {
        var missing = CreateInitializationSegment(VideoTrack(), AudioTrack());
        var duplicate = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            MovieExtends(
                TrackExtends(7),
                TrackExtends(9)),
            MovieExtends(
                TrackExtends(7),
                TrackExtends(9)));

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .MissingMovieExtendsBox,
            PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                missing,
                missing.Length,
                out var missingProjection));
        Assert.Null(missingProjection);
        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .DuplicateMovieExtendsBox,
            PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                duplicate,
                duplicate.Length,
                out var duplicateProjection));
        Assert.Null(duplicateProjection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_RequiresTrackExtendsForBothProfileTracks(bool omitVideo)
    {
        var movieExtends = omitVideo
            ? MovieExtends(TrackExtends(9))
            : MovieExtends(TrackExtends(7));
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            movieExtends);

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            omitVideo
                ? PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                    .MissingVideoTrackExtends
                : PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                    .MissingAudioTrackExtends,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_UnknownTrackIdFailsClosed()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            MovieExtends(
                TrackExtends(7),
                TrackExtends(9),
                TrackExtends(11)));

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .UnknownTrackId,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackExtendsFailsClosed()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            MovieExtends(
                TrackExtends(7),
                TrackExtends(7),
                TrackExtends(9)));

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .DuplicateTrackExtends,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MalformedTrackExtendsFailsClosed()
    {
        var malformed = TrackExtends(7);
        malformed[8] = 1;
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            MovieExtends(malformed, TrackExtends(9)));

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .InvalidTrackExtendsBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RequiresFirstSampleDescriptionForV1Profile()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(),
            AudioTrack(),
            MovieExtends(
                TrackExtends(7, sampleDescriptionIndex: 2),
                TrackExtends(9)));

        var result = PlaybackFmp4LiveInitializationFragmentDefaultsProjector
            .Project(segment, segment.Length, out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .UnsupportedSampleDescriptionIndex,
            result);
        Assert.Null(projection);
    }

    private static byte[] VideoTrack() =>
        Track(7, Vide, ValidVideoSampleDescription());

    private static byte[] AudioTrack() =>
        Track(9, Soun, ValidAudioSampleDescription());

    private static byte[] ValidVideoSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        return box;
    }

    private static byte[] ValidAudioSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        return box;
    }

    private static byte[] Track(
        uint trackId,
        uint handlerType,
        byte[] sampleDescription) =>
        Box(
            Trak,
            TrackHeader(trackId),
            Box(
                Mdia,
                Handler(handlerType),
                Box(Minf, Box(Stbl, sampleDescription))));

    private static byte[] TrackHeader(uint trackId)
    {
        var box = new byte[TkhdBoxLength];
        WriteHeader(box, Tkhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), trackId);
        return box;
    }

    private static byte[] Handler(uint handlerType)
    {
        var box = new byte[33];
        WriteHeader(box, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), handlerType);
        return box;
    }

    private static byte[] MovieExtends(params byte[][] children) =>
        Box(Mvex, children);

    private static byte[] TrackExtends(
        uint trackId,
        uint sampleDescriptionIndex = 1,
        uint sampleDuration = 0,
        uint sampleSize = 0,
        uint sampleFlags = 0)
    {
        var box = new byte[32];
        WriteHeader(box, Trex);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            trackId);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            sampleDescriptionIndex);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(20, 4),
            sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(24, 4),
            sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(28, 4),
            sampleFlags);
        return box;
    }

    private static byte[] CreateInitializationSegment(
        params byte[][] movieChildren) =>
        Concat(Box(Ftyp), Box(Moov, movieChildren));

    private static byte[] Box(uint type, params byte[][] payloads)
    {
        var box = new byte[8 + payloads.Sum(payload => payload.Length)];
        WriteHeader(box, type);
        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(box, offset);
            offset += payload.Length;
        }

        return box;
    }

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }

        return result;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            checked((uint)box.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
