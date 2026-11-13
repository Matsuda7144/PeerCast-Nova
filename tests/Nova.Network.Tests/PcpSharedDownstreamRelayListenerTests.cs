using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpSharedDownstreamRelayListenerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly ChannelId FirstChannelId = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    private static readonly ChannelId SecondChannelId = new(
        Guid.ParseExact("102132435465768798a9babbdcddedef", "N"));

    [Fact]
    public async Task ListenerRoutesDifferentChannelsThroughOneBoundPort()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var firstFanout = new PcpRelayFanout();
        await using var secondFanout = new PcpRelayFanout();
        await using var firstRoute = registry.Register(
            FirstChannelId,
            firstFanout);
        await using var secondRoute = registry.Register(
            SecondChannelId,
            secondFanout);
        var handler = new RecordingRoutedHandler(expectedCallCount: 2);
        await using var listener = new PcpSharedDownstreamRelayListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            registry,
            handler);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);

        var runTask = listener.RunAsync(timeoutSource.Token);
        var boundEndPoint = listener.LocalEndPoint;
        Assert.NotEqual(0, boundEndPoint.Port);

        var connector = new TcpTransportConnector();
        await using var firstClient = await connector.ConnectAsync(
            boundEndPoint,
            timeoutSource.Token);
        await using var secondClient = await connector.ConnectAsync(
            boundEndPoint,
            timeoutSource.Token);

        await firstClient.WriteAsync(
            new PcpRelayRequest
            {
                ChannelId = FirstChannelId.Value,
                Host = "127.0.0.1",
            }.Encode(),
            timeoutSource.Token);
        await secondClient.WriteAsync(
            new PcpRelayRequest
            {
                ChannelId = SecondChannelId.Value,
                Host = "127.0.0.1",
            }.Encode(),
            timeoutSource.Token);

        await handler.WaitForExpectedCallsAsync(timeoutSource.Token);
        await listener.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        var calls = handler.Calls;

        Assert.Equal(
            PcpSharedDownstreamRelayListenerEndReason.Stopped,
            result.EndReason);
        Assert.Equal(2, calls.Count);
        Assert.Contains(
            calls,
            call => call.ChannelId == FirstChannelId.Value &&
                ReferenceEquals(call.Fanout, firstFanout));
        Assert.Contains(
            calls,
            call => call.ChannelId == SecondChannelId.Value &&
                ReferenceEquals(call.Fanout, secondFanout));
        Assert.Equal(2, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(2, result.Snapshot.RoutedConnectionCount);
        Assert.Equal(2, result.Snapshot.CompletedConnectionCount);
        Assert.Equal(0, result.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, result.Snapshot.NotFoundCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task ListenerReturnsNotFoundForUnknownChannelWithoutCallingHandler()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        var handler = new RecordingRoutedHandler(expectedCallCount: 1);
        await using var listener = new PcpSharedDownstreamRelayListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            registry,
            handler);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);

        var runTask = listener.RunAsync(timeoutSource.Token);
        var boundEndPoint = listener.LocalEndPoint;
        var connector = new TcpTransportConnector();
        await using var client = await connector.ConnectAsync(
            boundEndPoint,
            timeoutSource.Token);
        var reader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await client.WriteAsync(
                new PcpRelayRequest
                {
                    ChannelId = FirstChannelId.Value,
                    Host = "127.0.0.1",
                }.Encode(),
                timeoutSource.Token);

            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                reader,
                32 * 1024,
                timeoutSource.Token);
            Assert.Equal(404, response.StatusCode);
        }
        finally
        {
            await reader.CompleteAsync();
        }

        await listener.StopAsync(timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);

        Assert.Empty(handler.Calls);
        Assert.Equal(1, result.Snapshot.AcceptedConnectionCount);
        Assert.Equal(0, result.Snapshot.RoutedConnectionCount);
        Assert.Equal(1, result.Snapshot.NotFoundCount);
        Assert.Equal(0, result.Snapshot.BadRequestCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    private sealed class RecordingRoutedHandler :
        IPcpDownstreamRoutedConnectionHandler
    {
        private readonly object _sync = new();
        private readonly int _expectedCallCount;
        private readonly List<RecordedCall> _calls = [];
        private readonly TaskCompletionSource<bool> _expectedCallsReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingRoutedHandler(int expectedCallCount)
        {
            _expectedCallCount = expectedCallCount;
        }

        public IReadOnlyList<RecordedCall> Calls
        {
            get
            {
                lock (_sync)
                {
                    return _calls.ToArray();
                }
            }
        }

        public ValueTask<PcpDownstreamChildResult> RunAsync(
            TcpTransportConnection connection,
            PipeReader reader,
            PcpDownstreamRelayRequest request,
            PcpRelayFanout fanout,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(fanout);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _calls.Add(new RecordedCall(request.ChannelId, fanout));
                if (_calls.Count >= _expectedCallCount)
                {
                    _expectedCallsReached.TrySetResult(true);
                }
            }

            return ValueTask.FromResult(new PcpDownstreamChildResult(
                PcpDownstreamChildEndReason.Stopped,
                HttpStatusCode: null,
                HandshakeAccepted: false,
                CleanupCompleted: true,
                DeliveredPacketCount: 0,
                DeliveredByteCount: 0,
                DiscardedPacketCount: 0,
                DiscardedByteCount: 0));
        }

        public Task<bool> WaitForExpectedCallsAsync(
            CancellationToken cancellationToken)
        {
            return _expectedCallsReached.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed record RecordedCall(
        Guid ChannelId,
        PcpRelayFanout Fanout);
}
