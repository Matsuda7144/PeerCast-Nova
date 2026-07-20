using System.Buffers.Binary;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackOneEventFinalEosFlvParserTests
{
    private static readonly PlaybackBoundedDemuxTimeBase TimeBase = new(1, 1_000);

    [Fact]
    public void Parse_final_empty_input_emits_source_owned_video_then_audio_eos()
    {
        using var parser = CreateBootstrappedParser();
        var source = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);

        var videoFinal = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        var videoCompletion = Assert.IsType<PlaybackBoundedDemuxReadCompletion>(
            videoFinal.Completion);
        var videoEos = Assert.Single(videoCompletion.Events);
        var blocked = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [CreateEosInput(PlaybackBoundedDemuxMediaKind.Audio, 0)],
            0,
            0);

        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, videoFinal.Result);
        Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, blocked.Result);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, videoEos.MediaKind);
        Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, videoEos.Kind);
        Assert.Equal(0, videoEos.Sequence);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.ActiveCompletion,
            parser.Parse(ReadOnlyMemory<byte>.Empty).Result);
        blocked.Dispose();
        videoCompletion.Dispose();

        var audioFinal = parser.Parse(ReadOnlyMemory<byte>.Empty);
        var audioCompletion = Assert.IsType<PlaybackBoundedDemuxReadCompletion>(
            audioFinal.Completion);
        var audioEos = Assert.Single(audioCompletion.Events);
        var audioBlocked = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.EndOfStream,
            [CreateEosInput(PlaybackBoundedDemuxMediaKind.Audio, 1)],
            0,
            0);

        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, audioFinal.Result);
        Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, audioBlocked.Result);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, audioEos.MediaKind);
        Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, audioEos.Kind);
        Assert.Equal(0, audioEos.Sequence);
        audioBlocked.Dispose();
        audioCompletion.Dispose();

        var completed = parser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, completed.Result);
        Assert.Null(completed.Completion);
    }

    [Fact]
    public void Parse_explicit_video_eos_then_final_emits_only_audio()
    {
        using var parser = CreateBootstrappedParser();
        var explicitVideo = parser.Parse(BuildVideoEndTag());
        using (var completion = explicitVideo.Completion)
        {
            var eos = Assert.Single(completion!.Events);
            Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, eos.MediaKind);
            Assert.Equal(0, eos.Sequence);
        }

        var final = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        using var finalCompletion = final.Completion;
        var audioEos = Assert.Single(finalCompletion!.Events);

        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, final.Result);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, audioEos.MediaKind);
        Assert.Equal(0, audioEos.Sequence);
    }

    [Fact]
    public void Parse_final_packet_latches_finalization_until_packet_is_released()
    {
        using var parser = CreateBootstrappedParser();
        var packetOperation = parser.Parse(
            BuildVideoNaluTag(40, -10, true, [0, 0, 0, 1, 0x65]),
            isFinal: true);
        var packetCompletion = Assert.IsType<PlaybackBoundedDemuxReadCompletion>(
            packetOperation.Completion);
        var packet = Assert.Single(packetCompletion.Events);

        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, packetOperation.Result);
        Assert.Equal(PlaybackBoundedDemuxEventKind.Packet, packet.Kind);
        Assert.Equal(0, packet.Sequence);
        Assert.Equal(TimeSpan.FromMilliseconds(40), packet.DecodeTimestamp.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(30), packet.PresentationTimestamp.Value);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.ActiveCompletion,
            parser.Parse(ReadOnlyMemory<byte>.Empty).Result);

        packetCompletion.Dispose();
        var videoFinal = parser.Parse(ReadOnlyMemory<byte>.Empty);
        using var videoCompletion = videoFinal.Completion;
        var videoEos = Assert.Single(videoCompletion!.Events);

        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, videoEos.MediaKind);
        Assert.Equal(1, videoEos.Sequence);
    }

    [Fact]
    public void Parse_truncated_final_input_faults_the_facade()
    {
        using var parser = CreateBootstrappedParser();
        var tag = BuildAudioRawTag(10, [1, 2, 3]);

        var result = parser.Parse(tag.AsMemory(0, tag.Length - 1), isFinal: true);

        Assert.Equal(PlaybackBoundedFlvParserResult.TruncatedData, result.Result);
        Assert.Null(result.Completion);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.Faulted,
            parser.Parse(ReadOnlyMemory<byte>.Empty).Result);
    }

    [Fact]
    public void Parse_discontinuity_resets_final_sequence_state()
    {
        using var parser = CreateBootstrappedParser();
        var firstVideoFinal = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        firstVideoFinal.Completion!.Dispose();
        var firstAudioFinal = parser.Parse(ReadOnlyMemory<byte>.Empty);
        firstAudioFinal.Completion!.Dispose();

        var reset = parser.Parse(BuildBootstrap(), discontinuity: true);
        var secondVideoFinal = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        using var secondCompletion = secondVideoFinal.Completion;
        var secondVideoEos = Assert.Single(secondCompletion!.Events);

        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            reset.Result);
        Assert.Equal(1, reset.Generation);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, secondVideoEos.MediaKind);
        Assert.Equal(0, secondVideoEos.Sequence);
    }

    [Fact]
    public void Final_eos_composes_through_the_one_event_submission_state_machine()
    {
        using var parser = CreateBootstrappedParser();
        var source = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);
        using var nativeSession = new PlaybackBoundedFlvNativeDecoderSession(
            source,
            source.Video.StreamId,
            source.Audio.StreamId,
            source.Video.TimeBase,
            source.Audio.TimeBase,
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264),
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac));
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(
            new AcceptingOperations());

        var videoFinal = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        var videoResult = machine.Begin(nativeSession, source, videoFinal.Completion);
        var audioFinal = parser.Parse(ReadOnlyMemory<byte>.Empty);
        var audioResult = machine.Begin(nativeSession, source, audioFinal.Completion);
        var completed = parser.Parse(ReadOnlyMemory<byte>.Empty);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted, videoResult);
        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted, audioResult);
        Assert.True(machine.VideoFlushAccepted);
        Assert.True(machine.AudioFlushAccepted);
        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, completed.Result);
        Assert.Null(completed.Completion);
    }

    private static PlaybackOneEventFinalEosFlvParser CreateBootstrappedParser()
    {
        var parser = new PlaybackOneEventFinalEosFlvParser();
        var result = parser.Parse(BuildBootstrap());
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            result.Result);
        return parser;
    }

    private static PlaybackBoundedDemuxEventInput CreateEosInput(
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence) =>
        new(
            PlaybackBoundedDemuxEventKind.EndOfStream,
            mediaKind == PlaybackBoundedDemuxMediaKind.Video ? 0 : 1,
            mediaKind,
            sequence,
            ReadOnlyMemory<byte>.Empty,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);

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

    private static byte[] BuildVideoEndTag() =>
        BuildTag(9, 0, [0x17, 2, 0, 0, 0]);

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

    private sealed class FakeContextOwner(PlaybackDecoderFfmpegCodecId codecId) :
        IPlaybackBoundedFlvNativeContextOwner
    {
        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
        }
    }

    private sealed class AcceptingOperations : IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion) =>
            new(
                PlaybackMetadataAwareNativePacketResult.Submitted,
                packet: null,
                sourceCompletion);

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput;

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush) =>
            PlaybackMetadataAwareNativePacketResult.NativeRejected;

        public void ReleasePending()
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
