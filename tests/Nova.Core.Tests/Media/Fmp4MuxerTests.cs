using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PeerCast.Nova.Core.Media;

namespace PeerCast.Nova.Core.Tests.Media;

public sealed class Fmp4MuxerTests
{
    private static readonly byte[] AvcConfiguration =
    [
        1, 66, 0, 30, 0xff, 0xe1,
        0, 2, 0x67, 0x42,
        1, 0, 1, 0x68,
    ];

    private static readonly byte[] MainProfileFixtureAvcConfiguration =
    [
        1, 77, 0x5c, 11, 0xff, 0xe1,
        0, 13, 0x67, 0x4d, 0x5c, 0x0b, 0x8c, 0x68, 0x47, 0x30,
        0x10, 0x1e, 0x11, 0x08, 0xd4,
        1, 0, 4, 0x68, 0xee, 0x3c, 0x80,
    ];

    private static readonly byte[] HighProfileExtendedAvcConfiguration =
    [
        1, 100, 0, 40, 0xff, 0xe1,
        0, 2, 0x67, 0x64,
        1, 0, 1, 0x68,
        0xfd, 0xf8, 0xf8, 1,
        0, 1, 0x6d,
    ];

    private static readonly byte[] AacConfiguration = [0x11, 0x90];

    private static readonly byte[] AacConfigurationWithSbrAbsentSyncExtension =
        [0x11, 0x90, 0x56, 0xe5, 0x00];

    [Fact]
    public void Accepts_and_preserves_exact_aac_lc_configuration_in_decoder_specific_info()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(aac: AacConfiguration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(AacConfiguration,
            FindAudioSpecificConfig(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Track_codec_configuration_copies_constructor_input_and_isolates_public_reads()
    {
        var avcInput = AvcConfiguration.ToArray();
        var aacInput = AacConfiguration.ToArray();
        var tracks = new[]
        {
            new Fmp4MuxTrackDescription(
                1,
                Fmp4MuxMediaKind.Video,
                Fmp4MuxCodec.Avc1,
                90_000,
                avcInput,
                width: 640,
                height: 360),
            new Fmp4MuxTrackDescription(
                2,
                Fmp4MuxMediaKind.Audio,
                Fmp4MuxCodec.AacLc,
                48_000,
                aacInput,
                sampleRate: 48_000,
                channelCount: 2),
        };
        Array.Fill(avcInput, (byte)0);
        Array.Fill(aacInput, (byte)0);

        Assert.Equal(AvcConfiguration, tracks[0].CodecConfiguration.ToArray());
        Assert.Equal(AacConfiguration, tracks[1].CodecConfiguration.ToArray());

        var firstAvcRead = tracks[0].CodecConfiguration;
        var secondAvcRead = tracks[0].CodecConfiguration;
        var firstAacRead = tracks[1].CodecConfiguration;
        var secondAacRead = tracks[1].CodecConfiguration;
        MutateReturnedCodecConfiguration(firstAvcRead, 0, 0);
        MutateReturnedCodecConfiguration(firstAacRead, 0, 0);

        Assert.Equal(AvcConfiguration, secondAvcRead.ToArray());
        Assert.Equal(AacConfiguration, secondAacRead.ToArray());
        Assert.Equal(AvcConfiguration, tracks[0].CodecConfiguration.ToArray());
        Assert.Equal(AacConfiguration, tracks[1].CodecConfiguration.ToArray());
    }

    [Fact]
    public void Post_create_codec_configuration_getter_mutation_cannot_change_initialization()
    {
        using var baselineMuxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            baselineMuxer.GenerateInitializationSegment(out var baseline));
        var baselineInitialization = Assert.IsType<byte[]>(baseline);

        var tracks = Tracks();
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(tracks, out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        MutateReturnedCodecConfiguration(tracks[0].CodecConfiguration, 0, 0);
        MutateReturnedCodecConfiguration(tracks[1].CodecConfiguration, 0, 0);
        Assert.Equal(AvcConfiguration, tracks[0].CodecConfiguration.ToArray());
        Assert.Equal(AacConfiguration, tracks[1].CodecConfiguration.ToArray());

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        var initialization = Assert.IsType<byte[]>(generated);
        Assert.Equal(baselineInitialization, initialization);
        Assert.Equal(AvcConfiguration, FindAvcConfiguration(initialization));
        Assert.Equal(AacConfiguration, FindAudioSpecificConfig(initialization));
    }

    [Fact]
    public void Stored_esds_has_zero_es_id_and_preserves_descriptor_hierarchy()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(aac: AacConfiguration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        var initialization = Assert.IsType<byte[]>(generated);

        var esdsTypeOffset = initialization.AsSpan().IndexOf("esds"u8);
        Assert.True(esdsTypeOffset >= 4);
        var esdsBoxOffset = esdsTypeOffset - 4;
        var esdsBoxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            initialization.AsSpan(esdsBoxOffset, 4)));
        Assert.True(esdsBoxLength >= 12);
        Assert.True(esdsBoxOffset + esdsBoxLength <= initialization.Length);

        var descriptors = initialization.AsSpan(
            esdsTypeOffset + 4,
            esdsBoxLength - 8);
        Assert.Equal(new byte[4], descriptors[..4].ToArray());

        var offset = 4;
        Assert.Equal((byte)0x03, descriptors[offset++]);
        var elementaryLength = ReadDescriptorLength(descriptors, ref offset);
        var elementaryEnd = offset + elementaryLength;
        Assert.True(elementaryEnd <= descriptors.Length);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(
            descriptors.Slice(offset, 2)));
        Assert.Equal((byte)0, descriptors[offset + 2]);
        offset += 3;

        Assert.Equal((byte)0x04, descriptors[offset++]);
        var decoderConfigLength = ReadDescriptorLength(descriptors, ref offset);
        var decoderConfigStart = offset;
        var decoderConfigEnd = decoderConfigStart + decoderConfigLength;
        Assert.True(decoderConfigEnd <= elementaryEnd);

        offset = decoderConfigStart + 13;
        Assert.Equal((byte)0x05, descriptors[offset++]);
        var audioConfigLength = ReadDescriptorLength(descriptors, ref offset);
        Assert.Equal(decoderConfigEnd, offset + audioConfigLength);
        Assert.Equal(AacConfiguration,
            descriptors.Slice(offset, audioConfigLength).ToArray());

        offset = decoderConfigEnd;
        Assert.Equal((byte)0x06, descriptors[offset++]);
        var slConfigLength = ReadDescriptorLength(descriptors, ref offset);
        Assert.Equal(elementaryEnd, offset + slConfigLength);
        Assert.Equal(new byte[] { 0x02 },
            descriptors.Slice(offset, slConfigLength).ToArray());
    }

    [Fact]
    public void Accepts_and_preserves_aac_lc_sync_extension_when_sbr_is_absent()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(
                Tracks(aac: AacConfigurationWithSbrAbsentSyncExtension),
                out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(AacConfigurationWithSbrAbsentSyncExtension,
            FindAudioSpecificConfig(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Accepts_and_preserves_aac_lc_frame_length_flag()
    {
        var configuration = BuildAacConfiguration(frameLengthFlag: true);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(aac: configuration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(configuration,
            FindAudioSpecificConfig(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Rejects_aac_lc_configuration_with_sbr_sync_extension_without_throwing()
    {
        AssertUnsupportedAacConfiguration([0x11, 0x90, 0x56, 0xe5, 0x98]);
    }

    [Fact]
    public void Rejects_aac_lc_configuration_with_trailing_zero_byte_without_throwing()
    {
        AssertUnsupportedAacConfiguration([0x11, 0x90, 0x00]);
    }

    [Fact]
    public void Rejects_aac_lc_sync_extension_with_nonzero_alignment_bits()
    {
        AssertUnsupportedAacConfiguration([0x11, 0x90, 0x56, 0xe5, 0x01]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rejects_unsupported_audio_object_types(int audioObjectType)
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(audioObjectType: audioObjectType));
    }

    [Fact]
    public void Rejects_escaped_audio_object_type_without_throwing()
    {
        var configuration = BuildEscapedAacConfiguration(escapedObjectType: 0);
        AssertUnsupportedAacConfiguration(configuration);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0xf8)]
    public void Rejects_truncated_audio_object_type_or_frequency_field(byte firstByte)
    {
        AssertUnsupportedAacConfiguration([firstByte]);
    }

    [Theory]
    [InlineData(2, 64_000)]
    [InlineData(3, 48_000)]
    [InlineData(4, 44_100)]
    [InlineData(5, 32_000)]
    [InlineData(6, 24_000)]
    [InlineData(7, 22_050)]
    [InlineData(8, 16_000)]
    [InlineData(9, 12_000)]
    [InlineData(10, 11_025)]
    [InlineData(11, 8_000)]
    [InlineData(12, 7_350)]
    public void Accepts_indexed_aac_sample_rates(int frequencyIndex, int sampleRate)
    {
        var configuration = BuildAacConfiguration(frequencyIndex: frequencyIndex);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(
                Tracks(aac: configuration, audioSampleRate: sampleRate),
                out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void Rejects_reserved_aac_sample_rate_indices(int frequencyIndex)
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(frequencyIndex: frequencyIndex));
    }

    [Fact]
    public void Accepts_and_preserves_aac_explicit_sample_rate()
    {
        var configuration = BuildAacConfiguration(
            frequencyIndex: 15,
            explicitSampleRate: 48_000);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(aac: configuration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(configuration,
            FindAudioSpecificConfig(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Accepts_explicit_aac_sample_rate_at_track_metadata_upper_bound()
    {
        var configuration = BuildAacConfiguration(
            frequencyIndex: 15,
            explicitSampleRate: ushort.MaxValue);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(
                Tracks(aac: configuration, audioSampleRate: ushort.MaxValue),
                out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    [InlineData(0x00ff_ffff)]
    public void Rejects_explicit_aac_sample_rates_outside_supported_track_range(
        int sampleRate)
    {
        var configuration = BuildAacConfiguration(
            frequencyIndex: 15,
            explicitSampleRate: sampleRate);
        AssertUnsupportedAacConfiguration(configuration);
    }

    [Theory]
    [InlineData(0, 96_000)]
    [InlineData(1, 88_200)]
    public void Rejects_indexed_aac_sample_rates_above_supported_track_range(
        int frequencyIndex,
        int sampleRate)
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(frequencyIndex: frequencyIndex),
            audioSampleRate: sampleRate);
    }

    [Fact]
    public void Rejects_aac_sample_rate_mismatch_with_track_metadata()
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(frequencyIndex: 4));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 8)]
    public void Accepts_supported_direct_aac_channel_configurations(
        int channelConfiguration,
        int channelCount)
    {
        var configuration = BuildAacConfiguration(
            channelConfiguration: channelConfiguration);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(
                Tracks(aac: configuration, audioChannelCount: channelCount),
                out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(15)]
    public void Rejects_unsupported_aac_channel_configurations(
        int channelConfiguration)
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(channelConfiguration: channelConfiguration));
    }

    [Fact]
    public void Rejects_aac_channel_configuration_mismatch_with_track_metadata()
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(channelConfiguration: 1));
    }

    [Fact]
    public void Rejects_aac_configuration_with_core_coder_dependency()
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(dependsOnCoreCoder: true));
    }

    [Fact]
    public void Rejects_aac_configuration_with_extension_flag()
    {
        AssertUnsupportedAacConfiguration(
            BuildAacConfiguration(extensionFlag: true));
    }

    [Fact]
    public void Initialization_is_deterministic_and_returned_as_an_owned_copy()
    {
        using var muxer = CreateMuxer();

        Assert.Equal(
            Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var first));
        Assert.Equal(
            Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var second));
        var initialization = Assert.IsType<byte[]>(first);
        var repeated = Assert.IsType<byte[]>(second);

        Assert.Equal(initialization, repeated);
        Assert.Equal(
            Convert.FromHexString("000000186674797069736f360000000069736f3664617368"),
            initialization.AsSpan(0, 24).ToArray());
        var ftypLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            initialization.AsSpan(0, 4)));
        Assert.Equal(24, ftypLength);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(initialization, 4, 4));
        Assert.Equal("iso6", System.Text.Encoding.ASCII.GetString(initialization, 8, 4));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32BigEndian(
            initialization.AsSpan(12, 4)));
        var compatibleBrands = Enumerable.Range(0, (ftypLength - 16) / 4)
            .Select(index => System.Text.Encoding.ASCII.GetString(
                initialization,
                16 + (index * 4),
                4))
            .ToArray();
        Assert.Equal(2, compatibleBrands.Length);
        Assert.Equal("iso6", compatibleBrands[0]);
        Assert.Equal("dash", compatibleBrands[1]);
        Assert.DoesNotContain("mp41", compatibleBrands);
        Assert.Equal(1, muxer.Snapshot.InitializationGeneratedCount);
        Assert.Equal(initialization.Length, muxer.Snapshot.OutputByteCount);

        Array.Fill(initialization, (byte)0xff);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(repeated, 4, 4));
    }

    [Fact]
    public void Fragment_has_big_endian_boxes_correct_offsets_and_video_then_audio_payloads()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        byte[] videoPayload = [0, 0, 0, 2, 0x65, 0x88];
        byte[] audioPayload = [0x21, 0x10];
        var samples = new Fmp4MuxEncodedSample[]
        {
            new(2, 0, 0, 1_024, audioPayload, Fmp4MuxRandomAccessKind.NotApplicable),
            new(1, 90_000, 89_993, 1_001, videoPayload,
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        };

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(samples, out var media));
        var output = Assert.IsType<Fmp4MuxMediaSegment>(media);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            output.Bytes.AsSpan(0, 4)));
        Assert.Equal("moof", System.Text.Encoding.ASCII.GetString(output.Bytes, 4, 4));
        Assert.Equal(1U, output.SequenceNumber);
        Assert.Equal("mdat", System.Text.Encoding.ASCII.GetString(output.Bytes, moofLength + 4, 4));
        Assert.Equal(videoPayload.Concat(audioPayload),
            output.Bytes.AsSpan(moofLength + 8).ToArray());

        var trunOffsets = FindTrunDataOffsets(output.Bytes.AsSpan(0, moofLength));
        Assert.Equal(2, trunOffsets.Count);
        Assert.Equal(moofLength + 8, trunOffsets[0].DataOffset);
        Assert.Equal(moofLength + 8 + videoPayload.Length, trunOffsets[1].DataOffset);
        var tfhdFlags = FindTfhdFlags(output.Bytes.AsSpan(0, moofLength));
        Assert.Equal(2, tfhdFlags.Count);
        Assert.All(tfhdFlags, flags => Assert.Equal(0x020000U, flags));
        Assert.Equal((byte)1, trunOffsets[0].Version);
        Assert.Equal(-7, trunOffsets[0].FirstCompositionOffset);
        Assert.Equal(muxer.Snapshot.InputPayloadByteCount,
            videoPayload.Length + audioPayload.Length);
    }

    [Fact]
    public void Fragment_serializes_consistent_video_sync_non_sync_and_audio_flags()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        Fmp4MuxEncodedSample[] samples =
        [
            new(1, 0, 0, 1_001, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(2, 0, 0, 1_024, new byte[] { 0x21, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
            new(1, 1_001, 1_001, 1_001, new byte[] { 0, 0, 0, 2, 0x41, 0x9a },
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
        ];

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(samples, out var generated));
        var segment = Assert.IsType<Fmp4MuxMediaSegment>(generated);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            segment.Bytes));
        var sampleFlagsByTrack = FindTrunSampleFlags(
            segment.Bytes.AsSpan(0, moofLength));

        Assert.Equal(new uint[] { 0x02000000, 0x01010000 },
            sampleFlagsByTrack[1]);
        Assert.Equal(new uint[] { 0 }, sampleFlagsByTrack[2]);
        Assert.Equal(2U, (sampleFlagsByTrack[1][0] >> 24) & 0x03);
        Assert.Equal(0U, sampleFlagsByTrack[1][0] & 0x00010000);
        Assert.Equal(1U, (sampleFlagsByTrack[1][1] >> 24) & 0x03);
        Assert.Equal(0x00010000U,
            sampleFlagsByTrack[1][1] & 0x00010000);
    }

    [Fact]
    public void Rejects_video_indeterminate_random_access_atomically_and_remains_usable()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        var before = muxer.Snapshot;
        var indeterminateVideo = new Fmp4MuxEncodedSample(
            1,
            0,
            0,
            1_001,
            new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
            Fmp4MuxRandomAccessKind.Indeterminate);
        Assert.Equal(Fmp4MuxerResult.InvalidInput,
            muxer.MuxFragment([indeterminateVideo], out var rejected));
        Assert.Null(rejected);

        var afterRejection = muxer.Snapshot;
        Assert.Equal(before.State, afterRejection.State);
        Assert.Equal(before.InitializationGeneratedCount,
            afterRejection.InitializationGeneratedCount);
        Assert.Equal(before.MediaFragmentGeneratedCount,
            afterRejection.MediaFragmentGeneratedCount);
        Assert.Equal(before.InputSampleCount, afterRejection.InputSampleCount);
        Assert.Equal(before.InputPayloadByteCount,
            afterRejection.InputPayloadByteCount);
        Assert.Equal(before.OutputByteCount, afterRejection.OutputByteCount);
        Assert.Equal(before.NextFragmentSequenceNumber,
            afterRejection.NextFragmentSequenceNumber);
        Assert.Equal(before.ValidationFailureCount + 1,
            afterRejection.ValidationFailureCount);

        var validVideo = indeterminateVideo with
        {
            RandomAccessKind = Fmp4MuxRandomAccessKind.IndependentlyDecodable,
        };
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment([validVideo], out var accepted));
        var segment = Assert.IsType<Fmp4MuxMediaSegment>(accepted);
        Assert.Equal(1U, segment.SequenceNumber);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            segment.Bytes));
        Assert.Equal(0UL, FindTrackFragmentTimelines(
            segment.Bytes.AsSpan(0, moofLength))[1].DecodeTime);
    }

    [Fact]
    public void Rejects_video_not_applicable_random_access()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        AssertRejected(muxer,
        [
            new(1, 0, 0, 1_001, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                Fmp4MuxRandomAccessKind.NotApplicable),
        ]);
    }

    [Theory]
    [InlineData(Fmp4MuxRandomAccessKind.Indeterminate)]
    [InlineData(Fmp4MuxRandomAccessKind.IndependentlyDecodable)]
    [InlineData(Fmp4MuxRandomAccessKind.NotIndependentlyDecodable)]
    public void Rejects_video_random_access_kinds_for_audio(
        Fmp4MuxRandomAccessKind randomAccessKind)
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        AssertRejected(muxer,
        [
            new(2, 0, 0, 1_024, new byte[] { 0x21, 0x10 }, randomAccessKind),
        ]);
    }

    [Fact]
    public void Rejects_undefined_random_access_enum_value()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        AssertRejected(muxer,
        [
            new(1, 0, 0, 1_001, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                (Fmp4MuxRandomAccessKind)999),
        ]);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public void Rejects_intra_fragment_dts_gaps_per_track_atomically_and_remains_usable(
        uint trackId)
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        Fmp4MuxEncodedSample[] gapSamples = trackId == 1
            ?
            [
                new(1, 0, 0, 100, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                    Fmp4MuxRandomAccessKind.IndependentlyDecodable),
                new(2, 0, 0, 1_024, new byte[] { 0x21, 0x10 },
                    Fmp4MuxRandomAccessKind.NotApplicable),
                new(1, 150, 150, 100, new byte[] { 0, 0, 0, 2, 0x41, 0x9a },
                    Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
            ]
            :
            [
                new(2, 0, 0, 100, new byte[] { 0x21, 0x10 },
                    Fmp4MuxRandomAccessKind.NotApplicable),
                new(1, 0, 0, 100, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                    Fmp4MuxRandomAccessKind.IndependentlyDecodable),
                new(2, 150, 150, 100, new byte[] { 0x22, 0x10 },
                    Fmp4MuxRandomAccessKind.NotApplicable),
            ];

        var before = muxer.Snapshot;
        Assert.Equal(Fmp4MuxerResult.InvalidInput,
            muxer.MuxFragment(gapSamples, out var rejected));
        Assert.Null(rejected);

        var afterRejection = muxer.Snapshot;
        Assert.Equal(before.State, afterRejection.State);
        Assert.Equal(before.InitializationGeneratedCount,
            afterRejection.InitializationGeneratedCount);
        Assert.Equal(before.MediaFragmentGeneratedCount,
            afterRejection.MediaFragmentGeneratedCount);
        Assert.Equal(before.InputSampleCount, afterRejection.InputSampleCount);
        Assert.Equal(before.InputPayloadByteCount,
            afterRejection.InputPayloadByteCount);
        Assert.Equal(before.OutputByteCount, afterRejection.OutputByteCount);
        Assert.Equal(before.NextFragmentSequenceNumber,
            afterRejection.NextFragmentSequenceNumber);
        Assert.Equal(before.ValidationFailureCount + 1,
            afterRejection.ValidationFailureCount);

        Fmp4MuxEncodedSample[] contiguousSamples = trackId == 1
            ?
            [
                new(1, 0, 0, 100, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                    Fmp4MuxRandomAccessKind.IndependentlyDecodable),
                new(1, 100, 100, 100, new byte[] { 0, 0, 0, 2, 0x41, 0x9a },
                    Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
            ]
            :
            [
                new(2, 0, 0, 100, new byte[] { 0x21, 0x10 },
                    Fmp4MuxRandomAccessKind.NotApplicable),
                new(2, 100, 100, 100, new byte[] { 0x22, 0x10 },
                    Fmp4MuxRandomAccessKind.NotApplicable),
            ];

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(contiguousSamples, out var accepted));
        var media = Assert.IsType<Fmp4MuxMediaSegment>(accepted);
        Assert.Equal(1U, media.SequenceNumber);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            media.Bytes));
        var timeline = FindTrackFragmentTimelines(media.Bytes.AsSpan(0, moofLength));
        Assert.Equal(0UL, timeline[trackId].DecodeTime);
        Assert.Equal(new uint[] { 100, 100 }, timeline[trackId].SampleDurations);
        Assert.Equal(1, muxer.Snapshot.MediaFragmentGeneratedCount);
        Assert.Equal(2, muxer.Snapshot.InputSampleCount);
    }

    [Fact]
    public void Interleaved_tracks_accept_independent_contiguous_dts_timelines()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        Fmp4MuxEncodedSample[] samples =
        [
            new(1, 0, 0, 100, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(2, 0, 0, 1_024, new byte[] { 0x21, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
            new(1, 100, 100, 100, new byte[] { 0, 0, 0, 2, 0x41, 0x9a },
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
            new(2, 1_024, 1_024, 1_024, new byte[] { 0x22, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
        ];

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(samples, out var generated));
        var media = Assert.IsType<Fmp4MuxMediaSegment>(generated);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            media.Bytes));
        var timelines = FindTrackFragmentTimelines(media.Bytes.AsSpan(0, moofLength));

        Assert.Equal(0UL, timelines[1].DecodeTime);
        Assert.Equal(new uint[] { 100, 100 }, timelines[1].SampleDurations);
        Assert.Equal(0UL, timelines[2].DecodeTime);
        Assert.Equal(new uint[] { 1_024, 1_024 }, timelines[2].SampleDurations);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public void Accepts_cross_fragment_forward_dts_gap_and_preserves_tfdt(uint trackId)
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(
                [CreateSingleTrackSample(trackId, dts: 0)],
                out var firstFragment));
        Assert.Equal(1U,
            Assert.IsType<Fmp4MuxMediaSegment>(firstFragment).SequenceNumber);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(
                [CreateSingleTrackSample(trackId, dts: 150)],
                out var secondFragment));
        var media = Assert.IsType<Fmp4MuxMediaSegment>(secondFragment);
        Assert.Equal(2U, media.SequenceNumber);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            media.Bytes));
        var timeline = FindTrackFragmentTimelines(media.Bytes.AsSpan(0, moofLength));
        Assert.Equal(150UL, timeline[trackId].DecodeTime);
        Assert.Equal(new uint[] { 100 }, timeline[trackId].SampleDurations);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public void Rejects_cross_fragment_dts_overlap(uint trackId)
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(
                [CreateSingleTrackSample(trackId, dts: 0)],
                out _));

        Assert.Equal(Fmp4MuxerResult.InvalidInput,
            muxer.MuxFragment(
                [CreateSingleTrackSample(trackId, dts: 50)],
                out var rejected));
        Assert.Null(rejected);
        Assert.Equal(2UL, muxer.Snapshot.NextFragmentSequenceNumber);
        Assert.Equal(1, muxer.Snapshot.MediaFragmentGeneratedCount);
        Assert.Equal(1, muxer.Snapshot.ValidationFailureCount);
    }

    [Fact]
    public void Same_track_configuration_and_samples_produce_byte_identical_fragments()
    {
        using var first = CreateMuxer();
        using var second = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            first.GenerateInitializationSegment(out var firstInitialization));
        Assert.Equal(Fmp4MuxerResult.Produced,
            second.GenerateInitializationSegment(out var secondInitialization));
        Assert.Equal(firstInitialization, secondInitialization);
        Assert.Equal(Fmp4MuxerResult.Produced,
            first.MuxFragment(ValidSamples(), out var firstMedia));
        Assert.Equal(Fmp4MuxerResult.Produced,
            second.MuxFragment(ValidSamples(), out var secondMedia));
        Assert.Equal(
            Assert.IsType<Fmp4MuxMediaSegment>(firstMedia).Bytes,
            Assert.IsType<Fmp4MuxMediaSegment>(secondMedia).Bytes);
    }

    [Fact]
    public void Invalid_fragment_is_atomic_and_muxer_remains_usable()
    {
        using var muxer = CreateMuxer();
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        var rejected = new[]
        {
            new Fmp4MuxEncodedSample(99, 0, 0, 1, new byte[] { 1 },
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        };
        Assert.Equal(Fmp4MuxerResult.InvalidInput,
            muxer.MuxFragment(rejected, out var rejectedOutput));
        Assert.Null(rejectedOutput);
        Assert.Equal(1UL, muxer.Snapshot.NextFragmentSequenceNumber);
        Assert.Equal(0, muxer.Snapshot.MediaFragmentGeneratedCount);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(ValidSamples(), out var accepted));
        Assert.Equal(1U, Assert.IsType<Fmp4MuxMediaSegment>(accepted).SequenceNumber);
    }

    [Fact]
    public void Sequence_can_emit_uint_max_once_then_fails_without_wrapping()
    {
        using var muxer = CreateMuxer(new()
        {
            InitialFragmentSequenceNumber = uint.MaxValue,
        });
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(ValidSamples(), out var last));
        Assert.Equal(uint.MaxValue,
            Assert.IsType<Fmp4MuxMediaSegment>(last).SequenceNumber);

        var next = new[]
        {
            new Fmp4MuxEncodedSample(1, 1_001, 1_001, 1_001, new byte[] { 0, 0, 0, 2, 0x41, 1 },
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
            new Fmp4MuxEncodedSample(2, 1_024, 1_024, 1_024, new byte[] { 0x22, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable),
        };
        Assert.Equal(Fmp4MuxerResult.SequenceExhausted,
            muxer.MuxFragment(next, out var overflow));
        Assert.Null(overflow);
        Assert.Equal((ulong)uint.MaxValue + 1,
            muxer.Snapshot.NextFragmentSequenceNumber);
        Assert.Equal(1, muxer.Snapshot.MediaFragmentGeneratedCount);
    }

    [Fact]
    public void Input_payload_is_copied_into_the_generated_segment()
    {
        var avc = AvcConfiguration.ToArray();
        var trackDescriptions = Tracks(avc);
        Array.Fill(avc, (byte)0);
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(trackDescriptions, out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        byte[] payload = [0, 0, 0, 2, 0x65, 0x88];
        var expected = payload.ToArray();
        var samples = new[]
        {
            new Fmp4MuxEncodedSample(1, 0, 0, 1_001, payload,
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        };
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.MuxFragment(samples, out var segment));
        Array.Fill(payload, (byte)0xff);
        var media = Assert.IsType<Fmp4MuxMediaSegment>(segment);
        var moofLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(media.Bytes));
        Assert.Equal(expected, media.Bytes.AsSpan(moofLength + 8).ToArray());
    }

    [Fact]
    public void Rejects_invalid_track_inventory_and_codec_configuration()
    {
        Assert.Equal(Fmp4MuxerCreateResult.InvalidInput,
            Fmp4Muxer.Create([], out var noTracks));
        Assert.Null(noTracks);

        var duplicateId = Tracks();
        duplicateId[1] = new(1, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc,
            48_000, AacConfiguration, sampleRate: 48_000, channelCount: 2);
        Assert.Equal(Fmp4MuxerCreateResult.InvalidInput,
            Fmp4Muxer.Create(duplicateId, out var duplicate));
        Assert.Null(duplicate);

        var missingAvcc = Tracks();
        missingAvcc[0] = new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1,
            90_000, ReadOnlyMemory<byte>.Empty, width: 640, height: 360);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile,
            Fmp4Muxer.Create(missingAvcc, out var noAvcc));
        Assert.Null(noAvcc);

        var missingAsc = Tracks();
        missingAsc[1] = new(2, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc,
            48_000, ReadOnlyMemory<byte>.Empty, sampleRate: 48_000, channelCount: 2);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile,
            Fmp4Muxer.Create(missingAsc, out var noAsc));
        Assert.Null(noAsc);

        var invalidTimescale = Tracks();
        invalidTimescale[0] = new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1,
            0, AvcConfiguration, width: 640, height: 360);
        Assert.Equal(Fmp4MuxerCreateResult.InvalidInput,
            Fmp4Muxer.Create(invalidTimescale, out var noTimescale));
        Assert.Null(noTimescale);

        var unsupportedCodec = Tracks();
        unsupportedCodec[0] = new(1, Fmp4MuxMediaKind.Video,
            (Fmp4MuxCodec)99, 90_000, AvcConfiguration,
            width: 640, height: 360);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile,
            Fmp4Muxer.Create(unsupportedCodec, out var unsupported));
        Assert.Null(unsupported);
    }

    [Fact]
    public void Rejects_two_video_tracks_without_throwing()
    {
        var tracks = new[]
        {
            Tracks()[0],
            new Fmp4MuxTrackDescription(3, Fmp4MuxMediaKind.Video,
                Fmp4MuxCodec.Avc1, 90_000, AvcConfiguration,
                width: 640, height: 360),
        };
        Fmp4Muxer? muxer = null;
        var result = default(Fmp4MuxerCreateResult);

        var exception = Record.Exception(() =>
        {
            result = Fmp4Muxer.Create(tracks, out muxer);
        });

        Assert.Null(exception);
        Assert.Equal(Fmp4MuxerCreateResult.InvalidInput, result);
        Assert.Null(muxer);
    }

    [Fact]
    public void Rejects_two_audio_tracks_without_throwing()
    {
        var tracks = new[]
        {
            Tracks()[1],
            new Fmp4MuxTrackDescription(3, Fmp4MuxMediaKind.Audio,
                Fmp4MuxCodec.AacLc, 48_000, AacConfiguration,
                sampleRate: 48_000, channelCount: 2),
        };
        Fmp4Muxer? muxer = null;
        var result = default(Fmp4MuxerCreateResult);

        var exception = Record.Exception(() =>
        {
            result = Fmp4Muxer.Create(tracks, out muxer);
        });

        Assert.Null(exception);
        Assert.Equal(Fmp4MuxerCreateResult.InvalidInput, result);
        Assert.Null(muxer);
    }

    [Fact]
    public void Accepts_one_valid_video_and_audio_track()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
    }

    [Theory]
    [InlineData((byte)0xfc)]
    [InlineData((byte)0xfd)]
    [InlineData((byte)0xff)]
    public void Accepts_valid_avc_nal_length_sizes_and_preserves_avcC(
        byte lengthSizeByte)
    {
        var avcConfiguration = AvcConfiguration.ToArray();
        avcConfiguration[4] = lengthSizeByte;

        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(avcConfiguration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(avcConfiguration,
            FindAvcConfiguration(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Accepts_and_preserves_exact_profile_77_retained_fixture_configuration()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(MainProfileFixtureAvcConfiguration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(MainProfileFixtureAvcConfiguration,
            FindAvcConfiguration(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Accepts_and_preserves_exact_high_profile_extension_configuration()
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(HighProfileExtendedAvcConfiguration), out var created));
        using var muxer = Assert.IsType<Fmp4Muxer>(created);

        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var generated));
        Assert.Equal(HighProfileExtendedAvcConfiguration,
            FindAvcConfiguration(Assert.IsType<byte[]>(generated)));
    }

    [Fact]
    public void Rejects_profile_66_configuration_with_one_trailing_byte_without_throwing()
    {
        AssertUnsupportedAvcConfiguration(AvcConfiguration.Append((byte)0).ToArray());
    }

    [Fact]
    public void Rejects_profile_77_configuration_with_one_trailing_byte_without_throwing()
    {
        AssertUnsupportedAvcConfiguration(
            MainProfileFixtureAvcConfiguration.Append((byte)0).ToArray());
    }

    [Fact]
    public void Rejects_multiple_trailing_bytes_without_throwing()
    {
        AssertUnsupportedAvcConfiguration(
            AvcConfiguration.Concat(new byte[] { 0, 0, 0 }).ToArray());
    }

    [Fact]
    public void Rejects_unparsed_bytes_after_high_profile_extension()
    {
        AssertUnsupportedAvcConfiguration(
            HighProfileExtendedAvcConfiguration.Append((byte)0).ToArray());
    }

    [Fact]
    public void Rejects_malformed_high_profile_extension_fields()
    {
        var invalidReservedBits = HighProfileExtendedAvcConfiguration.ToArray();
        invalidReservedBits[14] = 0x01;
        var truncatedExtensionParameterSet =
            HighProfileExtendedAvcConfiguration[..^1];

        AssertUnsupportedAvcConfiguration(invalidReservedBits);
        AssertUnsupportedAvcConfiguration(truncatedExtensionParameterSet);
    }

    [Fact]
    public void Rejects_reserved_avc_nal_length_size_without_throwing()
    {
        var avcConfiguration = AvcConfiguration.ToArray();
        avcConfiguration[4] = 0xfe;
        Fmp4Muxer? muxer = null;
        var result = default(Fmp4MuxerCreateResult);

        var exception = Record.Exception(() =>
        {
            result = Fmp4Muxer.Create(Tracks(avcConfiguration), out muxer);
        });

        Assert.Null(exception);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile, result);
        Assert.Null(muxer);
    }

    [Fact]
    public void Rejects_invalid_avc_configuration_records_without_throwing()
    {
        var invalidVersion = AvcConfiguration.ToArray();
        invalidVersion[0] = 2;

        var missingSps = AvcConfiguration.ToArray();
        missingSps[5] = 0xe0;

        var malformedSpsLength = new byte[]
        {
            1, 66, 0, 30, 0xff, 0xe1, 0, 2, 0x67,
        };

        var missingPps = AvcConfiguration[..10];

        var zeroPpsCount = AvcConfiguration.ToArray();
        zeroPpsCount[10] = 0;

        var malformedPpsLength = new byte[]
        {
            1, 66, 0, 30, 0xff, 0xe1,
            0, 2, 0x67, 0x42,
            1, 0, 2, 0x68,
        };

        byte[][] invalidConfigurations =
        [
            AvcConfiguration[..6],
            invalidVersion,
            missingSps,
            malformedSpsLength,
            missingPps,
            zeroPpsCount,
            malformedPpsLength,
        ];

        foreach (var avcConfiguration in invalidConfigurations)
        {
            Fmp4Muxer? muxer = null;
            var result = default(Fmp4MuxerCreateResult);
            var exception = Record.Exception(() =>
            {
                result = Fmp4Muxer.Create(Tracks(avcConfiguration), out muxer);
            });

            Assert.Null(exception);
            Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile, result);
            Assert.Null(muxer);
        }
    }

    [Fact]
    public void Rejects_invalid_avc_upper_reserved_bits()
    {
        var avcConfiguration = AvcConfiguration.ToArray();
        avcConfiguration[4] = 0xf8;

        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile,
            Fmp4Muxer.Create(Tracks(avcConfiguration), out var muxer));
        Assert.Null(muxer);
    }

    [Fact]
    public void Rejects_bad_sample_metadata_and_limits_without_advancing_sequence()
    {
        using var muxer = CreateMuxer(new()
        {
            MaximumSamplesPerFragment = 2,
            MaximumSampleBytes = 8,
            MaximumFragmentPayloadBytes = 12,
        });
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));

        AssertRejected(muxer, []);
        AssertRejected(muxer,
        [
            new(1, 0, 0, 0, new byte[] { 1 }, Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        ]);
        AssertRejected(muxer,
        [
            new(1, 1, 1, 1, new byte[] { 1 }, Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(1, 0, 0, 1, new byte[] { 2 }, Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        ]);
        AssertRejected(muxer,
        [
            new(1, 0, long.MaxValue, 1, new byte[] { 1 }, Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        ]);
        AssertRejected(muxer,
        [
            new(1, 0, 0, 1, ReadOnlyMemory<byte>.Empty,
                Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        ]);
        AssertRejected(muxer,
        [
            new(1, 0, 0, 1, new byte[9], Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        ], Fmp4MuxerResult.LimitExceeded);
        AssertRejected(muxer,
        [
            new(1, 0, 0, 1, new byte[] { 1 }, Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(2, 0, 0, 1, new byte[] { 2 }, Fmp4MuxRandomAccessKind.NotApplicable),
            new(1, 1, 1, 1, new byte[] { 3 }, Fmp4MuxRandomAccessKind.NotIndependentlyDecodable),
        ], Fmp4MuxerResult.LimitExceeded);
        AssertRejected(muxer,
        [
            new(1, 0, 0, 1, new byte[8], Fmp4MuxRandomAccessKind.IndependentlyDecodable),
            new(2, 0, 0, 1, new byte[8], Fmp4MuxRandomAccessKind.NotApplicable),
        ], Fmp4MuxerResult.LimitExceeded);
        Assert.Equal(1UL, muxer.Snapshot.NextFragmentSequenceNumber);
        Assert.Equal(0, muxer.Snapshot.InputSampleCount);
    }

    [Fact]
    public void Rejects_output_larger_than_configured_segment_limit_and_disposal()
    {
        using var muxer = CreateMuxer(new()
        {
            MaximumMediaSegmentBytes = 128,
            MaximumFragmentPayloadBytes = 120,
            MaximumSampleBytes = 64,
        });
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out _));
        AssertRejected(muxer, ValidSamples(), Fmp4MuxerResult.LimitExceeded);
        Assert.Equal(1UL, muxer.Snapshot.NextFragmentSequenceNumber);

        muxer.Dispose();
        Assert.Equal(Fmp4MuxerResult.InvalidState,
            muxer.MuxFragment(ValidSamples(), out var afterDispose));
        Assert.Null(afterDispose);
    }

    private static void AssertRejected(
        Fmp4Muxer muxer,
        IReadOnlyList<Fmp4MuxEncodedSample> samples,
        Fmp4MuxerResult expected = Fmp4MuxerResult.InvalidInput)
    {
        Assert.Equal(expected, muxer.MuxFragment(samples, out var output));
        Assert.Null(output);
    }

    private static Fmp4Muxer CreateMuxer(Fmp4MuxerOptions? options = null)
    {
        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(Tracks(), out var created, options));
        return Assert.IsType<Fmp4Muxer>(created);
    }

    private static Fmp4MuxEncodedSample CreateSingleTrackSample(uint trackId, long dts) =>
        trackId == 1
            ? new(
                trackId,
                dts,
                dts,
                100,
                new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
                Fmp4MuxRandomAccessKind.IndependentlyDecodable)
            : new(
                trackId,
                dts,
                dts,
                100,
                new byte[] { 0x21, 0x10 },
                Fmp4MuxRandomAccessKind.NotApplicable);

    private static void AssertUnsupportedAvcConfiguration(byte[] avcConfiguration)
    {
        Fmp4Muxer? muxer = null;
        var result = default(Fmp4MuxerCreateResult);
        var exception = Record.Exception(() =>
        {
            result = Fmp4Muxer.Create(Tracks(avcConfiguration), out muxer);
        });

        Assert.Null(exception);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile, result);
        Assert.Null(muxer);
    }

    private static void AssertUnsupportedAacConfiguration(
        byte[] aacConfiguration,
        int audioSampleRate = 48_000,
        int audioChannelCount = 2)
    {
        Fmp4Muxer? muxer = null;
        var result = default(Fmp4MuxerCreateResult);
        var exception = Record.Exception(() =>
        {
            result = Fmp4Muxer.Create(
                Tracks(
                    aac: aacConfiguration,
                    audioSampleRate: audioSampleRate,
                    audioChannelCount: audioChannelCount),
                out muxer);
        });

        Assert.Null(exception);
        Assert.Equal(Fmp4MuxerCreateResult.UnsupportedProfile, result);
        Assert.Null(muxer);
    }

    private static Fmp4MuxTrackDescription[] Tracks(
        byte[]? avc = null,
        byte[]? aac = null,
        int audioSampleRate = 48_000,
        int audioChannelCount = 2) =>
    [
        new(1, Fmp4MuxMediaKind.Video, Fmp4MuxCodec.Avc1, 90_000,
            avc ?? AvcConfiguration, width: 640, height: 360),
        new(2, Fmp4MuxMediaKind.Audio, Fmp4MuxCodec.AacLc,
            checked((uint)audioSampleRate), aac ?? AacConfiguration,
            sampleRate: audioSampleRate, channelCount: audioChannelCount),
    ];

    private static byte[] BuildAacConfiguration(
        int audioObjectType = 2,
        int frequencyIndex = 3,
        int channelConfiguration = 2,
        int explicitSampleRate = 48_000,
        bool frameLengthFlag = false,
        bool dependsOnCoreCoder = false,
        bool extensionFlag = false)
    {
        var bitCount = frequencyIndex == 15 ? 40 : 16;
        if (dependsOnCoreCoder)
        {
            bitCount += 14;
        }

        if (extensionFlag)
        {
            bitCount++;
        }

        var configuration = new byte[(bitCount + 7) / 8];
        var bitOffset = 0;
        WriteBits(configuration, ref bitOffset, checked((uint)audioObjectType), 5);
        WriteBits(configuration, ref bitOffset, checked((uint)frequencyIndex), 4);
        if (frequencyIndex == 15)
        {
            WriteBits(configuration, ref bitOffset,
                checked((uint)explicitSampleRate), 24);
        }

        WriteBits(configuration, ref bitOffset,
            checked((uint)channelConfiguration), 4);
        WriteBits(configuration, ref bitOffset, frameLengthFlag ? 1U : 0U, 1);
        WriteBits(configuration, ref bitOffset,
            dependsOnCoreCoder ? 1U : 0U, 1);
        if (dependsOnCoreCoder)
        {
            WriteBits(configuration, ref bitOffset, 0x1234, 14);
        }

        WriteBits(configuration, ref bitOffset, extensionFlag ? 1U : 0U, 1);
        if (extensionFlag)
        {
            WriteBits(configuration, ref bitOffset, 0, 1);
        }

        return configuration;
    }

    private static byte[] BuildEscapedAacConfiguration(int escapedObjectType)
    {
        var configuration = new byte[3];
        var bitOffset = 0;
        WriteBits(configuration, ref bitOffset, 31, 5);
        WriteBits(configuration, ref bitOffset,
            checked((uint)escapedObjectType), 6);
        WriteBits(configuration, ref bitOffset, 3, 4);
        WriteBits(configuration, ref bitOffset, 2, 4);
        WriteBits(configuration, ref bitOffset, 0, 3);
        return configuration;
    }

    private static void WriteBits(
        Span<byte> destination,
        ref int bitOffset,
        uint value,
        int count)
    {
        for (var bit = count - 1; bit >= 0; bit--)
        {
            if (((value >> bit) & 1) != 0)
            {
                destination[bitOffset / 8] |=
                    (byte)(1 << (7 - bitOffset % 8));
            }

            bitOffset++;
        }
    }

    private static byte[] FindAudioSpecificConfig(ReadOnlySpan<byte> initialization)
    {
        var typeOffset = initialization.IndexOf("esds"u8);
        Assert.True(typeOffset >= 4);
        var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            initialization.Slice(typeOffset - 4, 4)));
        Assert.True(boxLength >= 12);
        Assert.True(typeOffset - 4 + boxLength <= initialization.Length);

        var descriptors = initialization.Slice(typeOffset + 4, boxLength - 8);
        var offset = 4;
        Assert.Equal((byte)0x03, descriptors[offset++]);
        var elementaryLength = ReadDescriptorLength(descriptors, ref offset);
        Assert.True(offset + elementaryLength <= descriptors.Length);
        offset += 3;

        Assert.Equal((byte)0x04, descriptors[offset++]);
        var decoderConfigLength = ReadDescriptorLength(descriptors, ref offset);
        Assert.True(offset + decoderConfigLength <= descriptors.Length);
        offset += 13;

        Assert.Equal((byte)0x05, descriptors[offset++]);
        var audioConfigLength = ReadDescriptorLength(descriptors, ref offset);
        Assert.True(offset + audioConfigLength <= descriptors.Length);
        return descriptors.Slice(offset, audioConfigLength).ToArray();
    }

    private static void MutateReturnedCodecConfiguration(
        ReadOnlyMemory<byte> configuration,
        int index,
        byte value)
    {
        Assert.True(MemoryMarshal.TryGetArray(configuration, out var exposed));
        Assert.NotNull(exposed.Array);
        exposed.Array![exposed.Offset + index] = value;
    }

    private static int ReadDescriptorLength(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var length = 0;
        for (var index = 0; index < 4; index++)
        {
            var value = bytes[offset++];
            length = (length << 7) | (value & 0x7f);
            if ((value & 0x80) == 0)
            {
                return length;
            }
        }

        throw new InvalidDataException("Descriptor length exceeds four bytes.");
    }

    private static Dictionary<uint, (ulong DecodeTime, uint[] SampleDurations)>
        FindTrackFragmentTimelines(ReadOnlySpan<byte> moof)
    {
        var timelines = new Dictionary<uint, (ulong, uint[])>();
        var childOffset = 8;
        while (childOffset < moof.Length)
        {
            var childLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                moof[childOffset..]));
            Assert.True(childLength >= 8 && childLength <= moof.Length - childOffset);
            if (System.Text.Encoding.ASCII.GetString(
                    moof.Slice(childOffset + 4, 4)) == "traf")
            {
                uint trackId = 0;
                ulong decodeTime = 0;
                uint[]? sampleDurations = null;
                var trafOffset = childOffset + 8;
                var trafEnd = childOffset + childLength;
                while (trafOffset < trafEnd)
                {
                    var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                        moof[trafOffset..]));
                    Assert.True(boxLength >= 8 && boxLength <= trafEnd - trafOffset);
                    var boxType = System.Text.Encoding.ASCII.GetString(
                        moof.Slice(trafOffset + 4, 4));
                    if (boxType == "tfhd")
                    {
                        trackId = BinaryPrimitives.ReadUInt32BigEndian(
                            moof.Slice(trafOffset + 12, 4));
                    }
                    else if (boxType == "tfdt")
                    {
                        Assert.Equal((byte)1, moof[trafOffset + 8]);
                        decodeTime = BinaryPrimitives.ReadUInt64BigEndian(
                            moof.Slice(trafOffset + 12, 8));
                    }
                    else if (boxType == "trun")
                    {
                        var sampleCount = checked((int)
                            BinaryPrimitives.ReadUInt32BigEndian(
                                moof.Slice(trafOffset + 12, 4)));
                        sampleDurations = new uint[sampleCount];
                        for (var index = 0; index < sampleCount; index++)
                        {
                            sampleDurations[index] =
                                BinaryPrimitives.ReadUInt32BigEndian(
                                    moof.Slice(trafOffset + 20 + (index * 16), 4));
                        }
                    }

                    trafOffset += boxLength;
                }

                Assert.NotEqual(0U, trackId);
                timelines.Add(trackId,
                    (decodeTime, Assert.IsType<uint[]>(sampleDurations)));
            }

            childOffset += childLength;
        }

        return timelines;
    }

    private static Fmp4MuxEncodedSample[] ValidSamples() =>
    [
        new(1, 0, 0, 1_001, new byte[] { 0, 0, 0, 2, 0x65, 0x88 },
            Fmp4MuxRandomAccessKind.IndependentlyDecodable),
        new(2, 0, 0, 1_024, new byte[] { 0x21, 0x10 },
            Fmp4MuxRandomAccessKind.NotApplicable),
    ];

    private static byte[] FindAvcConfiguration(ReadOnlySpan<byte> initialization)
    {
        var typeOffset = initialization.IndexOf("avcC"u8);
        Assert.True(typeOffset >= 4);
        var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            initialization.Slice(typeOffset - 4, 4)));
        Assert.True(boxLength >= 8);
        Assert.True(typeOffset - 4 + boxLength <= initialization.Length);
        return initialization.Slice(typeOffset + 4, boxLength - 8).ToArray();
    }

    private static List<(int DataOffset, byte Version, int FirstCompositionOffset)>
        FindTrunDataOffsets(ReadOnlySpan<byte> moof)
    {
        var results = new List<(int, byte, int)>();
        var childOffset = 8;
        while (childOffset < moof.Length)
        {
            var childLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                moof[childOffset..]));
            var childType = System.Text.Encoding.ASCII.GetString(
                moof.Slice(childOffset + 4, 4));
            if (childType == "traf")
            {
                var trafOffset = childOffset + 8;
                var trafEnd = childOffset + childLength;
                while (trafOffset < trafEnd)
                {
                    var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                        moof[trafOffset..]));
                    if (System.Text.Encoding.ASCII.GetString(
                            moof.Slice(trafOffset + 4, 4)) == "trun")
                    {
                        results.Add((
                            BinaryPrimitives.ReadInt32BigEndian(
                                moof.Slice(trafOffset + 16, 4)),
                            moof[trafOffset + 8],
                            BinaryPrimitives.ReadInt32BigEndian(
                                moof.Slice(trafOffset + 32, 4))));
                    }

                    trafOffset += boxLength;
                }
            }

            childOffset += childLength;
        }

        return results;
    }

    private static List<uint> FindTfhdFlags(ReadOnlySpan<byte> moof)
    {
        var results = new List<uint>();
        var childOffset = 8;
        while (childOffset < moof.Length)
        {
            var childLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                moof[childOffset..]));
            if (System.Text.Encoding.ASCII.GetString(
                    moof.Slice(childOffset + 4, 4)) == "traf")
            {
                var trafOffset = childOffset + 8;
                var trafEnd = checked(childOffset + childLength);
                while (trafOffset < trafEnd)
                {
                    var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                        moof[trafOffset..]));
                    if (System.Text.Encoding.ASCII.GetString(
                            moof.Slice(trafOffset + 4, 4)) == "tfhd")
                    {
                        results.Add(
                            ((uint)moof[trafOffset + 9] << 16) |
                            ((uint)moof[trafOffset + 10] << 8) |
                            moof[trafOffset + 11]);
                    }

                    trafOffset = checked(trafOffset + boxLength);
                }
            }

            childOffset = checked(childOffset + childLength);
        }

        return results;
    }

    private static Dictionary<uint, uint[]> FindTrunSampleFlags(
        ReadOnlySpan<byte> moof)
    {
        var results = new Dictionary<uint, uint[]>();
        var childOffset = 8;
        while (childOffset < moof.Length)
        {
            var childLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                moof[childOffset..]));
            var childType = System.Text.Encoding.ASCII.GetString(
                moof.Slice(childOffset + 4, 4));
            if (childType == "traf")
            {
                var trackId = 0U;
                uint[]? sampleFlags = null;
                var trafOffset = childOffset + 8;
                var trafEnd = checked(childOffset + childLength);
                while (trafOffset < trafEnd)
                {
                    var boxLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                        moof[trafOffset..]));
                    var boxType = System.Text.Encoding.ASCII.GetString(
                        moof.Slice(trafOffset + 4, 4));
                    if (boxType == "tfhd")
                    {
                        trackId = BinaryPrimitives.ReadUInt32BigEndian(
                            moof.Slice(trafOffset + 12, 4));
                    }
                    else if (boxType == "trun")
                    {
                        var runFlags = ((uint)moof[trafOffset + 9] << 16) |
                            ((uint)moof[trafOffset + 10] << 8) |
                            moof[trafOffset + 11];
                        Assert.NotEqual(0U, runFlags & 0x000400);
                        var sampleCount = checked((int)
                            BinaryPrimitives.ReadUInt32BigEndian(
                                moof.Slice(trafOffset + 12, 4)));
                        sampleFlags = new uint[sampleCount];
                        for (var index = 0; index < sampleCount; index++)
                        {
                            var sampleFlagsOffset = checked(
                                trafOffset + 28 + (index * 16));
                            sampleFlags[index] =
                                BinaryPrimitives.ReadUInt32BigEndian(
                                    moof.Slice(sampleFlagsOffset, 4));
                        }
                    }

                    trafOffset = checked(trafOffset + boxLength);
                }

                Assert.NotEqual(0U, trackId);
                results.Add(trackId, Assert.IsType<uint[]>(sampleFlags));
            }

            childOffset = checked(childOffset + childLength);
        }

        return results;
    }
}
