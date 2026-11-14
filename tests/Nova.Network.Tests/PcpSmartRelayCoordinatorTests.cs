using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpRelayCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmartRecovery_EofOrUnavailableKeepsOwnerListenerAndReplacesMediaGeneration(bool unavailable)
    {
        using var clock = new AnnounceManualTimeProvider();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var transport = CreateTransportOptions();
        await using var upstream = new TcpTransportListener(new(IPAddress.Loopback, 0), transport);
        upstream.Start();
        var options = CreateCoordinatorOptions(transport, 0);
        options = options with
        {
            UpstreamSession = options.UpstreamSession with
            { SmartRelay = new() { TimeProvider = clock, JitterSample = () => .5 }, Stream = new() { ReadIdleTimeout = TestTimeout } }
        };
        var tracker = CreateTracker();
        await using var coordinator = new PcpRelayCoordinator(new TcpTransportConnector(transport), tracker, new(IPAddress.Loopback, 0), options);
        var loseParent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endParent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = SmartParent(upstream, 0, 100, 0x42, [1, 2, 3], loseParent.Task, unavailable, timeout.Token, 512);
        var second = NextParent();
        async Task NextParent()
        {
            await first;
            await SmartParent(upstream, 103, 103, 0x55, [4, 5], endParent.Task, false, timeout.Token);
        }
        var run = coordinator.RunAsync(upstream.LocalEndPoint.Address.ToString(), upstream.LocalEndPoint.Port, cancellationToken: timeout.Token);
        try
        {
            await PcpSmartRelayRecoveryTests.Until(() => coordinator.SmartRelaySnapshot.ResumePosition == 103 || run.IsCompleted || first.IsFaulted || second.IsFaulted);
            if (first.IsFaulted) await first;
            if (second.IsFaulted) await second;
            Assert.False(run.IsCompleted, $"{coordinator.SmartRelaySnapshot}; {tracker.Snapshot.Fault}; {run.Exception}");
            Assert.Equal(512, coordinator.DeclaredBitrateKbps);
            var observed = coordinator.ObserveRelayTree();
            Assert.Equal(PcpRelayConnectionPhase.Connected, observed.Upstream!.Phase);
            Assert.NotNull(observed.Upstream.ConnectionId);
            var bound = coordinator.LocalEndPoint;
            await using var child = await ConnectDownstreamAsync(bound, transport, timeout.Token);
            Assert.Equal(new byte[] { 0x42 }, (await child.ReadChannelPacketsAsync(1, timeout.Token))[0].Payload);
            loseParent.TrySetResult();
            Assert.Equal(1003U, (await child.ReadQuitAsync(timeout.Token)).Code);
            await PcpSmartRelayRecoveryTests.Until(() => coordinator.SmartRelaySnapshot.State == PcpSmartRelayState.Backoff && clock.ActiveTimerCount > 0);
            Assert.False(run.IsCompleted);
            Assert.Equal(0, coordinator.DownstreamFanout.ActiveChildCount);
            Assert.False(coordinator.DownstreamFanout.HasRetainedHead);
            Assert.Null(coordinator.DeclaredBitrateKbps);
            var recovering = coordinator.ObserveRelayTree();
            Assert.Equal(PcpSmartRelayState.Backoff, recovering.Recovery);
            Assert.NotEqual(PcpRelayConnectionPhase.Connected, recovering.Upstream!.Phase);
            Assert.DoesNotContain(recovering.Downstreams, c => c.Phase == PcpRelayConnectionPhase.Connected);
            clock.Advance(TimeSpan.FromSeconds(1));
            await PcpSmartRelayRecoveryTests.Until(() => coordinator.SmartRelaySnapshot.ResumePosition == 105);
            Assert.Null(coordinator.DeclaredBitrateKbps);
            Assert.Equal(512, tracker.Snapshot.Metadata.BitrateKbps);
            Assert.Equal(bound, coordinator.LocalEndPoint);
            Assert.Equal(LocalSessionId, tracker.Snapshot.SessionId);
            Assert.True(coordinator.SmartRelaySnapshot.Discontinuities > 0);
            var resumed = coordinator.ObserveRelayTree();
            Assert.Equal(observed.OwnerId, resumed.OwnerId);
            Assert.Equal(PcpRelayConnectionPhase.Connected, resumed.Upstream!.Phase);
            Assert.NotEqual(observed.Upstream.ConnectionId, resumed.Upstream.ConnectionId);
            await using var replacement = await ConnectDownstreamAsync(bound, transport, timeout.Token);
            Assert.Equal(new byte[] { 0x55 }, (await replacement.ReadChannelPacketsAsync(1, timeout.Token))[0].Payload);
        }
        finally { loseParent.TrySetResult(); endParent.TrySetResult(); await coordinator.StopAsync(timeout.Token); }
        var result = await run;
        await first; await second;
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(0, coordinator.DownstreamSnapshot.AdmissionCleanupPendingCount);
        Assert.False(coordinator.ObserveRelayTree().Running);
        Assert.Equal(PcpRelayConnectionPhase.Disconnected, coordinator.ObserveRelayTree().Upstream!.Phase);
    }

    private static async Task SmartParent(TcpTransportListener listener, long requestPosition, uint dataPosition,
        byte head, byte[] data, Task release, bool unavailable, CancellationToken token, int? bitrateKbps = null)
    {
        await using var connection = await listener.AcceptAsync(token);
        var pipe = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await ReadAndAssertUpstreamRequestAsync(pipe, requestPosition, token);
            await WriteHttpResponseAsync(connection, token); await ReadAndAssertHeloAsync(pipe, token);
            await WriteAtomsAsync(connection, [CreateOlehAtom(), PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1))], token);
            if (bitrateKbps is not null)
                await connection.WriteAsync(PcpInitialChannelMetadata.Encode(ChannelGuid, new(contentType: "FLV", bitrateKbps: bitrateKbps)), token);
            await WriteAtomsAsync(connection, [CreatePacketAtom(0, [head], 0x64616568), CreatePacketAtom(dataPosition, data, 0x61746164)], token);
            await release.WaitAsync(token);
            if (unavailable) await WriteAtomsAsync(connection, [PcpQuitCodec.Create(1003)], token);
        }
        finally { await pipe.CompleteAsync(); }
    }
}
