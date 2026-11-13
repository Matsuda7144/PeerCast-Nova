using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamRelayServerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public async Task Referrals_FullServerWritesOrderedBoundedHostAtoms(int count)
    {
        var options = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout(new PcpRelayFanoutOptions { MaxChildren = 1 });
        await using var server = CreateServer(fanout, options);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        await using var accepted = await ConnectPeerAsync(server.LocalEndPoint, options, deadline.Token);
        for (var i = 0; i < count; i++)
            server.Peers.Observe(PcpPeerDirectoryTests.Host(10000 + i) with { ChannelId = ChannelId.Value },
                new(PcpPeerSourceKind.UpstreamHandshake, 1));
        await using var full = await ConnectPeerAsync(server.LocalEndPoint, options, deadline.Token);
        var result = full.Handshake;
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal(1003U, result.TerminalCode);
        Assert.Equal(Math.Min(count, 8), result.RelayHostCandidates.Length);
        Assert.Equal(PcpKnownAtomNames.Oleh, result.ReceivedAtomNames[0]);
        Assert.Equal(PcpKnownAtomNames.Quit, result.ReceivedAtomNames[^1]);
        Assert.All(result.ReceivedAtomNames.Skip(1).SkipLast(1), name => Assert.Equal(PcpKnownAtomNames.Host, name));
        Assert.Equal(result.RelayHostCandidates.Length + 2, result.ReceivedAtomNames.Length);
        Assert.All(result.RelayHostCandidates, host =>
        {
            Assert.Equal(ChannelId.Value, host.ChannelId);
            Assert.Null(host.LocalEndpoint);
            Assert.True(host.RelayAvailable);
        });
        Assert.Equal(1, fanout.ActiveChildCount);
        await accepted.SendQuitAsync(deadline.Token);
        await server.StopAsync(deadline.Token);
        await run;
        Assert.False(server.Peers.IsActive);
        Assert.Equal(0, server.Peers.Count);
    }

    [Fact]
    public async Task Referrals_DownstreamHostIsLearnedButDirectPeerIsNeverIntroduced()
    {
        var options = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout(new PcpRelayFanoutOptions { MaxChildren = 1 });
        await using var server = CreateServer(fanout, options);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        var directId = Guid.NewGuid();
        await using var direct = await ConnectPeerAsync(server.LocalEndPoint, options, deadline.Token, directId);
        var self = PcpPeerDirectoryTests.Host(sid: directId) with { ChannelId = ChannelId.Value };
        var other = PcpPeerDirectoryTests.Host(7150) with { ChannelId = ChannelId.Value };
        await direct.SendHostAsync(PcpReferralPlanner.CreateHost(self), deadline.Token);
        await direct.SendHostAsync(PcpReferralPlanner.CreateHost(other), deadline.Token);
        while (server.Peers.Count != 1) await Task.Delay(1, deadline.Token);
        Assert.Equal(PcpPeerSourceKind.DownstreamControl, Assert.Single(server.Peers.Snapshot()).Source.Kind);
        await fanout.ConsumeAsync(CreatePacket([7, 8, 9]), deadline.Token);
        await direct.ReadChannelPayloadAsync(deadline.Token);
        Assert.Equal([7, 8, 9], direct.LastPayload);
        await using var full = await ConnectPeerAsync(server.LocalEndPoint, options, deadline.Token);
        Assert.Equal(other.PeerSessionId, Assert.Single(full.Handshake.RelayHostCandidates).PeerSessionId);
        await direct.SendQuitAsync(deadline.Token);
        await server.StopAsync(deadline.Token);
        await run;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referrals_OwnerRetireOrAdmissionCloseBeforeHeloSuppressesHosts(bool closeListener)
    {
        var options = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout();
        var admission = new ReferralAdmission(ChannelId);
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout,
            ChannelId, LocalSessionId, options, admission: admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        var host = PcpPeerDirectoryTests.Host() with { ChannelId = ChannelId.Value };
        server.Peers.Observe(host, new(PcpPeerSourceKind.UpstreamHandshake, 1));
        await using var connection = await new TcpTransportConnector(options).ConnectAsync(
            server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        var pipe = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await connection.WriteAsync(new PcpRelayRequest { ChannelId = ChannelId.Value }.Encode(), deadline.Token);
            Assert.Equal(503, (await PcpHttpHeaderBlockReader.ReadResponseAsync(pipe, 32768, deadline.Token)).StatusCode);
            if (closeListener) await server.StopAcceptingAsync();
            else admission.IsCurrent = false;
            var bytes = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(bytes, PcpHeloCodec.CreateHelo("Synthetic peer", Guid.NewGuid()));
            await connection.WriteAsync(bytes.WrittenMemory, deadline.Token);
            await using var atoms = new PcpAtomPipeMessageReader(pipe);
            Assert.Equal(PcpKnownAtomNames.Oleh, (await atoms.ReadNextAsync(deadline.Token)).Name);
            Assert.Equal(PcpQuitReason.Unavailable, PcpQuitCodec.Parse(await atoms.ReadNextAsync(deadline.Token)).Reason);
            await server.StopAsync(deadline.Token);
            await run;
            Assert.False(server.Peers.Observe(host, new(PcpPeerSourceKind.UpstreamStream, 1)));
        }
        finally { await pipe.CompleteAsync(); }
    }

    [Fact]
    public async Task Referrals_CancelWhileWaitingForHeloClosesDirectoryAndRetainsNoNewCandidate()
    {
        var options = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout();
        var admission = new ReferralAdmission(ChannelId);
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout,
            ChannelId, LocalSessionId, options, admission: admission);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var run = server.RunAsync(deadline.Token);
        var host = PcpPeerDirectoryTests.Host() with { ChannelId = ChannelId.Value };
        server.Peers.Observe(host, new(PcpPeerSourceKind.UpstreamHandshake, 1));
        await using var connection = await new TcpTransportConnector(options).ConnectAsync(
            server.LocalEndPoint.Address.ToString(), server.LocalEndPoint.Port, deadline.Token);
        var pipe = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await connection.WriteAsync(new PcpRelayRequest { ChannelId = ChannelId.Value }.Encode(), deadline.Token);
            Assert.Equal(503, (await PcpHttpHeaderBlockReader.ReadResponseAsync(pipe, 32768, deadline.Token)).StatusCode);
            server.RequestStop();
            await run.WaitAsync(deadline.Token);
            Assert.Equal(0, server.Peers.Count);
            Assert.False(server.Peers.Observe(host, new(PcpPeerSourceKind.UpstreamStream, 1)));
        }
        finally { await pipe.CompleteAsync(); }
    }

    private sealed class ReferralAdmission(ChannelId channel) : IPcpDownstreamRelayAdmission, IPcpDownstreamRelayCapacity
    {
        public ChannelId ChannelId => channel;
        public bool IsCurrent { get; set; } = true;
        public bool CanReserve => false;
        public bool TryReserve(long connectionId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IDisposable? reservation,
            CancellationToken cancellationToken = default)
        { reservation = null; return false; }
    }
}
