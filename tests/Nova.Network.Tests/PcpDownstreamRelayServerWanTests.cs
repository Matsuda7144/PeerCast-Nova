using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    private static PcpWanIngressOptions FixtureWan => new() { SourceAllowed = address => address is not null && IPAddress.IsLoopback(address) };
    private static PcpDownstreamRelayServer CreateWanServer(PcpRelayFanout fanout, WanAdmission admission,
        PcpWanIngressOptions? limits = null, IPcpDownstreamPacketWriter? writer = null, PcpDownstreamChildOptions? childOptions = null) =>
        new(new(IPAddress.Loopback, 0), fanout, ChannelId, LocalSessionId, childOptions: childOptions,
            admission: admission, initialMetadata: new(name: "WAN fixture", contentType: "FLV"), wanOptions: limits ?? FixtureWan)
        { PacketWriterFactory = _ => writer };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WanPingUsesOneListenerAndCurrentSidWithoutMediaOrRelayReservations(bool byteByByte)
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission(0);
        await using var server = CreateWanServer(fanout, admission);
        var run = server.RunAsync(deadline.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        var helo = PcpAtom.Parent(PcpKnownAtomNames.Helo, PcpAtom.Leaf(PcpKnownAtomNames.HeloSessionId, PcpWireValueCodec.EncodeGuid(Guid.NewGuid())));
        var request = PingRequest(helo);
        if (byteByByte)
            foreach (var value in request) await client.WriteAsync(new byte[] { value }, deadline.Token);
        else await client.WriteAsync(request, deadline.Token);
        var pipe = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await using var atoms = new PcpAtomPipeMessageReader(pipe);
        var oleh = await atoms.ReadNextAsync(deadline.Token);
        Assert.Equal(LocalSessionId, PcpHeloCodec.ParseOleh(oleh).SessionId);
        await Assert.ThrowsAsync<PcpProtocolException>(() => atoms.ReadNextAsync(deadline.Token).AsTask()); // EOF, no CHAN/HEAD/DATA/OK.
        await pipe.CompleteAsync();
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(1, server.Snapshot.WanIngress!.PingResponses);
        Assert.Equal(0, server.Snapshot.WanIngress.ActivePings);
        Assert.Equal(0, server.Snapshot.AcceptedHandshakeCount); // Relay and Ping evidence remain distinct.
        await server.StopAsync(deadline.Token);
        Assert.Equal(0, (await run).Snapshot.CleanupIncompleteCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("parent")]
    [InlineData("duplicate")]
    [InlineData("version")]
    [InlineData("quit")]
    [InlineData("garbage")]
    public async Task WanPingRejectsInvalidInputWithoutDisclosingMetadata(string kind)
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission();
        await using var server = CreateWanServer(fanout, admission);
        _ = server.RunAsync(deadline.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        var sid = PcpAtom.Leaf(PcpKnownAtomNames.HeloSessionId, PcpWireValueCodec.EncodeGuid(kind == "empty" ? Guid.Empty : Guid.NewGuid()));
        var helo = kind switch
        {
            "missing" => PcpAtom.Parent(PcpKnownAtomNames.Helo),
            "parent" => PcpAtom.Parent(PcpKnownAtomNames.Helo, PcpAtom.Parent(PcpKnownAtomNames.HeloSessionId)),
            "duplicate" => PcpAtom.Parent(PcpKnownAtomNames.Helo, sid, sid),
            "quit" => PcpAtom.Leaf(PcpKnownAtomNames.Quit, PcpWireValueCodec.EncodeUInt32(1000)),
            _ => PcpAtom.Parent(PcpKnownAtomNames.Helo, sid),
        };
        var request = PingRequest(helo);
        if (kind == "version") request[8] = 100;
        if (kind == "garbage") request[0] = (byte)'x';
        await client.WriteAsync(request, deadline.Token);
        Assert.Equal(0, await client.ReadAsync(new byte[1], deadline.Token));
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, server.Snapshot.WanIngress!.PingResponses);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, server.Snapshot.WanIngress.PendingHandshakes);
        await server.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task WanHttpDispatchPreservesCombinedRequestAndHeloAndMetadataHeadDataOrder()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        using var admission = new WanAdmission();
        await fanout.ConsumeAsync(new(ChannelId, UpstreamSessionId, 0, 0x64616568, 0, 0, new byte[] { 1, 2, 3 }, null), deadline.Token);
        await using var server = CreateWanServer(fanout, admission);
        _ = server.RunAsync(deadline.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, PcpHeloCodec.CreateHelo("fixture", Guid.NewGuid()));
        var header = new PcpRelayRequest { ChannelId = ChannelId.Value, Host = "127.0.0.1", UserAgent = "fixture", StreamPosition = 0, PcpVersion = 1 }.Encode();
        await client.WriteAsync(header.Concat(writer.WrittenSpan.ToArray()).ToArray(), deadline.Token);
        var pipe = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(pipe, 32768, deadline.Token);
        Assert.Equal(200, response.StatusCode);
        await using var atoms = new PcpAtomPipeMessageReader(pipe);
        Assert.Equal(LocalSessionId, PcpHeloCodec.ParseOleh(await atoms.ReadNextAsync(deadline.Token)).SessionId);
        Assert.Equal(PcpKnownAtomNames.Okay, (await atoms.ReadNextAsync(deadline.Token)).Name);
        var metadata = await atoms.ReadNextAsync(deadline.Token);
        Assert.NotNull(metadata.FindFirst(PcpKnownAtomNames.ChannelInfo));
        Assert.Null(metadata.FindFirst(PcpKnownAtomNames.ChannelPacket));
        var head = (await atoms.ReadNextAsync(deadline.Token)).FindFirst(PcpKnownAtomNames.ChannelPacket)!;
        Assert.Equal("head", head.FindFirst(PcpKnownAtomNames.Type)!.Content.Span.Length == 4 ? System.Text.Encoding.ASCII.GetString(head.FindFirst(PcpKnownAtomNames.Type)!.Content.Span) : "invalid");
        await fanout.ConsumeAsync(new(ChannelId, UpstreamSessionId, 1, 0x61746164, 3, 3, new byte[] { 4, 5, 6 }, null), deadline.Token);
        var data = (await atoms.ReadNextAsync(deadline.Token)).FindFirst(PcpKnownAtomNames.ChannelPacket)!;
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(data.FindFirst(PcpKnownAtomNames.Type)!.Content.Span));
        Assert.Equal(new byte[] { 4, 5, 6 }, data.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
        Assert.Equal(1, admission.ReservationCount);
        await pipe.CompleteAsync();
        await client.DisposeAsync();
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, server.Snapshot.WanIngress!.PendingHandshakes);
        await server.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task WanDispatchCannotBypassZeroRelayAdmission()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission(0);
        await using var server = CreateWanServer(fanout, admission);
        _ = server.RunAsync(deadline.Token);
        await using var client = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, client.Handshake.Outcome);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(0, admission.ReservationCount);
        await server.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task WanPartialPreambleTimesOutAndDoesNotResetHttpHandshakeDeadline()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission();
        using var clock = new AnnounceManualTimeProvider();
        await using var server = CreateWanServer(fanout, admission, FixtureWan with { Clock = clock });
        _ = server.RunAsync(deadline.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        await WaitForAcceptedCountAsync(server, 1, deadline.Token);
        while (clock.TimerCount < 2) await Task.Delay(5, deadline.Token);
        await client.WriteAsync("p"u8.ToArray(), deadline.Token);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await client.ReadAsync(new byte[1], deadline.Token));
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(1, server.Snapshot.HandshakeTimedOutCount);
        Assert.Equal(0, server.Snapshot.WanIngress!.PendingHandshakes);
        await server.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task WanHttpDeadlineIncludesTimeAlreadySpentInDispatch()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission();
        using var clock = new AnnounceManualTimeProvider();
        await using var server = CreateWanServer(fanout, admission, FixtureWan with { Clock = clock });
        _ = server.RunAsync(deadline.Token);
        await using var client = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        await WaitForAcceptedCountAsync(server, 1, deadline.Token);
        while (clock.TimerCount < 2) await Task.Delay(5, deadline.Token);
        clock.Advance(TimeSpan.FromSeconds(2));
        await client.WriteAsync("GET /"u8.ToArray(), deadline.Token);
        while (clock.ActiveTimerCount > 1) await Task.Delay(5, deadline.Token);
        clock.Advance(TimeSpan.FromSeconds(8)); // Ten seconds since accept, not ten seconds since dispatch.
        Assert.Equal(0, await client.ReadAsync(new byte[1], deadline.Token));
        await WaitForCompletedCountAsync(server, 1, deadline.Token);
        Assert.Equal(1, server.Snapshot.HandshakeTimedOutCount);
        Assert.Equal(0, server.Snapshot.WanIngress!.PendingHandshakes);
        await server.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task WanStopClosesBothProtocolRolesAndRejectsNewAccepts()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission();
        await using var server = CreateWanServer(fanout, admission);
        _ = server.RunAsync(deadline.Token);
        await using var http = await ConnectPeerAsync(server.LocalEndPoint, CreateTransportOptions(), deadline.Token);
        await using var ping = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        var preamble = new byte[12]; PcpPreamble.Write(preamble);
        await ping.WriteAsync(preamble, deadline.Token);
        await WaitForAcceptedCountAsync(server, 2, deadline.Token);
        await server.StopAcceptingAsync();
        var result = await server.DrainAfterUpstreamEndAsync(TimeSpan.FromSeconds(5), deadline.Token);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.WanIngress!.ActivePings);
        Assert.Equal(0, result.Snapshot.WanIngress.PendingHandshakes);
        Assert.Equal(0, admission.ReservationCount);
        await Assert.ThrowsAsync<SocketException>(() => new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token).AsTask());
    }

    [Fact]
    public async Task WanDelayedPingWriteRetainsCleanupEvidenceAndItsOriginalSid()
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var fanout = new PcpRelayFanout();
        using var admission = new WanAdmission();
        var write = new WanDelayedWriter();
        await using var server = CreateWanServer(fanout, admission, FixtureWan with { PingTimeout = TimeSpan.FromMilliseconds(200) }, write);
        _ = server.RunAsync(deadline.Token);
        await using var ping = await new TcpTransportConnector().ConnectAsync(server.LocalEndPoint, deadline.Token);
        await ping.WriteAsync(PingRequest(PcpHeloCodec.CreateHelo("fixture", Guid.NewGuid())), deadline.Token);
        await write.Entered.Task.WaitAsync(deadline.Token);
        try
        {
            Assert.Equal(LocalSessionId, PcpHeloCodec.ParseOleh(AnnounceTestData.Decode(write.Bytes!)).SessionId);
            await WaitForCompletedCountAsync(server, 1, deadline.Token);
            var result = await server.DrainAfterUpstreamEndAsync(TimeSpan.FromMilliseconds(150), deadline.Token);
            Assert.False(result.CleanupCompleted);
            Assert.True(result.Snapshot.CleanupIncompleteCount > 0);
            Assert.True(result.Snapshot.AdmissionCleanupPendingCount > 0);
            Assert.Equal(1, result.Snapshot.WanIngress!.ActivePings);
        }
        finally { write.Release.TrySetResult(); }
        await server.StopAsync(deadline.Token);
    }

    private static byte[] PingRequest(PcpAtom atom)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);
        var preamble = new byte[12]; PcpPreamble.Write(preamble);
        return preamble.Concat(writer.WrittenSpan.ToArray()).ToArray();
    }
    private sealed class WanDelayedWriter : IPcpDownstreamPacketWriter
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal byte[]? Bytes { get; private set; }
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        { Bytes = buffer.ToArray(); Entered.TrySetResult(); await Release.Task; }
    }
    private sealed class WanAdmission : IPcpDownstreamRelayAdmission, IDisposable
    {
        private readonly RelayAdmissionPolicy _policy;
        private readonly RelayAdmissionPolicy.Channel _owner;
        internal WanAdmission(int maximum = 4)
        {
            _policy = new(new(MaxRelaysTotal: maximum));
            _owner = _policy.OpenChannel(ChannelId, UpstreamSessionId, RelayChannelKind.Broadcast);
        }
        public ChannelId ChannelId => PcpDownstreamRelayServerTests.ChannelId;
        internal long ReservationCount => _policy.ReservedCount;
        public bool TryReserve(long connectionId, [NotNullWhen(true)] out IDisposable? reservation, CancellationToken cancellationToken = default)
        { reservation = _owner.TryReserve(connectionId, cancellationToken).Reservation; return reservation is not null; }
        public void Dispose() { _owner.Dispose(); _policy.Dispose(); }
    }
}
