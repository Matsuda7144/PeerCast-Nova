using System.IO.Pipelines;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamChildSessionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InitialMetadataUsesExistingDeadlineAndCancellationBeforeMediaAdmission(int ending)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var operation = new CancellationTokenSource();
        var transport = CreateTransportOptions();
        await using var listener = CreateListener(transport);
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await using var healthy = fanout.AddChild(new NoopRelaySink());
        listener.Start();
        if (ending == 0)
            await fanout.ConsumeAsync(new(ChannelId, UpstreamSessionId, 0, 0x64616568, 0, 0, new byte[] { 1, 2, 3 }, null), timeout.Token);
        var entered = new TaskCompletionSource<MetadataGateWriter>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionReady = new TaskCompletionSource<PcpDownstreamChildSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeAsync();
        async Task<PcpDownstreamChildResult> ServeAsync()
        {
            var connection = await listener.AcceptAsync(timeout.Token);
            var writer = new MetadataGateWriter(connection);
            entered.TrySetResult(writer);
            await using var child = new PcpDownstreamChildSession(connection, fanout, ChannelId, LocalSessionId,
                new() { WriteTimeout = ending == 1 ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(5) }, writer,
                initialMetadata: PcpInitialChannelMetadata.Encode(ChannelId.Value, new ObsPublishChannelOptions().CreateMetadata()));
            sessionReady.TrySetResult(child);
            return await child.RunAsync(operation.Token);
        }
        await using var client = await new TcpTransportConnector(transport).ConnectAsync("127.0.0.1", listener.LocalEndPoint.Port, timeout.Token);
        var pipe = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var handshake = await NegotiateAsync(client, pipe, listener.LocalEndPoint, ChannelId.Value, timeout.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            var writer = await entered.Task.WaitAsync(timeout.Token);
            await writer.Entered.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, writer.Calls); // Only initial control info; retained HEAD cannot overtake it.
            if (ending == 0)
            {
                writer.Release.TrySetResult();
                await using var reader = new PcpAtomPipeMessageReader(pipe);
                var info = await reader.ReadNextAsync(timeout.Token);
                Assert.NotNull(info.FindFirst(PcpKnownAtomNames.ChannelInfo));
                Assert.Null(info.FindFirst(PcpKnownAtomNames.ChannelPacket));
                var head = await reader.ReadNextAsync(timeout.Token);
                Assert.Equal(new byte[] { 1, 2, 3 }, head.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
                await client.WriteAsync(EncodeTerminal(PcpKnownAtomNames.Quit), timeout.Token);
            }
            else if (ending == 2) await operation.CancelAsync();
            else if (ending == 3) await (await sessionReady.Task).DisposeAsync();
            var result = await server.WaitAsync(timeout.Token);
            Assert.True(result.CleanupCompleted);
            Assert.Equal(ending == 0 ? 1 : 0, result.DeliveredPacketCount);
            Assert.Equal(ending == 0 ? 3 : 0, result.DeliveredByteCount);
            Assert.Equal(ending == 0, result.HandshakeAccepted);
            if (ending == 1) Assert.Equal(PcpDownstreamChildEndReason.WriteTimedOut, result.EndReason);
            Assert.Equal(1, fanout.ActiveChildCount); // Slow/cancelled child never releases an unrelated child.
        }
        finally { await operation.CancelAsync(); await pipe.CompleteAsync(); }
    }

    [Fact]
    public async Task FailedHandshakeNeverWritesOptInInitialInfo()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var transport = CreateTransportOptions();
        await using var listener = CreateListener(transport);
        await using var fanout = new PcpRelayFanout();
        listener.Start();
        MetadataGateWriter? writer = null;
        var server = ServeAsync();
        async Task<PcpDownstreamChildResult> ServeAsync()
        {
            var connection = await listener.AcceptAsync(timeout.Token);
            writer = new(connection);
            await using var child = new PcpDownstreamChildSession(connection, fanout, ChannelId, LocalSessionId, null, writer,
                initialMetadata: PcpInitialChannelMetadata.Encode(ChannelId.Value, new ObsPublishChannelOptions().CreateMetadata()));
            return await child.RunAsync(timeout.Token);
        }
        await using var client = await new TcpTransportConnector(transport).ConnectAsync("127.0.0.1", listener.LocalEndPoint.Port, timeout.Token);
        var pipe = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            Assert.Equal(PcpCompatibilityOutcome.HttpRejected, (await NegotiateAsync(client, pipe, listener.LocalEndPoint, Guid.NewGuid(), timeout.Token)).Outcome);
            var result = await server.WaitAsync(timeout.Token);
            Assert.True(result.CleanupCompleted);
            Assert.False(result.HandshakeAccepted);
            Assert.Equal(0, writer!.Calls);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally { await pipe.CompleteAsync(); }
    }

    private sealed class MetadataGateWriter(TcpTransportConnection connection) : IPcpDownstreamPacketWriter
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            await connection.WriteAsync(buffer, token);
        }
    }
}
