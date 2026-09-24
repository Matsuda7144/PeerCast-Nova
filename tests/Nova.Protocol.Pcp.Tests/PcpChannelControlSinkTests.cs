using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpChannelControlSinkTests
{
    private static readonly Guid ChannelId = Guid.ParseExact(
        "531dc8dfc7fb42928ac2c0a626517a87",
        "N");

    [Fact]
    public void ObservedShape_DecodesControlWithoutRetainingPacketData()
    {
        var packetData = new byte[128];
        packetData.AsSpan().Fill(0xA5);

        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelId)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Type,
                    PcpWireValueCodec.EncodeUInt32(1)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.ChannelPacketContinuation,
                    [0x07]),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Position,
                    PcpWireValueCodec.EncodeUInt32(4096)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Data,
                    packetData)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelInfo,
                TextLeaf(PcpKnownAtomNames.Type, "TEST"),
                TextLeaf(PcpKnownAtomNames.StreamType, "video/test"),
                TextLeaf(PcpKnownAtomNames.Name, "Synthetic channel"),
                TextLeaf(PcpKnownAtomNames.Genre, "Test"),
                TextLeaf(PcpKnownAtomNames.Description, "Synthetic fixture"),
                TextLeaf(PcpKnownAtomNames.Comment, "No captured content"),
                TextLeaf(PcpKnownAtomNames.Url, "https://example.invalid/"),
                TextLeaf(PcpKnownAtomNames.StreamExtension, ".test"),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Bitrate,
                    PcpWireValueCodec.EncodeUInt32(512)),
                TextLeaf(PcpAtomName.FromAscii("unkn"), "ignored")),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelTrack,
                TextLeaf(PcpKnownAtomNames.Title, "Track"),
                TextLeaf(PcpKnownAtomNames.Genre, "Genre"),
                TextLeaf(PcpKnownAtomNames.Album, "Album"),
                TextLeaf(PcpKnownAtomNames.Creator, "Creator"),
                TextLeaf(
                    PcpKnownAtomNames.Url,
                    "https://example.invalid/track")));

        var message = DecodeSingle(atom);

        Assert.Equal(ChannelId, message.ChannelId);

        var packet = Assert.IsType<PcpChannelPacketHeader>(message.Packet);
        Assert.Equal((uint)1, packet.Type);
        Assert.Equal((uint)4096, packet.Position);
        Assert.Equal(packetData.Length, packet.DataLength);
        Assert.Equal((byte)0x07, packet.Continuation);
        Assert.True(packet.HasContinuation);

        var info = Assert.IsType<PcpChannelInfo>(message.Info);
        Assert.Equal("TEST", info.ContentType);
        Assert.Equal("video/test", info.StreamType);
        Assert.Equal("Synthetic channel", info.Name);
        Assert.Equal("Test", info.Genre);
        Assert.Equal("Synthetic fixture", info.Description);
        Assert.Equal("No captured content", info.Comment);
        Assert.Equal("https://example.invalid/", info.Url);
        Assert.Equal(".test", info.StreamExtension);
        Assert.Equal((uint)512, info.BitrateKbps);

        var track = Assert.IsType<PcpChannelTrack>(message.Track);
        Assert.Equal("Track", track.Title);
        Assert.Equal("Genre", track.Genre);
        Assert.Equal("Album", track.Album);
        Assert.Equal("Creator", track.Creator);
        Assert.Equal("https://example.invalid/track", track.Url);

        Assert.DoesNotContain(
            typeof(PcpChannelPacketHeader).GetProperties(),
            property => property.PropertyType == typeof(byte[]) ||
                property.PropertyType == typeof(ReadOnlyMemory<byte>));
    }

    [Fact]
    public void MissingChannelId_IsRejected()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelInfo,
                TextLeaf(PcpKnownAtomNames.Name, "No ID")));

        Assert.Throws<InvalidDataException>(() => DecodeSingle(atom));
    }

    [Fact]
    public void OversizedText_IsRejectedBeforeMetadataCopy()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelId)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelInfo,
                TextLeaf(PcpKnownAtomNames.Name, "too long")));

        var limits = new PcpChannelControlLimits
        {
            MaxTextBytes = 4,
        };

        Assert.Throws<InvalidDataException>(
            () => DecodeSingle(atom, limits));
    }

    [Fact]
    public void DuplicateKnownField_IsRejected()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelId)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelInfo,
                TextLeaf(PcpKnownAtomNames.Name, "First"),
                TextLeaf(PcpKnownAtomNames.Name, "Second")));

        Assert.Throws<InvalidDataException>(() => DecodeSingle(atom));
    }

    [Fact]
    public void ContinuationField_RequiresSingleByte()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelId)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Type,
                    PcpWireValueCodec.EncodeUInt32(1)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Position,
                    PcpWireValueCodec.EncodeUInt32(4096)),
                PcpAtom.Leaf(PcpKnownAtomNames.ChannelPacketContinuation, [0x07, 0x08]),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, [0x00])));

        Assert.Throws<InvalidDataException>(() => DecodeSingle(atom));
    }

    [Fact]
    public void KnownSectionEncodedAsLeaf_IsRejected()
    {
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelId)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelInfo,
                []));

        Assert.Throws<InvalidDataException>(() => DecodeSingle(atom));
    }

    private static PcpChannelControlMessage DecodeSingle(
        PcpAtom atom,
        PcpChannelControlLimits? limits = null)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);

        var messages = new List<PcpChannelControlMessage>();
        var sink = new PcpChannelControlSink(messages.Add, limits);
        var decoder = new PcpAtomDecoder();

        var result = decoder.Decode(
            new ReadOnlySequence<byte>(writer.WrittenMemory),
            isFinalBlock: true,
            sink);

        Assert.Equal(PcpDecodeStatus.Completed, result.Status);
        return Assert.Single(messages);
    }

    private static PcpAtom TextLeaf(PcpAtomName name, string value)
    {
        return PcpAtom.Leaf(
            name,
            PcpWireValueCodec.EncodeNullTerminatedUtf8(value));
    }
}
