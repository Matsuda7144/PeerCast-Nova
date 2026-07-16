using System.Buffers;
using System.IO.Pipelines;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomPipeMessageReaderTests
{
    [Fact]
    public async Task ReadNext_FollowingDataExceedsLimit_ReturnsFirstAtom()
    {
        var pipe = new Pipe();
        var encoded = new ArrayBufferWriter<byte>();

        PcpAtomTreeCodec.Write(
            encoded,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Okay,
                PcpWireValueCodec.EncodeUInt32(1)));

        PcpAtomTreeCodec.Write(
            encoded,
            PcpAtom.Leaf(
                PcpAtomName.FromAscii("data"),
                new byte[64]));

        await pipe.Writer.WriteAsync(encoded.WrittenMemory);
        await pipe.Writer.CompleteAsync();

        await using var reader = new PcpAtomPipeMessageReader(
            pipe.Reader,
            maxBufferedBytes: 16,
            completeReader: true);

        var atom = await reader.ReadNextAsync();

        Assert.Equal(PcpKnownAtomNames.Okay, atom.Name);
        Assert.Equal(
            (uint)1,
            PcpWireValueCodec.DecodeUInt32(atom.Content.Span));
    }

    [Fact]
    public async Task ReadNext_FirstAtomExceedsLimit_RejectsBeforeContentCopy()
    {
        var pipe = new Pipe();
        var encoded = new ArrayBufferWriter<byte>();

        PcpAtomTreeCodec.Write(
            encoded,
            PcpAtom.Leaf(
                PcpAtomName.FromAscii("data"),
                new byte[9]));

        await pipe.Writer.WriteAsync(encoded.WrittenMemory);
        await pipe.Writer.CompleteAsync();

        await using var reader = new PcpAtomPipeMessageReader(
            pipe.Reader,
            maxBufferedBytes: 16,
            completeReader: true);

        var exception = await Assert.ThrowsAsync<PcpProtocolException>(
            async () => await reader.ReadNextAsync());

        Assert.Equal(
            PcpParseError.ContentLengthLimitExceeded,
            exception.Error);
    }
}
