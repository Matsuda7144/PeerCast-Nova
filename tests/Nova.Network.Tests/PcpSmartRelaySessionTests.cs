using System.IO.Pipelines;
using System.Net.Sockets;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionTests
{
    [Fact]
    public async Task SmartRecovery_ControlTrafficDoesNotMaskExplicitMediaStallProfile()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var (listener, connector) = CreateTransportPair();
        await using var owned = listener;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<TcpTransportConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PcpChannelSession(connector, CreateTracker(), new CollectingPacketConsumer(),
            CreateOptions(0) with
            {
                SmartRelay = new()
                {
                    TimeProvider = clock,
                    JitterSample = () => .5,
                    MediaStallTimeout = TimeSpan.FromSeconds(20)
                }
            });
        var peer = Peer();
        var run = session.RunAsync(listener.LocalEndPoint.Address.ToString(), listener.LocalEndPoint.Port, timeout.Token);
        try
        {
            var connection = await ready.Task.WaitAsync(timeout.Token);
            await PcpSmartRelayRecoveryTests.Until(() => session.SmartRelaySnapshot.ResumePosition == 101);
            clock.Advance(TimeSpan.FromSeconds(10));
            await PcpSmartRelayRecoveryTests.Until(() => session.SmartRelaySnapshot.State == PcpSmartRelayState.Degraded);
            await WriteAtomsAsync(connection, [PcpAtom.Leaf(PcpAtomName.FromAscii("ping"), [1])], timeout.Token);
            clock.Advance(TimeSpan.FromSeconds(10));
            await PcpSmartRelayRecoveryTests.Until(() => session.SmartRelaySnapshot.State == PcpSmartRelayState.Backoff);
            Assert.Equal(1, session.SmartRelaySnapshot.AttemptCount);
            await session.StopAsync(); await run;
            await PcpSmartRelayRecoveryTests.Until(() => clock.ActiveTimerCount == 0);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally { release.TrySetResult(); await peer; }
        async Task Peer()
        {
            await using var connection = await listener.AcceptAsync(timeout.Token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                await ReadAndAssertRequestAsync(reader, 0, timeout.Token);
                await WriteHttpResponseAsync(connection, 200, "OK", timeout.Token);
                await ReadAndAssertHeloAsync(reader, SessionId.Value, timeout.Token);
                await WriteAtomsAsync(connection, [CreateOlehAtom(), PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)),
                    SmartData(100, [1])], timeout.Token);
                ready.TrySetResult(connection);
                await release.Task.WaitAsync(timeout.Token);
            }
            finally { await reader.CompleteAsync(); }
        }
    }

    [Fact]
    public async Task SmartRecovery_UnconfirmedTransportCleanupFailsOwnerWithoutRetry()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var (listener, _) = CreateTransportPair();
        await using var owned = listener;
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var transport = new CleanupConnector(async token =>
        {
            Interlocked.Increment(ref attempts);
            var client = new TcpClient();
            await client.ConnectAsync(listener.LocalEndPoint, token);
            return new TcpTransportConnection(client, () => { cleanupEntered.TrySetResult(); releaseCleanup.Task.GetAwaiter().GetResult(); });
        });
        await using var session = new PcpChannelSession(transport, CreateTracker(), new CollectingPacketConsumer(),
            CreateOptions(0) with { SmartRelay = new() { TimeProvider = clock } });
        var peer = RunAcceptedAttemptAsync(listener, 0, SessionId.Value, [], timeout.Token);
        var run = session.RunAsync("explicit-bootstrap", 7144, timeout.Token);
        try
        {
            await cleanupEntered.Task.WaitAsync(timeout.Token);
            clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(timeout.Token));
            Assert.Equal(PcpSmartRelayState.Failed, session.SmartRelaySnapshot.State);
            Assert.False(session.SmartRelayCleanupConfirmed);
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(1, attempts);
        }
        finally { releaseCleanup.TrySetResult(); await peer; }
    }

    private static PcpAtom SmartData(uint position, byte[] bytes) => PcpAtom.Parent(PcpKnownAtomNames.Channel,
        PcpAtom.Leaf(PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(ChannelGuid)),
        PcpAtom.Parent(PcpKnownAtomNames.ChannelPacket,
            PcpAtom.Leaf(PcpKnownAtomNames.Type, PcpWireValueCodec.EncodeUInt32(0x61746164)),
            PcpAtom.Leaf(PcpKnownAtomNames.Position, PcpWireValueCodec.EncodeUInt32(position)),
            PcpAtom.Leaf(PcpKnownAtomNames.Data, bytes)));

    private sealed class CleanupConnector(Func<CancellationToken, ValueTask<TcpTransportConnection>> connect) : IPcpChannelTransportConnector
    {
        public ValueTask<TcpTransportConnection> ConnectOriginalAsync(string host, int port, CancellationToken token) => connect(token);
        public ValueTask<TcpTransportConnection> ConnectRedirectAsync(System.Net.IPEndPoint endpoint, CancellationToken token) => connect(token);
    }
}
