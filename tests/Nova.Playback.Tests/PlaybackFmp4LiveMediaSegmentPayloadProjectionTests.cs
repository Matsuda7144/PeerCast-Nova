using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaSegmentPayloadProjectionTests
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
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;
    private const uint Trun = 0x7472756e;
    private const uint Mdat = 0x6d646174;
    private const uint Free = 0x66726565;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Project_VideoAndAudioSamplesAreContainedInMediaData()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            61,
            TrackFragment(7, 90_000, TrackRun(1, 0)),
            TrackFragment(9, 48_000, TrackRun(1, 0)));
        var videoOffset = placeholder.Length + 8;
        var audioOffset = videoOffset + 4;
        var fragment = MovieFragment(
            61,
            TrackFragment(7, 90_000, TrackRun(1, videoOffset)),
            TrackFragment(9, 48_000, TrackRun(1, audioOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(7));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaSegmentPayloadProjection>(projection);
        Assert.Equal(61U, projected.SequenceNumber);
        Assert.Equal(2, projected.Tracks.Count);
        Assert.Equal(7U, projected.Tracks[0].TrackId);
        Assert.Equal(videoOffset, (int)projected.Tracks[0].DataOrigin.RunDataStart);
        Assert.Single(projected.Tracks[0].SampleByteRanges.Ranges);
        Assert.Equal(4U, projected.Tracks[0].SampleByteRanges.Ranges[0].Length);
        Assert.Equal(9U, projected.Tracks[1].TrackId);
        Assert.Equal(audioOffset, (int)projected.Tracks[1].DataOrigin.RunDataStart);
        Assert.Single(projected.Tracks[1].SampleByteRanges.Ranges);
        Assert.Equal(3U, projected.Tracks[1].SampleByteRanges.Ranges[0].Length);
    }

    [Theory]
    [InlineData(7U, 4)]
    [InlineData(9U, 3)]
    public void Project_SingleTrackSegmentIsProjected(uint trackId, int payloadLength)
    {
        var defaults = ProjectInitializationDefaults();
        var decodeTime = trackId == 7 ? 90_000UL : 48_000UL;
        var placeholder = MovieFragment(
            62,
            TrackFragment(trackId, decodeTime, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            62,
            TrackFragment(trackId, decodeTime, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(payloadLength));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaSegmentPayloadProjection>(projection);
        var track = Assert.Single(projected.Tracks);
        Assert.Equal(trackId, track.TrackId);
        Assert.Single(track.SampleByteRanges.Ranges);
    }

    [Fact]
    public void Project_RequiresExplicitTrackRunDataOffset()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            63,
            TrackFragment(7, 90_000, TrackRun(1, null)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidRunDataOrigin,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsExplicitTrackFragmentBaseOrigin()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            64,
            TrackFragment(
                7,
                90_000,
                TrackRun(1, 0),
                defaultBaseIsMoof: false,
                baseDataOffset: 0));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            64,
            TrackFragment(
                7,
                90_000,
                TrackRun(1, dataOffset),
                defaultBaseIsMoof: false,
                baseDataOffset: 0));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.UnsupportedDataOrigin,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsRunStartBeforeMediaDataPayload()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            65,
            TrackFragment(7, 90_000, TrackRun(1, 0)));
        var fragment = MovieFragment(
            65,
            TrackFragment(7, 90_000, TrackRun(1, placeholder.Length)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .SampleOutsideMediaData,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsSampleWhoseEndExceedsMediaData()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            66,
            TrackFragment(7, 90_000, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            66,
            TrackFragment(7, 90_000, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(3));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .SampleOutsideMediaData,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsOverlappingTrackSampleRanges()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            72,
            TrackFragment(7, 90_000, TrackRun(1, 0)),
            TrackFragment(9, 48_000, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            72,
            TrackFragment(7, 90_000, TrackRun(1, dataOffset)),
            TrackFragment(9, 48_000, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .OverlappingSampleRanges,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsZeroSizedIndividualSample()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            67,
            TrackFragment(7, 90_000, TrackRun(1, 0, sampleSize: 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            67,
            TrackFragment(
                7,
                90_000,
                TrackRun(1, dataOffset, sampleSize: 0)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(0));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.ZeroSampleSize,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_ZeroSampleRunRemainsProjectedAtThisLayer()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            68,
            TrackFragment(7, 90_000, TrackRun(0, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            68,
            TrackFragment(7, 90_000, TrackRun(0, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(0));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaSegmentPayloadProjection>(projection);
        var track = Assert.Single(projected.Tracks);
        Assert.Empty(track.SampleByteRanges.Ranges);
    }

    [Fact]
    public void Project_InvalidMediaEnvelopeFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            69,
            TrackFragment(7, 90_000, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            69,
            TrackFragment(7, 90_000, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4), Box(Free));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult.InvalidMediaEnvelope,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_BindingFromDifferentFragmentFailsThroughResolvedRunLayer()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            70,
            TrackFragment(7, 91_000, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var original = MovieFragment(
            70,
            TrackFragment(7, 90_000, TrackRun(1, dataOffset)));
        var actual = MovieFragment(
            70,
            TrackFragment(7, 91_000, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, original);
        var segment = Concat(actual, MediaData(4));

        var result = PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
            segment,
            segment.Length,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .InvalidResolvedRunProjection,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MissingInputsFailClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var placeholder = MovieFragment(
            71,
            TrackFragment(7, 90_000, TrackRun(1, 0)));
        var dataOffset = placeholder.Length + 8;
        var fragment = MovieFragment(
            71,
            TrackFragment(7, 90_000, TrackRun(1, dataOffset)));
        var binding = Bind(defaults.Source, fragment);
        var segment = Concat(fragment, MediaData(4));

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .MissingInitializationDefaults,
            PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
                segment,
                segment.Length,
                null,
                binding,
                out var missingDefaults));
        Assert.Null(missingDefaults);

        Assert.Equal(
            PlaybackFmp4LiveMediaSegmentPayloadProjectionResult
                .MissingFragmentBinding,
            PlaybackFmp4LiveMediaSegmentPayloadProjector.Project(
                segment,
                segment.Length,
                defaults,
                null,
                out var missingBinding));
        Assert.Null(missingBinding);
    }

    private static PlaybackFmp4LiveInitializationFragmentDefaultsProjection
        ProjectInitializationDefaults()
    {
        var segment = Concat(
            Box(Ftyp),
            Box(
                Moov,
                InitializationTrack(7, Vide, ValidVideoSampleDescription()),
                InitializationTrack(9, Soun, ValidAudioSampleDescription()),
                Box(
                    Mvex,
                    TrackExtends(7, 1, 1_001, 4, 0x02000000),
                    TrackExtends(9, 1, 1_024, 3, 0))));

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                segment,
                segment.Length,
                out var defaults));
        return Assert.IsType<
            PlaybackFmp4LiveInitializationFragmentDefaultsProjection>(defaults);
    }

    private static PlaybackFmp4LiveMediaFragmentProfileBinding Bind(
        PlaybackFmp4LiveInitializationProfileProjection profile,
        byte[] fragment)
    {
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                fragment,
                out var identity));
        var projectedIdentity = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentIdentityProjection>(identity);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                fragment,
                projectedIdentity,
                out var timeline));
        var projectedTimeline = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(timeline);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound,
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                profile,
                projectedIdentity,
                projectedTimeline,
                out var binding));
        return Assert.IsType<PlaybackFmp4LiveMediaFragmentProfileBinding>(binding);
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

    private static byte[] InitializationTrack(
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

    private static byte[] TrackExtends(
        uint trackId,
        uint sampleDescriptionIndex,
        uint sampleDuration,
        uint sampleSize,
        uint sampleFlags)
    {
        var box = new byte[32];
        WriteHeader(box, Trex);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            sampleDescriptionIndex);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24, 4), sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(28, 4), sampleFlags);
        return box;
    }

    private static byte[] MovieFragment(
        uint sequenceNumber,
        params byte[][] tracks)
    {
        var children = new byte[tracks.Length + 1][];
        children[0] = MovieFragmentHeader(sequenceNumber);
        tracks.CopyTo(children, 1);
        return Box(Moof, children);
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(
        uint trackId,
        ulong decodeTime,
        byte[] trackRun,
        bool defaultBaseIsMoof = true,
        ulong? baseDataOffset = null) =>
        Box(
            Traf,
            TrackFragmentHeader(trackId, defaultBaseIsMoof, baseDataOffset),
            TrackDecodeTime(decodeTime),
            trackRun);

    private static byte[] TrackFragmentHeader(
        uint trackId,
        bool defaultBaseIsMoof,
        ulong? baseDataOffset)
    {
        var flags = defaultBaseIsMoof ? 0x020000U : 0U;
        if (baseDataOffset.HasValue)
        {
            flags |= 0x000001U;
        }

        var box = new byte[16 + (baseDataOffset.HasValue ? 8 : 0)];
        WriteHeader(box, Tfhd);
        WriteFullBoxHeader(box, flags);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        if (baseDataOffset is { } explicitBase)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(16, 8), explicitBase);
        }

        return box;
    }

    private static byte[] TrackDecodeTime(ulong decodeTime)
    {
        var box = new byte[16];
        WriteHeader(box, Tfdt);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            checked((uint)decodeTime));
        return box;
    }

    private static byte[] TrackRun(
        uint sampleCount,
        int? dataOffset,
        uint? sampleSize = null)
    {
        var flags = 0U;
        var optionalHeaderSize = 0;
        var entrySize = 0;
        if (dataOffset.HasValue)
        {
            flags |= 0x000001U;
            optionalHeaderSize += 4;
        }

        if (sampleSize.HasValue)
        {
            flags |= 0x000200U;
            entrySize += 4;
        }

        var box = new byte[
            16 + optionalHeaderSize + checked((int)sampleCount) * entrySize];
        WriteHeader(box, Trun);
        WriteFullBoxHeader(box, flags);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sampleCount);
        var cursor = 16;
        if (dataOffset is { } relativeOffset)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                box.AsSpan(cursor, 4),
                relativeOffset);
            cursor += 4;
        }

        if (sampleSize is { } size)
        {
            for (var index = 0U; index < sampleCount; index++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    box.AsSpan(cursor, 4),
                    size);
                cursor += 4;
            }
        }

        return box;
    }

    private static byte[] MediaData(int payloadLength) =>
        Box(Mdat, new byte[payloadLength]);

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

    private static void WriteFullBoxHeader(byte[] box, uint flags) =>
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(8, 4), flags);
}
