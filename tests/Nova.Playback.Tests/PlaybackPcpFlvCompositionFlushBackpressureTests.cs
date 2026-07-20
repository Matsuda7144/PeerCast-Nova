using System.Buffers.Binary;
using System.Numerics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFlvCompositionFlushBackpressureTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));
    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task CompleteInput_flush_eagain_advances_after_frame_progress()
    {
        var operations = new FlushBackpressureOperations();
        using var factory = new FlushBackpressureGenerationFactory(operations);
        await using var boundary = new PlaybackPcpFlvCompositionBoundary(
            ChannelId,
            SessionId,
            factory);
        var packet = new PcpChannelPacket(
            ChannelId,
            SessionId,
            0,
            1,
            100,
            100,
            BuildBootstrap(),
            discontinuity: null);

        await boundary.ConsumeAsync(packet, CancellationToken.None);
        await boundary.CompleteInputAsync();

        Assert.Equal(1, boundary.Snapshot.DecoderAdvanceCount);
        Assert.Equal(PlaybackPcpFlvCompositionState.Finalized, boundary.Snapshot.State);
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
        Assert.False(factory.Generation!.Submission.HasActiveSubmission);
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

    private sealed class FlushBackpressureGenerationFactory(
        FlushBackpressureOperations operations) :
        IPlaybackPcpFlvCompositionGenerationFactory
    {
        public PlaybackPcpFlvCompositionGeneration? Generation { get; private set; }

        public PlaybackPcpFlvCompositionGenerationCreation Create(
            PlaybackBoundedDemuxSession sourceSession)
        {
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
            Generation = new PlaybackPcpFlvCompositionGeneration(
                sourceSession,
                nativeSession,
                submission);
            return new(PlaybackPcpFlvCompositionGenerationResult.Created, Generation);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FlushBackpressureOperations :
        IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        private int _submissionCount;

        public List<PlaybackBoundedDemuxEvent> Events { get; } = [];

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            Events.Add(Assert.Single(sourceCompletion.Events));
            var result = _submissionCount++ == 0
                ? PlaybackMetadataAwareNativePacketResult.NeedFrameDrain
                : PlaybackMetadataAwareNativePacketResult.Submitted;
            return new(result, packet: null, sourceCompletion);
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable;

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush) =>
            PlaybackMetadataAwareNativePacketResult.Submitted;

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
