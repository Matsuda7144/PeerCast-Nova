using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationProfileProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Project_OneAvcAndOneAacTrackBindByTrackId()
    {
        var segment = CreateInitializationSegment(
            Track(7, Vide, ValidVideoSampleDescription()),
            Track(9, Soun, ValidAudioSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationProfileProjection>(projection);
        Assert.Equal(7U, projected.VideoTrackId);
        Assert.Equal(9U, projected.AudioTrackId);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            projected.VideoConfiguration.CodecCandidate);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            projected.AudioConfiguration.CodecCandidate);
        Assert.Equal((ushort)640, projected.VideoConfiguration.VisualEntry.Width);
        Assert.Equal((ushort)360, projected.VideoConfiguration.VisualEntry.Height);
        Assert.Equal((ushort)2, projected.AudioConfiguration.AudioEntry.ChannelCount);
        Assert.Equal(
            (ushort)48_000,
            projected.AudioConfiguration.AudioEntry.IntegerSampleRate);
    }

    [Fact]
    public void Project_RequiresExactlyTwoInitializationTracks()
    {
        var segment = CreateInitializationSegment(
            Track(7, Vide, ValidVideoSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .UnsupportedTrackCount,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateVideoTrackIsRejected()
    {
        var segment = CreateInitializationSegment(
            Track(7, Vide, ValidVideoSampleDescription()),
            Track(9, Vide, ValidVideoSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .DuplicateVideoTrack,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateAudioTrackIsRejected()
    {
        var segment = CreateInitializationSegment(
            Track(7, Soun, ValidAudioSampleDescription()),
            Track(9, Soun, ValidAudioSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .DuplicateAudioTrack,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidAvcConfigurationIsRejected()
    {
        var segment = CreateInitializationSegment(
            Track(7, Vide, VideoSampleDescriptionWithoutConfiguration()),
            Track(9, Soun, ValidAudioSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidVideoConfiguration,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidAacConfigurationIsRejected()
    {
        var segment = CreateInitializationSegment(
            Track(7, Vide, ValidVideoSampleDescription()),
            Track(9, Soun, AudioSampleDescriptionWithoutConfiguration()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidAudioConfiguration,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InvalidTrackIdentityFailsClosed()
    {
        var segment = CreateInitializationSegment(
            Track(0, Vide, ValidVideoSampleDescription()),
            Track(9, Soun, ValidAudioSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidTrackIdentityProjection,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_HandlerCodecMismatchFailsClosed()
    {
        var segment = CreateInitializationSegment(
            Track(7, Soun, ValidVideoSampleDescription()),
            Track(9, Soun, ValidAudioSampleDescription()));

        var result = PlaybackFmp4LiveInitializationProfileProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult
                .InvalidSampleDescriptionProjection,
            result);
        Assert.Null(projection);
    }

    private static byte[] ValidVideoSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        return box;
    }

    private static byte[] VideoSampleDescriptionWithoutConfiguration()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry();
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

    private static byte[] AudioSampleDescriptionWithoutConfiguration()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry();
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
