using System.Buffers.Binary;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBoundedFlvParserTests
{
    [Fact]
    public void Parse_BootstrapCreatesSessionAndStopsBeforeMediaSuffix()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var bootstrap = BuildBootstrap();
        var media = BuildVideoNaluTag(
            timestamp: 100,
            compositionTime: -20,
            keyframe: true,
            [0, 0, 0, 2, 0x65, 0x88]);
        var input = Concat(bootstrap, media);

        var bootstrapResult = parser.Parse(input);

        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            bootstrapResult.Result);
        Assert.Equal(bootstrap.Length, bootstrapResult.ConsumedByteCount);
        Assert.Null(bootstrapResult.Completion);
        var session = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);
        Assert.Equal(320, session.Video.Width);
        Assert.Equal(240, session.Video.Height);
        Assert.Equal(48_000, session.Audio.SampleRate);
        Assert.Equal(2, session.Audio.ChannelCount);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 1_000), session.Video.TimeBase);

        var packetResult = parser.Parse(input.AsMemory(bootstrap.Length));
        using var completion = packetResult.Completion;
        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, packetResult.Result);
        var packet = Assert.Single(completion!.Events);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, packet.MediaKind);
        Assert.Equal(new byte[] { 0, 0, 0, 2, 0x65, 0x88 }, packet.Payload.ToArray());
        Assert.Equal(TimeSpan.FromMilliseconds(100), packet.DecodeTimestamp.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(80), packet.PresentationTimestamp.Value);
        Assert.Equal(PlaybackBoundedDemuxKeyframeState.Keyframe, packet.KeyframeState);
    }

    [Fact]
    public void Parse_RequiresCompletionDisposalBeforeNextPacket()
    {
        using var parser = CreateBootstrappedParser();
        var video = parser.Parse(BuildVideoNaluTag(
            0,
            0,
            true,
            [0, 0, 0, 1, 0x65]));
        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, video.Result);

        var audioTag = BuildAudioRawTag(0, [1, 2, 3]);
        var blocked = parser.Parse(audioTag);
        Assert.Equal(PlaybackBoundedFlvParserResult.ActiveCompletion, blocked.Result);
        Assert.Equal(0, blocked.ConsumedByteCount);

        video.Completion!.Dispose();
        var audio = parser.Parse(audioTag);
        using var audioCompletion = audio.Completion;
        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, audio.Result);
        Assert.Equal(
            PlaybackBoundedDemuxMediaKind.Audio,
            audioCompletion!.Events[0].MediaKind);
    }

    [Fact]
    public void Parse_CopiesIncompleteHeaderAndTagAcrossCallerChunks()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var bootstrap = BuildBootstrap();

        var first = parser.Parse(bootstrap.AsMemory(0, 5));
        Assert.Equal(PlaybackBoundedFlvParserResult.NeedMoreInput, first.Result);
        Assert.Equal(5, first.CarryByteCount);

        var second = parser.Parse(bootstrap.AsMemory(5));
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            second.Result);

        var tag = BuildAudioRawTag(25, [7, 8, 9, 10]);
        var split = tag.Length - 3;
        var firstHalf = tag.AsMemory(0, split).ToArray();
        var partial = parser.Parse(firstHalf);
        Assert.Equal(PlaybackBoundedFlvParserResult.NeedMoreInput, partial.Result);
        firstHalf.AsSpan().Fill(0xFF);

        var completed = parser.Parse(tag.AsMemory(split));
        using var completion = completed.Completion;
        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, completed.Result);
        Assert.Equal(
            new byte[] { 7, 8, 9, 10 },
            completion!.Events[0].Payload.ToArray());
    }

    [Fact]
    public void Parse_RejectsMalformedPreviousTagSize()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var tag = BuildVideoSequenceTag();
        tag[^1] ^= 1;

        var result = parser.Parse(Concat(BuildHeader(), tag));

        Assert.Equal(PlaybackBoundedFlvParserResult.MalformedData, result.Result);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.Faulted,
            parser.Parse(ReadOnlyMemory<byte>.Empty).Result);
    }

    [Fact]
    public void Parse_RejectsMediaBeforeBothSequenceHeaders()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var input = Concat(
            BuildHeader(),
            BuildVideoSequenceTag(),
            BuildVideoNaluTag(0, 0, true, [0, 0, 0, 1, 0x65]));

        var result = parser.Parse(input);

        Assert.Equal(
            PlaybackBoundedFlvParserResult.InvalidStreamDescription,
            result.Result);
    }

    [Fact]
    public void Parse_SkipsScriptAndExactDuplicateConfiguration()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var bootstrap = Concat(
            BuildHeader(),
            BuildVideoSequenceTag(),
            BuildTag(18, 0, [1, 2, 3, 4]),
            BuildAudioSequenceTag());
        var bootstrapResult = parser.Parse(bootstrap);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            bootstrapResult.Result);
        Assert.Equal(3, bootstrapResult.SkippedTagCount);

        var input = Concat(
            BuildVideoSequenceTag(),
            BuildAudioSequenceTag(),
            BuildAudioRawTag(5, [0xAA]));
        var result = parser.Parse(input);
        using var completion = result.Completion;

        Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, result.Result);
        Assert.Equal(2, result.SkippedTagCount);
        Assert.Equal(new byte[] { 0xAA }, completion!.Events[0].Payload.ToArray());
    }

    [Fact]
    public void Parse_ChangedConfigurationFailsClosed()
    {
        using var parser = CreateBootstrappedParser();

        var result = parser.Parse(BuildAudioSequenceTag([0x12, 0x10]));

        Assert.Equal(
            PlaybackBoundedFlvParserResult.InvalidStreamDescription,
            result.Result);
    }

    [Fact]
    public void Parse_FinalInputEmitsBothStreamEos()
    {
        using var parser = CreateBootstrappedParser();

        var result = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        using var completion = result.Completion;

        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, result.Result);
        Assert.Equal(2, completion!.Events.Count);
        Assert.All(
            completion.Events,
            item => Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, item.Kind));
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, completion.Events[0].MediaKind);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, completion.Events[1].MediaKind);
    }

    [Fact]
    public void Parse_AvcEndThenFinalInputEmitsOnlyAudioEos()
    {
        using var parser = CreateBootstrappedParser();
        var videoEnd = parser.Parse(BuildVideoEndTag());
        using (var videoCompletion = videoEnd.Completion)
        {
            Assert.Equal(PlaybackBoundedFlvParserResult.PacketsAvailable, videoEnd.Result);
            Assert.Equal(
                PlaybackBoundedDemuxMediaKind.Video,
                videoCompletion!.Events[0].MediaKind);
        }

        var final = parser.Parse(ReadOnlyMemory<byte>.Empty, isFinal: true);
        using var finalCompletion = final.Completion;
        Assert.Equal(PlaybackBoundedFlvParserResult.EndOfStream, final.Result);
        var audioEnd = Assert.Single(finalCompletion!.Events);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, audioEnd.MediaKind);
    }

    [Fact]
    public void Parse_DiscontinuityDisposesOldSessionAndRequiresNewBootstrap()
    {
        using var parser = CreateBootstrappedParser();
        var oldSession = Assert.IsType<PlaybackBoundedDemuxSession>(parser.Session);

        var result = parser.Parse(BuildBootstrap(), discontinuity: true);

        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            result.Result);
        Assert.Equal(1, result.Generation);
        Assert.True(oldSession.IsDisposed);
        Assert.NotSame(oldSession, parser.Session);
    }

    [Fact]
    public void Parse_RejectsInvalidAvccNalLengthWalk()
    {
        using var parser = CreateBootstrappedParser();

        var result = parser.Parse(BuildVideoNaluTag(
            0,
            0,
            true,
            [0, 0, 0, 4, 0x65, 0x88]));

        Assert.Equal(PlaybackBoundedFlvParserResult.MalformedData, result.Result);
    }

    [Fact]
    public void Parse_PreservesNegativePresentationTimestamp()
    {
        using var parser = CreateBootstrappedParser();
        var result = parser.Parse(BuildVideoNaluTag(
            timestamp: 10,
            compositionTime: -20,
            keyframe: false,
            [0, 0, 0, 1, 0x41]));
        using var completion = result.Completion;

        var packet = Assert.Single(completion!.Events);
        Assert.Equal(TimeSpan.FromMilliseconds(10), packet.DecodeTimestamp.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(-10), packet.PresentationTimestamp.Value);
        Assert.Equal(PlaybackBoundedDemuxKeyframeState.NotKeyframe, packet.KeyframeState);
    }

    [Fact]
    public void Parse_RejectsUnsupportedAacObjectType()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var input = Concat(
            BuildHeader(),
            BuildVideoSequenceTag(),
            BuildAudioSequenceTag(BuildAudioSpecificConfig(5, 3, 2)));

        var result = parser.Parse(input);

        Assert.Equal(PlaybackBoundedFlvParserResult.UnsupportedCodec, result.Result);
    }

    [Fact]
    public void Parse_RejectsInvalidAvcConfiguration()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var configuration = BuildAvcConfiguration();
        configuration[0] = 2;

        var result = parser.Parse(Concat(
            BuildHeader(),
            BuildVideoSequenceTag(configuration)));

        Assert.Equal(
            PlaybackBoundedFlvParserResult.InvalidStreamDescription,
            result.Result);
    }

    [Fact]
    public void Parse_EnforcesScriptAndInputObservationLimits()
    {
        var options = new PlaybackBoundedFlvParserOptions
        {
            MaximumScriptTagDataBytes = 2,
        };
        using var parser = new PlaybackBoundedFlvParser(options);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.LimitExceeded,
            parser.Parse(Concat(BuildHeader(), BuildTag(18, 0, [1, 2, 3]))).Result);

        var smallDemux = new PlaybackBoundedDemuxOptions
        {
            MaximumInputBytesPerRead = 4 * 1024 * 1024,
            MaximumOwnedPacketBytesPerRead = 4 * 1024 * 1024,
        };
        var largeOptions = new PlaybackBoundedFlvParserOptions
        {
            DemuxOptions = smallDemux,
            MaximumSkippedTagBytesPerOperation = 4 * 1024 * 1024,
        };
        using var inputParser = new PlaybackBoundedFlvParser(largeOptions);
        Assert.Equal(
            PlaybackBoundedFlvParserResult.LimitExceeded,
            inputParser.Parse(new byte[4 * 1024 * 1024 + 1]).Result);
    }

    [Fact]
    public void Parse_CancellationAndDisposalAreExplicit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var parser = new PlaybackBoundedFlvParser();

        Assert.Equal(
            PlaybackBoundedFlvParserResult.Cancelled,
            parser.Parse(BuildHeader(), cancellationToken: cancellation.Token).Result);
        Assert.Null(parser.Session);

        parser.Dispose();
        parser.Dispose();
        Assert.Equal(
            PlaybackBoundedFlvParserResult.Disposed,
            parser.Parse(ReadOnlyMemory<byte>.Empty).Result);
    }

    [Fact]
    public void Parse_FinalIncompleteRecordReturnsTruncatedData()
    {
        using var parser = CreateBootstrappedParser();
        var tag = BuildAudioRawTag(0, [1, 2, 3]);

        var result = parser.Parse(tag.AsMemory(0, tag.Length - 1), isFinal: true);

        Assert.Equal(PlaybackBoundedFlvParserResult.TruncatedData, result.Result);
    }

    [Fact]
    public void Parse_RejectsUnsupportedHeaderFlags()
    {
        using var parser = new PlaybackBoundedFlvParser();
        var header = BuildHeader();
        header[4] = 1;

        Assert.Equal(
            PlaybackBoundedFlvParserResult.UnsupportedContainer,
            parser.Parse(header).Result);
    }

    [Fact]
    public void Constructor_RejectsCarryThatCannotHoldAcceptedRecord()
    {
        var options = new PlaybackBoundedFlvParserOptions
        {
            MaximumCarryBytes = 1024,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackBoundedFlvParser(options));
    }

    private static PlaybackBoundedFlvParser CreateBootstrappedParser()
    {
        var parser = new PlaybackBoundedFlvParser();
        var result = parser.Parse(BuildBootstrap());
        Assert.Equal(
            PlaybackBoundedFlvParserResult.StreamDescriptionAvailable,
            result.Result);
        return parser;
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

    private static byte[] BuildVideoSequenceTag(byte[]? configuration = null) =>
        BuildTag(
            9,
            0,
            Concat(
                [0x17, 0, 0, 0, 0],
                configuration ?? BuildAvcConfiguration()));

    private static byte[] BuildAudioSequenceTag(byte[]? configuration = null) =>
        BuildTag(
            8,
            0,
            Concat([0xAF, 0], configuration ?? new byte[] { 0x11, 0x90 }));

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

    internal static byte[] BuildAvcConfiguration()
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

    private static byte[] BuildAudioSpecificConfig(
        uint objectType,
        uint frequencyIndex,
        uint channelConfiguration)
    {
        var writer = new TestBitWriter();
        writer.WriteBits(objectType, 5);
        writer.WriteBits(frequencyIndex, 4);
        writer.WriteBits(channelConfiguration, 4);
        return writer.ToBytes();
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

        public byte[] ToBytes()
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
