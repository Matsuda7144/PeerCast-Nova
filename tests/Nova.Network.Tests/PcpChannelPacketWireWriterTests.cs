using System.Buffers;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpChannelPacketWireWriterTests
{
    [Fact]
    public void WritePrefix_DeclaresPayloadWithoutCopyingItIntoPrefix()
    {
        var channelId = Guid.ParseExact(
            "531dc8dfc7fb42928ac2c0a626517a87",
            "N");
        byte[] payload = [0x10, 0x20, 0x30];
        var packet = new PcpRelayPacket(
            sequence: 7,
            type: 1,
            wirePosition: 1234,
            streamPosition: 1234,
            payload,
            discontinuity: null);
        var prefix = new byte[PcpChannelPacketWireWriter.PrefixLength];

        var written = PcpChannelPacketWireWriter.WritePrefix(
            prefix,
            channelId,
            packet,
            maxPacketBytes: payload.Length);

        Assert.Equal(PcpChannelPacketWireWriter.PrefixLength, written);

        var encoded = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(encoded, 0);
        payload.CopyTo(encoded, prefix.Length);
        var reader = new SequenceReader<byte>(
            new ReadOnlySequence<byte>(encoded));

        Assert.True(PcpAtomTreeCodec.TryRead(
            ref reader,
            new PcpProtocolLimits(),
            out var channel,
            out var error));
        Assert.Equal(PcpParseError.None, error);
        Assert.Equal(PcpKnownAtomNames.Channel, channel.Name);
        Assert.Equal(2, channel.Children.Count);
        Assert.Equal(
            channelId,
            PcpWireValueCodec.DecodeGuid(
                channel.FindFirst(PcpKnownAtomNames.ChannelId)!.Content.Span));

        var packetAtom = channel.FindFirst(
            PcpKnownAtomNames.ChannelPacket)!;
        Assert.Equal(
            1U,
            PcpWireValueCodec.DecodeUInt32(
                packetAtom.FindFirst(PcpKnownAtomNames.Type)!.Content.Span));
        Assert.Equal(
            1234U,
            PcpWireValueCodec.DecodeUInt32(
                packetAtom.FindFirst(PcpKnownAtomNames.Position)!.Content.Span));
        Assert.Equal(
            payload,
            packetAtom.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
        Assert.Equal(encoded.Length, reader.Consumed);
    }

    [Fact]
    public void WritePrefix_RejectsOversizedPacketBeforeWriting()
    {
        var packet = new PcpRelayPacket(
            sequence: 0,
            type: 1,
            wirePosition: 0,
            streamPosition: 0,
            payload: new byte[2],
            discontinuity: null);
        var prefix = new byte[PcpChannelPacketWireWriter.PrefixLength];

        Assert.Throws<InvalidDataException>(
            () => PcpChannelPacketWireWriter.WritePrefix(
                prefix,
                Guid.NewGuid(),
                packet,
                maxPacketBytes: 1));
    }
}
