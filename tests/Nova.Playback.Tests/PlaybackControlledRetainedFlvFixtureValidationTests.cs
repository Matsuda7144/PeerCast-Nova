using System.Buffers.Binary;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackControlledRetainedFlvFixtureValidationTests
{
    [Fact]
    public void Controlled_fixture_preserves_interleave_timing_backpressure_and_per_stream_eos()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var bootstrap = parser.Parse(BuildBootstrap());
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            bootstrap.Result);
        var source = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);
        using var nativeSession = CreateNativeSession(source);
        var operations = new FixtureOperations();
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        operations.SubmitResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.ReceiveResults.Enqueue(PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        operations.RetryResults.Enqueue(PlaybackMetadataAwareNativePacketResult.Submitted);
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);

        var video0 = ParseCompletion(
            parser,
            BuildVideoNaluTag(
                timestamp: 100,
                compositionTime: -20,
                keyframe: true,
                [0, 0, 0, 2, 0x65, 0x88]),
            PlaybackBoundedFlvParserResult.PacketsAvailable);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted,
            machine.Begin(nativeSession, source, video0));
        Assert.True(video0.IsDisposed);

        var audio0 = ParseCompletion(
            parser,
            BuildAudioRawTag(90, [0x11, 0x22]),
            PlaybackBoundedFlvParserResult.PacketsAvailable);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired,
            machine.Begin(nativeSession, source, audio0));
        Assert.False(audio0.IsDisposed);
        var blockedVideo = parser.Parse(BuildVideoNaluTag(
            timestamp: 140,
            compositionTime: 10,
            keyframe: false,
            [0, 0, 0, 1, 0x41]));
        Assert.Equal(PlaybackBoundedFlvParserResult.ActiveCompletion, blockedVideo.Result);
        Assert.Equal(0, blockedVideo.ConsumedByteCount);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            machine.Advance());
        Assert.True(audio0.IsDisposed);

        var video1 = ParseCompletion(
            parser,
            BuildVideoNaluTag(
                timestamp: 140,
                compositionTime: 10,
                keyframe: false,
                [0, 0, 0, 1, 0x41]),
            PlaybackBoundedFlvParserResult.PacketsAvailable);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted,
            machine.Begin(nativeSession, source, video1));

        var audio1 = ParseCompletion(
            parser,
            BuildAudioRawTag(120, [0x33]),
            PlaybackBoundedFlvParserResult.PacketsAvailable);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.PacketAccepted,
            machine.Begin(nativeSession, source, audio1));

        var videoEnd = ParseCompletion(
            parser,
            BuildVideoEndTag(),
            PlaybackBoundedFlvParserResult.PacketsAvailable);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.ReceiveRequired,
            machine.Begin(nativeSession, source, videoEnd));
        Assert.False(videoEnd.IsDisposed);
        var blockedFinal = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        Assert.Equal(PlaybackBoundedFlvParserResult.ActiveCompletion, blockedFinal.Result);
        Assert.Equal(0, blockedFinal.ConsumedByteCount);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FrameProgressAndAccepted,
            machine.Advance());
        Assert.True(videoEnd.IsDisposed);

        var final = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, final.Result);
        var audioEnd = Assert.IsType<PlaybackBoundedDemuxReadCompletion>(final.Completion);
        Assert.Single(audioEnd.Events);
        Assert.Equal(
            PlaybackFiniteOneEventPacketSubmissionResult.FlushAccepted,
            machine.Begin(nativeSession, source, audioEnd));

        Assert.True(machine.VideoFlushAccepted);
        Assert.True(machine.AudioFlushAccepted);
        Assert.False(machine.VideoEndOfStream);
        Assert.False(machine.AudioEndOfStream);
        Assert.False(machine.HasActiveSubmission);
        Assert.Equal(6, operations.SubmitCount);
        Assert.Equal(2, operations.ReceiveCount);
        Assert.Equal(2, operations.RetryCount);
        Assert.Equal(6, operations.ReleaseCount);
        Assert.Equal(1, operations.OwnerDisposeCount);
        Assert.Equal([false, true], operations.RetryWasFlush);

        Assert.Collection(
            operations.Events,
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.Packet,
                PlaybackBoundedDemuxMediaKind.Video,
                sequence: 0,
                expectedPayload: [0, 0, 0, 2, 0x65, 0x88],
                expectedPts: TimeSpan.FromMilliseconds(80),
                expectedDts: TimeSpan.FromMilliseconds(100),
                PlaybackBoundedDemuxKeyframeState.Keyframe),
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.Packet,
                PlaybackBoundedDemuxMediaKind.Audio,
                sequence: 0,
                expectedPayload: [0x11, 0x22],
                expectedPts: TimeSpan.FromMilliseconds(90),
                expectedDts: TimeSpan.FromMilliseconds(90),
                PlaybackBoundedDemuxKeyframeState.NotApplicable),
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.Packet,
                PlaybackBoundedDemuxMediaKind.Video,
                sequence: 1,
                expectedPayload: [0, 0, 0, 1, 0x41],
                expectedPts: TimeSpan.FromMilliseconds(150),
                expectedDts: TimeSpan.FromMilliseconds(140),
                PlaybackBoundedDemuxKeyframeState.NotKeyframe),
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.Packet,
                PlaybackBoundedDemuxMediaKind.Audio,
                sequence: 1,
                expectedPayload: [0x33],
                expectedPts: TimeSpan.FromMilliseconds(120),
                expectedDts: TimeSpan.FromMilliseconds(120),
                PlaybackBoundedDemuxKeyframeState.NotApplicable),
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.EndOfStream,
                PlaybackBoundedDemuxMediaKind.Video,
                sequence: 2,
                expectedPayload: [],
                expectedPts: null,
                expectedDts: null,
                PlaybackBoundedDemuxKeyframeState.NotApplicable),
            item => AssertEvent(
                item,
                PlaybackBoundedDemuxEventKind.EndOfStream,
                PlaybackBoundedDemuxMediaKind.Audio,
                sequence: 2,
                expectedPayload: [],
                expectedPts: null,
                expectedDts: null,
                PlaybackBoundedDemuxKeyframeState.NotApplicable));
    }

    [Fact]
    public void Unpartitioned_final_dual_eos_fails_closed_before_native_submission()
    {
        using var parser = new PlaybackBoundedFlvParser();
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            parser.Parse(BuildBootstrap()).Result);
        var source = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);
        using var nativeSession = CreateNativeSession(source);
        var operations = new FixtureOperations();
        using var machine = new PlaybackFiniteOneEventPacketSubmissionStateMachine(operations);

        var final = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, final.Result);
        var completion = Assert.IsType<PlaybackBoundedDemuxReadCompletion>(final.Completion);
        Assert.Equal(2, completion.Events.Count);

        var result = machine.Begin(nativeSession, source, completion);

        Assert.Equal(PlaybackFiniteOneEventPacketSubmissionResult.EventRejected, result);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.SubmitCount);
        Assert.False(machine.VideoFlushAccepted);
        Assert.False(machine.AudioFlushAccepted);
    }

    private static PlaybackBoundedDemuxReadCompletion ParseCompletion(
        PlaybackBoundedFlvParser parser,
        ReadOnlyMemory<byte> input,
        PlaybackBoundedFlvParserResult expectedResult)
    {
        var operation = parser.Parse(input);
        Assert.Equal(expectedResult, operation.Result);
        return Assert.IsType<PlaybackBoundedDemuxReadCompletion>(operation.Completion);
    }

    private static PlaybackBoundedFlvNativeDecoderSession CreateNativeSession(
        PlaybackBoundedDemuxSession source) =>
        new(
            source,
            source.Video.StreamId,
            source.Audio.StreamId,
            source.Video.TimeBase,
            source.Audio.TimeBase,
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264),
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac));

    private static void AssertEvent(
        EventSnapshot actual,
        PlaybackBoundedDemuxEventKind expectedKind,
        PlaybackBoundedDemuxMediaKind expectedMediaKind,
        long sequence,
        byte[] expectedPayload,
        TimeSpan? expectedPts,
        TimeSpan? expectedDts,
        PlaybackBoundedDemuxKeyframeState expectedKeyframe)
    {
        Assert.Equal(expectedKind, actual.Kind);
        Assert.Equal(expectedMediaKind, actual.MediaKind);
        Assert.Equal(sequence, actual.Sequence);
        Assert.Equal(expectedPayload, actual.Payload);
        Assert.Equal(expectedPts, actual.PresentationTimestamp);
        Assert.Equal(expectedDts, actual.DecodeTimestamp);
        Assert.Equal(expectedKeyframe, actual.KeyframeState);
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
        Assert.Equal(0, width % 16);
        Assert.Equal(0, height % 16);
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

    private sealed record EventSnapshot(
        PlaybackBoundedDemuxEventKind Kind,
        PlaybackBoundedDemuxMediaKind MediaKind,
        long Sequence,
        byte[] Payload,
        TimeSpan? PresentationTimestamp,
        TimeSpan? DecodeTimestamp,
        PlaybackBoundedDemuxKeyframeState KeyframeState)
    {
        public static EventSnapshot Create(PlaybackBoundedDemuxEvent item) =>
            new(
                item.Kind,
                item.MediaKind,
                item.Sequence,
                item.Payload.ToArray(),
                item.PresentationTimestamp.IsKnown
                    ? item.PresentationTimestamp.Value
                    : null,
                item.DecodeTimestamp.IsKnown
                    ? item.DecodeTimestamp.Value
                    : null,
                item.KeyframeState);
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

    private sealed class FakePacketOwner(FixtureOperations operations) :
        IPlaybackMetadataAwareNativePacketOwner
    {
        private int _isDisposed;

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            PlaybackMetadataAwareNativeSendResult.Rejected;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                operations.OwnerDisposeCount++;
            }
        }
    }

    private sealed class FixtureOperations : IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        public Queue<PlaybackMetadataAwareNativePacketResult> SubmitResults { get; } = new();

        public Queue<PlaybackDecoderFfmpegFrameReceiveResult> ReceiveResults { get; } = new();

        public Queue<PlaybackMetadataAwareNativePacketResult> RetryResults { get; } = new();

        public List<EventSnapshot> Events { get; } = [];

        public List<bool> RetryWasFlush { get; } = [];

        public int SubmitCount { get; private set; }

        public int ReceiveCount { get; private set; }

        public int RetryCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int OwnerDisposeCount { get; set; }

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            SubmitCount++;
            var demuxEvent = Assert.Single(sourceCompletion.Events);
            Events.Add(EventSnapshot.Create(demuxEvent));
            var result = SubmitResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.Submitted
                : SubmitResults.Dequeue();
            return new(
                result,
                result == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain &&
                    demuxEvent.Kind == PlaybackBoundedDemuxEventKind.Packet
                    ? new FakePacketOwner(this)
                    : null,
                sourceCompletion);
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            ReceiveCount++;
            return ReceiveResults.Count == 0
                ? PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput
                : ReceiveResults.Dequeue();
        }

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush)
        {
            RetryCount++;
            RetryWasFlush.Add(isFlush);
            return RetryResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.NativeRejected
                : RetryResults.Dequeue();
        }

        public void ReleasePending() => ReleaseCount++;
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
