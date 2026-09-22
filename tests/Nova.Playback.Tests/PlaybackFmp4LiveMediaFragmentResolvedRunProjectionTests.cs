using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaFragmentResolvedRunProjectionTests
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
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Project_ResolvesVideoAndAudioFromTrackExtendsDefaults()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            41,
            TrackFragment(9, 48_000, new[] { TrackRun(1) }),
            TrackFragment(7, 90_000, new[] { TrackRun(1) }));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection);
        Assert.Equal(41U, projected.SequenceNumber);
        Assert.True(projected.ContainsVideo);
        Assert.True(projected.ContainsAudio);
        Assert.Equal(7U, projected.VideoTrack!.TrackId);
        Assert.Equal(9U, projected.AudioTrack!.TrackId);
        Assert.Equal(1_001U, projected.VideoTrack.ResolvedRun.Samples[0].SampleDuration);
        Assert.Equal(4_096U, projected.VideoTrack.ResolvedRun.Samples[0].SampleSize);
        Assert.Equal(
            0x02000000U,
            projected.VideoTrack.ResolvedRun.Samples[0].SampleFlags);
        Assert.Equal(1_024U, projected.AudioTrack.ResolvedRun.Samples[0].SampleDuration);
        Assert.Equal(1_024U, projected.AudioTrack.ResolvedRun.Samples[0].SampleSize);
        Assert.Equal(0U, projected.AudioTrack.ResolvedRun.Samples[0].SampleFlags);
    }

    [Theory]
    [InlineData(7U, true)]
    [InlineData(9U, false)]
    public void Project_SingleTrackFragmentsRemainSingleTrack(
        uint trackId,
        bool expectVideo)
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            42,
            TrackFragment(
                trackId,
                expectVideo ? 90_000UL : 48_000UL,
                new[] { TrackRun(1) }));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection);
        Assert.Single(projected.Tracks);
        Assert.Equal(expectVideo, projected.ContainsVideo);
        Assert.Equal(!expectVideo, projected.ContainsAudio);
    }

    [Fact]
    public void Project_TrackFragmentHeaderDefaultsOverrideTrackExtendsDefaults()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            43,
            TrackFragment(
                7,
                90_000,
                new[] { TrackRun(1) },
                defaultDuration: 2_002,
                defaultSize: 2_048,
                defaultFlags: 0x01010000));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            result);
        var sample = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection)
            .VideoTrack!
            .ResolvedRun
            .Samples[0];
        Assert.Equal(2_002U, sample.SampleDuration);
        Assert.Equal(2_048U, sample.SampleSize);
        Assert.Equal(0x01010000U, sample.SampleFlags);
    }

    [Fact]
    public void Project_TrackRunEntriesOverrideFragmentAndTrackExtendsDefaults()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            44,
            TrackFragment(
                7,
                90_000,
                new[] { TrackRun(3_003, 777, 0x02000000) },
                defaultDuration: 2_002,
                defaultSize: 2_048,
                defaultFlags: 0x01010000));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            result);
        var sample = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection)
            .VideoTrack!
            .ResolvedRun
            .Samples[0];
        Assert.Equal(3_003U, sample.SampleDuration);
        Assert.Equal(777U, sample.SampleSize);
        Assert.Equal(0x02000000U, sample.SampleFlags);
    }

    [Fact]
    public void Project_MissingTrackRunFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            45,
            TrackFragment(7, 90_000, Array.Empty<byte[]>()));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.MissingTrackRun,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_DuplicateTrackRunFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            46,
            TrackFragment(7, 90_000, new[] { TrackRun(1), TrackRun(1) }));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.DuplicateTrackRun,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_MalformedTrackRunFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var malformed = TrackRun(1);
        malformed[8] = 2;
        var fragment = MovieFragment(
            47,
            TrackFragment(7, 90_000, new[] { malformed }));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.InvalidTrackRun,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_BindingFromDifferentDecodeTimelineFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var original = MovieFragment(
            48,
            TrackFragment(7, 90_000, new[] { TrackRun(1) }));
        var different = MovieFragment(
            48,
            TrackFragment(7, 91_000, new[] { TrackRun(1) }));
        var binding = Bind(defaults.Source, original);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            different,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .FragmentBindingMismatch,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_InitializationProfileMismatchFailsClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var differentDefaults = ProjectInitializationDefaults(17, 19);
        var fragment = MovieFragment(
            49,
            TrackFragment(17, 90_000, new[] { TrackRun(1) }));
        var binding = Bind(differentDefaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InitializationProfileMismatch,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_RejectsSampleDescriptionOverrideOutsideV1Profile()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            50,
            TrackFragment(
                7,
                90_000,
                new[] { TrackRun(1) },
                sampleDescriptionIndex: 2));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .UnsupportedSampleDescriptionIndex,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_ZeroSampleDescriptionOverrideFailsResolver()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            51,
            TrackFragment(
                7,
                90_000,
                new[] { TrackRun(1) },
                sampleDescriptionIndex: 0));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.ResolveFailed,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_EmptyTrackRunRemainsProjectedAtThisLayer()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            52,
            TrackFragment(7, 90_000, new[] { TrackRun(0) }));
        var binding = Bind(defaults.Source, fragment);

        var result = PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
            fragment,
            defaults,
            binding,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection);
        Assert.Empty(projected.VideoTrack!.ResolvedRun.Samples);
    }

    [Fact]
    public void Project_MissingInputsFailClosed()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            53,
            TrackFragment(7, 90_000, new[] { TrackRun(1) }));
        var binding = Bind(defaults.Source, fragment);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .MissingInitializationDefaults,
            PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
                fragment,
                null,
                binding,
                out var missingDefaults));
        Assert.Null(missingDefaults);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .MissingFragmentBinding,
            PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
                fragment,
                defaults,
                null,
                out var missingBinding));
        Assert.Null(missingBinding);
    }

    private static PlaybackFmp4LiveInitializationFragmentDefaultsProjection
        ProjectInitializationDefaults(
            uint videoTrackId = 7,
            uint audioTrackId = 9)
    {
        var segment = Concat(
            Box(Ftyp),
            Box(
                Moov,
                InitializationTrack(
                    videoTrackId,
                    Vide,
                    ValidVideoSampleDescription()),
                InitializationTrack(
                    audioTrackId,
                    Soun,
                    ValidAudioSampleDescription()),
                Box(
                    Mvex,
                    TrackExtends(
                        videoTrackId,
                        1,
                        1_001,
                        4_096,
                        0x02000000),
                    TrackExtends(audioTrackId, 1, 1_024, 1_024, 0))));

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
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(20, 4),
            sampleDuration);
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
        byte[][] trackRuns,
        uint? sampleDescriptionIndex = null,
        uint? defaultDuration = null,
        uint? defaultSize = null,
        uint? defaultFlags = null)
    {
        var children = new byte[trackRuns.Length + 2][];
        children[0] = TrackFragmentHeader(
            trackId,
            sampleDescriptionIndex,
            defaultDuration,
            defaultSize,
            defaultFlags);
        children[1] = TrackDecodeTime(decodeTime);
        trackRuns.CopyTo(children, 2);
        return Box(Traf, children);
    }

    private static byte[] TrackFragmentHeader(
        uint trackId,
        uint? sampleDescriptionIndex,
        uint? defaultDuration,
        uint? defaultSize,
        uint? defaultFlags)
    {
        var flags = 0U;
        var optionalFieldCount = 0;
        if (sampleDescriptionIndex.HasValue)
        {
            flags |= 0x000002;
            optionalFieldCount++;
        }

        if (defaultDuration.HasValue)
        {
            flags |= 0x000008;
            optionalFieldCount++;
        }

        if (defaultSize.HasValue)
        {
            flags |= 0x000010;
            optionalFieldCount++;
        }

        if (defaultFlags.HasValue)
        {
            flags |= 0x000020;
            optionalFieldCount++;
        }

        var box = new byte[16 + (optionalFieldCount * 4)];
        WriteHeader(box, Tfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(8, 4), flags);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        var cursor = 16;
        WriteOptionalUInt32(box, ref cursor, sampleDescriptionIndex);
        WriteOptionalUInt32(box, ref cursor, defaultDuration);
        WriteOptionalUInt32(box, ref cursor, defaultSize);
        WriteOptionalUInt32(box, ref cursor, defaultFlags);
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

    private static byte[] TrackRun(uint sampleCount)
    {
        var box = new byte[16];
        WriteHeader(box, Trun);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sampleCount);
        return box;
    }

    private static byte[] TrackRun(
        uint sampleDuration,
        uint sampleSize,
        uint sampleFlags)
    {
        var box = new byte[28];
        WriteHeader(box, Trun);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(8, 4), 0x000700);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24, 4), sampleFlags);
        return box;
    }

    private static void WriteOptionalUInt32(
        byte[] box,
        ref int cursor,
        uint? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(cursor, 4),
            value.Value);
        cursor += 4;
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
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            checked((uint)box.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
