using System.Buffers.Binary;
using System.Numerics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFlvCompositionBoundaryTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));
    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task Consume_one_callback_with_bootstrap_and_two_packets_preserves_suffix()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        var payload = Concat(
            BuildBootstrap(),
            BuildVideoNaluTag(40, -10, true, [0, 0, 0, 1, 0x65]),
            BuildAudioRawTag(50, [1, 2, 3]));

        await boundary.ConsumeAsync(CreatePacket(0, payload), CancellationToken.None);

        var operations = Assert.Single(factory.Operations);
        Assert.Collection(
            operations.Events,
            video =>
            {
                Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, video.MediaKind);
                Assert.Equal(PlaybackBoundedDemuxEventKind.Packet, video.Kind);
                Assert.Equal(0, video.Sequence);
            },
            audio =>
            {
                Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, audio.MediaKind);
                Assert.Equal(PlaybackBoundedDemuxEventKind.Packet, audio.Kind);
                Assert.Equal(0, audio.Sequence);
            });
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(payload.Length, boundary.Snapshot.AcceptedPayloadByteCount);
        Assert.True(boundary.Snapshot.HasDecoderGeneration);
    }

    [Fact]
    public async Task Consume_split_record_retains_only_parser_carry()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        var bootstrap = BuildBootstrap();
        var first = bootstrap[..8];
        var second = bootstrap[8..];

        await boundary.ConsumeAsync(CreatePacket(0, first), CancellationToken.None);
        first.AsSpan().Fill(0xFF);
        await boundary.ConsumeAsync(CreatePacket(1, second), CancellationToken.None);

        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(bootstrap.Length, boundary.Snapshot.AcceptedPayloadByteCount);
        Assert.True(boundary.Snapshot.HasDecoderGeneration);
    }

    [Fact]
    public async Task Consume_rejects_wrong_channel_with_fixed_consumer_failure()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        var packet = new PcpChannelPacket(
            new ChannelId(Guid.ParseExact("102132435465768798A9BABBDCDDEDEF", "N")),
            SessionId,
            0,
            1,
            100,
            100,
            BuildBootstrap(),
            discontinuity: null);

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvCompositionException>(
            () => ((IPcpChannelPacketConsumer)boundary).ConsumeAsync(
                packet,
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvCompositionResult.InvalidIdentity, exception.Result);
        Assert.Equal("The PCP-to-FLV composition boundary failed.", exception.Message);
        Assert.Equal(PlaybackPcpFlvCompositionState.Faulted, boundary.Snapshot.State);
        Assert.True(factory.IsDisposed);
    }

    [Fact]
    public async Task Consume_rejects_non_monotonic_packet_sequence()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory, initialPacketSequence: 4);

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvCompositionException>(
            () => boundary.ConsumeAsync(
                CreatePacket(3, BuildBootstrap()),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvCompositionResult.SequenceRejected, exception.Result);
        Assert.Equal(
            PlaybackPcpFlvCompositionResult.SequenceRejected,
            boundary.Snapshot.Result);
        Assert.Equal(0, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Consume_creates_exact_generation_before_media_submission()
    {
        var factory = new FakeGenerationFactory
        {
            CreationResult = PlaybackPcpFlvCompositionGenerationResult.Rejected,
        };
        await using var boundary = CreateBoundary(factory);
        var payload = Concat(
            BuildBootstrap(),
            BuildVideoNaluTag(1, 0, true, [0, 0, 0, 1, 0x65]));

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvCompositionException>(
            () => boundary.ConsumeAsync(
                CreatePacket(0, payload),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvCompositionResult.GenerationRejected, exception.Result);
        Assert.Equal(1, factory.CreateCount);
        Assert.Empty(factory.Operations);
    }

    [Fact]
    public async Task Consume_packet_eagain_advances_once_after_frame_progress()
    {
        var scripted = new ScriptedSubmissionOperations();
        scripted.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        scripted.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        scripted.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        var factory = new FakeGenerationFactory(_ => scripted);
        await using var boundary = CreateBoundary(factory);

        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        await boundary.ConsumeAsync(
            CreatePacket(
                1,
                BuildVideoNaluTag(10, 0, true, [0, 0, 0, 1, 0x65])),
            CancellationToken.None);

        Assert.Equal(1, boundary.Snapshot.DecoderAdvanceCount);
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
        Assert.False(factory.Generations[0].Submission.HasActiveSubmission);
    }

    [Fact]
    public async Task Consume_decoder_no_progress_faults_without_releasing_callback_success()
    {
        var scripted = new ScriptedSubmissionOperations();
        scripted.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        scripted.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput);
        var factory = new FakeGenerationFactory(_ => scripted);
        await using var boundary = CreateBoundary(factory);

        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvCompositionException>(
            () => boundary.ConsumeAsync(
                CreatePacket(
                    1,
                    BuildVideoNaluTag(10, 0, true, [0, 0, 0, 1, 0x65])),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvCompositionResult.DecoderNoProgress, exception.Result);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(PlaybackPcpFlvCompositionState.Faulted, boundary.Snapshot.State);
        Assert.True(factory.Generations[0].IsDisposed);
    }

    [Fact]
    public async Task Consume_repeated_eagain_is_bounded_by_decoder_work_limit()
    {
        var scripted = new ScriptedSubmissionOperations();
        scripted.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        scripted.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        scripted.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        var factory = new FakeGenerationFactory(_ => scripted);
        var options = new PlaybackPcpFlvCompositionOptions
        {
            MaximumDecoderAdvancesPerCall = 1,
        };
        await using var boundary = CreateBoundary(factory, options: options);

        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvCompositionException>(
            () => boundary.ConsumeAsync(
                CreatePacket(
                    1,
                    BuildVideoNaluTag(10, 0, true, [0, 0, 0, 1, 0x65])),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvCompositionResult.WorkLimitExceeded, exception.Result);
        Assert.Equal(1, boundary.Snapshot.DecoderAdvanceCount);
        Assert.True(factory.Generations[0].IsDisposed);
    }

    [Fact]
    public async Task CompleteInput_emits_video_then_audio_eos_and_is_idempotent()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);

        await boundary.CompleteInputAsync();
        await boundary.CompleteInputAsync();

        var operations = Assert.Single(factory.Operations);
        Assert.Collection(
            operations.Events,
            video =>
            {
                Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, video.MediaKind);
                Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, video.Kind);
            },
            audio =>
            {
                Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, audio.MediaKind);
                Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, audio.Kind);
            });
        Assert.Equal(PlaybackPcpFlvCompositionState.Finalized, boundary.Snapshot.State);
        Assert.Equal(PlaybackPcpFlvCompositionResult.Finalized, boundary.Snapshot.Result);
    }

    [Fact]
    public async Task Empty_packet_and_cancellation_are_not_inferred_as_eos()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        await boundary.ConsumeAsync(
            CreatePacket(1, ReadOnlyMemory<byte>.Empty),
            CancellationToken.None);
        Assert.Empty(factory.Operations[0].Events);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => boundary.CompleteInputAsync(cancellation.Token).AsTask());

        Assert.Empty(factory.Operations[0].Events);
        Assert.Equal(PlaybackPcpFlvCompositionResult.Cancelled, boundary.Snapshot.Result);
        Assert.True(factory.IsDisposed);
    }

    [Fact]
    public async Task Discontinuity_replaces_generation_before_new_source_submission()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var firstGeneration = factory.Generations[0];
        var firstSource = factory.Sources[0];

        await boundary.ConsumeAsync(
            CreatePacket(1, BuildBootstrap(), discontinuity: true),
            CancellationToken.None);
        await boundary.ConsumeAsync(
            CreatePacket(
                2,
                BuildVideoNaluTag(5, 0, true, [0, 0, 0, 1, 0x65])),
            CancellationToken.None);

        Assert.Equal(2, factory.CreateCount);
        Assert.True(firstGeneration.IsDisposed);
        Assert.True(firstSource.IsDisposed);
        Assert.NotSame(firstSource, factory.Sources[1]);
        Assert.Single(factory.Operations[1].Events);
        Assert.Same(factory.Sources[1], factory.Generations[1].SourceSession);
    }

    [Fact]
    public async Task Dispose_releases_generation_then_parser_then_factory()
    {
        var factory = new FakeGenerationFactory();
        var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = factory.Generations[0];
        var source = factory.Sources[0];

        boundary.Dispose();
        boundary.Dispose();

        Assert.True(generation.IsDisposed);
        Assert.True(source.IsDisposed);
        Assert.True(factory.GenerationDisposedWhenFactoryDisposed);
        Assert.True(factory.SourceDisposedWhenFactoryDisposed);
        Assert.Equal(PlaybackPcpFlvCompositionState.Disposed, boundary.Snapshot.State);
    }

    private static PlaybackPcpFlvCompositionBoundary CreateBoundary(
        FakeGenerationFactory factory,
        long initialPacketSequence = 0,
        PlaybackPcpFlvCompositionOptions? options = null) =>
        new(
            ChannelId,
            SessionId,
            factory,
            initialPacketSequence,
            options);

    private static PcpChannelPacket CreatePacket(
        long sequence,
        ReadOnlyMemory<byte> payload,
        bool discontinuity = false)
    {
        var wirePosition = checked((uint)(100 + sequence));
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            1,
            wirePosition,
            discontinuity ? null : wirePosition,
            payload,
            discontinuity
                ? new PcpStreamDiscontinuity(200, wirePosition, 200)
                : null);
    }

    private static byte[] BuildBootstrap() =>
        Concat(BuildHeader(), BuildVideoSequenceTag(), BuildAudioSequenceTag());

    private static byte[] BuildHeader() =>
    [
        (byte)'F', (byte)'L', (byte)'V',
        1,
        0x05,
        0, 0, 0, 9,
        0, 0, 0, 0,
    ];

    private static byte[] BuildVideoSequenceTag() =>
        BuildTag(
            9,
            0,
            Concat([0x17, 0, 0, 0, 0], BuildAvcConfiguration()));

    private static byte[] BuildAudioSequenceTag() =>
        BuildTag(8, 0, [0xAF, 0, 0x11, 0x90]);

    private static byte[] BuildVideoNaluTag(
        uint timestamp,
        int compositionTime,
        bool keyframe,
        byte[] payload) =>
        BuildTag(
            9,
            timestamp,
            Concat(
                [
                    keyframe ? (byte)0x17 : (byte)0x27,
                    1,
                    (byte)((compositionTime >> 16) & 0xFF),
                    (byte)((compositionTime >> 8) & 0xFF),
                    (byte)(compositionTime & 0xFF),
                ],
                payload));

    private static byte[] BuildAudioRawTag(uint timestamp, byte[] payload) =>
        BuildTag(8, timestamp, Concat([0xAF, 1], payload));

    private static byte[] BuildTag(byte tagType, uint timestamp, byte[] body)
    {
        var result = new byte[11 + body.Length + 4];
        result[0] = tagType;
        WriteUInt24(result.AsSpan(1, 3), body.Length);
        result[4] = (byte)((timestamp >> 16) & 0xFF);
        result[5] = (byte)((timestamp >> 8) & 0xFF);
        result[6] = (byte)(timestamp & 0xFF);
        result[7] = (byte)(timestamp >> 24);
        body.CopyTo(result.AsSpan(11));
        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(11 + body.Length, 4),
            checked((uint)(11 + body.Length)));
        return result;
    }

    private static byte[] BuildAvcConfiguration()
    {
        var sps = BuildBaselineSps(320, 240);
        byte[] pps = [0x68, 0xCE, 0x06, 0xE2];
        var result = new byte[6 + 2 + sps.Length + 1 + 2 + pps.Length];
        result[0] = 1;
        result[1] = 66;
        result[2] = 0;
        result[3] = 30;
        result[4] = 0xFF;
        result[5] = 0xE1;
        var offset = 6;
        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(offset, 2),
            checked((ushort)sps.Length));
        offset += 2;
        sps.CopyTo(result.AsSpan(offset));
        offset += sps.Length;
        result[offset++] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(offset, 2),
            checked((ushort)pps.Length));
        offset += 2;
        pps.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static byte[] BuildBaselineSps(int width, int height)
    {
        var writer = new TestBitWriter();
        writer.WriteBits(66, 8);
        writer.WriteBits(0, 8);
        writer.WriteBits(30, 8);
        writer.WriteUnsignedExpGolomb(0);
        writer.WriteUnsignedExpGolomb(0);
        writer.WriteUnsignedExpGolomb(0);
        writer.WriteUnsignedExpGolomb(0);
        writer.WriteUnsignedExpGolomb(1);
        writer.WriteBit(false);
        writer.WriteUnsignedExpGolomb(checked((uint)(width / 16 - 1)));
        writer.WriteUnsignedExpGolomb(checked((uint)(height / 16 - 1)));
        writer.WriteBit(true);
        writer.WriteBit(true);
        writer.WriteBit(false);
        writer.WriteBit(false);
        return Concat([0x67], writer.ToRbsp());
    }

    private static byte[] Concat(params byte[][] values)
    {
        var length = values.Sum(value => value.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result.AsSpan(offset));
            offset += value.Length;
        }

        return result;
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)((value >> 16) & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)(value & 0xFF);
    }

    private sealed class FakeGenerationFactory :
        IPlaybackPcpFlvCompositionGenerationFactory
    {
        private readonly Func<int, ScriptedSubmissionOperations> _operationsFactory;

        public FakeGenerationFactory(
            Func<int, ScriptedSubmissionOperations>? operationsFactory = null)
        {
            _operationsFactory = operationsFactory ?? (_ => new());
        }

        public PlaybackPcpFlvCompositionGenerationResult CreationResult { get; init; } =
            PlaybackPcpFlvCompositionGenerationResult.Created;

        public int CreateCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool GenerationDisposedWhenFactoryDisposed { get; private set; }

        public bool SourceDisposedWhenFactoryDisposed { get; private set; }

        public List<PlaybackPcpFlvCompositionGeneration> Generations { get; } = [];

        public List<PlaybackBoundedDemuxSession> Sources { get; } = [];

        public List<ScriptedSubmissionOperations> Operations { get; } = [];

        public PlaybackPcpFlvCompositionGenerationCreation Create(
            PlaybackBoundedDemuxSession sourceSession)
        {
            CreateCount = checked(CreateCount + 1);
            Sources.Add(sourceSession);
            if (CreationResult != PlaybackPcpFlvCompositionGenerationResult.Created)
            {
                return new(CreationResult, null);
            }

            var operations = _operationsFactory(CreateCount - 1);
            Operations.Add(operations);
            var nativeSession = new PlaybackBoundedFlvNativeDecoderSession(
                sourceSession,
                sourceSession.Video.StreamId,
                sourceSession.Audio.StreamId,
                sourceSession.Video.TimeBase,
                sourceSession.Audio.TimeBase,
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264),
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac));
            var submission = new PlaybackFiniteOneEventPacketSubmissionStateMachine(
                operations);
            var generation = new PlaybackPcpFlvCompositionGeneration(
                sourceSession,
                nativeSession,
                submission);
            Generations.Add(generation);
            return new(PlaybackPcpFlvCompositionGenerationResult.Created, generation);
        }

        public void Dispose()
        {
            IsDisposed = true;
            GenerationDisposedWhenFactoryDisposed =
                Generations.Count == 0 || Generations.All(generation => generation.IsDisposed);
            SourceDisposedWhenFactoryDisposed =
                Sources.Count == 0 || Sources.All(source => source.IsDisposed);
        }
    }

    private sealed class ScriptedSubmissionOperations :
        IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        public Queue<PlaybackMetadataAwareNativePacketResult> SubmitResults { get; } = [];

        public Queue<PlaybackDecoderFfmpegFrameReceiveResult> ReceiveResults { get; } = [];

        public Queue<PlaybackMetadataAwareNativePacketResult> RetryResults { get; } = [];

        public List<PlaybackBoundedDemuxEvent> Events { get; } = [];

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            Events.Add(Assert.Single(sourceCompletion.Events));
            var result = SubmitResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.Submitted
                : SubmitResults.Dequeue();
            return new(result, packet: null, sourceCompletion);
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            ReceiveResults.Count == 0
                ? PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput
                : ReceiveResults.Dequeue();

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush) =>
            RetryResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.Submitted
                : RetryResults.Dequeue();

        public void ReleasePending()
        {
        }
    }

    private sealed class FakeContextOwner(PlaybackDecoderFfmpegCodecId codecId) :
        IPlaybackBoundedFlvNativeContextOwner
    {
        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
        }
    }

    private sealed class TestBitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteBit(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (var index = count - 1; index >= 0; index--)
            {
                WriteBit(((value >> index) & 1) != 0);
            }
        }

        public void WriteUnsignedExpGolomb(uint value)
        {
            var codeNumber = checked(value + 1);
            var bitCount = 32 - BitOperations.LeadingZeroCount(codeNumber);
            for (var index = 0; index < bitCount - 1; index++)
            {
                WriteBit(false);
            }

            WriteBits(codeNumber, bitCount);
        }

        public byte[] ToRbsp()
        {
            WriteBit(true);
            while ((_bits.Count & 7) != 0)
            {
                WriteBit(false);
            }

            return ToBytes();
        }

        private byte[] ToBytes()
        {
            var result = new byte[(_bits.Count + 7) / 8];
            for (var index = 0; index < _bits.Count; index++)
            {
                if (_bits[index])
                {
                    result[index >> 3] |= (byte)(1 << (7 - (index & 7)));
                }
            }

            return result;
        }
    }
}
