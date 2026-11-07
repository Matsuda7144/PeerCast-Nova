using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamChildSessionTests
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
    public async Task Session_NegotiatesWritesChanAndHandlesRemoteQuit()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        TcpTransportConnection? observed = null;
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options: null,
            timeoutSource.Token, observedConnection: c => observed = c);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                ChannelId.Value,
                timeoutSource.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            Assert.Equal(200, handshake.HttpStatusCode);

            byte[] payload = [0x10, 0x20, 0x30, 0x40];
            await fanout.ConsumeAsync(
                CreatePacket(payload),
                timeoutSource.Token);

            await using var atomReader = new PcpAtomPipeMessageReader(
                pipeReader,
                maxBufferedBytes: 1024 * 1024);
            var channel = await atomReader.ReadNextAsync(
                timeoutSource.Token);
            AssertChannelPacket(channel, payload);
            Assert.NotNull(observed);
            Assert.True(observed.Traffic.IsEstablished);

            await client.WriteAsync(
                EncodeTerminal(PcpKnownAtomNames.Quit),
                timeoutSource.Token);

            var result = await serverTask.WaitAsync(timeoutSource.Token);
            Assert.Equal(
                PcpDownstreamChildEndReason.QuitReceived,
                result.EndReason);
            Assert.Equal(200, result.HttpStatusCode);
            Assert.True(result.HandshakeAccepted);
            Assert.Equal(1, result.DeliveredPacketCount);
            Assert.Equal(payload.Length, result.DeliveredByteCount);
            Assert.Equal(0, result.DiscardedPacketCount);
            Assert.Equal(0, fanout.ActiveChildCount);
            Assert.False(observed.Traffic.IsEstablished);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_Uses503OlehQuitWhenBoundedFanoutIsFull()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions { MaxChildren = 1 });
        await using var occupied = fanout.AddChild(new NoopRelaySink());
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options: null,
            timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                ChannelId.Value,
                timeoutSource.Token);
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(PcpCompatibilityOutcome.RelayFull, handshake.Outcome);
            Assert.Equal(503, handshake.HttpStatusCode);
            Assert.Equal(PcpKnownAtomNames.Oleh, handshake.ReceivedAtomNames[0]);
            Assert.Equal(PcpKnownAtomNames.Quit, handshake.TerminalAtom);
            Assert.Equal(
                PcpDownstreamChildEndReason.RelayFull,
                result.EndReason);
            Assert.Equal(503, result.HttpStatusCode);
            Assert.False(result.HandshakeAccepted);
            Assert.Equal(1, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_RejectsWrongChannelBeforeChildRegistration()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options: null,
            timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                Guid.NewGuid(),
                timeoutSource.Token);
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(
                PcpCompatibilityOutcome.HttpRejected,
                handshake.Outcome);
            Assert.Equal(404, handshake.HttpStatusCode);
            Assert.Equal(
                PcpDownstreamChildEndReason.ProtocolRejected,
                result.EndReason);
            Assert.Equal(404, result.HttpStatusCode);
            Assert.False(result.HandshakeAccepted);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_RejectsNonAsciiHttpWithoutPersistingInput()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options: null,
            timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var prefix = Encoding.ASCII.GetBytes(string.Concat(
                "GET /channel/",
                ChannelId.Value.ToString("N"),
                " HTTP/1.0\r\nX-Test:"));
            var suffix = Encoding.ASCII.GetBytes(
                "\r\nx-peercast-pcp:1\r\nx-peercast-pos:0\r\n\r\n");
            var request = new byte[prefix.Length + 1 + suffix.Length];
            prefix.CopyTo(request, 0);
            request[prefix.Length] = 0x80;
            suffix.CopyTo(request, prefix.Length + 1);

            await client.WriteAsync(request, timeoutSource.Token);
            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                pipeReader,
                32 * 1024,
                timeoutSource.Token);
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(
                PcpDownstreamChildEndReason.ProtocolRejected,
                result.EndReason);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_HandshakeTimeoutStopsReservedChild()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        var options = new PcpDownstreamChildOptions
        {
            HandshakeTimeout = TimeSpan.FromMilliseconds(150),
            WriteTimeout = TimeSpan.FromSeconds(2),
        };
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options,
            timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await client.WriteAsync(
                new PcpRelayRequest
                {
                    ChannelId = ChannelId.Value,
                    Host = "127.0.0.1",
                }.Encode(),
                timeoutSource.Token);
            var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
                pipeReader,
                32 * 1024,
                timeoutSource.Token);
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(
                PcpDownstreamChildEndReason.HandshakeTimedOut,
                result.EndReason);
            Assert.False(result.HandshakeAccepted);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_FanoutStopEndsAcceptedChildWithoutInboundData()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        var fanout = new PcpRelayFanout();
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options: null,
            timeoutSource.Token);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                ChannelId.Value,
                timeoutSource.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);

            await fanout.DisposeAsync();
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(
                PcpDownstreamChildEndReason.FanoutStopped,
                result.EndReason);
            Assert.True(result.HandshakeAccepted);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
            await fanout.DisposeAsync();
        }
    }

    [Fact]
    public async Task Session_NonReadingPeerIsolatedByFiniteWriteTimeout()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout();
        listener.Start();

        var options = new PcpDownstreamChildOptions
        {
            WriteTimeout = TimeSpan.FromMilliseconds(150),
        };
        var packetWriter = new BlockingPacketWriter();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options,
            timeoutSource.Token,
            packetWriter);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                ChannelId.Value,
                timeoutSource.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);

            await fanout.ConsumeAsync(
                CreatePacket([0x55]),
                timeoutSource.Token);
            var result = await serverTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(
                PcpDownstreamChildEndReason.WriteTimedOut,
                result.EndReason);
            Assert.True(result.HandshakeAccepted);
            Assert.Equal(0, result.DeliveredPacketCount);
            Assert.Equal(1, result.DiscardedPacketCount);
            Assert.Equal(1, result.DiscardedByteCount);
            Assert.Equal(2, packetWriter.CallCount);
            Assert.Equal(0, fanout.ActiveChildCount);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public async Task Session_UncooperativeWriterReturnsAtFiniteCleanupBoundary()
    {
        var transportOptions = CreateTransportOptions();
        await using var listener = CreateListener(transportOptions);
        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions
            {
                MaxChildren = 1,
                ChildStopTimeout = TimeSpan.FromMilliseconds(50),
            });
        listener.Start();

        var options = new PcpDownstreamChildOptions
        {
            WriteTimeout = TimeSpan.FromSeconds(5),
        };
        var packetWriter = new UncooperativePacketWriter();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = AcceptAndRunAsync(
            listener,
            fanout,
            options,
            timeoutSource.Token,
            packetWriter);
        await using var client = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            client.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var handshake = await NegotiateAsync(
                client,
                pipeReader,
                listener.LocalEndPoint,
                ChannelId.Value,
                timeoutSource.Token);
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);

            await fanout.ConsumeAsync(
                CreatePacket([0x66]),
                timeoutSource.Token);
            await packetWriter.Entered.Task.WaitAsync(timeoutSource.Token);
            await client.WriteAsync(
                EncodeTerminal(PcpKnownAtomNames.Quit),
                timeoutSource.Token);

            var result = await serverTask.WaitAsync(timeoutSource.Token);
            Assert.Equal(
                PcpDownstreamChildEndReason.QuitReceived,
                result.EndReason);
            Assert.False(result.CleanupCompleted);
            Assert.Equal(0, fanout.ActiveChildCount);
            Assert.False(fanout.TryAddChild(
                new NoopRelaySink(),
                out _));

            packetWriter.Release.TrySetResult();

            PcpRelayChildRegistration? replacement = null;
            while (!fanout.TryAddChild(
                new NoopRelaySink(),
                out replacement))
            {
                await Task.Delay(10, timeoutSource.Token);
            }

            await replacement.DisposeAsync();
        }
        finally
        {
            packetWriter.Release.TrySetResult();
            await pipeReader.CompleteAsync();
        }
    }

    [Fact]
    public void OptionsAndResult_KeepFinitePrivacySafeBoundary()
    {
        Assert.Throws<InvalidOperationException>(
            () => new PcpDownstreamChildOptions
            {
                HandshakeTimeout = Timeout.InfiniteTimeSpan,
            }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new PcpDownstreamChildOptions
            {
                MaxPacketBytes =
                    PcpDownstreamChildOptions.MaximumPacketBytes + 1,
            }.Validate());
        Assert.Throws<ArgumentException>(
            () => new PcpDownstreamChildOptions
            {
                UserAgent = "Nova\r\nInjected: value",
            }.Validate());

        var properties = typeof(PcpDownstreamChildResult).GetProperties();
        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            property => typeof(Exception).IsAssignableFrom(
                property.PropertyType));
    }

    private static TcpTransportListener CreateListener(
        TcpTransportOptions options)
    {
        return new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            options);
    }

    private static TcpTransportOptions CreateTransportOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
    }

    private static async Task<PcpDownstreamChildResult> AcceptAndRunAsync(
        TcpTransportListener listener,
        PcpRelayFanout fanout,
        PcpDownstreamChildOptions? options,
        CancellationToken cancellationToken,
        IPcpDownstreamPacketWriter? packetWriter = null,
        Action<TcpTransportConnection>? observedConnection = null)
    {
        var connection = await listener.AcceptAsync(cancellationToken);
        Assert.False(connection.Traffic.IsEstablished);
        observedConnection?.Invoke(connection);
        await using var session = new PcpDownstreamChildSession(
            connection,
            fanout,
            ChannelId,
            LocalSessionId,
            options,
            packetWriter);
        var result = await session.RunAsync(cancellationToken);
        Assert.False(connection.Traffic.IsEstablished);
        return result;
    }

    private static ValueTask<PcpRelayHandshakeResult> NegotiateAsync(
        TcpTransportConnection client,
        PipeReader pipeReader,
        IPEndPoint serverEndPoint,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        return PcpRelayHandshakeClient.NegotiateAsync(
            client,
            pipeReader,
            serverEndPoint.Address.ToString(),
            serverEndPoint.Port,
            channelId,
            Guid.NewGuid(),
            streamPosition: 0,
            pcpVersion: 1,
            new PcpRelayHandshakeSettings(
                MaxHttpHeaderBytes: 32 * 1024,
                MaxHandshakeAtomBytes: 64 * 1024,
                MaxAtomsBeforeTerminal: 16,
                UserAgent: "PeerCastNova/Test",
                ServantVersion: PcpHeloCodec.LegacyServantVersion),
            cancellationToken);
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

    private static byte[] EncodeTerminal(PcpAtomName name)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            writer,
            PcpAtom.Leaf(
                name,
                PcpWireValueCodec.EncodeUInt32(0)));
        return writer.WrittenSpan.ToArray();
    }

    private static void AssertChannelPacket(
        PcpAtom channel,
        byte[] expectedPayload)
    {
        Assert.Equal(PcpKnownAtomNames.Channel, channel.Name);
        Assert.True(channel.IsParent);
        Assert.Equal(
            ChannelId.Value,
            PcpWireValueCodec.DecodeGuid(
                channel.FindFirst(PcpKnownAtomNames.ChannelId)!.Content.Span));

        var packet = channel.FindFirst(PcpKnownAtomNames.ChannelPacket)!;
        Assert.Equal(
            1U,
            PcpWireValueCodec.DecodeUInt32(
                packet.FindFirst(PcpKnownAtomNames.Type)!.Content.Span));
        Assert.Equal(
            1234U,
            PcpWireValueCodec.DecodeUInt32(
                packet.FindFirst(PcpKnownAtomNames.Position)!.Content.Span));
        Assert.Equal(
            expectedPayload,
            packet.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());
    }

    private sealed class NoopRelaySink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPacketWriter :
        IPcpDownstreamPacketWriter
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return;
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
        }
    }

    private sealed class UncooperativePacketWriter :
        IPcpDownstreamPacketWriter
    {
        private int _callCount;

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
