using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackTimingProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Mdhd = 0x6d646864;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Free = 0x66726565;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const int TkhdBoxLength = 92;
    private const ushort UndLanguage = 0x55c4;

    [Fact]
    public void Project_BindsVideoAndAudioTimescalesFromMediaHeaders()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(MediaHeaderVersion0(90_000, 180_000)),
            AudioTrack(MediaHeaderVersion1(48_000, 96_000)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackTimingProjection>(projection);
        Assert.Equal(7U, projected.VideoTrackId);
        Assert.Equal(9U, projected.AudioTrackId);
        Assert.Equal(90_000U, projected.VideoTimescale);
        Assert.Equal(48_000U, projected.AudioTimescale);
        Assert.Equal((byte)0, projected.VideoTrack.Timing.Version);
        Assert.Equal((byte)1, projected.AudioTrack.Timing.Version);
        Assert.Equal(180_000UL, projected.VideoTrack.Timing.DurationUnits);
        Assert.Equal(96_000UL, projected.AudioTrack.Timing.DurationUnits);
    }

    [Fact]
    public void Project_ToleratesUnrelatedValidMediaSiblings()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(
                Box(Free),
                MediaHeaderVersion0(90_000),
                Box(Free)),
            AudioTrack(
                Box(Free),
                MediaHeaderVersion0(48_000),
                Box(Free)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackTimingProjection>(projection);
        Assert.Equal(90_000U, projected.VideoTimescale);
        Assert.Equal(48_000U, projected.AudioTimescale);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_RequiresMediaHeaderForBothProfileTracks(bool omitVideo)
    {
        var segment = CreateInitializationSegment(
            omitVideo
                ? VideoTrack(Box(Free))
                : VideoTrack(MediaHeaderVersion0(90_000)),
            omitVideo
                ? AudioTrack(MediaHeaderVersion0(48_000))
                : AudioTrack(Box(Free)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .MissingMediaHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_RejectsDuplicateMediaHeaders(bool duplicateVideo)
    {
        var segment = CreateInitializationSegment(
            duplicateVideo
                ? VideoTrack(
                    MediaHeaderVersion0(90_000),
                    MediaHeaderVersion0(90_000))
                : VideoTrack(MediaHeaderVersion0(90_000)),
            duplicateVideo
                ? AudioTrack(MediaHeaderVersion0(48_000))
                : AudioTrack(
                    MediaHeaderVersion0(48_000),
                    MediaHeaderVersion0(48_000)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .DuplicateMediaHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsZeroTimescaleMediaHeader()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(MediaHeaderVersion0(0)),
            AudioTrack(MediaHeaderVersion0(48_000)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidMediaHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsUnsupportedMediaHeaderVersion()
    {
        var invalid = MediaHeaderVersion0(90_000);
        invalid[8] = 2;
        var segment = CreateInitializationSegment(
            VideoTrack(invalid),
            AudioTrack(MediaHeaderVersion0(48_000)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidMediaHeaderBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidInitializationProfileFailsClosed()
    {
        var segment = CreateInitializationSegment(
            VideoTrack(MediaHeaderVersion0(90_000)));

        var result = PlaybackFmp4LiveInitializationTrackTimingProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidInitializationProfile,
            result);
        Assert.Null(projection);
    }

    private static byte[] VideoTrack(params byte[][] mediaChildren) =>
        Track(7, Vide, ValidVideoSampleDescription(), mediaChildren);

    private static byte[] AudioTrack(params byte[][] mediaChildren) =>
        Track(9, Soun, ValidAudioSampleDescription(), mediaChildren);

    private static byte[] Track(
        uint trackId,
        uint handlerType,
        byte[] sampleDescription,
        params byte[][] mediaChildren)
    {
        var children = new byte[mediaChildren.Length + 2][];
        mediaChildren.CopyTo(children, 0);
        children[^2] = Handler(handlerType);
        children[^1] = Box(Minf, Box(Stbl, sampleDescription));
        return Box(
            Trak,
            TrackHeader(trackId),
            Box(Mdia, children));
    }

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

    private static byte[] MediaHeaderVersion0(
        uint timescale,
        uint duration = 0)
    {
        var box = new byte[32];
        WriteHeader(box, Mdhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24, 4), duration);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), UndLanguage);
        return box;
    }

    private static byte[] MediaHeaderVersion1(
        uint timescale,
        ulong duration = 0)
    {
        var box = new byte[44];
        WriteHeader(box, Mdhd);
        box[8] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(28, 4), timescale);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(32, 8), duration);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(40, 2), UndLanguage);
        return box;
    }

    private static byte[] CreateInitializationSegment(params byte[][] tracks) =>
        Concat(Box(Ftyp), Box(Moov, tracks));

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
