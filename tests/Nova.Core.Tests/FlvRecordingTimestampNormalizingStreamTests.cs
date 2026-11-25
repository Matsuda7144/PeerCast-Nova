using System.Buffers.Binary;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class FlvRecordingTimestampNormalizingStreamTests : IDisposable
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-FlvRecordingTimestampNormalizingStreamTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Normalize_RealisticTimeline_UsesOneBaselineAndPreservesAllOtherBytes()
    {
        var input = BuildObservedTimelineFixture();
        var result = await NormalizeAsync(input, [1, 3, 2, 7, 5]);
        var originalTags = ParseTags(input);
        var normalizedTags = ParseTags(result.Output);
        var mediaTags = originalTags.Where(tag => tag.IsMedia).ToArray();
        var normalizedMediaTags = normalizedTags.Where(tag => tag.IsMedia).ToArray();

        Assert.Equal(4_127_509u, mediaTags[0].Timestamp);
        Assert.Equal(0u, normalizedMediaTags[0].Timestamp);
        Assert.Equal(4_162_602u, mediaTags[^1].Timestamp);
        Assert.Equal(35_093u, normalizedMediaTags[^1].Timestamp);
        Assert.Equal(mediaTags.Length, normalizedMediaTags.Length);
        Assert.Equal(
            mediaTags.Select(tag => tag.Timestamp - mediaTags[0].Timestamp),
            normalizedMediaTags.Select(tag => tag.Timestamp));
        Assert.Equal(CreateExpectedNormalizedBytes(input), result.Output);
        Assert.All(normalizedTags, tag =>
        {
            Assert.Equal((uint)(11 + tag.DataSize), tag.PreviousTagSize);
            Assert.Equal(0u, tag.StreamId);
        });
        Assert.Null(result.FaultStage);
        Assert.Equal(RecordingFileWriteCompletionKind.EndOfStream, result.WriteResult.CompletionKind);
    }

    [Fact]
    public async Task Normalize_OneByteReads_ProducesSameOutput()
    {
        var input = BuildObservedTimelineFixture();
        var irregular = await NormalizeAsync(input, [1, 3, 2, 7, 5]);
        var oneByte = await NormalizeAsync(input, [1]);

        Assert.Equal(irregular.Output, oneByte.Output);
        Assert.Equal(CreateExpectedNormalizedBytes(input), oneByte.Output);
    }

    [Fact]
    public async Task Normalize_ScriptAndCodecPreambles_DoNotEstablishBaseline()
    {
        var input = BuildFlv(
            new Tag(ScriptTagType, 1_000_000, "onMetaData"u8.ToArray()),
            AacConfiguration(0),
            AvcConfiguration(0),
            AudioMedia(4_127_509, 0x11, 0x22),
            VideoMedia(4_127_520, 0x33, 0x44));

        var result = await NormalizeAsync(input, [2, 1, 6]);
        var tags = ParseTags(result.Output);

        Assert.Equal(1_000_000u, tags[0].Timestamp);
        Assert.Equal(0u, tags[1].Timestamp);
        Assert.Equal(0u, tags[2].Timestamp);
        Assert.Equal(0u, tags[3].Timestamp);
        Assert.Equal(11u, tags[4].Timestamp);
        Assert.Equal(CreateExpectedNormalizedBytes(input), result.Output);
    }

    [Fact]
    public async Task Normalize_VideoFirst_UsesSameBaselineForFollowingAudio()
    {
        var input = BuildFlv(
            AvcConfiguration(0),
            AacConfiguration(0),
            VideoMedia(4_127_509, 0x41),
            AudioMedia(4_127_520, 0x42),
            VideoMedia(4_127_530, 0x43));

        var result = await NormalizeAsync(input, [1, 4, 2]);
        var media = ParseTags(result.Output).Where(tag => tag.IsMedia).ToArray();

        Assert.Equal([0u, 11u, 21u], media.Select(tag => tag.Timestamp));
        Assert.Equal(CreateExpectedNormalizedBytes(input), result.Output);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 35_093u)]
    [InlineData(4_127_509u, 4_162_602u)]
    [InlineData(4_294_967_285u, 4_294_967_295u)]
    public async Task Normalize_ZeroAndNearUint32Boundaries_SubtractsWithoutWrapping(
        uint firstTimestamp,
        uint finalTimestamp)
    {
        var input = BuildFlv(
            AudioMedia(firstTimestamp, 0x11),
            VideoMedia(finalTimestamp, 0x22));

        var result = await NormalizeAsync(input, [5, 1, 3]);
        var media = ParseTags(result.Output).Where(tag => tag.IsMedia).ToArray();

        Assert.Equal(0u, media[0].Timestamp);
        Assert.Equal(finalTimestamp - firstTimestamp, media[1].Timestamp);
        Assert.Equal(CreateExpectedNormalizedBytes(input), result.Output);
    }

    [Fact]
    public async Task Normalize_SmallBackwardsTimestampJitter_ClampsMonotonicallyAndContinues()
    {
        var input = BuildFlv(
            AudioMedia(4_127_509, 0x11),
            VideoMedia(4_127_508, 0x22),
            AudioMedia(4_127_520, 0x33));
        var result = await NormalizeAsync(input, [1, 4, 3]);
        var media = ParseTags(result.Output).Where(tag => tag.IsMedia).ToArray();

        Assert.Equal([0u, 0u, 11u], media.Select(tag => tag.Timestamp));
        Assert.Null(result.FaultStage);
        Assert.Equal(RecordingFileWriteCompletionKind.EndOfStream, result.WriteResult.CompletionKind);
    }

    [Fact]
    public async Task Normalize_LargeTimestampReset_FinalizesPreviousCompleteTags()
    {
        var input = BuildFlv(
            AudioMedia(4_127_509, 0x11),
            VideoMedia(0u, 0x22));
        var result = await NormalizeAsync(input, [1, 4, 3]);
        var media = ParseTags(result.Output).Where(tag => tag.IsMedia).ToArray();

        Assert.Single(media);
        Assert.Equal(0u, media[0].Timestamp);
        Assert.Null(result.FaultStage);
        Assert.Equal(RecordingFileWriteCompletionKind.EndOfStream, result.WriteResult.CompletionKind);
    }

    [Theory]
    [MemberData(nameof(TruncatedTrailingTagInputs))]
    public async Task Normalize_TruncatedTrailingTagAfterCompleteTags_FinalizesCompletePrefix(
        string scenario,
        byte[] input,
        byte[] expectedCompletePrefix)
    {
        _ = scenario;
        var result = await NormalizeAsync(input, [1, 2, 3, 5]);

        Assert.Null(result.FaultStage);
        Assert.Equal(RecordingFileWriteCompletionKind.EndOfStream, result.WriteResult.CompletionKind);
        Assert.Equal(CreateExpectedNormalizedBytes(expectedCompletePrefix), result.Output);
    }

    [Theory]
    [MemberData(nameof(MalformedFlvInputs))]
    public async Task Normalize_MalformedOrTruncatedFlv_FailsWithoutFinalizing(
        string scenario,
        byte[] input)
    {
        _ = scenario;
        var result = await NormalizeExpectFailureAsync(input, [1, 2, 3, 5]);

        Assert.Equal(RecordingFailureStage.StreamTransform, result.FaultStage);
        Assert.False(File.Exists(result.Path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Normalize_SourceReadFailure_RemainsSourceReadStage()
    {
        var input = BuildFlv(AudioMedia(42, 0x11));
        var path = GetPath("source-read-failure.flv");
        var source = new FailAfterPrefixReadStream(input, headerLength: 13);
        var transform = new FlvRecordingTimestampNormalizingStream(source);
        RecordingFailureStage? faultStage = null;

        await Assert.ThrowsAsync<IOException>(async () =>
            await RecordingStreamFileWriter.WriteAsync(
                transform,
                path,
                (stage, _) => faultStage = stage,
                CancellationToken.None));
        await transform.DisposeAsync();

        Assert.Equal(RecordingFailureStage.SourceRead, faultStage);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Normalize_StopMidTag_FinalizesOnlyPreviousCompleteTags()
    {
        var header = CreateHeader();
        var configuration = CreateTag(AvcConfiguration(0));
        var firstMediaTag = CreateTag(VideoMedia(4_127_509, 0x55, 0x66));
        var nextTagPrefix = CreateTag(AudioMedia(4_127_520, 0x77))[..6];
        var completePrefix = Concat(header, configuration, firstMediaTag);
        var sourceBytes = Concat(completePrefix, nextTagPrefix);
        var source = new BlockingAfterPrefixReadStream(sourceBytes);
        var transform = new FlvRecordingTimestampNormalizingStream(source);
        var path = GetPath("stopped-mid-tag.flv");
        using var stopping = new CancellationTokenSource();
        var writeTask = RecordingStreamFileWriter.WriteAsync(
                transform,
                path,
                (stage, _) => Assert.Fail($"Unexpected {stage} failure."),
                stopping.Token)
            .AsTask();

        await source.BlockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();
        var writeResult = await writeTask.WaitAsync(TimeSpan.FromSeconds(5));
        await transform.DisposeAsync();

        Assert.Equal(RecordingFileWriteCompletionKind.Stopped, writeResult.CompletionKind);
        Assert.Equal(
            CreateExpectedNormalizedBytes(completePrefix),
            await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task FileSink_FlvPath_AppliesNormalizerBeforeGenericWriter()
    {
        var input = BuildObservedTimelineFixture();
        var path = GetPath("sink-output.flv");
        var candidate = new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(name: "Test", streamExtension: "flv"));
        var sink = new RecordingFileSink(
            new DelegateSourceProvider(_ => new FragmentingReadStream(input, [1, 3, 4])),
            new ConstantPathProvider(path));
        var started = await sink.StartAsync(
            RecordingSinkStartRequest.Manual(candidate),
            CancellationToken.None);
        var session = Assert.IsType<RecordingSinkSession>(started.Session);

        await WaitForStatusAsync(
            sink,
            session,
            RecordingFileSinkSessionStatusKind.Completed);
        Assert.Equal(CreateExpectedAfterFirstIdr(input), await File.ReadAllBytesAsync(path));

        var stopped = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            CancellationToken.None);
        Assert.Equal(RecordingSinkStopResultKind.Stopped, stopped.Kind);
    }

    public static IEnumerable<object[]> MalformedFlvInputs()
    {
        var valid = BuildFlv(
            AacConfiguration(0),
            AudioMedia(100, 0x11, 0x22),
            VideoMedia(110, 0x33, 0x44));
        var tagStart = CreateHeader().Length;

        var badSignature = (byte[])valid.Clone();
        badSignature[0] = (byte)'X';
        yield return ["signature", badSignature];

        var invalidDataOffset = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(invalidDataOffset.AsSpan(5, 4), 8);
        yield return ["data offset", invalidDataOffset];

        var badStreamId = (byte[])valid.Clone();
        badStreamId[tagStart + 8] = 1;
        yield return ["stream id", badStreamId];

        var badPreviousTagSize = (byte[])valid.Clone();
        badPreviousTagSize[^1] ^= 1;
        yield return ["previous tag size", badPreviousTagSize];

        var oversizedDataSize = (byte[])valid.Clone();
        oversizedDataSize[tagStart + 1] = 0xFF;
        oversizedDataSize[tagStart + 2] = 0xFF;
        oversizedDataSize[tagStart + 3] = 0xFF;
        yield return ["declared data size", oversizedDataSize];

        yield return ["truncated header", valid[..7]];
        yield return ["truncated tag header", valid[..(tagStart + 7)]];

        var missingInitialPreviousTagSize = CreateHeader()[..^1];
        yield return ["truncated initial PreviousTagSize", missingInitialPreviousTagSize];
    }

    public static IEnumerable<object[]> TruncatedTrailingTagInputs()
    {
        var completePrefix = BuildFlv(
            AacConfiguration(0),
            AudioMedia(100, 0x11, 0x22));
        var valid = BuildFlv(
            AacConfiguration(0),
            AudioMedia(100, 0x11, 0x22),
            VideoMedia(110, 0x33, 0x44));

        yield return ["truncated tag payload", valid[..^7], completePrefix];
        yield return ["truncated PreviousTagSize", valid[..^1], completePrefix];
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] BuildObservedTimelineFixture()
    {
        return BuildFlv(
            new Tag(ScriptTagType, 0, "onMetaData\0\x05duration\0\0\0\0\0\0\0\0"u8.ToArray()),
            AacConfiguration(0),
            AvcConfiguration(0),
            AudioMedia(4_127_509, 0xAF, 0x01, 0x11, 0x12),
            VideoMedia(4_127_520, 0x17, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x65, 0xAA),
            AudioMedia(4_127_530, 0xAF, 0x01, 0x21, 0x22),
            VideoMedia(4_127_530, 0x27, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x41, 0xBC),
            VideoMedia(4_127_543, 0x27, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x41, 0xBB),
            VideoMedia(4_162_602, 0x27, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x41, 0xCC));
    }

    private static byte[] CreateExpectedAfterFirstIdr(byte[] source)
    {
        var tags = ParseTags(source);
        var idrIndex = tags.FindIndex(tag =>
        {
            if (tag.Type != VideoTagType || !tag.IsMedia || tag.DataSize < 11)
            {
                return false;
            }

            var body = source.AsSpan(tag.Offset + 11, (int)tag.DataSize);
            return (body[0] & 0x0F) == AvcCodecId &&
                   body[1] == AvcNalu &&
                   body[5] == 0 &&
                   body[6] == 0 &&
                   body[7] == 0 &&
                   body[8] == 2 &&
                   (body[9] & 0x1F) == 5;
        });
        Assert.True(idrIndex >= 0, "The fixture must contain an AVC IDR tag.");

        var headerLength = checked(
            (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(5, 4)) + 4);
        var retained = new List<byte>(source.Length);
        retained.AddRange(source.AsSpan(0, headerLength).ToArray());
        for (var index = 0; index < tags.Count; index++)
        {
            if (tags[index].IsMedia && index < idrIndex)
            {
                continue;
            }

            retained.AddRange(source.AsSpan(tags[index].Offset, tags[index].Length).ToArray());
        }

        return CreateExpectedNormalizedBytes(retained.ToArray());
    }

    private async Task<NormalizationResult> NormalizeAsync(
        byte[] input,
        int[] chunkPattern)
    {
        var path = GetPath($"normalized-{Guid.NewGuid():N}.flv");
        var transform = new FlvRecordingTimestampNormalizingStream(
            new FragmentingReadStream(input, chunkPattern));
        RecordingFailureStage? faultStage = null;
        RecordingFileWriteResult writeResult;

        try
        {
            writeResult = await RecordingStreamFileWriter.WriteAsync(
                transform,
                path,
                (stage, _) => faultStage = stage,
                CancellationToken.None);
        }
        finally
        {
            await transform.DisposeAsync();
        }

        return new NormalizationResult(
            await File.ReadAllBytesAsync(path),
            path,
            faultStage,
            writeResult);
    }

    private async Task<NormalizationFailure> NormalizeExpectFailureAsync(
        byte[] input,
        int[] chunkPattern)
    {
        var path = GetPath($"invalid-{Guid.NewGuid():N}.flv");
        var transform = new FlvRecordingTimestampNormalizingStream(
            new FragmentingReadStream(input, chunkPattern));
        RecordingFailureStage? faultStage = null;

        try
        {
            await Assert.ThrowsAsync<RecordingStreamTransformException>(async () =>
                await RecordingStreamFileWriter.WriteAsync(
                    transform,
                    path,
                    (stage, _) => faultStage = stage,
                    CancellationToken.None));
        }
        finally
        {
            await transform.DisposeAsync();
        }

        return new NormalizationFailure(path, faultStage);
    }

    private static byte[] CreateExpectedNormalizedBytes(byte[] source)
    {
        var expected = (byte[])source.Clone();
        var tags = ParseTags(source);
        var baseline = tags.First(tag => tag.IsMedia).Timestamp;

        foreach (var tag in tags.Where(tag => tag.IsMedia))
        {
            WriteTimestamp(expected.AsSpan(tag.Offset, tag.Length), tag.Timestamp - baseline);
        }

        return expected;
    }

    private static List<ParsedTag> ParseTags(byte[] bytes)
    {
        var dataOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)));
        var offset = checked(dataOffset + 4);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataOffset, 4)));
        var tags = new List<ParsedTag>();

        while (offset < bytes.Length)
        {
            Assert.True(bytes.Length - offset >= 11, "A tag header is truncated.");
            var dataSize = ReadUInt24(bytes.AsSpan(offset + 1, 3));
            var length = checked(11 + (int)dataSize + 4);
            Assert.True(bytes.Length - offset >= length, "A tag is truncated.");
            var previousTagSize = BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(offset + 11 + (int)dataSize, 4));
            var streamId = ReadUInt24(bytes.AsSpan(offset + 8, 3));
            Assert.Equal((uint)(11 + dataSize), previousTagSize);
            Assert.Equal(0u, streamId);

            var type = bytes[offset];
            var body = bytes.AsSpan(offset + 11, (int)dataSize);
            var isMedia = type switch
            {
                AudioTagType => IsAudioMedia(body),
                VideoTagType => IsVideoMedia(body),
                ScriptTagType => false,
                _ => throw new InvalidOperationException($"Unexpected tag type {type}."),
            };
            tags.Add(new ParsedTag(
                offset,
                length,
                type,
                dataSize,
                ReadTimestamp(bytes.AsSpan(offset, length)),
                streamId,
                previousTagSize,
                isMedia));
            offset += length;
        }

        Assert.Equal(bytes.Length, offset);
        return tags;
    }

    private static bool IsAudioMedia(ReadOnlySpan<byte> body)
    {
        return body[0] >> 4 != AacSoundFormat || body[1] == AacRaw;
    }

    private static bool IsVideoMedia(ReadOnlySpan<byte> body)
    {
        return (body[0] & 0x0F) != AvcCodecId || body[1] == AvcNalu;
    }

    private static byte[] BuildFlv(params Tag[] tags)
    {
        var records = tags.Select(CreateTag).ToArray();
        return Concat([CreateHeader(), .. records]);
    }

    private static byte[] CreateHeader()
    {
        return [(byte)'F', (byte)'L', (byte)'V', 1, 0x05, 0, 0, 0, 9, 0, 0, 0, 0];
    }

    private static byte[] CreateTag(Tag tag)
    {
        var record = new byte[11 + tag.Body.Length + 4];
        record[0] = tag.Type;
        WriteUInt24(record.AsSpan(1, 3), checked((uint)tag.Body.Length));
        WriteTimestamp(record, tag.Timestamp);
        tag.Body.CopyTo(record, 11);
        BinaryPrimitives.WriteUInt32BigEndian(
            record.AsSpan(11 + tag.Body.Length, 4),
            checked((uint)(11 + tag.Body.Length)));
        return record;
    }

    private static Tag AacConfiguration(uint timestamp) =>
        new(AudioTagType, timestamp, [0xAF, AacSequenceHeader, 0x12, 0x10]);

    private static Tag AvcConfiguration(uint timestamp) =>
        new(
            VideoTagType,
            timestamp,
            [
                0x17, AvcSequenceHeader, 0, 0, 0,
                1, 0x64, 0, 0x1F, 0xFF, 0xE1,
                0, 2, 0x67, 0x64,
                1, 0, 2, 0x68, 0xEE,
            ]);

    private static Tag AudioMedia(uint timestamp, params byte[] body) =>
        new(AudioTagType, timestamp, body);

    private static Tag VideoMedia(uint timestamp, params byte[] body) =>
        new(VideoTagType, timestamp, body);

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

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
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

            if (status?.Kind == RecordingFileSinkSessionStatusKind.Faulted)
            {
                Assert.Fail(
                    $"The file sink faulted at {status.Fault!.Stage}: {status.Fault.ExceptionType}.");
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

    private sealed class FailAfterPrefixReadStream(byte[] bytes, int headerLength) : Stream
    {
        private int offset;

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
            if (offset >= headerLength)
            {
                return ValueTask.FromException<int>(new IOException("Synthetic source fault."));
            }

            var count = Math.Min(buffer.Length, headerLength - offset);
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

    private sealed record NormalizationResult(
        byte[] Output,
        string Path,
        RecordingFailureStage? FaultStage,
        RecordingFileWriteResult WriteResult);

    private sealed record NormalizationFailure(
        string Path,
        RecordingFailureStage? FaultStage);

    private const byte AudioTagType = 8;
    private const byte VideoTagType = 9;
    private const byte ScriptTagType = 18;
    private const byte AvcCodecId = 7;
    private const byte AacSoundFormat = 10;
    private const byte AacSequenceHeader = 0;
    private const byte AacRaw = 1;
    private const byte AvcSequenceHeader = 0;
    private const byte AvcNalu = 1;
}
