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
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    private static readonly ChannelSessionId UpstreamSessionId = new(
        Guid.ParseExact("3f2504e04f8911d39a0c0305e82c3301", "N"));

    private static readonly Guid LocalSessionId = Guid.ParseExact(
        "102132435465768798a9babbdcddedef",
        "N");

    [Fact]
    public async Task Server_RelaysOnePacketToTwoConcurrentChildren()
    {
        var transportOptions = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, transportOptions);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var runTask = server.RunAsync(timeoutSource.Token);

        await using var first = await ConnectPeerAsync(
            server.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        await using var second = await ConnectPeerAsync(
            server.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, first.Handshake.Outcome);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, second.Handshake.Outcome);
        Assert.Equal(2, server.Snapshot.ActiveConnectionCount);

        byte[] payload = [0x10, 0x20, 0x30];
        await fanout.ConsumeAsync(
            CreatePacket(payload),
            timeoutSource.Token);
        await Task.WhenAll(
            first.ReadChannelPayloadAsync(timeoutSource.Token),
            second.ReadChannelPayloadAsync(timeoutSource.Token));
        Assert.Equal(payload, first.LastPayload);
        Assert.Equal(payload, second.LastPayload);

        await first.SendQuitAsync(timeoutSource.Token);
        await second.SendQuitAsync(timeoutSource.Token);
        await WaitForCompletedCountAsync(
            server,
            expected: 2,
            timeoutSource.Token);
        await server.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(
            PcpDownstreamRelayServerEndReason.Stopped,
            result.EndReason);
        Assert.Equal(PcpDownstreamRelayServerState.Stopped, result.Snapshot.State);
        Assert.Equal(2, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(2, result.Snapshot.CompletedConnectionCount);
        Assert.Equal(2, result.Snapshot.AcceptedHandshakeCount);
        Assert.Equal(2, result.Snapshot.QuitReceivedCount);
        Assert.Equal(2, result.Snapshot.DeliveredPacketCount);
        Assert.Equal(payload.Length * 2, result.Snapshot.DeliveredByteCount);
        Assert.Equal(0, result.Snapshot.DiscardedPacketCount);
        Assert.Equal(0, result.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task Server_StopCancelsChildStillWaitingForHttpRequest()
    {
        var transportOptions = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(fanout, transportOptions);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var runTask = server.RunAsync(timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                server.LocalEndPoint.Address.ToString(),
                server.LocalEndPoint.Port,
                timeoutSource.Token);

        await WaitForAcceptedCountAsync(
            server,
            expected: 1,
            timeoutSource.Token);
        await server.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(
            PcpDownstreamRelayServerEndReason.Stopped,
            result.EndReason);
        Assert.Equal(1, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(1, result.Snapshot.CompletedConnectionCount);
        Assert.Equal(1, result.Snapshot.StoppedCount);
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task Server_ReportsRelayFullWithoutConsumingSecondFanoutSlot()
    {
        var transportOptions = CreateTransportOptions();
        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions { MaxChildren = 1 });
        await using var server = CreateServer(fanout, transportOptions);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var runTask = server.RunAsync(timeoutSource.Token);

        await using var accepted = await ConnectPeerAsync(
            server.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        await using var full = await ConnectPeerAsync(
            server.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);

        Assert.Equal(
            PcpCompatibilityOutcome.Accepted,
            accepted.Handshake.Outcome);
        Assert.Equal(PcpCompatibilityOutcome.RelayFull, full.Handshake.Outcome);
        Assert.Equal(1, fanout.ActiveChildCount);

        await WaitForCompletedCountAsync(
            server,
            expected: 1,
            timeoutSource.Token);
        await accepted.SendQuitAsync(timeoutSource.Token);
        await WaitForCompletedCountAsync(
            server,
            expected: 2,
            timeoutSource.Token);
        await server.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(2, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(1, result.Snapshot.AcceptedHandshakeCount);
        Assert.Equal(1, result.Snapshot.RelayFullCount);
        Assert.Equal(1, result.Snapshot.QuitReceivedCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task Server_StatusIsPrivacySafeAndLifecycleIsSingleUse()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(
            fanout,
            CreateTransportOptions());

        Assert.Throws<InvalidOperationException>(() => server.LocalEndPoint);
        var snapshotProperties =
            typeof(PcpDownstreamRelayServerSnapshot).GetProperties();
        Assert.DoesNotContain(
            snapshotProperties,
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            snapshotProperties,
            property => typeof(Exception).IsAssignableFrom(
                property.PropertyType));
        Assert.DoesNotContain(
            snapshotProperties,
            property => property.PropertyType == typeof(IPEndPoint));
        Assert.DoesNotContain(
            snapshotProperties,
            property => property.PropertyType == typeof(ChannelId));

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var runTask = server.RunAsync(timeoutSource.Token);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = server.RunAsync(timeoutSource.Token);
            });
        await server.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(PcpDownstreamRelayServerState.Stopped, result.Snapshot.State);
        Assert.Equal(
            PcpDownstreamRelayServerEndReason.Stopped,
            result.Snapshot.EndReason);
    }

    [Fact]
    public async Task Server_CallerCancellationHasFixedTerminalReason()
    {
        await using var fanout = new PcpRelayFanout();
        await using var server = CreateServer(
            fanout,
            CreateTransportOptions());
        using var callerSource = new CancellationTokenSource();
        var runTask = server.RunAsync(callerSource.Token);

        callerSource.Cancel();
        var result = await runTask.WaitAsync(TestTimeout);

        Assert.Equal(
            PcpDownstreamRelayServerEndReason.CallerCanceled,
            result.EndReason);
        Assert.Equal(PcpDownstreamRelayServerState.Stopped, result.Snapshot.State);
        Assert.Equal(0, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    private static PcpDownstreamRelayServer CreateServer(
        PcpRelayFanout fanout,
        TcpTransportOptions transportOptions)
    {
        return new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            ChannelId,
            LocalSessionId,
            transportOptions);
    }

    private static TcpTransportOptions CreateTransportOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
    }

    private static async Task<TestPeer> ConnectPeerAsync(
        IPEndPoint endPoint,
        TcpTransportOptions transportOptions,
        CancellationToken cancellationToken,
        Guid? peerSessionId = null)
    {
        var connection = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                endPoint.Address.ToString(),
                endPoint.Port,
                cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await PcpRelayHandshakeClient.NegotiateAsync(
                connection,
                pipeReader,
                endPoint.Address.ToString(),
                endPoint.Port,
                ChannelId.Value,
                peerSessionId ?? Guid.NewGuid(),
                streamPosition: 0,
                pcpVersion: 1,
                new PcpRelayHandshakeSettings(
                    MaxHttpHeaderBytes: 32 * 1024,
                    MaxHandshakeAtomBytes: 64 * 1024,
                    MaxAtomsBeforeTerminal: 16,
                    UserAgent: "PeerCastNova/Test",
                    ServantVersion: PcpHeloCodec.LegacyServantVersion),
                cancellationToken);
            return new TestPeer(connection, pipeReader, handshake);
        }
        catch
        {
            await pipeReader.CompleteAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private static PcpChannelPacket CreatePacket(byte[] payload)
    {
        return new PcpChannelPacket(
            ChannelId,
            UpstreamSessionId,
            sequence: 0,
            type: 1,
            wirePosition: 1234,
            streamPosition: 1234,
            payload,
            discontinuity: null);
    }

    private static async Task WaitForAcceptedCountAsync(
        PcpDownstreamRelayServer server,
        long expected,
        CancellationToken cancellationToken)
    {
        while (server.Snapshot.AcceptedConnectionCount < expected)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitForCompletedCountAsync(
        PcpDownstreamRelayServer server,
        long expected,
        CancellationToken cancellationToken)
    {
        while (server.Snapshot.CompletedConnectionCount < expected)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class TestPeer : IAsyncDisposable
    {
        private readonly TcpTransportConnection _connection;
        private readonly PipeReader _pipeReader;

        public TestPeer(
            TcpTransportConnection connection,
            PipeReader pipeReader,
            PcpRelayHandshakeResult handshake)
        {
            _connection = connection;
            _pipeReader = pipeReader;
            Handshake = handshake;
        }

        public PcpRelayHandshakeResult Handshake { get; }

        public byte[]? LastPayload { get; private set; }
        internal ValueTask SendHostAsync(PcpAtom host, CancellationToken token)
        {
            var writer = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(writer, host);
            return _connection.WriteAsync(writer.WrittenMemory, token);
        }
        internal async Task<PcpAtom> ReadAtomAsync(CancellationToken token)
        {
            await using var reader = new PcpAtomPipeMessageReader(_pipeReader);
            return await reader.ReadNextAsync(token);
        }
        internal async Task<byte[]> ReadWireToEndAsync(CancellationToken token)
        {
            using var bytes = new MemoryStream();
            while (true)
            {
                var read = await _pipeReader.ReadAsync(token);
                foreach (var segment in read.Buffer) bytes.Write(segment.Span);
                _pipeReader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted) return bytes.ToArray();
            }
        }

        public async Task ReadChannelPayloadAsync(
            CancellationToken cancellationToken)
        {
            await using var atomReader = new PcpAtomPipeMessageReader(
                _pipeReader,
                maxBufferedBytes: 1024 * 1024);
            var channel = await atomReader.ReadNextAsync(cancellationToken);
            var packet = channel.FindFirst(PcpKnownAtomNames.ChannelPacket)!;
            LastPayload = packet.FindFirst(
                PcpKnownAtomNames.Data)!.Content.ToArray();
        }

        public ValueTask SendQuitAsync(CancellationToken cancellationToken)
        {
            var writer = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(
                writer,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Quit,
                    PcpWireValueCodec.EncodeUInt32(0)));
            return _connection.WriteAsync(
                writer.WrittenMemory,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _pipeReader.CompleteAsync();
            await _connection.DisposeAsync();
        }
    }
}
