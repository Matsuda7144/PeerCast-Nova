using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamChildSessionRoutedConnectionHandlerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    private static readonly Guid LocalSessionId = Guid.ParseExact(
        "102132435465768798a9babbdcddedef",
        "N");

    [Fact]
    public async Task HandlerContinuesFromPreRoutedReaderAndBorrowsTransport()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var acceptTask = listener.AcceptAsync(timeoutSource.Token).AsTask();
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        await using var server = await acceptTask;
        var serverReader = PipeReader.Create(
            server.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));
        var clientReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await client.WriteAsync(
                CreatePipelinedRequestAndHelo(),
                timeoutSource.Token);

            var validation = await PcpDownstreamRelayRequestReader.ReadAsync(
                serverReader,
                32 * 1024,
                expectedPcpVersion: 1,
                timeoutSource.Token);
            Assert.True(validation.IsAccepted);
            Assert.NotNull(validation.Request);

            var handler =
                new PcpDownstreamChildSessionRoutedConnectionHandler(
                    LocalSessionId);
            var handlerTask = handler.RunAsync(
                server,
                serverReader,
                validation.Request!,
                fanout,
                timeoutSource.Token).AsTask();

            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                clientReader,
                32 * 1024,
                timeoutSource.Token);
            Assert.Equal(200, response.StatusCode);

            await using (var atomReader = new PcpAtomPipeMessageReader(
                clientReader,
                maxBufferedBytes: 64 * 1024))
            {
                var oleh = await atomReader.ReadNextAsync(timeoutSource.Token);
                var okay = await atomReader.ReadNextAsync(timeoutSource.Token);
                Assert.Equal(PcpKnownAtomNames.Oleh, oleh.Name);
                Assert.Equal(PcpKnownAtomNames.Okay, okay.Name);
            }

            await client.WriteAsync(
                EncodeTerminal(PcpKnownAtomNames.Quit),
                timeoutSource.Token);
            var result = await handlerTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(PcpDownstreamChildEndReason.QuitReceived, result.EndReason);
            Assert.Equal(200, result.HttpStatusCode);
            Assert.True(result.HandshakeAccepted);
            Assert.Equal(0, fanout.ActiveChildCount);

            await client.WriteAsync(new byte[] { 0x7a }, timeoutSource.Token);
            var serverMarker = await serverReader.ReadAsync(timeoutSource.Token);
            Assert.True(serverMarker.Buffer.Length >= 1);
            Assert.Equal(0x7a, serverMarker.Buffer.Slice(0, 1).FirstSpan[0]);
            var serverConsumed = serverMarker.Buffer.GetPosition(1);
            serverReader.AdvanceTo(serverConsumed, serverConsumed);

            await server.WriteAsync(new byte[] { 0x7b }, timeoutSource.Token);
            var clientMarker = await clientReader.ReadAsync(timeoutSource.Token);
            Assert.True(clientMarker.Buffer.Length >= 1);
            Assert.Equal(0x7b, clientMarker.Buffer.Slice(0, 1).FirstSpan[0]);
            var clientConsumed = clientMarker.Buffer.GetPosition(1);
            clientReader.AdvanceTo(clientConsumed, clientConsumed);
        }
        finally
        {
            await serverReader.CompleteAsync();
            await clientReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task SharedListenerUsesProductionChildHandoffForPipelinedHelo()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        await using var route = registry.Register(ChannelId, fanout);
        var handler = new PcpDownstreamChildSessionRoutedConnectionHandler(
            LocalSessionId);
        await using var listener = new PcpSharedDownstreamRelayListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            registry,
            handler,
            CreateTransportOptions());
        using var timeoutSource = new CancellationTokenSource(TestTimeout);

        var runTask = listener.RunAsync(timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            CreateTransportOptions()).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var clientReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await client.WriteAsync(
                CreatePipelinedRequestAndHelo(),
                timeoutSource.Token);

            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                clientReader,
                32 * 1024,
                timeoutSource.Token);
            Assert.Equal(200, response.StatusCode);

            await using (var atomReader = new PcpAtomPipeMessageReader(
                clientReader,
                maxBufferedBytes: 64 * 1024))
            {
                var oleh = await atomReader.ReadNextAsync(timeoutSource.Token);
                var okay = await atomReader.ReadNextAsync(timeoutSource.Token);
                Assert.Equal(PcpKnownAtomNames.Oleh, oleh.Name);
                Assert.Equal(PcpKnownAtomNames.Okay, okay.Name);
            }

            await client.WriteAsync(
                EncodeTerminal(PcpKnownAtomNames.Quit),
                timeoutSource.Token);

            while (listener.Snapshot.CompletedConnectionCount == 0)
            {
                await Task.Delay(10, timeoutSource.Token);
            }
        }
        finally
        {
            await clientReader.CompleteAsync();
        }

        await listener.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Equal(
            PcpSharedDownstreamRelayListenerEndReason.Stopped,
            result.EndReason);
        Assert.Equal(1, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(1, result.Snapshot.RoutedConnectionCount);
        Assert.Equal(1, result.Snapshot.CompletedConnectionCount);
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.HandlerFailureCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    private static TcpTransportOptions CreateTransportOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
    }

    private static byte[] CreatePipelinedRequestAndHelo()
    {
        var request = new PcpRelayRequest
        {
            ChannelId = ChannelId.Value,
            Host = "127.0.0.1",
        }.Encode();
        var helo = EncodeAtom(PcpHeloCodec.CreateHelo(
            "PeerCastNova/Test",
            Guid.NewGuid()));
        var bytes = new byte[request.Length + helo.Length];
        request.AsSpan().CopyTo(bytes);
        helo.AsSpan().CopyTo(bytes.AsSpan(request.Length));
        return bytes;
    }

    private static byte[] EncodeTerminal(PcpAtomName name)
    {
        return EncodeAtom(PcpAtom.Leaf(
            name,
            PcpWireValueCodec.EncodeUInt32(0)));
    }

    private static byte[] EncodeAtom(PcpAtom atom)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);
        return writer.WrittenSpan.ToArray();
    }
}
