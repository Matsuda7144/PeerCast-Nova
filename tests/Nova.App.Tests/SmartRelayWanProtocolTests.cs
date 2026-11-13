using System.Buffers;
using System.IO.Pipelines;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPublicHttpAndPcpHandoffPreservesCombinedAndSplitInput(bool split)
    {
        await using var test = await Fixture.Create();
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        await using var client = await new TcpTransportConnector(Transport).ConnectAsync("127.0.0.1", test.Port, test.Token);
        var reader = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var control = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(control, PcpHeloCodec.CreateHelo("PeerCastStation/Test", Guid.NewGuid()));
            var bytes = new PcpRelayRequest { ChannelId = channel.Owner.ChannelId.Value, Host = "127.0.0.1" }.Encode()
                .Concat(control.WrittenSpan.ToArray()).ToArray();
            if (split) foreach (var value in bytes) await client.WriteAsync(new byte[] { value }, test.Token);
            else await client.WriteAsync(bytes, test.Token);
            Assert.Equal(200, (await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 32768, test.Token)).StatusCode);
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            Assert.Equal(channel.Owner.SessionId.Value, PcpHeloCodec.ParseOleh(await atoms.ReadNextAsync(test.Token)).SessionId);
            Assert.Equal(PcpKnownAtomNames.Okay, (await atoms.ReadNextAsync(test.Token)).Name);
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [42]), test.Token);
            var head = await atoms.ReadNextAsync(test.Token);
            Assert.Equal(new byte[] { 42 }, head.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
            var quit = new ArrayBufferWriter<byte>(); PcpAtomTreeCodec.Write(quit, PcpQuitCodec.Create(1000));
            await client.WriteAsync(quit.WrittenMemory, test.Token);
        }
        finally { await reader.CompleteAsync(); }
        await UntilAsync(() => test.Base.State.DownstreamRelays.ActiveRelayCount == 0, test.Token);
        Assert.Equal(0, test.Base.State.NetworkPolicies.Relays.ReservedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedHeloAndOversizeControlCannotKeepReservationsOrStopOtherChannels(bool emptySid)
    {
        await using var test = await Fixture.Create();
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await channel.Owner.PublicReady;
        await using (var bad = await new TcpTransportConnector(Transport).ConnectAsync("127.0.0.1", test.Port, test.Token))
        {
            var reader = PipeReader.Create(bad.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                await bad.WriteAsync(new PcpRelayRequest { ChannelId = channel.Owner.ChannelId.Value, Host = "127.0.0.1" }.Encode(), test.Token);
                Assert.Equal(200, (await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 32768, test.Token)).StatusCode);
                var bytes = new ArrayBufferWriter<byte>();
                PcpAtomTreeCodec.Write(bytes, emptySid ? PcpHeloCodec.CreateHelo("fixture", Guid.Empty) : PcpAtom.Parent(PcpKnownAtomNames.Helo));
                await bad.WriteAsync(bytes.WrittenMemory, test.Token);
                await using var atoms = new PcpAtomPipeMessageReader(reader);
                Assert.Equal(0U, PcpQuitCodec.Parse(await atoms.ReadNextAsync(test.Token)).Code);
                await Assert.ThrowsAsync<PcpProtocolException>(() => atoms.ReadNextAsync(test.Token).AsTask());
            }
            finally { await reader.CompleteAsync(); }
        }
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 0, test.Token);
        Assert.Equal(0, test.Base.State.DownstreamRelays.ActiveRelayCount);
        await using var oversized = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token);
        await oversized.SendBytes(new byte[] { (byte)'h', (byte)'o', (byte)'s', (byte)'t', 1, 16, 0, 0 }, test.Token);
        Assert.Equal(0, await oversized.Read(new byte[1], test.Token));
        await UntilAsync(() => test.Base.State.DownstreamRelays.ActiveRelayCount == 0, test.Token);
        await using var healthy = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token);
        Assert.Equal(200, healthy.Handshake.HttpStatusCode);
        await healthy.Send(PcpAtom.Parent(PcpKnownAtomNames.Host), test.Token);
        await healthy.Quit(test.Token);
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 0, test.Token);
        Assert.Equal(0, channel.Owner.ViewingRuntime!.DownstreamServer.Peers.Count);
        Assert.Equal(0, test.Base.State.NetworkPolicies.Relays.ReservedCount);
    }
}
