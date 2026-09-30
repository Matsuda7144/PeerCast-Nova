using PeerCast.Nova.Core.Media;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4MuxerAcceptanceTests
{
    [Fact]
    public void Retained_fixture_roundtrips_through_generated_initialization_and_fragments()
    {
        var retained = Fmp4PcpLoopbackAcceptanceFixture.Load();
        Assert.Equal(14_777, retained.Bytes.Length);
        Assert.Equal(
            Fmp4PcpLoopbackAcceptanceFixture.ExpectedSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(retained.Bytes)));
        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                retained.InitializationSegment,
                out var sourceAdapter));
        var source = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(sourceAdapter);
        var inputTracks = CreateMuxTracks(source.StreamDescription.TrackDescriptions);
        var expectedByFragment = new List<ExpectedFragment>();

        foreach (var media in retained.MediaSegments)
        {
            Assert.Equal(
                PlaybackEncodedSampleBatchCreationResult.Created,
                source.CreateBatch(media, out var sourceBatch));
            using var batch = Assert.IsType<PlaybackEncodedSampleBatch>(sourceBatch);
            expectedByFragment.Add(CaptureExpected(batch));
        }

        var createResult = Fmp4Muxer.Create(inputTracks, out var created);
        Assert.True(createResult == Fmp4MuxerCreateResult.Created,
            $"{createResult}; video={Convert.ToHexString(inputTracks[0].CodecConfiguration.Span)}; " +
            $"audio={Convert.ToHexString(inputTracks[1].CodecConfiguration.Span)}; " +
            $"rate/channels={inputTracks[1].SampleRate}/{inputTracks[1].ChannelCount}");
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var initialization));
        var generatedInitialization = Assert.IsType<byte[]>(initialization);
        Assert.Equal(
            PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
            PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                generatedInitialization,
                generatedInitialization.Length,
                out var initEnvelope));
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Initialization, initEnvelope!.Kind);
        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult.Projected,
            PlaybackFmp4LiveInitializationProfileProjector.Project(
                generatedInitialization,
                generatedInitialization.Length,
                out var profile));
        var projectedProfile = Assert.IsType<
            PlaybackFmp4LiveInitializationProfileProjection>(profile);
        Assert.Equal(1U, projectedProfile.VideoTrackId);
        Assert.Equal(2U, projectedProfile.AudioTrackId);

        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                generatedInitialization,
                out var roundtripAdapter));
        var roundtrip = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(
            roundtripAdapter);
        AssertTrackDescriptionsEquivalent(
            source.StreamDescription.TrackDescriptions,
            roundtrip.StreamDescription.TrackDescriptions);

        var outputFragments = new List<byte[]>(expectedByFragment.Count);
        for (var index = 0; index < expectedByFragment.Count; index++)
        {
            var expected = expectedByFragment[index];
            Assert.Equal(Fmp4MuxerResult.Produced,
                muxer.MuxFragment(expected.Samples, out var generated));
            var segment = Assert.IsType<Fmp4MuxMediaSegment>(generated);
            Assert.Equal((uint)(index + 1), segment.SequenceNumber);
            Assert.Equal(
                PlaybackFmp4LiveSegmentEnvelopeValidationResult.Accepted,
                PlaybackFmp4LiveSegmentEnvelopeValidator.Validate(
                    segment.Bytes,
                    segment.Bytes.Length,
                    out var mediaEnvelope));
            Assert.Equal(PlaybackFmp4LiveSegmentKind.Media, mediaEnvelope!.Kind);
            var moof = segment.Bytes.AsSpan(0, mediaEnvelope.FirstBoxLength);
            Assert.Equal(
                PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
                PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                    moof,
                    out var identity));
            var projectedIdentity = Assert.IsType<
                PlaybackFmp4LiveMediaFragmentIdentityProjection>(identity);
            Assert.Equal((uint)(index + 1), projectedIdentity.SequenceNumber);
            Assert.Equal(
                PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
                PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                    moof,
                    projectedIdentity,
                    out var timeline));
            var projectedTimeline = Assert.IsType<
                PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(timeline);
            foreach (var track in projectedTimeline.Tracks)
            {
                var firstSample = expected.Samples.First(sample =>
                    sample.TrackId == track.TrackId);
                Assert.Equal((ulong)firstSample.DtsTicks,
                    track.BaseMediaDecodeTime);
            }

            AssertDirectMediaBoxAndDataOffsets(segment.Bytes, expected.Samples);

            Assert.Equal(
                PlaybackEncodedSampleBatchCreationResult.Created,
                roundtrip.CreateBatch(segment.Bytes, out var outputBatch));
            using var parsed = Assert.IsType<PlaybackEncodedSampleBatch>(outputBatch);
            Assert.Equal((uint)(index + 1), parsed.FragmentSequence);
            AssertSamplesEquivalent(expected, parsed);
            outputFragments.Add(segment.Bytes);
        }

        Assert.Equal(2, outputFragments.Count);
        Assert.Equal(2, muxer.Snapshot.MediaFragmentGeneratedCount);
        Assert.Equal(1, muxer.Snapshot.InitializationGeneratedCount);
        Assert.Equal(expectedByFragment.Sum(static fragment => fragment.Samples.Count),
            muxer.Snapshot.InputSampleCount);
    }

    [Fact]
    public void Synthetic_pts_not_equal_to_dts_survives_signed_trun_composition_offsets()
    {
        var tracks = new[]
        {
            new Fmp4MuxTrackDescription(
                7,
                Fmp4MuxMediaKind.Video,
                Fmp4MuxCodec.Avc1,
                90_000,
                new byte[]
                {
                    1, 66, 0, 30, 0xff, 0xe1,
                    0, 2, 0x67, 0x42,
                    1, 0, 1, 0x68,
                },
                width: 640,
                height: 360),
            new Fmp4MuxTrackDescription(
                9,
                Fmp4MuxMediaKind.Audio,
                Fmp4MuxCodec.AacLc,
                48_000,
                new byte[] { 0x11, 0x90 },
                sampleRate: 48_000,
                channelCount: 2),
        };
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(tracks, out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var initialization));
        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                Assert.IsType<byte[]>(initialization),
                out var adapter));
        var parsedAdapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(adapter);

        var samples = new Fmp4MuxEncodedSample[]
        {
            new(9, 0, 0, 1_024, new byte[] { 0x21, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
            new(7, 90_000, 89_993, 1_001, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(9, 1_024, 1_024, 1_024, new byte[] { 0x22, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
            new(7, 91_001, 91_003, 1_001, new byte[] { 0, 0, 0, 2, 0x41, 0x9a },
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
        };
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(samples, out var generated));
        var media = Assert.IsType<Fmp4MuxMediaSegment>(generated);
        Assert.Equal(
            PlaybackEncodedSampleBatchCreationResult.Created,
            parsedAdapter.CreateBatch(media.Bytes, out var batch));
        using var parsed = Assert.IsType<PlaybackEncodedSampleBatch>(batch);

        var firstVideo = parsed.Samples.Single(sample =>
            sample.TrackId == 7 && sample.SampleOrdinal == 0);
        var secondVideo = parsed.Samples.Single(sample =>
            sample.TrackId == 7 && sample.SampleOrdinal == 1);
        Assert.Equal(90_000, firstVideo.DtsTicks);
        Assert.Equal(89_993, firstVideo.PtsTicks);
        Assert.Equal(91_001, secondVideo.DtsTicks);
        Assert.Equal(91_003, secondVideo.PtsTicks);
        Assert.Equal(1_001, firstVideo.DurationTicks);
        Assert.Equal(1_001, secondVideo.DurationTicks);
        Assert.Equal(
            PlaybackEncodedRandomAccessClassification.IndependentlyDecodable,
            firstVideo.RandomAccessClassification);
        Assert.Equal(
            PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable,
            secondVideo.RandomAccessClassification);
    }

    private static Fmp4MuxTrackDescription[] CreateMuxTracks(
        IReadOnlyList<PlaybackEncodedTrackDescription> descriptions)
    {
        var result = new Fmp4MuxTrackDescription[descriptions.Count];
        for (var index = 0; index < descriptions.Count; index++)
        {
            var track = descriptions[index];
            Assert.Equal(1, track.TimeBase.Numerator);
            result[index] = new(
                track.TrackId,
                track.MediaRole == PlaybackEncodedMediaRole.Video
                    ? Fmp4MuxMediaKind.Video
                    : Fmp4MuxMediaKind.Audio,
                track.CodecVariant == PlaybackEncodedCodecVariant.Avc1
                    ? Fmp4MuxCodec.Avc1
                    : Fmp4MuxCodec.AacLc,
                checked((uint)track.TimeBase.Denominator),
                track.CodecConfiguration.ToArray(),
                track.Width ?? 0,
                track.Height ?? 0,
                track.SampleRate ?? 0,
                track.ChannelCount ?? 0);
        }

        return result;
    }

    private static ExpectedFragment CaptureExpected(PlaybackEncodedSampleBatch batch)
    {
        var samples = new Fmp4MuxEncodedSample[batch.Samples.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = batch.Samples[index];
            var payload = new byte[sample.PayloadSlice.Length];
            batch.CopyPayloadTo(sample, payload);
            samples[index] = new(
                sample.TrackId,
                sample.DtsTicks,
                sample.PtsTicks,
                sample.DurationTicks,
                payload,
                sample.RandomAccessClassification switch
                {
                    PlaybackEncodedRandomAccessClassification.IndependentlyDecodable =>
                        Fmp4MuxRandomAccessKind.IndependentlyDecodable,
                    PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable =>
                        Fmp4MuxRandomAccessKind.NotIndependentlyDecodable,
                    PlaybackEncodedRandomAccessClassification.NotApplicable =>
                        Fmp4MuxRandomAccessKind.NotApplicable,
                    _ => Fmp4MuxRandomAccessKind.Indeterminate,
                });
        }

        return new(samples);
    }

    private static void AssertTrackDescriptionsEquivalent(
        IReadOnlyList<PlaybackEncodedTrackDescription> expected,
        IReadOnlyList<PlaybackEncodedTrackDescription> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].TrackId, actual[index].TrackId);
            Assert.Equal(expected[index].MediaRole, actual[index].MediaRole);
            Assert.Equal(expected[index].CodecKind, actual[index].CodecKind);
            Assert.Equal(expected[index].CodecVariant, actual[index].CodecVariant);
            Assert.Equal(expected[index].TimeBase, actual[index].TimeBase);
            Assert.Equal(expected[index].NalLengthSize, actual[index].NalLengthSize);
            Assert.Equal(expected[index].Width, actual[index].Width);
            Assert.Equal(expected[index].Height, actual[index].Height);
            Assert.Equal(expected[index].SampleRate, actual[index].SampleRate);
            Assert.Equal(expected[index].ChannelCount, actual[index].ChannelCount);
            Assert.Equal(expected[index].CodecConfiguration.ToArray(),
                actual[index].CodecConfiguration.ToArray());
        }
    }

    private static void AssertSamplesEquivalent(
        ExpectedFragment expected,
        PlaybackEncodedSampleBatch actual)
    {
        Assert.Equal(expected.Samples.Count, actual.Samples.Count);
        for (var index = 0; index < expected.Samples.Count; index++)
        {
            var source = expected.Samples[index];
            var parsed = actual.Samples[index];
            Assert.Equal(source.TrackId, parsed.TrackId);
            Assert.Equal(source.DtsTicks, parsed.DtsTicks);
            Assert.Equal(source.PtsTicks, parsed.PtsTicks);
            Assert.Equal(source.DurationTicks, parsed.DurationTicks);
            Assert.Equal(source.RandomAccessKind, parsed.RandomAccessClassification switch
            {
                PlaybackEncodedRandomAccessClassification.IndependentlyDecodable =>
                    Fmp4MuxRandomAccessKind.IndependentlyDecodable,
                PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable =>
                    Fmp4MuxRandomAccessKind.NotIndependentlyDecodable,
                PlaybackEncodedRandomAccessClassification.NotApplicable =>
                    Fmp4MuxRandomAccessKind.NotApplicable,
                _ => Fmp4MuxRandomAccessKind.Indeterminate,
            });
            var payload = new byte[parsed.PayloadSlice.Length];
            actual.CopyPayloadTo(parsed, payload);
            Assert.Equal(source.Payload.ToArray(), payload);
        }
    }

    private static void AssertDirectMediaBoxAndDataOffsets(
        byte[] media,
        IReadOnlyList<Fmp4MuxEncodedSample> samples)
    {
        var moofLength = checked((int)System.Buffers.Binary.BinaryPrimitives
            .ReadUInt32BigEndian(media));
        Assert.Equal("moof", System.Text.Encoding.ASCII.GetString(media, 4, 4));
        Assert.Equal("mdat", System.Text.Encoding.ASCII.GetString(media, moofLength + 4, 4));
        var trackIds = new List<uint>();
        var childOffset = 8;
        while (childOffset < moofLength)
        {
            var childLength = checked((int)System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32BigEndian(media.AsSpan(childOffset, 4)));
            if (System.Text.Encoding.ASCII.GetString(media, childOffset + 4, 4) == "traf")
            {
                var trafEnd = childOffset + childLength;
                var nestedOffset = childOffset + 8;
                uint? trackId = null;
                int? dataOffset = null;
                while (nestedOffset < trafEnd)
                {
                    var nestedLength = checked((int)System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt32BigEndian(media.AsSpan(nestedOffset, 4)));
                    var type = System.Text.Encoding.ASCII.GetString(media, nestedOffset + 4, 4);
                    if (type == "tfhd")
                    {
                        trackId = System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt32BigEndian(media.AsSpan(nestedOffset + 12, 4));
                    }
                    else if (type == "trun")
                    {
                        dataOffset = System.Buffers.Binary.BinaryPrimitives
                            .ReadInt32BigEndian(media.AsSpan(nestedOffset + 16, 4));
                    }

                    nestedOffset += nestedLength;
                }

                Assert.True(trackId.HasValue);
                Assert.True(dataOffset.HasValue);
                trackIds.Add(trackId.Value);
                var expectedOffset = moofLength + 8 + samples
                    .Where(sample => sample.TrackId < trackId.Value)
                    .Sum(static sample => sample.Payload.Length);
                Assert.Equal(expectedOffset, dataOffset.Value);
            }

            childOffset += childLength;
        }

        Assert.Equal(new uint[] { 1, 2 }, trackIds);
        var expectedPayload = samples.Where(static sample => sample.TrackId == 1)
            .Concat(samples.Where(static sample => sample.TrackId == 2))
            .SelectMany(static sample => sample.Payload.ToArray())
            .ToArray();
        Assert.Equal(expectedPayload, media.AsSpan(moofLength + 8).ToArray());
    }

    private sealed record ExpectedFragment(
        IReadOnlyList<Fmp4MuxEncodedSample> Samples);
}
