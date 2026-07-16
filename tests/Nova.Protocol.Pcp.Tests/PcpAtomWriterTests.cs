using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomWriterTests
{
    [Fact]
    public void WriteLeaf_ProducesHeaderAndPayload()
    {
        var writer = new ArrayBufferWriter<byte>();

        PcpAtomWriter.WriteLeaf(
            writer,
            PcpAtomName.FromAscii("data"),
            [0x10, 0x20, 0x30]);

        var sequence = new ReadOnlySequence<byte>(writer.WrittenMemory);
        var reader = new SequenceReader<byte>(sequence);

        var success = PcpAtomSequenceCodec.TryReadHeader(
            ref reader,
            new PcpProtocolLimits(),
            out var header,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpParseError.None, error);
        Assert.False(header.IsParent);
        Assert.Equal(3, header.Value);

        Span<byte> payload = stackalloc byte[3];
        Assert.True(reader.TryCopyTo(payload));
        ReadOnlySpan<byte> expected = [0x10, 0x20, 0x30];
        Assert.True(payload.SequenceEqual(expected));
    }

    [Fact]
    public void WriteLeaf_RejectsPayloadOverLimit()
    {
        var writer = new ArrayBufferWriter<byte>();
        var limits = new PcpProtocolLimits
        {
            MaxContentLength = 2,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PcpAtomWriter.WriteLeaf(
                writer,
                PcpAtomName.FromAscii("data"),
                [0x01, 0x02, 0x03],
                limits));
    }

    [Fact]
    public void WriteLeafHeader_WritesNoPayloadAndEnforcesLengthLimit()
    {
        var writer = new ArrayBufferWriter<byte>();
        var limits = new PcpProtocolLimits
        {
            MaxContentLength = 3,
        };

        PcpAtomWriter.WriteLeafHeader(
            writer,
            PcpKnownAtomNames.Data,
            contentLength: 3,
            limits);

        Assert.Equal(PcpAtomCodec.HeaderSize, writer.WrittenCount);

        var sequence = new ReadOnlySequence<byte>(writer.WrittenMemory);
        var reader = new SequenceReader<byte>(sequence);
        Assert.True(PcpAtomSequenceCodec.TryReadHeader(
            ref reader,
            limits,
            out var header,
            out var error));
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(PcpKnownAtomNames.Data, header.Name);
        Assert.False(header.IsParent);
        Assert.Equal(3, header.Value);
        Assert.Equal(PcpAtomCodec.HeaderSize, reader.Consumed);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PcpAtomWriter.WriteLeafHeader(
                writer,
                PcpKnownAtomNames.Data,
                contentLength: 4,
                limits));
    }
}
