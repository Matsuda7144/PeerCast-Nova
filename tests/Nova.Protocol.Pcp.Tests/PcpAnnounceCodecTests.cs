using System.Buffers;
using System.Net;

namespace PeerCast.Nova.Protocol.Pcp.Tests;

public sealed class PcpAnnounceCodecTests
{
    private static readonly Guid Sid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid Bcid = Guid.Parse("00224466-88aa-ccdd-eeff-112233445566");
    private static readonly Guid Cid = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");

    [Fact]
    public void HeloHasDistinctBroadcastIdentityAndNetworkOrderGids()
    {
        var helo = PcpAnnounceCodec.CreateHelo("Nova test", Sid, Bcid, 7144);
        Assert.Equal(Sid, PcpHeloCodec.ParseHelo(helo).SessionId);
        Assert.Equal((ushort)7144, PcpHeloCodec.ParseHelo(helo).OpenPort);
        Assert.Equal(Convert.FromHexString("0022446688aaccddeeff112233445566"), Field(helo, "bcid").Content.ToArray());
        Assert.Throws<ArgumentException>(() => PcpAnnounceCodec.CreateHelo("test", Sid, Sid, 7144));
        Assert.Throws<ArgumentException>(() => PcpAnnounceCodec.CreateHelo("test", Guid.Empty, Bcid, 7144));
    }

    [Theory]
    [InlineData(true, true, true, 0x17)]
    [InlineData(false, false, false, 0x01)]
    public void BroadcastHasRootEnvelopeChanHostAndNoMedia(bool receiving, bool relay, bool direct, byte flags)
    {
        var atom = PcpAnnounceCodec.CreateBroadcast(Cid, Sid, Bcid,
            PcpAtom.Parent(PcpKnownAtomNames.ChannelInfo, PcpAtom.Leaf(PcpKnownAtomNames.Name, PcpWireValueCodec.EncodeNullTerminatedUtf8("配信"))),
            PcpAtom.Parent(PcpKnownAtomNames.ChannelTrack), IPAddress.Parse("192.0.2.42"), 7144,
            3, 4, 123, receiving, relay, direct);
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);
        Assert.Equal("bcst", System.Text.Encoding.ASCII.GetString(writer.WrittenSpan[..4]));
        Assert.Equal(new byte[] { 8, 0, 0, 128 }, writer.WrittenSpan.Slice(4, 4).ToArray());
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(writer.WrittenMemory));
        Assert.True(PcpAtomTreeCodec.TryRead(ref reader, new(), out var decoded, out _));
        Assert.Equal((byte)1, Field(decoded, "grp").Content.Span[0]);
        Assert.Equal((byte)1, Field(decoded, "ttl").Content.Span[0]);
        Assert.Equal((byte)0, Field(decoded, "hops").Content.Span[0]);
        Assert.Equal(Sid, PcpWireValueCodec.DecodeGuid(Field(decoded, "from").Content.Span));
        Assert.Equal(Cid, PcpWireValueCodec.DecodeGuid(Field(decoded, "cid").Content.Span));
        Assert.Equal((uint)1218, PcpWireValueCodec.DecodeUInt32(Field(decoded, "vers").Content.Span));
        var chan = Field(decoded, "chan");
        Assert.Equal(Cid, PcpWireValueCodec.DecodeGuid(Field(chan, "id").Content.Span));
        Assert.Equal(Bcid, PcpWireValueCodec.DecodeGuid(Field(chan, "bcid").Content.Span));
        var host = Field(decoded, "host");
        Assert.Equal(Sid, PcpWireValueCodec.DecodeGuid(Field(host, "id").Content.Span));
        Assert.Equal(new byte[] { 42, 2, 0, 192 }, Field(host, "ip").Content.ToArray());
        Assert.Equal(new byte[] { 0xe8, 0x1b }, Field(host, "port").Content.ToArray());
        Assert.Equal((uint)3, PcpWireValueCodec.DecodeUInt32(Field(host, "numl").Content.Span));
        Assert.Equal((uint)4, PcpWireValueCodec.DecodeUInt32(Field(host, "numr").Content.Span));
        Assert.Equal((uint)123, PcpWireValueCodec.DecodeUInt32(Field(host, "uptm").Content.Span));
        Assert.Equal(flags, Field(host, "flg1").Content.Span[0]);
        Assert.DoesNotContain(Flatten(decoded), a => a.Name == PcpKnownAtomNames.ChannelPacket || a.Name == PcpKnownAtomNames.Data);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("bad-size")]
    [InlineData("bad-disable")]
    public void InvalidOlehIsRejected(string kind)
    {
        var sid = PcpAtom.Leaf(PcpKnownAtomNames.HeloSessionId, PcpWireValueCodec.EncodeGuid(kind == "empty" ? Guid.Empty : Sid));
        PcpAtom[] fields = kind switch
        {
            "missing" => [],
            "duplicate" => [sid, sid],
            "parent" => [PcpAtom.Parent(PcpKnownAtomNames.HeloSessionId)],
            "bad-size" => [PcpAtom.Leaf(PcpKnownAtomNames.HeloSessionId, new byte[15])],
            "bad-disable" => [sid, PcpAtom.Leaf(PcpAnnounceCodec.Disable, new byte[1])],
            _ => [sid],
        };
        Assert.Throws<InvalidDataException>(() => PcpAnnounceCodec.ParseOleh(PcpAtom.Parent(PcpKnownAtomNames.Oleh, fields)));
    }

    [Fact]
    public void BanAndQuitHaveTheirOwnWireContracts()
    {
        var oleh = PcpHeloCodec.CreateOleh("tracker", Sid);
        Assert.Throws<PcpAnnounceBannedException>(() => PcpAnnounceCodec.ParseOleh(PcpAtom.Parent(oleh.Name,
            oleh.Children.Concat(new[] { PcpAtom.Leaf(PcpAnnounceCodec.Disable, PcpWireValueCodec.EncodeUInt32(1)) }).ToArray())));
        Assert.Equal(new byte[] { 0xe8, 0x03, 0, 0 }, PcpAnnounceCodec.CreateQuit().Content.ToArray());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PushAndCoutRequestsRoundTripAndRejectMalformedFields(bool useCoutName, bool isRelay)
    {
        var ip = IPAddress.Parse("198.41.0.10");
        var atom = PcpAnnounceCodec.CreatePushRequest(ip, 17144, Cid, isRelay: isRelay, useCoutName: useCoutName);
        Assert.Equal(useCoutName ? PcpKnownAtomNames.Cout : PcpKnownAtomNames.Push, atom.Name);

        Assert.True(PcpAnnounceCodec.TryParsePushRequest(atom, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(ip, parsed.Address);
        Assert.Equal((ushort)17144, parsed.Port);
        Assert.Equal(Cid, parsed.ChannelId);
        Assert.Equal(isRelay, parsed.IsRelay);

        // cid フォールバックおよび rly 省略時の既定値 (true) を確認
        var cidOnlyCout = PcpAtom.Parent(
            PcpKnownAtomNames.Cout,
            PcpAtom.Leaf(PcpKnownAtomNames.PushIp, PcpWireValueCodec.EncodeLegacyIpv4(ip)),
            PcpAtom.Leaf(PcpKnownAtomNames.PushPort, PcpWireValueCodec.EncodeUInt16(7144)),
            PcpAtom.Leaf(PcpKnownAtomNames.BroadcastChannelId, PcpWireValueCodec.EncodeGuid(Cid)));
        Assert.True(PcpAnnounceCodec.TryParsePushRequest(cidOnlyCout, out var fallbackParsed));
        Assert.Equal(new PcpPushRelayRequest(ip, 7144, Cid, true), fallbackParsed);

        // 不正入力（チャンネル不一致・ポート0・不正IP・不明アトム名）は拒否される
        var mismatchedCid = PcpAtom.Parent(
            PcpKnownAtomNames.Cout,
            PcpAtom.Leaf(PcpKnownAtomNames.PushIp, PcpWireValueCodec.EncodeLegacyIpv4(ip)),
            PcpAtom.Leaf(PcpKnownAtomNames.PushPort, PcpWireValueCodec.EncodeUInt16(7144)),
            PcpAtom.Leaf(PcpKnownAtomNames.PushChannel, PcpWireValueCodec.EncodeGuid(Cid)),
            PcpAtom.Leaf(PcpKnownAtomNames.BroadcastChannelId, PcpWireValueCodec.EncodeGuid(Sid)));
        Assert.False(PcpAnnounceCodec.TryParsePushRequest(mismatchedCid, out _));
        Assert.False(PcpAnnounceCodec.TryParsePushRequest(PcpAtom.Parent(PcpKnownAtomNames.Helo), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcpAnnounceCodec.CreatePushRequest(ip, 0, Cid));
        Assert.Throws<ArgumentException>(() => PcpAnnounceCodec.CreatePushRequest(IPAddress.Any, 7144, Cid));
        Assert.Throws<ArgumentException>(() => PcpAnnounceCodec.CreatePushRequest(ip, 7144, Guid.Empty));
    }

    [Fact]
    public void EncodeGivRequestFormatsStandardPeerCastHeader()
    {
        var bytes = PcpAnnounceCodec.EncodeGivRequest(Cid);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Equal($"GIV /{Cid:N}\r\n\r\n", text);
        Assert.Throws<ArgumentException>(() => PcpAnnounceCodec.EncodeGivRequest(Guid.Empty));
    }

    [Fact]
    public void RootControlParsesUpdateIntervalImmediateUpdateAndNextRetry()
    {
        var root = PcpAtom.Parent(
            PcpKnownAtomNames.Root,
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdateInterval, PcpWireValueCodec.EncodeUInt32(60)),
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdate, []),
            PcpAtom.Leaf(PcpKnownAtomNames.RootNext, PcpWireValueCodec.EncodeUInt32(120)),
            PcpAtom.Leaf(PcpAtomName.FromAscii("mesg"), PcpWireValueCodec.EncodeNullTerminatedUtf8("ok")));

        Assert.True(PcpAnnounceCodec.TryParseRootControl(root, out var info));
        Assert.NotNull(info);
        Assert.Equal(TimeSpan.FromSeconds(60), info.UpdateInterval);
        Assert.True(info.RequestImmediateUpdate);
        Assert.Equal(TimeSpan.FromSeconds(120), info.NextRetryInterval);

        // クランプ範囲確認（uint: 最小10秒・最大600秒、next: 最小5秒・最大3600秒）
        var clamped = PcpAtom.Parent(
            PcpKnownAtomNames.Root,
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdateInterval, PcpWireValueCodec.EncodeUInt32(1)),
            PcpAtom.Leaf(PcpKnownAtomNames.RootNext, PcpWireValueCodec.EncodeUInt32(99999)));
        Assert.True(PcpAnnounceCodec.TryParseRootControl(clamped, out var clampedInfo));
        Assert.Equal(TimeSpan.FromSeconds(10), clampedInfo!.UpdateInterval);
        Assert.False(clampedInfo.RequestImmediateUpdate);
        Assert.Equal(TimeSpan.FromSeconds(3600), clampedInfo.NextRetryInterval);

        // 不正長さ uint は拒否
        var invalid = PcpAtom.Parent(
            PcpKnownAtomNames.Root,
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdateInterval, new byte[] { 1, 2 }));
        Assert.False(PcpAnnounceCodec.TryParseRootControl(invalid, out _));
    }

    private static PcpAtom Field(PcpAtom atom, string name) => Assert.Single(atom.Children, a => a.Name == PcpAtomName.FromAscii(name));
    private static IEnumerable<PcpAtom> Flatten(PcpAtom atom) => new[] { atom }.Concat(atom.Children.SelectMany(Flatten));
}
