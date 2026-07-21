using System.Buffers.Binary;
using System.Numerics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

internal static class PlaybackPcpFlvAwaitedDecodeCompositionTestSupport
{
    internal static readonly ChannelId ChannelId = new(
        Guid.ParseExact("642ED9E0B91349E0ABCE5B4E6EC1CC38", "N"));
    internal static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("8A54CA9872E84D18B26940BF1375F224", "N"));

    internal static PlaybackPcpFlvAwaitedDecodeCompositionBoundary CreateBoundary(
        FakeGenerationFactory factory,
        long initialPacketSequence = 0,
        PlaybackPcpFlvAwaitedDecodeOptions? options = null) =>
        new(
            ChannelId,
            SessionId,
            factory,
            initialPacketSequence,
            options);

    internal static PcpChannelPacket CreatePacket(
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

    internal static byte[] BuildBootstrap() =>
        Concat(BuildHeader(), BuildVideoSequenceTag(), BuildAudioSequenceTag());

    internal static byte[] BuildVideoNaluTag(
        uint timestamp = 40,
        int compositionTime = 0,
        bool keyframe = true,
        byte[]? payload = null) =>
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
                payload ?? [0, 0, 0, 1, 0x65]));

    internal static byte[] BuildAudioRawTag(
        uint timestamp = 50,
        byte[]? payload = null) =>
        BuildTag(8, timestamp, Concat([0xAF, 1], payload ?? [1, 2, 3]));

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

    internal sealed class FakeGenerationFactory :
        IPlaybackPcpFlvAwaitedDecodeGenerationFactory
    {
        public PlaybackPcpFlvAwaitedDecodeGenerationResult CreationResult { get; set; } =
            PlaybackPcpFlvAwaitedDecodeGenerationResult.Created;

        public List<GenerationHarness> Generations { get; } = [];

        public List<string> Log { get; } = [];

        public bool IsDisposed { get; private set; }

        public int CreateCount { get; private set; }

        public PlaybackPcpFlvAwaitedDecodeGenerationCreation Create(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            int discontinuityGeneration)
        {
            CreateCount = checked(CreateCount + 1);
            if (CreationResult != PlaybackPcpFlvAwaitedDecodeGenerationResult.Created)
            {
                return new(CreationResult, null);
            }

            var packetOperations = new PlaybackSourceBoundOneFrameDecodeTestSupport
                .FakePacketOperations(Log);
            var receiveOperations = new PlaybackSourceBoundOneFrameDecodeTestSupport
                .FakeReceiveOperations();
            var nativeSession = new PlaybackBoundedFlvNativeDecoderSession(
                sourceSession,
                sourceSession.Video.StreamId,
                sourceSession.Audio.StreamId,
                sourceSession.Video.TimeBase,
                sourceSession.Audio.TimeBase,
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264, "video", Log),
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac, "audio", Log));
            var decodeBoundary = new PlaybackSourceBoundOneFrameDecodeBoundary(
                sourceSession,
                nativeSession,
                decoderGeneration,
                discontinuityGeneration,
                options: null,
                packetOperations,
                receiveOperations);
            var settlement = new FakeSettlement(Log);
            var generation = new PlaybackPcpFlvAwaitedDecodeGeneration(
                sourceSession,
                decodeBoundary,
                settlement,
                decoderGeneration,
                discontinuityGeneration);
            var harness = new GenerationHarness(
                sourceSession,
                nativeSession,
                packetOperations,
                receiveOperations,
                settlement,
                generation);
            Generations.Add(harness);
            return new(PlaybackPcpFlvAwaitedDecodeGenerationResult.Created, generation);
        }

        public void Dispose()
        {
            IsDisposed = true;
            Log.Add("factory");
        }
    }

    internal sealed record GenerationHarness(
        PlaybackBoundedDemuxSession Source,
        PlaybackBoundedFlvNativeDecoderSession NativeSession,
        PlaybackSourceBoundOneFrameDecodeTestSupport.FakePacketOperations PacketOperations,
        PlaybackSourceBoundOneFrameDecodeTestSupport.FakeReceiveOperations ReceiveOperations,
        FakeSettlement Settlement,
        PlaybackPcpFlvAwaitedDecodeGeneration Generation);

    internal sealed class FakeSettlement(List<string> log) :
        IPlaybackPcpFlvDecodedCompletionSettlement
    {
        private readonly TaskCompletionSource<bool> _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _isDisposed;

        public Queue<PlaybackPcpFlvDecodedSettlementResult> Results { get; } = [];

        public TaskCompletionSource<PlaybackPcpFlvDecodedSettlementResult>? Pending { get; set; }

        public List<CompletionSnapshot> Completions { get; } = [];

        public int CallCount { get; private set; }

        public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

        public Task Entered => _entered.Task;

        public async ValueTask<PlaybackPcpFlvDecodedSettlementResult> SettleAsync(
            PlaybackSourceBoundDecodedFrameCompletion completion,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Completions.Add(new(
                completion.SourceSession,
                completion.DecoderGeneration,
                completion.DiscontinuityGeneration,
                completion.MediaKind,
                completion.StreamId,
                completion.Sequence,
                completion.ManagedByteCount,
                completion.Frame));
            _entered.TrySetResult(true);

            if (Pending is not null)
            {
                return await Pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return Results.Count == 0
                ? PlaybackPcpFlvDecodedSettlementResult.Consumed
                : Results.Dequeue();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add("settlement");
            }
        }
    }

    internal sealed record CompletionSnapshot(
        PlaybackBoundedDemuxSession Source,
        int DecoderGeneration,
        int DiscontinuityGeneration,
        PlaybackBoundedDemuxMediaKind MediaKind,
        int StreamId,
        long Sequence,
        long ManagedByteCount,
        PlaybackDecoderFfmpegManagedFrame Frame);

    private sealed class FakeContextOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        string name,
        List<string> log) : IPlaybackBoundedFlvNativeContextOwner
    {
        private int _isDisposed;

        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add(name);
            }
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
