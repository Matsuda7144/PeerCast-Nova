using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveDeterministicFixtureTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mdat = 0x6d646174;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;
    private const uint Trun = 0x7472756e;
    private const uint VideoTrackId = 1;
    private const uint AudioTrackId = 2;
    private const uint SequenceNumber = 41;
    private const uint VideoDecodeTime = 90_000;
    private const ulong AudioDecodeTime = 1_024;

    [Fact]
    public void Fixture_IsDeterministicAndAcceptedByV1Envelopes()
    {
        var first = CreateFixture();
        var second = CreateFixture();

        Assert.True(first.InitializationSegment.AsSpan().SequenceEqual(
            second.InitializationSegment));
        Assert.True(first.MediaSegment.AsSpan().SequenceEqual(
            second.MediaSegment));

        Assert.Equal(
            PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
            PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                first.InitializationSegment,
                first.InitializationSegment.Length,
                out var initializationEnvelope));
        Assert.Equal(
            PlaybackFmp4LiveSegmentKind.Initialization,
            initializationEnvelope!.Kind);

        Assert.Equal(
            PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
            PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                first.MediaSegment,
                first.MediaSegment.Length,
                out var mediaEnvelope));
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Media, mediaEnvelope!.Kind);
    }

    [Fact]
    public void Fixture_AllChunkSplitsFailClosedUntilExactReassembly()
    {
        var fixture = CreateFixture();

        AssertEveryTwoChunkSplit(
            fixture.InitializationSegment,
            PlaybackFmp4LiveSegmentKind.Initialization);
        AssertEveryTwoChunkSplit(
            fixture.MediaSegment,
            PlaybackFmp4LiveSegmentKind.Media);
    }

    [Fact]
    public void Fixture_ProjectsAvcAndAacInitializationConfiguration()
    {
        var fixture = CreateFixture();

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                fixture.InitializationSegment,
                fixture.InitializationSegment.Length,
                out var sampleDescriptions));
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection>(
                sampleDescriptions);
        Assert.Equal(2, projected.Tracks.Count);
        var video = projected.Tracks.Single(
            track => track.Kind == PlaybackIsoBmffHandlerKind.Video);
        var audio = projected.Tracks.Single(
            track => track.Kind == PlaybackIsoBmffHandlerKind.Audio);

        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            video.CodecCandidate);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            audio.CodecCandidate);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                fixture.VideoSampleDescriptionBox,
                video,
                out var videoConfiguration));
        var avc = Assert.IsType<
            PlaybackFmp4LiveInitializationAvcTrackConfiguration>(
                videoConfiguration);
        Assert.Equal((ushort)640, avc.VisualEntry.Width);
        Assert.Equal((ushort)360, avc.VisualEntry.Height);
        Assert.Equal((byte)66, avc.Configuration.ProfileIndication);
        Assert.Equal((byte)4, avc.Configuration.NalUnitLengthBytes);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjector.Project(
                fixture.AudioSampleDescriptionBox,
                audio,
                out var audioConfiguration));
        var aac = Assert.IsType<
            PlaybackFmp4LiveInitializationAacTrackConfiguration>(
                audioConfiguration);
        Assert.Equal((ushort)2, aac.AudioEntry.ChannelCount);
        Assert.Equal((ushort)48_000, aac.AudioEntry.IntegerSampleRate);
        Assert.Equal(
            (ushort)0x1234,
            aac.ElementaryStreamDescriptor.ElementaryStreamId);
    }

    [Fact]
    public void Fixture_ProjectsMediaIdentityAndDecodeTimeline()
    {
        var fixture = CreateFixture();
        Assert.Equal(
            PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
            PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                fixture.MediaSegment,
                fixture.MediaSegment.Length,
                out var envelope));
        var accepted = Assert.IsType<PlaybackFmp4LiveSegmentEnvelope>(envelope);
        var movieFragment = fixture.MediaSegment.AsSpan(
            0,
            accepted.FirstBoxLength);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));
        var projectedIdentity = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentIdentityProjection>(identity);
        Assert.Equal(SequenceNumber, projectedIdentity.SequenceNumber);
        Assert.Collection(
            projectedIdentity.Tracks,
            track => Assert.Equal(VideoTrackId, track.TrackId),
            track => Assert.Equal(AudioTrackId, track.TrackId));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                movieFragment,
                projectedIdentity,
                out var timeline));
        var projectedTimeline = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(timeline);
        Assert.Collection(
            projectedTimeline.Tracks,
            track =>
            {
                Assert.Equal(VideoTrackId, track.TrackId);
                Assert.Equal((byte)0, track.Version);
                Assert.Equal((ulong)VideoDecodeTime, track.BaseMediaDecodeTime);
            },
            track =>
            {
                Assert.Equal(AudioTrackId, track.TrackId);
                Assert.Equal((byte)1, track.Version);
                Assert.Equal(AudioDecodeTime, track.BaseMediaDecodeTime);
            });
    }

    private static FoundationFixture CreateFixture()
    {
        var (videoSampleDescription, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        var (audioSampleDescription, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));

        var videoTrack = Track(Vide, videoSampleDescription);
        var audioTrack = Track(Soun, audioSampleDescription);
        var initializationSegment = Concat(
            Box(Ftyp),
            Box(Moov, videoTrack, audioTrack));

        byte[] videoSample = [0, 0, 0, 2, 0x65, 0x88];
        byte[] audioSample = [0x21, 0x10];
        var movieFragment = Box(
            Moof,
            MovieFragmentHeader(SequenceNumber),
            Box(
                Traf,
                TrackFragmentHeader(VideoTrackId),
                TrackDecodeTime(0, VideoDecodeTime),
                TrackRun(
                    checked((uint)videoSample.Length),
                    0x02000000)),
            Box(
                Traf,
                TrackFragmentHeader(AudioTrackId),
                TrackDecodeTime(1, AudioDecodeTime),
                TrackRun(
                    checked((uint)audioSample.Length),
                    0)));
        var mediaSegment = Concat(
            movieFragment,
            Box(Mdat, videoSample, audioSample));

        return new(
            initializationSegment,
            videoSampleDescription,
            audioSampleDescription,
            mediaSegment);
    }

    private static void AssertEveryTwoChunkSplit(
        byte[] segment,
        PlaybackFmp4LiveSegmentKind expectedKind)
    {
        for (var split = 1; split < segment.Length; split++)
        {
            var prefix = segment.AsSpan(0, split).ToArray();
            var suffix = segment.AsSpan(split).ToArray();

            var incompleteResult =
                PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                    prefix,
                    segment.Length,
                    out var incompleteEnvelope);
            Assert.NotEqual(
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
                incompleteResult);
            Assert.Null(incompleteEnvelope);

            var reassembled = new byte[segment.Length];
            prefix.AsSpan().CopyTo(reassembled);
            suffix.AsSpan().CopyTo(reassembled.AsSpan(prefix.Length));
            Assert.True(segment.AsSpan().SequenceEqual(reassembled));

            Assert.Equal(
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
                PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                    reassembled,
                    segment.Length,
                    out var envelope));
            Assert.Equal(
                expectedKind,
                Assert.IsType<PlaybackFmp4LiveSegmentEnvelope>(envelope).Kind);
        }
    }

    private static byte[] Track(uint handlerType, byte[] sampleDescription) =>
        Box(
            Trak,
            Box(
                Mdia,
                Handler(handlerType),
                Box(Minf, Box(Stbl, sampleDescription))));

    private static byte[] Handler(uint handlerType)
    {
        var box = new byte[33];
        WriteHeader(box, Hdlr);
        WriteUInt32(box, 16, handlerType);
        return box;
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        WriteUInt32(box, 12, sequenceNumber);
        return box;
    }

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        WriteUInt32(box, 12, trackId);
        return box;
    }

    private static byte[] TrackDecodeTime(byte version, ulong decodeTime)
    {
        var box = new byte[version == 1 ? 20 : 16];
        WriteHeader(box, Tfdt);
        box[8] = version;
        if (version == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(
                box.AsSpan(12, 8),
                decodeTime);
        }
        else
        {
            WriteUInt32(box, 12, checked((uint)decodeTime));
        }

        return box;
    }

    private static byte[] TrackRun(uint sampleSize, uint sampleFlags)
    {
        var box = new byte[24];
        WriteHeader(box, Trun);
        box[10] = 0x06;
        WriteUInt32(box, 12, 1);
        WriteUInt32(box, 16, sampleSize);
        WriteUInt32(box, 20, sampleFlags);
        return box;
    }

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
        WriteUInt32(box, 0, checked((uint)box.Length));
        WriteUInt32(box, 4, type);
    }

    private static void WriteUInt32(byte[] box, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4), value);

    private sealed record FoundationFixture(
        byte[] InitializationSegment,
        byte[] VideoSampleDescriptionBox,
        byte[] AudioSampleDescriptionBox,
        byte[] MediaSegment);
}
