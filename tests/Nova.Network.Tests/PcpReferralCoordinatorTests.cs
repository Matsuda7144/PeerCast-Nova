using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpRelayCoordinatorTests
{
    [Fact]
    public async Task Referrals_RealCoordinatorLearnsBothUpstreamPhasesAndIntroducesOnCapacityRefusal()
    {
        var transport = CreateTransportOptions();
        await using var upstream = new TcpTransportListener(new(IPAddress.Loopback, 0), transport);
        upstream.Start();
        var options = CreateCoordinatorOptions(transport, 0);
        options = options with { Fanout = options.Fanout with { MaxChildren = 1 } };
        var tracker = CreateTracker();
        await using var coordinator = new PcpRelayCoordinator(new TcpTransportConnector(transport),
            tracker, new(IPAddress.Loopback, 0), options);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var handshakeHost = PcpPeerDirectoryTests.Host() with { ChannelId = ChannelGuid };
        var streamHost = PcpPeerDirectoryTests.Host(7150) with { ChannelId = ChannelGuid };
        var peer = ReferralUpstreamAsync(upstream, handshakeHost, streamHost, release.Task, deadline.Token);
        var run = coordinator.RunAsync(upstream.LocalEndPoint.Address.ToString(),
            upstream.LocalEndPoint.Port, cancellationToken: deadline.Token);
        try
        {
            await coordinator.FirstPacketAccepted.WaitAsync(deadline.Token);
            // An established child fills the existing bounded fanout, not a new engine.
            await using var accepted = await ConnectDownstreamAsync(coordinator.LocalEndPoint, transport, deadline.Token);
            var probe = new PcpCompatibilityClient(new TcpTransportConnector(transport));
            var result = await probe.ProbeAsync(coordinator.LocalEndPoint.Address.ToString(),
                coordinator.LocalEndPoint.Port, ChannelGuid, cancellationToken: deadline.Token);
            Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
            Assert.Equal(1003U, result.TerminalCode);
            Assert.Equal(new[] { PcpKnownAtomNames.Oleh, PcpKnownAtomNames.Host, PcpKnownAtomNames.Host, PcpKnownAtomNames.Quit },
                result.ReceivedAtomNames);
            Assert.Equal(100, tracker.Snapshot.StreamPosition);
            Assert.Equal(LocalSessionId, tracker.Snapshot.SessionId);
            // The public probe retains names; inspect both exact HOST records through
            // the server/session integration tests and incremental upstream tests.
            Assert.Equal(1, coordinator.DownstreamFanout.ActiveChildCount);
        }
        finally
        {
            release.TrySetResult();
            await coordinator.StopAsync(deadline.Token);
            await peer;
            await run.WaitAsync(deadline.Token);
        }
    }

    private static async Task ReferralUpstreamAsync(TcpTransportListener listener,
        PcpRelayHostCandidate handshake, PcpRelayHostCandidate receiving, Task release, CancellationToken token)
    {
        await using var connection = await listener.AcceptAsync(token);
        var pipe = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await ReadAndAssertUpstreamRequestAsync(pipe, 0, token);
            await WriteHttpResponseAsync(connection, token);
            await ReadAndAssertHeloAsync(pipe, token);
            await WriteAtomsAsync(connection,
                [CreateOlehAtom(), PcpReferralPlanner.CreateHost(handshake),
                 PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)),
                 PcpReferralPlanner.CreateHost(receiving), CreatePacketAtom(100, [1, 2, 3])], token);
            await release.WaitAsync(token);
        }
        finally { await pipe.CompleteAsync(); }
    }
}
