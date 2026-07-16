using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAtomTreeCodecTests
{
    [Fact]
    public void Helo_RoundTripsWithNulPaddedChildNames()
    {
        var sessionId = Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N");

        var expected = PcpHeloCodec.CreateHelo(
            "PeerCastNova/Test",
            sessionId,
            openPort: 7144);

        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, expected);

        var sequence = new ReadOnlySequence<byte>(writer.WrittenMemory);
        var reader = new SequenceReader<byte>(sequence);

        var success = PcpAtomTreeCodec.TryRead(
            ref reader,
            new PcpProtocolLimits(),
            out var actual,
            out var error);

        Assert.True(success);
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(PcpKnownAtomNames.Helo, actual.Name);
        Assert.True(actual.IsParent);
        Assert.Equal(4, actual.Children.Count);
        Assert.Equal(
            sessionId,
            PcpWireValueCodec.DecodeGuid(
                actual.FindFirst(PcpKnownAtomNames.HeloSessionId)!.Content.Span));
        Assert.Equal(sequence.Length, reader.Consumed);
    }

    [Fact]
    public void IncompleteTree_DoesNotAdvanceReader()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Helo,
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(1218)));

        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);

        var truncated = writer.WrittenMemory[..^1];
        var sequence = new ReadOnlySequence<byte>(truncated);
        var reader = new SequenceReader<byte>(sequence);

        var success = PcpAtomTreeCodec.TryRead(
            ref reader,
            new PcpProtocolLimits(),
            out _,
            out var error);

        Assert.False(success);
        Assert.Equal(PcpParseError.NeedMoreData, error);
        Assert.Equal(0, reader.Consumed);
    }
}
