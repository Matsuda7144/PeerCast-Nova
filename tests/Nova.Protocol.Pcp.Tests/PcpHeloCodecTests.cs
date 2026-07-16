namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpHeloCodecTests
{
    private static readonly Guid SessionId = Guid.ParseExact(
        "00112233445566778899aabbccddeeff",
        "N");

    [Fact]
    public void ParseHelo_AcceptsObservedPingShapeAndIgnoresUnknownField()
    {
        var helo = PcpAtom.Parent(
            PcpKnownAtomNames.Helo,
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(
                    "PeerCastStation/Test")),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(SessionId)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloPing,
                PcpWireValueCodec.EncodeUInt16(7144)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(1218)),
            PcpAtom.Leaf(PcpAtomName.FromAscii("ext"), [0x01]));

        var result = PcpHeloCodec.ParseHelo(helo);

        Assert.Equal(SessionId, result.SessionId);
        Assert.Equal("PeerCastStation/Test", result.Agent);
        Assert.Equal((uint)1218, result.ServantVersion);
        Assert.Null(result.OpenPort);
        Assert.Equal((ushort)7144, result.PingPort);
    }

    [Fact]
    public void CreateOleh_ProducesMinimalOwnedReply()
    {
        var oleh = PcpHeloCodec.CreateOleh(
            "PeerCastNova/Test",
            SessionId);

        var result = PcpHeloCodec.ParseOleh(oleh);

        Assert.Equal(SessionId, result.SessionId);
        Assert.Equal("PeerCastNova/Test", result.Agent);
        Assert.Equal((uint)1218, result.ServantVersion);
        Assert.Null(result.RemotePort);
        Assert.Null(result.RemoteAddress);
    }

    [Fact]
    public void ParseHelo_RejectsMissingDuplicateParentAndConflictingFields()
    {
        Assert.Throws<InvalidDataException>(
            () => PcpHeloCodec.ParseHelo(
                PcpAtom.Parent(PcpKnownAtomNames.Helo)));

        Assert.Throws<InvalidDataException>(
            () => PcpHeloCodec.ParseHelo(
                PcpAtom.Parent(
                    PcpKnownAtomNames.Helo,
                    SessionLeaf(),
                    SessionLeaf())));

        var parentSessionField = PcpAtom.Parent(
            PcpKnownAtomNames.Helo,
            PcpAtom.Parent(PcpKnownAtomNames.HeloSessionId));

        Assert.Throws<InvalidDataException>(
            () => PcpHeloCodec.ParseHelo(parentSessionField));

        Assert.Throws<InvalidDataException>(
            () => PcpHeloCodec.ParseHelo(
                PcpAtom.Parent(
                    PcpKnownAtomNames.Helo,
                    SessionLeaf(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloPort,
                        PcpWireValueCodec.EncodeUInt16(7144)),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloPing,
                        PcpWireValueCodec.EncodeUInt16(7145)))));
    }

    private static PcpAtom SessionLeaf()
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.HeloSessionId,
            PcpWireValueCodec.EncodeGuid(SessionId));
    }
}
