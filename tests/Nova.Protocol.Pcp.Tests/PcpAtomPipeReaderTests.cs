using System.Buffers;
using System.IO.Pipelines;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomPipeReaderTests
{
    [Fact]
    public async Task NestedFixture_ParsesWhenWrittenOneByteAtATime()
    {
        var fixture = await File.ReadAllBytesAsync(GetFixturePath("synthetic-nested.bin"));
        var sink = new CollectingPcpAtomSink();

        await ParseInChunksAsync(fixture, chunkSize: 1, sink);

        Assert.Equal(
            [
                "start:root:depth=0:children=2",
                "leaf:one1:depth=1:payload=010203",
                "start:nest:depth=1:children=1",
                "leaf:zero:depth=2:payload=",
                "end:nest:depth=1",
                "end:root:depth=0",
            ],
            sink.Events);
    }

    [Fact]
    public async Task MultipleTopLevelAtoms_ParseFromSingleFlush()
    {
        var writer = new ArrayBufferWriter<byte>();

        PcpAtomWriter.WriteLeaf(
            writer,
            PcpAtomName.FromAscii("one1"),
            [0x01]);

        PcpAtomWriter.WriteLeaf(
            writer,
            PcpAtomName.FromAscii("two2"),
            [0x02, 0x03]);

        var sink = new CollectingPcpAtomSink();

        await ParseInChunksAsync(writer.WrittenMemory.ToArray(), int.MaxValue, sink);

        Assert.Equal(
            [
                "leaf:one1:depth=0:payload=01",
                "leaf:two2:depth=0:payload=0203",
            ],
            sink.Events);
    }

    [Fact]
    public async Task TruncatedLeaf_ThrowsUnexpectedEndOfStream()
    {
        var writer = new ArrayBufferWriter<byte>();

        {
            Span<byte> header = writer.GetSpan(PcpAtomCodec.HeaderSize);
            PcpAtomCodec.WriteHeader(
                header,
                new PcpAtomHeader(
                    PcpAtomName.FromAscii("data"),
                    isParent: false,
                    value: 4));

            writer.Advance(PcpAtomCodec.HeaderSize);
        }

        {
            Span<byte> payload = writer.GetSpan(2);
            payload[0] = 0xAA;
            payload[1] = 0xBB;
            writer.Advance(2);
        }

        var exception = await Assert.ThrowsAsync<PcpProtocolException>(
            () => ParseInChunksAsync(
                writer.WrittenMemory.ToArray(),
                chunkSize: 3,
                new CollectingPcpAtomSink()));

        Assert.Equal(PcpParseError.UnexpectedEndOfStream, exception.Error);
        Assert.Equal(0, exception.ByteOffset);
    }

    [Fact]
    public async Task OpenParentAtEof_ThrowsUnexpectedEndOfStream()
    {
        var writer = new ArrayBufferWriter<byte>();

        PcpAtomWriter.WriteParentHeader(
            writer,
            PcpAtomName.FromAscii("root"),
            childCount: 1);

        var exception = await Assert.ThrowsAsync<PcpProtocolException>(
            () => ParseInChunksAsync(
                writer.WrittenMemory.ToArray(),
                chunkSize: 2,
                new CollectingPcpAtomSink()));

        Assert.Equal(PcpParseError.UnexpectedEndOfStream, exception.Error);
        Assert.Equal(PcpAtomCodec.HeaderSize, exception.ByteOffset);
    }

    [Fact]
    public async Task ExcessiveNesting_IsRejected()
    {
        var limits = new PcpProtocolLimits
        {
            MaxNestingDepth = 2,
        };

        var writer = new ArrayBufferWriter<byte>();

        PcpAtomWriter.WriteParentHeader(
            writer,
            PcpAtomName.FromAscii("par1"),
            childCount: 1,
            limits);

        PcpAtomWriter.WriteParentHeader(
            writer,
            PcpAtomName.FromAscii("par2"),
            childCount: 1,
            limits);

        PcpAtomWriter.WriteParentHeader(
            writer,
            PcpAtomName.FromAscii("par3"),
            childCount: 0,
            limits: new PcpProtocolLimits
            {
                MaxNestingDepth = 3,
            });

        var exception = await Assert.ThrowsAsync<PcpProtocolException>(
            () => ParseInChunksAsync(
                writer.WrittenMemory.ToArray(),
                chunkSize: 5,
                new CollectingPcpAtomSink(),
                limits));

        Assert.Equal(PcpParseError.NestingDepthLimitExceeded, exception.Error);
        Assert.Equal(PcpAtomCodec.HeaderSize * 2, exception.ByteOffset);
    }

    [Fact]
    public async Task OversizedLeaf_IsRejectedFromHeader()
    {
        var limits = new PcpProtocolLimits
        {
            MaxContentLength = 4,
        };

        var bytes = new byte[PcpAtomCodec.HeaderSize];

        PcpAtomCodec.WriteHeader(
            bytes,
            new PcpAtomHeader(
                PcpAtomName.FromAscii("data"),
                isParent: false,
                value: 5));

        var exception = await Assert.ThrowsAsync<PcpProtocolException>(
            () => ParseInChunksAsync(
                bytes,
                chunkSize: bytes.Length,
                new CollectingPcpAtomSink(),
                limits));

        Assert.Equal(PcpParseError.ContentLengthLimitExceeded, exception.Error);
        Assert.Equal(0, exception.ByteOffset);
    }

    [Fact]
    public async Task EmptyCompletedPipe_CompletesCleanly()
    {
        var sink = new CollectingPcpAtomSink();

        await ParseInChunksAsync([], chunkSize: 1, sink);

        Assert.Empty(sink.Events);
    }

    [Fact]
    public void Decoder_StopAfterTopLevelYieldsBeforeFollowingBufferedAtom()
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomWriter.WriteLeaf(
            writer,
            PcpAtomName.FromAscii("one1"),
            [0x01]);
        PcpAtomWriter.WriteLeaf(
            writer,
            PcpAtomName.FromAscii("two2"),
            [0x02]);
        var input = new ReadOnlySequence<byte>(writer.WrittenMemory);
        var sink = new CollectingPcpAtomSink();
        var decoder = new PcpAtomDecoder();

        var first = decoder.Decode(
            input,
            isFinalBlock: true,
            sink,
            stopAfterTopLevelAtom: true);

        Assert.Equal(PcpDecodeStatus.TopLevelAtomCompleted, first.Status);
        Assert.Equal(0, input.Slice(first.Consumed, first.Examined).Length);
        Assert.Single(sink.Events);
        Assert.StartsWith("leaf:one1", sink.Events[0]);

        var remaining = input.Slice(first.Consumed);
        var second = decoder.Decode(
            remaining,
            isFinalBlock: true,
            sink,
            stopAfterTopLevelAtom: true);

        Assert.Equal(PcpDecodeStatus.TopLevelAtomCompleted, second.Status);
        Assert.Equal(2, sink.Events.Count);
        Assert.StartsWith("leaf:two2", sink.Events[1]);

        var completed = decoder.Decode(
            remaining.Slice(second.Consumed),
            isFinalBlock: true,
            sink,
            stopAfterTopLevelAtom: true);

        Assert.Equal(PcpDecodeStatus.Completed, completed.Status);
    }

    private static async Task ParseInChunksAsync(
        byte[] bytes,
        int chunkSize,
        IPcpAtomSink sink,
        PcpProtocolLimits? limits = null)
    {
        var pipe = new Pipe();
        var reader = new PcpAtomPipeReader(pipe.Reader, limits);
        var readTask = reader.ReadToEndAsync(sink);

        try
        {
            var effectiveChunkSize = Math.Max(1, chunkSize);

            for (var offset = 0; offset < bytes.Length;)
            {
                var count = Math.Min(effectiveChunkSize, bytes.Length - offset);
                var destination = pipe.Writer.GetSpan(count);

                bytes.AsSpan(offset, count).CopyTo(destination);
                pipe.Writer.Advance(count);

                var flushResult = await pipe.Writer.FlushAsync();

                if (flushResult.IsCompleted)
                {
                    break;
                }

                offset += count;
            }
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
        }

        await readTask;
    }

    private static string GetFixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }
}
