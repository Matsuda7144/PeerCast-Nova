using System.Buffers.Binary;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class FlvRecordingDecodableStartStreamTests : IDisposable
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-FlvRecordingDecodableStartStreamTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AlreadyCancelledWriter_DoesNotClassifyUnreadAudioOnlySourceAsNoStart()
    {
        Directory.CreateDirectory(directory);
        using var source = new MemoryStream(BuildFlv(
            [AacConfiguration(0), AacMedia(100, 0x11)],
            flags: 0x04));
        await using var transform = CreatePipeline(source);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var path = GetPath("cancelled-before-classification.flv");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RecordingStreamFileWriter.WriteForFileSinkAsync(
                transform,
                path,
                static (_, exception) => Assert.Fail(exception.GetType().Name),
                stopping.Token));

        Assert.Equal(0, source.Position);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task StartAtFirstIdr_DiscardsMidGopMediaAndUsesOneSharedBaseline()
    {
        var script = ScriptData(777, "metadata"u8.ToArray());
        var avcConfiguration = AvcConfiguration(123, 4);
        var aacConfiguration = AacConfiguration(456);
        var preStartAudio = AacMedia(4_970, 0x11);
        var preStartVideo1 = AvcNalu(4_980, 2, 0, 4, [0x41, 0x81]);
        var preStartAudio2 = AacMedia(4_990, 0x12);
        var preStartVideo2 = AvcNalu(4_995, 2, 0, 4, [0x41, 0x82]);
        var idr = AvcNalu(5_000, 1, 0, 4, [0x65, 0x91]);
        var followingAudio = AacMedia(5_020, 0x21);
        var followingVideo = AvcNalu(5_040, 2, 0, 4, [0x41, 0x92]);
        var input = BuildFlv(
            script,
            avcConfiguration,
            aacConfiguration,
            preStartAudio,
            preStartVideo1,
            preStartAudio2,
            preStartVideo2,
            idr,
            followingAudio,
            followingVideo);

        var result = await NormalizeAsync(input, [1, 7, 2, 13, 3]);
        var expected = NormalizeExpected(BuildFlv(
            script,
            avcConfiguration,
            aacConfiguration,
            idr,
            followingAudio,
            followingVideo));
        var media = ParseTags(result.Output).Where(static tag => tag.IsMedia).ToArray();

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.NotNull(result.Outcome.WriteResult);
        Assert.Equal(expected, result.Output);
        Assert.Equal(3, media.Length);
        Assert.Equal([0u, 20u, 40u], media.Select(static tag => tag.Timestamp));
        Assert.Equal(0x65, result.Output[media[0].Offset + TagHeaderLength + 5 + 4]);
    }

    [Fact]
    public async Task KeyframeFlagWithoutIdr_DoesNotEstablishRecordingStart()
    {
        var idrLookingByteInsideNonIdrPayload = AvcNalu(
            1_000,
            frameType: 1,
            compositionTime: 0,
            nalLengthSize: 4,
            [0x41, 0x65, 0xAA]);
        var result = await NormalizeAsync(
            BuildFlv(
                AvcConfiguration(0, 4),
                AacConfiguration(0),
                idrLookingByteInsideNonIdrPayload),
            [1]);

        Assert.True(result.Outcome.IsNoDecodableVideoStart);
        Assert.Null(result.Outcome.WriteResult);
        Assert.False(File.Exists(result.Path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task MultiNalTag_FindsIdrAfterEarlierNonIdrNal()
    {
        var idrTag = AvcNalu(
            7_500,
            frameType: 2,
            compositionTime: 0,
            nalLengthSize: 2,
            [0x41, 0x65, 0xFF],
            [0x06, 0x10],
            [0x65, 0x22]);
        var input = BuildFlv(
            AvcConfiguration(33, 2),
            idrTag,
            AacMedia(7_520, 0x33));

        var result = await NormalizeAsync(input, [4, 1, 2, 1, 17]);
        var expected = NormalizeExpected(input);
        var media = ParseTags(result.Output).Where(static tag => tag.IsMedia).ToArray();

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.Equal(expected, result.Output);
        Assert.Equal(0u, media[0].Timestamp);
        Assert.Equal(20u, media[1].Timestamp);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task AvccNalLengthSize_IsReadFromConfiguration(int nalLengthSize)
    {
        var input = BuildFlv(
            AvcConfiguration(10, nalLengthSize),
            AvcNalu(100, 1, 0, nalLengthSize, [0x65, 0xA1]));

        var result = await NormalizeAsync(input, [1, 5, 2, 3]);

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.Equal(NormalizeExpected(input), result.Output);
        Assert.Equal(0u, ParseTags(result.Output).Single(static tag => tag.IsMedia).Timestamp);
    }

    [Fact]
    public async Task UnsupportedThreeByteNalLengthSize_FailsSafely()
    {
        var input = BuildFlv(
            AvcConfiguration(0, 3),
            AvcNalu(100, 1, 0, 3, [0x65, 0xA1]));

        await AssertTransformFailureAsync(input);
    }

    [Theory]
    [InlineData("SPS", 13)]
    [InlineData("PPS", 20)]
    public async Task AvccParameterSetWithForbiddenBit_IsRejectedBeforeLaterIdr(
        string parameterSet,
        int headerOffset)
    {
        _ = parameterSet;
        var malformedConfiguration = AvcConfiguration(0, 4);
        malformedConfiguration.Body[headerOffset] |= 0x80;
        var input = BuildFlv(
            malformedConfiguration,
            AvcNalu(100, 1, 0, 4, [0x65, 0xA1]));

        await AssertTransformFailureAsync(input);
    }

    [Fact]
    public async Task AvcConfigurationExtensionWithForbiddenBit_IsRejectedBeforeLaterIdr()
    {
        var configuration = AvcConfiguration(0, 4);
        var extendedConfiguration = new Tag(
            configuration.Type,
            configuration.Timestamp,
            Concat(
                configuration.Body,
                [0xFD, 0xF8, 0xF8, 1, 0, 2, 0xED, 0xA5]));
        var input = BuildFlv(
            extendedConfiguration,
            AvcNalu(100, 1, 0, 4, [0x65, 0xA1]));

        await AssertTransformFailureAsync(input);
    }

    [Fact]
    public async Task MalformedNalLengthPrefix_FailsSafely()
    {
        var malformedIdr = AvcNaluWithPayload(
            100,
            frameType: 1,
            compositionTime: 0,
            [0, 0, 0, 20, 0x65]);
        var input = BuildFlv(
            AvcConfiguration(0, 4),
            malformedIdr);

        await AssertTransformFailureAsync(input);
    }

    [Fact]
    public async Task BootstrapAndTagFraming_ArePreservedExactly()
    {
        var script1 = ScriptData(100, [0x02, 0x03, 0x04]);
        var avcConfiguration1 = AvcConfiguration(111, 4, spsMarker: 0x64);
        var aacConfiguration1 = AacConfiguration(112, [0x12, 0x10]);
        var script2 = ScriptData(113, [0x05, 0x06]);
        var avcConfiguration2 = AvcConfiguration(114, 4, spsMarker: 0x6E);
        var aacConfiguration2 = AacConfiguration(115, [0x11, 0x90]);
        var idr = AvcNalu(200, 1, 0, 4, [0x65, 0x77]);
        var input = BuildFlv(
            script1,
            avcConfiguration1,
            aacConfiguration1,
            script2,
            avcConfiguration2,
            aacConfiguration2,
            idr);
        var expected = NormalizeExpected(BuildFlv(
            script1,
            avcConfiguration2,
            aacConfiguration2,
            script2,
            idr));

        var result = await NormalizeAsync(input, [3, 1, 11, 2, 5]);
        var parsed = ParseTags(result.Output);

        Assert.Equal(expected, result.Output);
        Assert.Equal(CreateHeader(0x05), result.Output[..FlvHeaderLength]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(
            result.Output.AsSpan(9, 4)));
        Assert.All(parsed, static tag =>
        {
            Assert.Equal(0u, tag.StreamId);
            Assert.Equal((uint)(TagHeaderLength + tag.DataSize), tag.PreviousTagSize);
        });
    }

    [Fact]
    public async Task CompositionTime_RemainsByteIdenticalWhileDtsIsRebased()
    {
        var idr = AvcNalu(9_000, 1, -37, 4, [0x65, 0x55]);
        var result = await NormalizeAsync(
            BuildFlv(
                AvcConfiguration(321, 4),
                idr,
                AacMedia(9_020, 0x66)),
            [2, 1, 9]);
        var outputIdr = ParseTags(result.Output).Single(static tag => tag.Type == VideoTagType && tag.IsMedia);

        Assert.Equal(0u, outputIdr.Timestamp);
        Assert.Equal([0xFF, 0xFF, 0xDB], result.Output.AsSpan(
            outputIdr.Offset + TagHeaderLength + 2,
            3).ToArray());
        Assert.Equal(
            CreateTag(idr).AsSpan(TagHeaderLength + 2, 3).ToArray(),
            result.Output.AsSpan(outputIdr.Offset + TagHeaderLength + 2, 3).ToArray());
    }

    [Fact]
    public async Task StopBeforeIdr_ReturnsSpecificOutcomeAndRemovesPartialFile()
    {
        var prefix = BuildFlv(
            AvcConfiguration(0, 4),
            AacConfiguration(0),
            AacMedia(4_900, 0x11),
            AvcNalu(4_950, 2, 0, 4, [0x41, 0x22]));
        using var source = new BlockingAfterPrefixReadStream(prefix);
        var path = GetPath("stopped-before-idr.flv");
        var sink = new RecordingFileSink(
            new DelegateSourceProvider(_ => source),
            new ConstantPathProvider(path));
        var started = await sink.StartAsync(
            RecordingSinkStartRequest.Manual(CreateCandidate()),
            CancellationToken.None);
        var session = Assert.IsType<RecordingSinkSession>(started.Session);

        await source.BlockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(RecordingSinkStopResultKind.NoDecodableVideoStart, stopped.Kind);
        Assert.Null(stopped.Fault);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task EndOfStreamBeforeIdr_PropagatesDistinctSinkAndSessionResult()
    {
        var input = BuildFlv(
            AvcConfiguration(0, 4),
            AacConfiguration(0),
            AacMedia(4_900, 0x11),
            AvcNalu(4_950, 2, 0, 4, [0x41, 0x22]));
        var path = GetPath("eof-before-idr.flv");
        var sink = new RecordingFileSink(
            new DelegateSourceProvider(_ => new MemoryStream(input)),
            new ConstantPathProvider(path));
        var candidate = CreateCandidate();
        var started = await sink.StartAsync(
            RecordingSinkStartRequest.Manual(candidate),
            CancellationToken.None);
        var session = Assert.IsType<RecordingSinkSession>(started.Session);

        await WaitForStatusAsync(
            sink,
            session,
            RecordingFileSinkSessionStatusKind.NoDecodableVideoStart);
        Assert.True(sink.TryGetStatus(session, out var fileStatus));
        Assert.Null(fileStatus!.WriteResult);
        Assert.Null(fileStatus.Fault);
        Assert.True(sink.TryGetSessionStatus(session, out var lifecycleStatus));
        Assert.Equal(
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart,
            lifecycleStatus!.Kind);

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);

        Assert.Equal(RecordingSinkStopResultKind.NoDecodableVideoStart, stopped.Kind);
        Assert.Null(stopped.Fault);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task RuntimeStop_PropagatesNoDecodableStartToApplicationResult()
    {
        var input = BuildFlv(
            AvcConfiguration(0, 4),
            AacMedia(4_900, 0x11),
            AvcNalu(4_950, 2, 0, 4, [0x41, 0x22]));
        var path = GetPath("runtime-no-start.flv");
        await using var runtime = new RecordingRuntime(
            new DelegateSourceProvider(_ => new MemoryStream(input)),
            new ConstantPathProvider(path));
        var candidate = CreateCandidate();

        var started = await runtime.StartManualAsync(candidate);
        var stopped = await runtime.StopAsync(candidate.ChannelId);

        Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
        Assert.Equal(
            RecordingAutomationStopResultKind.NoDecodableVideoStart,
            stopped.Kind);
        Assert.Equal(RecordingAutomationExecutionState.Idle, stopped.State);
        Assert.Null(stopped.Fault);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AudioOnlyHeader_DoesNotWaitForIdrAndKeepsCurrentNormalization()
    {
        var input = BuildFlv(
            [AacConfiguration(123), AacMedia(5_000, 0x11), AacMedia(5_020, 0x22)],
            flags: 0x04);

        var result = await NormalizeAsync(input, [1]);
        var media = ParseTags(result.Output).Where(static tag => tag.IsMedia).ToArray();

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.Equal(NormalizeExpected(input), result.Output);
        Assert.Equal([0u, 20u], media.Select(static tag => tag.Timestamp));
    }

    [Fact]
    public async Task NonAvcVideo_FallsBackToExistingRecordingBehavior()
    {
        var input = BuildFlv(
            AacMedia(100, 0x11),
            new Tag(VideoTagType, 110, [0x12, 0xA1]),
            AacMedia(120, 0x22));

        var result = await NormalizeAsync(input, [1, 4, 2, 6]);

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.Equal(NormalizeExpected(input), result.Output);
        Assert.Equal([0u, 10u, 20u],
            ParseTags(result.Output).Where(static tag => tag.IsMedia)
                .Select(static tag => tag.Timestamp));
    }

    [Fact]
    public async Task ArbitraryOneByteAndSplitReads_DoNotChangeStartDecisionOrOutput()
    {
        var input = BuildMidGopFixture();
        var expected = NormalizeExpected(BuildExpectedMidGopOutput());

        foreach (var pattern in new[]
                 {
                     new[] { 1 },
                     new[] { 13, 1, 10, 1, 5, 1, 2, 1, 3 },
                     new[] { 7, 4, 1, 19, 2, 8 },
                 })
        {
            var result = await NormalizeAsync(input, pattern);

            Assert.False(result.Outcome.IsNoDecodableVideoStart);
            Assert.Equal(expected, result.Output);
        }
    }

    [Fact]
    public async Task CancellationImmediatelyAfterIdrAcceptance_DrainsCompleteStartupPrefix()
    {
        var largeIdrNal = new byte[70_000];
        largeIdrNal[0] = 0x65;
        Array.Fill(largeIdrNal, (byte)0xA5, 1, largeIdrNal.Length - 1);
        var idr = AvcNalu(10_000, 1, 0, 4, largeIdrNal);
        var input = BuildFlv(
            AvcConfiguration(0, 4),
            idr);
        using var source = new BlockingAfterPrefixReadStream(input);
        using var stopping = new CancellationTokenSource();
        await using var transform = new CancelAfterReadStream(
            CreatePipeline(source),
            stopping,
            cancelAfterReadNumber: 1);
        var path = GetPath("cancel-during-idr-tag.flv");

        var outcome = await RecordingStreamFileWriter.WriteForFileSinkAsync(
            transform,
            path,
            static (_, exception) => Assert.Fail(exception.GetType().Name),
            stopping.Token);

        Assert.False(outcome.IsNoDecodableVideoStart);
        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, outcome.WriteResult!.CompletionKind);
        var output = await File.ReadAllBytesAsync(path);
        var parsed = ParseTags(output);
        Assert.Equal(2, parsed.Count);
        Assert.Equal((uint)(TagHeaderLength + parsed[1].DataSize), parsed[1].PreviousTagSize);
        Assert.Equal((uint)output.Length,
            checked((uint)(FlvHeaderLength + parsed.Sum(static tag => tag.Length))));
        Assert.Equal(NormalizeExpected(input), output);
    }

    [Fact]
    public async Task ProbeBufferLimit_FallsBackWithoutRetainingUnboundedPrefix()
    {
        var payload = new byte[1024];
        var repeatedAudio = Enumerable.Range(0, 1_100)
            .Select(index => AacMedia((uint)index, payload))
            .ToArray();
        var nonAvcVideo = new Tag(VideoTagType, 1_101, [0x12, 0xA1]);
        var input = BuildFlv([.. repeatedAudio, nonAvcVideo]);

        var result = await NormalizeAsync(input, [37, 1, 9, 2]);

        Assert.False(result.Outcome.IsNoDecodableVideoStart);
        Assert.Equal(NormalizeExpected(input), result.Output);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<TransformResult> NormalizeAsync(
        byte[] input,
        int[] chunkPattern)
    {
        var path = GetPath($"recording-{Guid.NewGuid():N}.flv");
        await using var transform = CreatePipeline(
            new FragmentingReadStream(input, chunkPattern));
        var outcome = await RecordingStreamFileWriter.WriteForFileSinkAsync(
            transform,
            path,
            static (_, exception) => Assert.Fail(exception.GetType().Name),
            CancellationToken.None);
        var output = File.Exists(path)
            ? await File.ReadAllBytesAsync(path)
            : [];
        return new TransformResult(outcome, output, path);
    }

    private async Task AssertTransformFailureAsync(byte[] input)
    {
        var path = GetPath($"invalid-{Guid.NewGuid():N}.flv");
        await using var transform = CreatePipeline(
            new FragmentingReadStream(input, [1, 2, 7]));

        await Assert.ThrowsAsync<RecordingStreamTransformException>(async () =>
            await RecordingStreamFileWriter.WriteForFileSinkAsync(
                transform,
                path,
                static (_, exception) => Assert.Fail(exception.GetType().Name),
                CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    private static FlvRecordingTimestampNormalizingStream CreatePipeline(Stream source)
    {
        return new FlvRecordingTimestampNormalizingStream(
            new FlvRecordingDecodableStartStream(source));
    }

    private static RecordingCandidate CreateCandidate()
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(name: "Test", streamExtension: "flv"));
    }

    private static byte[] BuildMidGopFixture()
    {
        return BuildFlv(
            ScriptData(777, "metadata"u8.ToArray()),
            AvcConfiguration(123, 4),
            AacConfiguration(456),
            AacMedia(4_970, 0x11),
            AvcNalu(4_980, 2, 0, 4, [0x41, 0x81]),
            AacMedia(4_990, 0x12),
            AvcNalu(4_995, 2, 0, 4, [0x41, 0x82]),
            AvcNalu(5_000, 1, 0, 4, [0x65, 0x91]),
            AacMedia(5_020, 0x21),
            AvcNalu(5_040, 2, 0, 4, [0x41, 0x92]));
    }

    private static byte[] BuildExpectedMidGopOutput()
    {
        return BuildFlv(
            ScriptData(777, "metadata"u8.ToArray()),
            AvcConfiguration(123, 4),
            AacConfiguration(456),
            AvcNalu(5_000, 1, 0, 4, [0x65, 0x91]),
            AacMedia(5_020, 0x21),
            AvcNalu(5_040, 2, 0, 4, [0x41, 0x92]));
    }

    private static byte[] BuildFlv(params Tag[] tags)
    {
        return BuildFlv(tags, flags: 0x05);
    }

    private static byte[] BuildFlv(Tag[] tags, byte flags)
    {
        var records = tags.Select(CreateTag).ToArray();
        return Concat([CreateHeader(flags), .. records]);
    }

    private static byte[] CreateHeader(byte flags)
    {
        return [(byte)'F', (byte)'L', (byte)'V', 1, flags, 0, 0, 0, 9, 0, 0, 0, 0];
    }

    private static Tag AvcConfiguration(
        uint timestamp,
        int nalLengthSize,
        byte spsMarker = 0x64)
    {
        var sps = new byte[] { 0x67, spsMarker, 0x00, 0x1F };
        byte[] pps = [0x68, 0xEE];
        var config = new byte[6 + 2 + sps.Length + 1 + 2 + pps.Length];
        config[0] = 1;
        config[1] = sps[1];
        config[2] = sps[2];
        config[3] = sps[3];
        config[4] = (byte)(0xFC | (nalLengthSize - 1));
        config[5] = 0xE1;
        var offset = 6;
        WriteUInt16(config.AsSpan(offset, 2), checked((ushort)sps.Length));
        offset += 2;
        sps.CopyTo(config, offset);
        offset += sps.Length;
        config[offset++] = 1;
        WriteUInt16(config.AsSpan(offset, 2), checked((ushort)pps.Length));
        offset += 2;
        pps.CopyTo(config, offset);

        return new Tag(
            VideoTagType,
            timestamp,
            Concat([0x17, AvcSequenceHeader, 0, 0, 0], config));
    }

    private static Tag AacConfiguration(uint timestamp, byte[]? payload = null)
    {
        payload ??= [0x12, 0x10];
        return new Tag(
            AudioTagType,
            timestamp,
            Concat([0xAF, AacSequenceHeader], payload));
    }

    private static Tag AacMedia(uint timestamp, params byte[] payload)
    {
        return new Tag(
            AudioTagType,
            timestamp,
            Concat([0xAF, AacRaw], payload));
    }

    private static Tag ScriptData(uint timestamp, byte[] payload)
    {
        return new Tag(ScriptTagType, timestamp, payload);
    }

    private static Tag AvcNalu(
        uint timestamp,
        int frameType,
        int compositionTime,
        int nalLengthSize,
        params byte[][] nalUnits)
    {
        using var payload = new MemoryStream();
        foreach (var nalUnit in nalUnits)
        {
            WriteVariableLength(payload, checked((uint)nalUnit.Length), nalLengthSize);
            payload.Write(nalUnit);
        }

        return AvcNaluWithPayload(
            timestamp,
            frameType,
            compositionTime,
            payload.ToArray());
    }

    private static Tag AvcNaluWithPayload(
        uint timestamp,
        int frameType,
        int compositionTime,
        byte[] avccPayload)
    {
        var encodedCompositionTime = unchecked((uint)compositionTime) & 0x00FF_FFFF;
        var body = Concat(
        [
            (byte)((frameType << 4) | 7),
            AvcNaluPacket,
            (byte)(encodedCompositionTime >> 16),
            (byte)(encodedCompositionTime >> 8),
            (byte)encodedCompositionTime,
        ],
        avccPayload);
        return new Tag(VideoTagType, timestamp, body);
    }

    private static byte[] CreateTag(Tag tag)
    {
        var bytes = new byte[TagHeaderLength + tag.Body.Length + PreviousTagSizeLength];
        bytes[0] = tag.Type;
        WriteUInt24(bytes.AsSpan(1, 3), checked((uint)tag.Body.Length));
        WriteTimestamp(bytes, tag.Timestamp);
        tag.Body.CopyTo(bytes, TagHeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(TagHeaderLength + tag.Body.Length, PreviousTagSizeLength),
            checked((uint)(TagHeaderLength + tag.Body.Length)));
        return bytes;
    }

    private static byte[] NormalizeExpected(byte[] input)
    {
        var output = (byte[])input.Clone();
        var tags = ParseTags(input);
        var baseline = tags.First(static tag => tag.IsMedia).Timestamp;
        foreach (var tag in tags.Where(static tag => tag.IsMedia))
        {
            WriteTimestamp(output.AsSpan(tag.Offset, tag.Length), tag.Timestamp - baseline);
        }

        return output;
    }

    private static List<ParsedTag> ParseTags(byte[] bytes)
    {
        var offset = FlvHeaderLength;
        var tags = new List<ParsedTag>();
        while (offset < bytes.Length)
        {
            Assert.True(bytes.Length - offset >= TagHeaderLength);
            var dataSize = ReadUInt24(bytes.AsSpan(offset + 1, 3));
            var length = checked(TagHeaderLength + (int)dataSize + PreviousTagSizeLength);
            Assert.True(bytes.Length - offset >= length);
            var streamId = ReadUInt24(bytes.AsSpan(offset + 8, 3));
            var previousTagSize = BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(offset + TagHeaderLength + (int)dataSize, PreviousTagSizeLength));
            var body = bytes.AsSpan(offset + TagHeaderLength, (int)dataSize);
            var type = bytes[offset];
            tags.Add(new ParsedTag(
                offset,
                length,
                type,
                dataSize,
                ReadTimestamp(bytes.AsSpan(offset, length)),
                streamId,
                previousTagSize,
                IsMedia(type, body)));
            offset += length;
        }

        Assert.Equal(bytes.Length, offset);
        return tags;
    }

    private static bool IsMedia(byte type, ReadOnlySpan<byte> body)
    {
        return type switch
        {
            AudioTagType => body[0] >> 4 != AacSoundFormat || body[1] == AacRaw,
            VideoTagType => (body[0] & 0x0F) != 7 || body[1] == AvcNaluPacket,
            ScriptTagType => false,
            _ => throw new InvalidOperationException($"Unexpected FLV tag type {type}."),
        };
    }

    private static uint ReadTimestamp(ReadOnlySpan<byte> tag)
    {
        return ((uint)tag[7] << 24) |
               ((uint)tag[4] << 16) |
               ((uint)tag[5] << 8) |
               tag[6];
    }

    private static void WriteTimestamp(Span<byte> tag, uint timestamp)
    {
        tag[4] = (byte)(timestamp >> 16);
        tag[5] = (byte)(timestamp >> 8);
        tag[6] = (byte)timestamp;
        tag[7] = (byte)(timestamp >> 24);
    }

    private static uint ReadUInt24(ReadOnlySpan<byte> value)
    {
        return ((uint)value[0] << 16) |
               ((uint)value[1] << 8) |
               value[2];
    }

    private static void WriteUInt24(Span<byte> value, uint number)
    {
        value[0] = (byte)(number >> 16);
        value[1] = (byte)(number >> 8);
        value[2] = (byte)number;
    }

    private static void WriteUInt16(Span<byte> value, ushort number)
    {
        BinaryPrimitives.WriteUInt16BigEndian(value, number);
    }

    private static void WriteVariableLength(
        Stream destination,
        uint value,
        int lengthSize)
    {
        for (var index = lengthSize - 1; index >= 0; index--)
        {
            destination.WriteByte((byte)(value >> (index * 8)));
        }
    }

    private static byte[] Concat(params byte[][] values)
    {
        var totalLength = values.Sum(static value => value.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }

        return result;
    }

    private string GetPath(string fileName)
    {
        return Path.Combine(directory, fileName);
    }

    private static async Task WaitForStatusAsync(
        RecordingFileSink sink,
        RecordingSinkSession session,
        RecordingFileSinkSessionStatusKind expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (sink.TryGetStatus(session, out var status) && status!.Kind == expected)
            {
                return;
            }

            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class DelegateSourceProvider(
        Func<RecordingSinkStartRequest, Stream> factory) : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(factory(request));
        }
    }

    private sealed class ConstantPathProvider(string path) : IRecordingFilePathProvider
    {
        public string GetPath(RecordingSinkStartRequest request, RecordingSessionId sessionId)
        {
            _ = request;
            _ = sessionId;
            return path;
        }
    }

    private sealed class FragmentingReadStream(byte[] bytes, int[] pattern) : Stream
    {
        private int offset;
        private int patternIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset == bytes.Length)
            {
                return ValueTask.FromResult(0);
            }

            var limit = pattern[patternIndex++ % pattern.Length];
            var count = Math.Min(Math.Min(buffer.Length, limit), bytes.Length - offset);
            bytes.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingAfterPrefixReadStream(byte[] bytes) : Stream
    {
        private int offset;

        public TaskCompletionSource BlockedReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (offset < bytes.Length)
            {
                var count = Math.Min(buffer.Length, bytes.Length - offset);
                bytes.AsMemory(offset, count).CopyTo(buffer);
                offset += count;
                return count;
            }

            BlockedReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelAfterReadStream(
        Stream inner,
        CancellationTokenSource stopping,
        int cancelAfterReadNumber) : Stream, IRecordingFileWriteOutcomeSource
    {
        private int readCount;

        private IRecordingFileWriteOutcomeSource OutcomeSource =>
            inner as IRecordingFileWriteOutcomeSource ??
            throw new InvalidOperationException(
                "The wrapped stream must expose the recording write outcome.");

        public bool NoDecodableVideoStart => OutcomeSource.NoDecodableVideoStart;

        public bool HasPendingDecodableStartOutput =>
            OutcomeSource.HasPendingDecodableStartOutput;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (Interlocked.Increment(ref readCount) == cancelAfterReadNumber)
            {
                stopping.Cancel();
            }

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed record Tag(byte Type, uint Timestamp, byte[] Body);

    private sealed record ParsedTag(
        int Offset,
        int Length,
        byte Type,
        uint DataSize,
        uint Timestamp,
        uint StreamId,
        uint PreviousTagSize,
        bool IsMedia);

    private sealed record TransformResult(
        RecordingFileWriteOutcome Outcome,
        byte[] Output,
        string Path);

    private const byte AudioTagType = 8;
    private const byte VideoTagType = 9;
    private const byte ScriptTagType = 18;
    private const byte AacSoundFormat = 10;
    private const byte AacSequenceHeader = 0;
    private const byte AacRaw = 1;
    private const byte AvcSequenceHeader = 0;
    private const byte AvcNaluPacket = 1;
    private const int FlvHeaderLength = 13;
    private const int TagHeaderLength = 11;
    private const int PreviousTagSizeLength = 4;
}
