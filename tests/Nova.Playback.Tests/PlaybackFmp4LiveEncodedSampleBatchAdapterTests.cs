using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveEncodedSampleBatchAdapterTests
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
    private const uint VideoTrackId = 7;
    private const uint AudioTrackId = 9;

    [Fact]
    public void CreateBatch_VideoAndAudioAreCopiedAndOrderedByTrackThenOrdinal()
    {
        var initialization = CreateInitializationSegment();
        var adapter = CreateAdapter(initialization);
        var media = CreateMediaSegment(
            61,
            AudioTrack(48_000, Sample([0xa1, 0xb2, 0xc3], 1_024, 0)),
            VideoTrack(
                90_000,
                Sample([0x11, 0x12, 0x13, 0x14], 1_001, 0x02000000),
                Sample([0x21, 0x22, 0x23, 0x24], 1_001, 0x01000000)));

        using var batch = CreateBatch(adapter, media);

        Assert.Equal(adapter.StreamDescription.StreamGenerationId,
            batch.StreamGenerationId);
        Assert.Equal(0UL, batch.EpochId);
        Assert.Equal(61U, batch.FragmentSequence);
        Assert.Equal(PlaybackEncodedDiscontinuity.None, batch.Discontinuity);
        Assert.Collection(
            batch.Samples,
            sample =>
            {
                Assert.Equal(VideoTrackId, sample.TrackId);
                Assert.Equal(0, sample.SampleOrdinal);
                Assert.Equal(90_000, sample.DtsTicks);
                Assert.Equal(90_000, sample.PtsTicks);
                Assert.Equal(1_001, sample.DurationTicks);
                Assert.Equal(
                    PlaybackEncodedRandomAccessClassification
                        .IndependentlyDecodable,
                    sample.RandomAccessClassification);
            },
            sample =>
            {
                Assert.Equal(VideoTrackId, sample.TrackId);
                Assert.Equal(1, sample.SampleOrdinal);
                Assert.Equal(
                    PlaybackEncodedRandomAccessClassification
                        .NotIndependentlyDecodable,
                    sample.RandomAccessClassification);
            },
            sample =>
            {
                Assert.Equal(AudioTrackId, sample.TrackId);
                Assert.Equal(PlaybackEncodedRandomAccessClassification
                    .NotApplicable, sample.RandomAccessClassification);
            });
    }

    [Fact]
    public void CreateBatch_VideoOnlyFragmentHasNoAudioSamples()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000))));

        var sample = Assert.Single(batch.Samples);
        Assert.Equal(VideoTrackId, sample.TrackId);
    }

    [Fact]
    public void CreateBatch_AudioOnlyFragmentHasNoVideoSamples()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                AudioTrack(48_000, Sample([5, 6, 7], 1_024, 0))));

        var sample = Assert.Single(batch.Samples);
        Assert.Equal(AudioTrackId, sample.TrackId);
        Assert.Equal(
            PlaybackEncodedRandomAccessClassification.NotApplicable,
            sample.RandomAccessClassification);
    }

    [Fact]
    public void CreateBatch_PreservesNegativePresentationTimestamp()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(
                    0,
                    Sample([1, 2, 3, 4], 1_001, 0x02000000, -7))));

        var sample = Assert.Single(batch.Samples);
        Assert.Equal(0, sample.DtsTicks);
        Assert.Equal(-7, sample.PtsTicks);
    }

    [Theory]
    [InlineData(0x02000000U, PlaybackEncodedRandomAccessClassification.IndependentlyDecodable)]
    [InlineData(0x01000000U, PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable)]
    [InlineData(0U, PlaybackEncodedRandomAccessClassification.Indeterminate)]
    public void CreateBatch_MapsFoundationRandomAccessClassification(
        uint flags,
        PlaybackEncodedRandomAccessClassification expected)
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, flags))));

        Assert.Equal(expected, Assert.Single(batch.Samples)
            .RandomAccessClassification);
    }

    [Fact]
    public void Create_MaterializesImmutableCodecConfigurationAndTrackMetadata()
    {
        var initialization = CreateInitializationSegment();
        var adapter = CreateAdapter(initialization);
        var tracks = adapter.StreamDescription.TrackDescriptions;

        Assert.Collection(
            tracks,
            video =>
            {
                Assert.Equal(VideoTrackId, video.TrackId);
                Assert.Equal(PlaybackEncodedMediaRole.Video, video.MediaRole);
                Assert.Equal(PlaybackEncodedCodecKind.Avc, video.CodecKind);
                Assert.Equal(PlaybackEncodedCodecVariant.Avc1, video.CodecVariant);
                Assert.Equal(new PlaybackEncodedTimeBase(1, 90_000), video.TimeBase);
                Assert.Equal((byte)4, video.NalLengthSize);
                Assert.Equal(640, video.Width);
                Assert.Equal(360, video.Height);
                Assert.NotEmpty(video.CodecConfiguration);
            },
            audio =>
            {
                Assert.Equal(AudioTrackId, audio.TrackId);
                Assert.Equal(PlaybackEncodedMediaRole.Audio, audio.MediaRole);
                Assert.Equal(PlaybackEncodedCodecKind.Aac, audio.CodecKind);
                Assert.Equal(PlaybackEncodedCodecVariant.Aac, audio.CodecVariant);
                Assert.Equal(new PlaybackEncodedTimeBase(1, 48_000), audio.TimeBase);
                Assert.Equal(48_000, audio.SampleRate);
                Assert.Equal(2, audio.ChannelCount);
                Assert.Null(audio.NalLengthSize);
                Assert.NotEmpty(audio.CodecConfiguration);
            });

        var exposed = Assert.IsAssignableFrom<IList<byte>>(
            tracks[0].CodecConfiguration);
        Assert.Throws<NotSupportedException>(() => exposed[0] = 0);
    }

    [Fact]
    public void Create_AssignsDistinctGenerationForEachProfileLifetime()
    {
        var initialization = CreateInitializationSegment();
        var first = CreateAdapter(initialization);
        var second = CreateAdapter(initialization);

        Assert.NotEqual(
            first.StreamDescription.StreamGenerationId,
            second.StreamDescription.StreamGenerationId);
    }

    [Fact]
    public void CreateBatch_CopiesPayloadAndDoesNotRetainSourceMemory()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var original = CreateMediaSegment(
            1,
            VideoTrack(90_000, Sample([1, 3, 5, 7], 1_001, 0x02000000)));
        var expected = new byte[] { 1, 3, 5, 7 };
        var result = adapter.CreateBatch(original, out var batch);
        var ownedBatch = Assert.IsType<PlaybackEncodedSampleBatch>(batch);
        Array.Fill(original, (byte)0xff);

        Assert.Equal(PlaybackEncodedSampleBatchCreationResult.Created, result);
        var copied = new byte[4];
        ownedBatch.CopyPayloadTo(Assert.Single(ownedBatch.Samples), copied);
        Assert.Equal(expected, copied);
        Assert.Equal(4, ownedBatch.PayloadByteLength);
    }

    [Fact]
    public void CreateBatch_RejectsTimestampOutsideInt64WithoutPublishing()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var result = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                VideoTrack(ulong.MaxValue, Sample([1, 2, 3, 4], 1, 0x02000000))),
            out var batch);

        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.TimestampOverflow,
            result);
        Assert.Null(batch);
    }

    [Fact]
    public void CreateBatch_RejectsNonPositiveDurationWithoutPublishing()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var result = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 0, 0x02000000))),
            out var batch);

        Assert.Equal(PlaybackEncodedSampleBatchCreationResult.InvalidDuration, result);
        Assert.Null(batch);
    }

    [Fact]
    public void CreateBatch_RejectsPayloadOffsetOutsideValidatedSource()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var result = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                [VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000))],
                dataOffsetAdjustment: 1_000_000),
            out var batch);

        Assert.NotEqual(PlaybackEncodedSampleBatchCreationResult.Created, result);
        Assert.Null(batch);
    }

    [Fact]
    public void PayloadSlice_RejectsOffsetLengthArithmeticOverflow()
    {
        Assert.False(PlaybackEncodedPayloadSlice.TryCreate(
            int.MaxValue,
            1,
            out _));
    }

    [Fact]
    public void CreateBatch_RejectsAggregatePayloadLimitBeforePublication()
    {
        var options = new PlaybackFmp4LiveEncodedSampleBatchAdapterOptions
        {
            MaximumTotalPayloadBytes = 6,
        };
        var adapter = CreateAdapter(CreateInitializationSegment(), options);
        var result = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000)),
                AudioTrack(48_000, Sample([5, 6, 7], 1_024, 0))),
            out var batch);

        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.TotalPayloadLimitExceeded,
            result);
        Assert.Null(batch);
    }

    [Fact]
    public void CreateBatch_RejectsSampleCountLimitBeforePublication()
    {
        var options = new PlaybackFmp4LiveEncodedSampleBatchAdapterOptions
        {
            MaximumSampleCount = 1,
        };
        var adapter = CreateAdapter(CreateInitializationSegment(), options);
        var result = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000)),
                AudioTrack(48_000, Sample([5, 6, 7], 1_024, 0))),
            out var batch);

        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.SampleCountLimitExceeded,
            result);
        Assert.Null(batch);
    }

    [Fact]
    public void CreateBatch_FailureAfterCopyPublishesNothingAndDoesNotAdvanceEpoch()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var rejected = adapter.CreateBatch(
            CreateMediaSegment(
                1,
                VideoTrack(
                    90_000,
                    Sample([1, 2, 3, 4], 1_001, 0x02000000),
                    Sample([5, 6, 7, 8], 1_001, 0x03000000))),
            out var failedBatch);

        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.InvalidSampleMetadata,
            rejected);
        Assert.Null(failedBatch);

        using var succeedingBatch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([9, 10, 11, 12], 1_001, 0x02000000))));
        Assert.Equal(0UL, succeedingBatch.EpochId);
        Assert.Equal(1U, succeedingBatch.FragmentSequence);
    }

    [Fact]
    public void CreateBatch_DisposeIsIdempotentAndAccessAfterDisposeFails()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000))));
        var sample = Assert.Single(batch.Samples);

        batch.Dispose();
        batch.Dispose();

        Assert.True(batch.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            batch.CopyPayloadTo(sample, new byte[4]));
    }

    [Fact]
    public void CreateBatch_GenerationIdentityIsTheStreamDescriptionIdentity()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var batch = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000))));

        Assert.Equal(
            batch.StreamDescription.StreamGenerationId,
            batch.StreamGenerationId);
    }

    [Fact]
    public void CreateBatch_ContinuousOmissionAndReentryDoNotCreateTrackEvents()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var first = CreateBatch(
            adapter,
            CreateMediaSegment(
                1,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000)),
                AudioTrack(48_000, Sample([5, 6, 7], 1_024, 0))));
        using var omitted = CreateBatch(
            adapter,
            CreateMediaSegment(
                2,
                VideoTrack(91_001, Sample([8, 9, 10, 11], 1_001, 0x02000000))));
        using var reentered = CreateBatch(
            adapter,
            CreateMediaSegment(
                3,
                AudioTrack(49_024, Sample([12, 13, 14], 1_024, 0))));

        Assert.Equal(0UL, first.EpochId);
        Assert.DoesNotContain(
            omitted.Samples,
            sample => sample.TrackId == AudioTrackId);
        Assert.Equal(0UL, omitted.EpochId);
        Assert.Equal(0UL, reentered.EpochId);
        Assert.Equal(PlaybackEncodedDiscontinuity.None, reentered.Discontinuity);
        Assert.Equal(AudioTrackId, Assert.Single(reentered.Samples).TrackId);
    }

    [Fact]
    public void CreateBatch_GlobalDiscontinuityStartsOneEpochAndInvalidAnchorReentryDoesNotDoubleIncrement()
    {
        var adapter = CreateAdapter(CreateInitializationSegment());
        using var first = CreateBatch(
            adapter,
            CreateMediaSegment(
                10,
                VideoTrack(90_000, Sample([1, 2, 3, 4], 1_001, 0x02000000)),
                AudioTrack(48_000, Sample([5, 6, 7], 1_024, 0))));
        using var discontinuous = CreateBatch(
            adapter,
            CreateMediaSegment(
                12,
                VideoTrack(100_000, Sample([8, 9, 10, 11], 1_001, 0x02000000))));
        using var reentry = CreateBatch(
            adapter,
            CreateMediaSegment(
                13,
                AudioTrack(50_000, Sample([12, 13, 14], 1_024, 0))));
        using var resumed = CreateBatch(
            adapter,
            CreateMediaSegment(
                14,
                VideoTrack(101_001, Sample([15, 16, 17, 18], 1_001, 0x02000000))));

        Assert.Equal(0UL, first.EpochId);
        Assert.Equal(1UL, discontinuous.EpochId);
        Assert.Equal(
            PlaybackEncodedDiscontinuity.Global,
            discontinuous.Discontinuity);
        Assert.Equal(1UL, reentry.EpochId);
        Assert.Equal(PlaybackEncodedDiscontinuity.None, reentry.Discontinuity);
        Assert.Equal(1UL, resumed.EpochId);
        Assert.Equal(PlaybackEncodedDiscontinuity.None, resumed.Discontinuity);
    }

    private static PlaybackFmp4LiveEncodedSampleBatchAdapter CreateAdapter(
        byte[] initializationSegment,
        PlaybackFmp4LiveEncodedSampleBatchAdapterOptions? options = null)
    {
        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                initializationSegment,
                out var adapter,
                options));
        return Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(adapter);
    }

    private static PlaybackEncodedSampleBatch CreateBatch(
        PlaybackFmp4LiveEncodedSampleBatchAdapter adapter,
        byte[] mediaSegment)
    {
        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.Created,
            adapter.CreateBatch(mediaSegment, out var batch));
        return Assert.IsType<PlaybackEncodedSampleBatch>(batch);
    }

    private static byte[] CreateInitializationSegment()
    {
        var (videoSampleDescription, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests.BaselineRecord());
        var (audioSampleDescription, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests.ElementaryStreamBox(
                    AudioElementaryStreamWithSpecificInfo()));
        return Concat(
            Box(Ftyp),
            Box(
                Moov,
                InitializationTrack(
                    VideoTrackId,
                    Vide,
                    90_000,
                    videoSampleDescription),
                InitializationTrack(
                    AudioTrackId,
                    Soun,
                    48_000,
                    audioSampleDescription),
                Box(
                    Mvex,
                    TrackExtends(VideoTrackId, 1_001, 4, 0x02000000),
                    TrackExtends(AudioTrackId, 1_024, 3, 0))));
    }

    private static byte[] CreateMediaSegment(
        uint sequenceNumber,
        params FixtureTrack[] tracks) =>
        CreateMediaSegment(sequenceNumber, tracks, dataOffsetAdjustment: 0);

    private static byte[] AudioElementaryStreamWithSpecificInfo()
    {
        var specificInfo = Descriptor(0x05, [0x11, 0x90]);
        var decoderPayload = new byte[13 + specificInfo.Length];
        decoderPayload[0] = 0x40;
        decoderPayload[1] = (5 << 2) | 1;
        specificInfo.CopyTo(decoderPayload, 13);
        var decoderConfig = Descriptor(0x04, decoderPayload);
        var slConfig = Descriptor(0x06, [2]);
        var elementaryStreamPayload = new byte[3 + decoderConfig.Length + slConfig.Length];
        elementaryStreamPayload[1] = 1;
        decoderConfig.CopyTo(elementaryStreamPayload, 3);
        slConfig.CopyTo(elementaryStreamPayload, 3 + decoderConfig.Length);
        return Descriptor(0x03, elementaryStreamPayload);
    }

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        if (payload.Length >= 128)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        return [tag, (byte)payload.Length, .. payload];
    }

    private static byte[] CreateMediaSegment(
        uint sequenceNumber,
        FixtureTrack[] tracks,
        int dataOffsetAdjustment)
    {
        var placeholderOffsets = tracks.Select(static _ => 0).ToArray();
        var placeholder = MovieFragment(sequenceNumber, tracks, placeholderOffsets);
        var offsets = new int[tracks.Length];
        var payloadOffset = 0;
        for (var index = 0; index < tracks.Length; index++)
        {
            offsets[index] = checked(
                placeholder.Length + 8 + payloadOffset + dataOffsetAdjustment);
            payloadOffset = checked(payloadOffset + tracks[index].Samples.Sum(
                static sample => sample.Payload.Length));
        }

        var fragment = MovieFragment(sequenceNumber, tracks, offsets);
        var mediaData = Box(
            Mdat,
            Concat(tracks.SelectMany(static track =>
                track.Samples.Select(static sample => sample.Payload)).ToArray()));
        return Concat(fragment, mediaData);
    }

    private static FixtureTrack VideoTrack(
        ulong decodeTime,
        params FixtureSample[] samples) =>
        new(VideoTrackId, decodeTime, samples);

    private static FixtureTrack AudioTrack(
        ulong decodeTime,
        params FixtureSample[] samples) =>
        new(AudioTrackId, decodeTime, samples);

    private static FixtureSample Sample(
        byte[] payload,
        uint duration,
        uint flags,
        int compositionOffset = 0) =>
        new(payload, duration, flags, compositionOffset);

    private static byte[] InitializationTrack(
        uint trackId,
        uint handlerType,
        uint timescale,
        byte[] sampleDescription) =>
        Box(
            Trak,
            TrackHeader(trackId),
            Box(
                Mdia,
                Handler(handlerType),
                MediaHeader(timescale),
                Box(Minf, Box(Stbl, sampleDescription))));

    private static byte[] TrackHeader(uint trackId)
    {
        var box = new byte[92];
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

    private static byte[] MediaHeader(uint timescale)
    {
        var box = new byte[32];
        WriteHeader(box, Mdhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), 0x55c4);
        return box;
    }

    private static byte[] TrackExtends(
        uint trackId,
        uint sampleDuration,
        uint sampleSize,
        uint sampleFlags)
    {
        var box = new byte[32];
        WriteHeader(box, Trex);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24, 4), sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(28, 4), sampleFlags);
        return box;
    }

    private static byte[] MovieFragment(
        uint sequenceNumber,
        IReadOnlyList<FixtureTrack> tracks,
        int[] dataOffsets)
    {
        var children = new byte[tracks.Count + 1][];
        children[0] = MovieFragmentHeader(sequenceNumber);
        for (var index = 0; index < tracks.Count; index++)
        {
            children[index + 1] = TrackFragment(
                tracks[index],
                dataOffsets[index]);
        }

        return Box(Moof, children);
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(FixtureTrack track, int dataOffset) =>
        Box(
            Traf,
            TrackFragmentHeader(track.TrackId),
            TrackDecodeTime(track.DecodeTime),
            TrackRun(track.Samples, dataOffset));

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        WriteFullBoxHeader(box, 0, 0x020000);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        return box;
    }

    private static byte[] TrackDecodeTime(ulong decodeTime)
    {
        var version = decodeTime > uint.MaxValue ? (byte)1 : (byte)0;
        var box = new byte[version == 1 ? 20 : 16];
        WriteHeader(box, Tfdt);
        box[8] = version;
        if (version == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(12, 8), decodeTime);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(12, 4),
                checked((uint)decodeTime));
        }

        return box;
    }

    private static byte[] TrackRun(
        IReadOnlyList<FixtureSample> samples,
        int dataOffset)
    {
        const uint flags = 0x000001 | 0x000100 | 0x000200 | 0x000400 | 0x000800;
        var version = samples.Any(static sample => sample.CompositionOffset < 0)
            ? (byte)1
            : (byte)0;
        var box = new byte[checked(20 + samples.Count * 16)];
        WriteHeader(box, Trun);
        WriteFullBoxHeader(box, version, flags);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            checked((uint)samples.Count));
        BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(16, 4), dataOffset);
        var cursor = 20;
        foreach (var sample in samples)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(cursor, 4),
                sample.Duration);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(cursor + 4, 4),
                checked((uint)sample.Payload.Length));
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(cursor + 8, 4),
                sample.Flags);
            BinaryPrimitives.WriteInt32BigEndian(
                box.AsSpan(cursor + 12, 4),
                sample.CompositionOffset);
            cursor += 16;
        }

        return box;
    }

    private static byte[] Box(uint type, params byte[][] payloads)
    {
        var box = new byte[checked(8 + payloads.Sum(static payload =>
            payload.Length))];
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
        var result = new byte[checked(values.Sum(static value => value.Length))];
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

    private static void WriteFullBoxHeader(
        byte[] box,
        byte version,
        uint flags)
    {
        box[8] = version;
        box[9] = (byte)(flags >> 16);
        box[10] = (byte)(flags >> 8);
        box[11] = (byte)flags;
    }

    private sealed record FixtureTrack(
        uint TrackId,
        ulong DecodeTime,
        IReadOnlyList<FixtureSample> Samples);

    private sealed record FixtureSample(
        byte[] Payload,
        uint Duration,
        uint Flags,
        int CompositionOffset);
}
