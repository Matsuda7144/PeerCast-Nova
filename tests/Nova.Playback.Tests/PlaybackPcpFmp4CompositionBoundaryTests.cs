using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFmp4CompositionBoundaryTests
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;
    private const uint MetadataPacketType = 0x6174656d;
    private const string FixtureSha256 =
        "3780066C30D70C5063368D708C3850410CAD03840E0B302966206C94AA0425EC";

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("642ED9E0B91349E0ABCE5B4E6EC1CC38", "N"));
    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("8A54CA9872E84D18B26940BF1375F224", "N"));

    [Fact]
    public async Task Packetized_fixture_reaches_decoder_and_both_output_boundaries()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);

        await boundary.ConsumeAsync(
            CreatePacket(0, HeadPacketType, fixture.Initialization.AsMemory(0, 5)),
            CancellationToken.None);
        Assert.Empty(decoders.Decoders);
        Assert.Equal(5, boundary.Snapshot.AssemblerCarryBytes);

        await boundary.ConsumeAsync(
            CreatePacket(1, HeadPacketType, fixture.Initialization.AsMemory(5)),
            CancellationToken.None);

        Assert.Single(decoders.Decoders);
        Assert.Equal(1, boundary.Snapshot.InitializationGenerationCount);
        Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);

        var media = fixture.Media[0];
        var moofLength = ReadBoxLength(media, 0);
        await boundary.ConsumeAsync(
            CreatePacket(2, DataPacketType, media.AsMemory(0, 11), wirePosition: 40_000),
            CancellationToken.None);
        await boundary.ConsumeAsync(
            CreatePacket(
                3,
                DataPacketType,
                media.AsMemory(11, moofLength + 2),
                wirePosition: 40_900),
            CancellationToken.None);
        var mediaTailAndNextHead = media
            .AsMemory(moofLength + 13)
            .ToArray()
            .Concat(fixture.Media[1].AsMemory(0, 13).ToArray())
            .ToArray();
        await boundary.ConsumeAsync(
            CreatePacket(4, DataPacketType, mediaTailAndNextHead, wirePosition: 41_200),
            CancellationToken.None);
        await boundary.ConsumeAsync(
            CreatePacket(
                5,
                DataPacketType,
                fixture.Media[1].AsMemory(13),
                wirePosition: 41_500),
            CancellationToken.None);

        Assert.Equal(2, boundary.Snapshot.MediaSegmentCount);
        Assert.Equal(2, boundary.Snapshot.BatchCount);
        Assert.Equal(2, boundary.Snapshot.DecoderSubmissionCount);
        Assert.Equal(0, boundary.Snapshot.DiscontinuityCount);
        var decoder = Assert.Single(decoders.Decoders);
        Assert.Equal(PlaybackMediaContainer.FragmentedMp4, decoder.Context.Container);
        Assert.NotEmpty(decoder.DecodedRoles);
        Assert.Contains(PlaybackEncodedMediaRole.Video, decoder.DecodedRoles);
        Assert.Contains(PlaybackEncodedMediaRole.Audio, decoder.DecodedRoles);
        var output = Assert.Single(outputs.Generations);
        Assert.True(output.VideoFrameCount > 0);
        Assert.True(output.AudioFrameCount > 0);

        await boundary.CompleteInputAsync();

        Assert.Equal(PlaybackPcpFmp4CompositionState.Completed, boundary.Snapshot.State);
        Assert.Equal(PlaybackPcpFmp4CompositionResult.Completed, boundary.Snapshot.Result);
        Assert.Equal(1, decoder.CompleteCount);
        Assert.Equal(1, output.CompleteCount);
    }

    [Fact]
    public async Task Multiple_complete_media_segments_in_one_data_packet_create_distinct_batches()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);
        await SendInitializationAsync(boundary, fixture.Initialization);
        var joinedMedia = fixture.Media[0].Concat(fixture.Media[1]).ToArray();

        await boundary.ConsumeAsync(
            CreatePacket(1, DataPacketType, joinedMedia),
            CancellationToken.None);

        Assert.Equal(2, boundary.Snapshot.MediaSegmentCount);
        Assert.Equal(2, boundary.Snapshot.BatchCount);
        Assert.Equal(2, boundary.Snapshot.DecoderSubmissionCount);
        Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);
    }

    [Fact]
    public async Task New_head_discards_media_carry_and_replaces_generation_without_eos()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);
        await SendInitializationAsync(boundary, fixture.Initialization);
        await boundary.ConsumeAsync(
            CreatePacket(
                1,
                DataPacketType,
                fixture.Media[0].AsMemory(0, 12)),
            CancellationToken.None);
        var previousDecoder = Assert.Single(decoders.Decoders);
        var previousOutput = Assert.Single(outputs.Generations);
        Assert.True(boundary.Snapshot.AssemblerCarryBytes > 0);

        await boundary.ConsumeAsync(
            CreatePacket(2, HeadPacketType, fixture.Initialization),
            CancellationToken.None);

        Assert.Equal(2, decoders.Decoders.Count);
        Assert.Equal(2, outputs.Generations.Count);
        Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);
        Assert.Equal(1, previousDecoder.DisposeCount);
        Assert.Equal(0, previousDecoder.CompleteCount);
        Assert.Equal(1, previousOutput.DisposeCount);
        Assert.Equal(0, previousOutput.CompleteCount);

        await boundary.ConsumeAsync(
            CreatePacket(3, DataPacketType, fixture.Media[0]),
            CancellationToken.None);
        Assert.Equal(1, boundary.Snapshot.DecoderSubmissionCount);
    }

    [Fact]
    public async Task Actual_discontinuity_discards_carry_and_creates_a_fresh_decoder_generation()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);
        await SendInitializationAsync(boundary, fixture.Initialization);
        await boundary.ConsumeAsync(
            CreatePacket(1, DataPacketType, fixture.Media[0].AsMemory(0, 12)),
            CancellationToken.None);
        var previousDecoder = Assert.Single(decoders.Decoders);
        var previousOutput = Assert.Single(outputs.Generations);

        await boundary.ConsumeAsync(
            CreatePacket(
                2,
                DataPacketType,
                fixture.Media[0],
                discontinuity: new(100, 200, 1_000)),
            CancellationToken.None);

        Assert.Equal(1, boundary.Snapshot.DiscontinuityCount);
        Assert.Equal(2, boundary.Snapshot.InitializationGenerationCount);
        Assert.Equal(2, decoders.Decoders.Count);
        Assert.Equal(1, previousDecoder.DisposeCount);
        Assert.Equal(1, previousOutput.DisposeCount);
        Assert.Equal(1, boundary.Snapshot.DecoderSubmissionCount);
    }

    [Fact]
    public async Task Unsupported_packet_keeps_sequence_accounting_coherent()
    {
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);

        await boundary.ConsumeAsync(
            CreatePacket(0, MetadataPacketType, ReadOnlyMemory<byte>.Empty),
            CancellationToken.None);

        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(1, boundary.Snapshot.IgnoredPacketCount);
        Assert.Equal(1, boundary.Snapshot.NextPacketSequence);
        await AssertCompositionFailureAsync(
            boundary.ConsumeAsync(
                CreatePacket(2, MetadataPacketType, ReadOnlyMemory<byte>.Empty),
                CancellationToken.None),
            PlaybackPcpFmp4CompositionResult.SequenceRejected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Foreign_channel_or_session_is_rejected(
        bool foreignChannel,
        bool foreignSession)
    {
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);
        var packet = new PcpChannelPacket(
            foreignChannel ? new ChannelId(Guid.NewGuid()) : ChannelId,
            foreignSession ? new ChannelSessionId(Guid.NewGuid()) : SessionId,
            sequence: 0,
            type: MetadataPacketType,
            wirePosition: 0,
            streamPosition: 0,
            payload: ReadOnlyMemory<byte>.Empty,
            discontinuity: null);

        await AssertCompositionFailureAsync(
            boundary.ConsumeAsync(packet, CancellationToken.None),
            PlaybackPcpFmp4CompositionResult.InvalidIdentity);
        Assert.Equal(0, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Media_before_initialization_fails_closed()
    {
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using var boundary = CreateBoundary(decoders, outputs);

        await AssertCompositionFailureAsync(
            boundary.ConsumeAsync(
                CreatePacket(0, DataPacketType, new byte[] { 1, 2, 3, 4 }),
                CancellationToken.None),
            PlaybackPcpFmp4CompositionResult.InitializationUnavailable);
        Assert.Empty(decoders.Decoders);
        Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);
    }

    [Fact]
    public async Task Explicit_flv_metadata_is_rejected_before_decoder_creation()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        var metadata = new ChannelMetadataSnapshot(contentType: "video/x-flv");
        await using var boundary = new PlaybackPcpFmp4CompositionBoundary(
            ChannelId,
            SessionId,
            decoders,
            outputs,
            () => metadata);

        await AssertCompositionFailureAsync(
            boundary.ConsumeAsync(
                CreatePacket(0, HeadPacketType, fixture.Initialization),
                CancellationToken.None),
            PlaybackPcpFmp4CompositionResult.UnsupportedProfile);
        Assert.Empty(decoders.Decoders);
    }

    [Fact]
    public async Task Oversized_box_is_rejected_before_growth_and_terminal_carry_is_truncation()
    {
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        await using (var boundary = CreateBoundary(
                         decoders,
                         outputs,
                         new() { MaximumSegmentBytes = 64 }))
        {
            var oversizedHeader = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(oversizedHeader.AsSpan(0, 4), 4096);
            BinaryPrimitives.WriteUInt32BigEndian(oversizedHeader.AsSpan(4, 4), 0x66747970);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await boundary.ConsumeAsync(
                    CreatePacket(0, HeadPacketType, oversizedHeader),
                    CancellationToken.None));
            Assert.Equal(PlaybackPcpFmp4CompositionState.Faulted, boundary.Snapshot.State);
            Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);
        }

        var fixture = ReadFixtureSegments();
        decoders = new RecordingDecoderFactory();
        outputs = new RecordingPresentationFactory();
        await using var truncated = CreateBoundary(decoders, outputs);
        await SendInitializationAsync(truncated, fixture.Initialization);
        await truncated.ConsumeAsync(
            CreatePacket(1, DataPacketType, fixture.Media[0].AsMemory(0, 12)),
            CancellationToken.None);

        await AssertCompositionFailureAsync(
            truncated.CompleteInputAsync(),
            PlaybackPcpFmp4CompositionResult.TruncatedInput);
        Assert.Equal(0, truncated.Snapshot.AssemblerCarryBytes);
        Assert.Equal(0, Assert.Single(decoders.Decoders).CompleteCount);
        Assert.Equal(0, Assert.Single(outputs.Generations).CompleteCount);
    }

    [Fact]
    public async Task Cancellation_faults_and_disposal_releases_partial_carry_without_eos()
    {
        var fixture = ReadFixtureSegments();
        var decoders = new RecordingDecoderFactory();
        var outputs = new RecordingPresentationFactory();
        using var cancellation = new CancellationTokenSource();
        outputs.OnCreate = cancellation.Cancel;
        var boundary = CreateBoundary(decoders, outputs);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            boundary.ConsumeAsync(
                CreatePacket(0, HeadPacketType, fixture.Initialization),
                cancellation.Token).AsTask());
        Assert.Equal(PlaybackPcpFmp4CompositionState.Faulted, boundary.Snapshot.State);
        Assert.Equal(PlaybackPcpFmp4CompositionResult.Cancelled, boundary.Snapshot.Result);
        Assert.Equal(0, boundary.Snapshot.AssemblerCarryBytes);
        Assert.Equal(1, Assert.Single(decoders.Decoders).DisposeCount);
        await boundary.DisposeAsync();
        Assert.Equal(PlaybackPcpFmp4CompositionState.Disposed, boundary.Snapshot.State);

        var disposalBoundary = CreateBoundary(
            new RecordingDecoderFactory(),
            new RecordingPresentationFactory());
        await disposalBoundary.ConsumeAsync(
            CreatePacket(0, HeadPacketType, fixture.Initialization.AsMemory(0, 5)),
            CancellationToken.None);
        await disposalBoundary.DisposeAsync();
        Assert.Equal(0, disposalBoundary.Snapshot.AssemblerCarryBytes);
        Assert.Equal(PlaybackPcpFmp4CompositionState.Disposed,
            disposalBoundary.Snapshot.State);
    }

    private static PlaybackPcpFmp4CompositionBoundary CreateBoundary(
        RecordingDecoderFactory decoderFactory,
        RecordingPresentationFactory presentationFactory,
        PlaybackPcpFmp4CompositionOptions? options = null) =>
        new(ChannelId, SessionId, decoderFactory, presentationFactory, options: options);

    private static async Task SendInitializationAsync(
        PlaybackPcpFmp4CompositionBoundary boundary,
        byte[] initialization) =>
        await boundary.ConsumeAsync(
            CreatePacket(0, HeadPacketType, initialization),
            CancellationToken.None);

    private static PcpChannelPacket CreatePacket(
        long sequence,
        uint type,
        ReadOnlyMemory<byte> payload,
        uint wirePosition = 100,
        PcpStreamDiscontinuity? discontinuity = null) =>
        new(
            ChannelId,
            SessionId,
            sequence,
            type,
            wirePosition,
            discontinuity is null ? wirePosition : null,
            payload,
            discontinuity);

    private static async Task AssertCompositionFailureAsync(
        ValueTask operation,
        PlaybackPcpFmp4CompositionResult expected)
    {
        var exception = await Assert.ThrowsAsync<PlaybackPcpFmp4CompositionException>(
            () => operation.AsTask());
        Assert.Equal(expected, exception.Result);
    }

    private static int ReadBoxLength(ReadOnlySpan<byte> bytes, int offset)
    {
        var result = PlaybackIsoBmffBoxHeaderParser.Parse(bytes[offset..], out var header);
        Assert.Equal(PlaybackIsoBmffBoxHeaderParseResult.Parsed, result);
        return checked((int)header!.BoxSize!.Value);
    }

    private static byte[] ReadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "PeerCast.Nova.Playback.Tests.Fixtures.fmp4-avc1-aac.mp4");
        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var fixture = memory.ToArray();
        Assert.Equal(FixtureSha256, Convert.ToHexString(SHA256.HashData(fixture)));
        return fixture;
    }

    private static (byte[] Initialization, byte[][] Media) ReadFixtureSegments()
    {
        var fixture = ReadFixture();
        var boxes = new List<byte[]>();
        var offset = 0;
        while (offset < fixture.Length)
        {
            var result = PlaybackIsoBmffBoxSequenceReader.ReadNext(
                fixture,
                offset,
                out var box);
            Assert.Equal(PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable, result);
            var location = Assert.IsType<PlaybackIsoBmffBoxLocation>(box);
            boxes.Add(fixture.AsSpan(offset, location.BoxLength).ToArray());
            offset = location.NextOffset;
        }

        Assert.Equal(6, boxes.Count);
        return (
            boxes[0].Concat(boxes[1]).ToArray(),
            [
                boxes[2].Concat(boxes[3]).ToArray(),
                boxes[4].Concat(boxes[5]).ToArray(),
            ]);
    }

    private sealed class RecordingDecoderFactory : IPlaybackDecoderFactory
    {
        public List<RecordingDecoder> Decoders { get; } = [];

        public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context)
        {
            var decoder = new RecordingDecoder(context);
            Decoders.Add(decoder);
            return decoder;
        }
    }

    private sealed class RecordingDecoder : IPlaybackDecoder
    {
        private readonly PlaybackDecoderCreationContext _context;

        public RecordingDecoder(PlaybackDecoderCreationContext context)
        {
            _context = context;
            Context = context;
        }

        public PlaybackDecoderCreationContext Context { get; }

        public List<PlaybackEncodedMediaRole> DecodedRoles { get; } = [];

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public async ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = input.Fmp4SampleMetadata ??
                throw new InvalidOperationException("The fMP4 sample metadata is missing.");
            DecodedRoles.Add(metadata.MediaRole);
            if (metadata.MediaRole == PlaybackEncodedMediaRole.Video)
            {
                await _context.OutputSink.WriteVideoAsync(
                    PlaybackDecodedVideoFrame.Create(
                        input.Sequence,
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(1),
                        PlaybackDecodedFrameDiscontinuityKind.None,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        1,
                        1,
                        4,
                        new byte[4]),
                    cancellationToken);
            }
            else
            {
                await _context.OutputSink.WriteAudioAsync(
                    PlaybackDecodedAudioFrame.Create(
                        input.Sequence,
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(20),
                        PlaybackDecodedFrameDiscontinuityKind.None,
                        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                        48_000,
                        2,
                        1,
                        new byte[4]),
                    cancellationToken);
            }
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPresentationFactory :
        IPlaybackPcpFmp4PresentationOutputGenerationFactory
    {
        public List<RecordingPresentationGeneration> Generations { get; } = [];

        public Action? OnCreate { get; set; }

        public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation)
        {
            OnCreate?.Invoke();
            var output = new RecordingPresentationGeneration(generation);
            Generations.Add(output);
            return output;
        }
    }

    private sealed class RecordingPresentationGeneration(long generation) :
        IPlaybackPcpFmp4PresentationOutputGeneration
    {
        public long Generation { get; } = generation;

        public int AudioFrameCount { get; private set; }

        public int VideoFrameCount { get; private set; }

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioFrameCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFrameCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
