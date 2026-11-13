using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531DC8DFC7FB42928AC2C0A626517A87",
        "N");

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Theory]
    [InlineData(4096, 0U)]
    [InlineData(1, 1003U)]
    [InlineData(4096, 1000U)]
    [InlineData(1, 1007U)]
    public async Task Run_CoalescedOkayChannelAndQuitUseSamePipeReader(int chunkSize, uint quitCode)
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 2));
        var serverTask = RunAcceptedAttemptAsync(
            listener,
            expectedPosition: 0,
            SessionId.Value,
            [
                CreatePacketAtom(100, [0x10, 0x20, 0x30]),
                CreateQuitAtom(quitCode),
            ],
            timeoutSource.Token, httpChunkSize: chunkSize);

        var result = await session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            timeoutSource.Token);
        await serverTask;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(new PcpQuitInfo(quitCode), result.Quit);
        Assert.Equal(SessionId, tracker.Snapshot.SessionId);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(0, result.ReconnectCount);
        Assert.Equal(1, result.ChannelMessageCount);
        Assert.Equal(1, result.PacketCount);
        Assert.Equal(3, result.PayloadByteCount);
        Assert.Equal(ChannelSessionState.Faulted, tracker.Snapshot.State);
        Assert.Equal(100, tracker.Snapshot.StreamPosition);
        AssertFault(
            tracker,
            ChannelFaultCategory.RemoteEnded,
            ChannelFaultStage.Receiving,
            attemptCount: 1,
            reconnectCount: 0);

        var packet = Assert.Single(consumer.Packets);
        Assert.Equal(0, packet.Sequence);
        Assert.Equal([0x10, 0x20, 0x30], packet.Payload);
    }

    [Fact]
    public async Task Run_ReconnectPreservesPositionSessionIdAndPacketSequence()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 1));
        var serverTask = RunTwoAcceptedAttemptsAsync(
            listener,
            SessionId.Value,
            timeoutSource.Token);

        var result = await session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            timeoutSource.Token);
        await serverTask;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(1, result.ReconnectCount);
        Assert.Equal(2, result.ChannelMessageCount);
        Assert.Equal(2, result.PacketCount);
        Assert.Equal(2, result.PayloadByteCount);
        Assert.Equal(200, tracker.Snapshot.StreamPosition);
        AssertFault(
            tracker,
            ChannelFaultCategory.RemoteEnded,
            ChannelFaultStage.Receiving,
            attemptCount: 2,
            reconnectCount: 1);
        Assert.Collection(
            consumer.Packets,
            first =>
            {
                Assert.Equal(0, first.Sequence);
                Assert.Equal(100U, first.WirePosition);
            },
            second =>
            {
                Assert.Equal(1, second.Sequence);
                Assert.Equal(200U, second.WirePosition);
            });
    }

    [Fact]
    public async Task Stop_CancelsBlockedReadClosesConnectionAndStopsCore()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 0));
        var serverTask = RunHeldAcceptedPeerAsync(
            listener,
            SessionId.Value,
            timeoutSource.Token);
        var runTask = session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            timeoutSource.Token);

        await WaitForStateAsync(
            tracker,
            ChannelSessionState.Receiving,
            timeoutSource.Token);
        await session.StopAsync(timeoutSource.Token);
        var result = await runTask;
        await serverTask;

        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.EndReason);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal(0, result.ChannelMessageCount);
        Assert.Equal(ChannelSessionState.Stopped, tracker.Snapshot.State);
        Assert.Null(tracker.Snapshot.Fault);
        Assert.Empty(consumer.Packets);
    }

    [Theory]
    [InlineData(1003U, PcpChannelSessionEndReason.RelayFull)]
    [InlineData(0U, PcpChannelSessionEndReason.RelayFull)]
    [InlineData(1005U, PcpChannelSessionEndReason.ProtocolRejected)]
    [InlineData(1007U, PcpChannelSessionEndReason.ProtocolRejected)]
    public async Task Run_HandshakeQuitIsTerminalWithoutReconnect(uint code, PcpChannelSessionEndReason endReason)
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 2));
        var serverTask = RunRelayFullPeerAsync(
            listener,
            SessionId.Value,
            timeoutSource.Token, code);

        var result = await session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            timeoutSource.Token);
        await serverTask;

        Assert.Equal(endReason, result.EndReason);
        Assert.Equal(new PcpQuitInfo(code), result.Quit);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(0, result.ReconnectCount);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal(ChannelSessionState.Faulted, tracker.Snapshot.State);
        AssertFault(
            tracker,
            endReason == PcpChannelSessionEndReason.RelayFull
                ? ChannelFaultCategory.RemoteUnavailable : ChannelFaultCategory.RemoteRejected,
            ChannelFaultStage.Negotiating,
            attemptCount: 1,
            reconnectCount: 0);
    }

    [Fact]
    public async Task Run_ConsumerIOExceptionIsWrappedAndNeverReconnected()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            new ThrowingPacketConsumer(),
            CreateOptions(maxReconnectAttempts: 2));
        var serverTask = RunAcceptedAttemptAsync(
            listener,
            expectedPosition: 0,
            SessionId.Value,
            [CreatePacketAtom(100, [0x01])],
            timeoutSource.Token);

        var exception = await Assert.ThrowsAsync<PcpChannelConsumerException>(
            () => session.RunAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token));
        await serverTask;

        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal(ChannelSessionState.Faulted, tracker.Snapshot.State);
        AssertFault(
            tracker,
            ChannelFaultCategory.ConsumerFailed,
            ChannelFaultStage.Consuming,
            attemptCount: 1,
            reconnectCount: 0);
    }

    [Fact]
    public async Task Run_InitialPositionSeedsFirstRelayRequest()
    {
        const long initialPosition = 8192;
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 0));
        var serverTask = RunAcceptedAttemptAsync(
            listener,
            expectedPosition: initialPosition,
            SessionId.Value,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var result = await session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            initialPosition,
            timeoutSource.Token);
        await serverTask;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(0, result.PacketCount);
        AssertFault(
            tracker,
            ChannelFaultCategory.RemoteEnded,
            ChannelFaultStage.Receiving,
            attemptCount: 1,
            reconnectCount: 0);
    }

    [Fact]
    public async Task RequestStop_FromPacketConsumerStopsWithoutDeadlock()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new StoppingPacketConsumer();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            CreateOptions(maxReconnectAttempts: 0));
        consumer.StopRequest = session.RequestStop;
        var serverTask = RunPacketThenHoldPeerAsync(
            listener,
            SessionId.Value,
            timeoutSource.Token);

        var result = await session.RunAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            timeoutSource.Token);
        await serverTask;

        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.EndReason);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal(1, result.ChannelMessageCount);
        Assert.Equal(1, result.PacketCount);
        Assert.Equal(ChannelSessionState.Stopped, tracker.Snapshot.State);
        Assert.Null(tracker.Snapshot.Fault);
    }

    [Fact]
    public async Task Run_RetryExhaustionPublishesSafeStageAndCounts()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            new CollectingPacketConsumer(),
            CreateOptions(maxReconnectAttempts: 1));
        var serverTask = RunTruncatedHandshakeAttemptsAsync(
            listener,
            attemptCount: 2,
            timeoutSource.Token);

        await Assert.ThrowsAnyAsync<IOException>(
            () => session.RunAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token));
        await serverTask;

        AssertFault(
            tracker,
            ChannelFaultCategory.ConnectionFailed,
            ChannelFaultStage.Negotiating,
            attemptCount: 2,
            reconnectCount: 1);
    }

    [Fact]
    public async Task Run_HandshakeTimeoutPublishesFixedTimeoutFault()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var options = CreateOptions(maxReconnectAttempts: 0) with
        {
            HandshakeTimeout = TimeSpan.FromMilliseconds(50),
        };
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            new CollectingPacketConsumer(),
            options);
        var serverTask = RunHeldHttpPeerAsync(
            listener,
            timeoutSource.Token);

        await Assert.ThrowsAsync<TimeoutException>(
            () => session.RunAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token));
        await serverTask;

        AssertFault(
            tracker,
            ChannelFaultCategory.TimedOut,
            ChannelFaultStage.Negotiating,
            attemptCount: 1,
            reconnectCount: 0);
    }

    [Fact]
    public async Task Run_ChannelMismatchPublishesProtocolFaultWithoutId()
    {
        var (listener, connector) = CreateTransportPair();
        await using var ownedListener = listener;
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        await using var session = new PcpChannelSession(
            connector,
            tracker,
            new CollectingPacketConsumer(),
            CreateOptions(maxReconnectAttempts: 0));
        var serverTask = RunAcceptedAttemptAsync(
            listener,
            expectedPosition: 0,
            SessionId.Value,
            [
                CreatePacketAtom(
                    Guid.ParseExact(
                        "102132435465768798A9BABBDCDDEDEF",
                        "N"),
                    position: 100,
                    [0x01]),
            ],
            timeoutSource.Token);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => session.RunAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token));
        await serverTask;

        AssertFault(
            tracker,
            ChannelFaultCategory.ProtocolViolation,
            ChannelFaultStage.Receiving,
            attemptCount: 1,
            reconnectCount: 0);
    }

    private static async Task RunTwoAcceptedAttemptsAsync(
        TcpTransportListener listener,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        await RunAcceptedAttemptAsync(
            listener,
            expectedPosition: 0,
            expectedSessionId,
            [CreatePacketAtom(100, [0x01])],
            cancellationToken);

        await RunAcceptedAttemptAsync(
            listener,
            expectedPosition: 100,
            expectedSessionId,
            [
                CreatePacketAtom(200, [0x02]),
                CreateQuitAtom(),
            ],
            cancellationToken);
    }

    private static async Task RunTruncatedHandshakeAttemptsAsync(
        TcpTransportListener listener,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            await using var connection = await listener.AcceptAsync(
                cancellationToken);
            var pipeReader = PipeReader.Create(
                connection.TransportStream,
                new StreamPipeReaderOptions(leaveOpen: true));

            try
            {
                await ReadAndAssertRequestAsync(
                    pipeReader,
                    expectedPosition: 0,
                    cancellationToken);
            }
            finally
            {
                await pipeReader.CompleteAsync();
            }
        }
    }

    private static async Task RunHeldHttpPeerAsync(
        TcpTransportListener listener,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        await ReadAndAssertRequestAsync(
            pipeReader,
            expectedPosition: 0,
            cancellationToken);
        await pipeReader.CompleteAsync();

        try
        {
            var buffer = new byte[1];
            _ = await connection.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException)
        {
            // Handshake timeout may reset rather than gracefully close TCP.
        }
        catch (SocketException)
        {
            // Handshake timeout may reset rather than gracefully close TCP.
        }
    }

    private static async Task RunAcceptedAttemptAsync(
        TcpTransportListener listener,
        long expectedPosition,
        Guid expectedSessionId,
        IReadOnlyList<PcpAtom> postHandshakeAtoms,
        CancellationToken cancellationToken,
        int? httpChunkSize = null,
        IReadOnlyList<PcpAtom>? preOkayAtoms = null)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertRequestAsync(
                pipeReader,
                expectedPosition,
                cancellationToken);
            var atoms = new List<PcpAtom>
            {
                CreateOlehAtom(),
            };
            if (preOkayAtoms is not null) atoms.AddRange(preOkayAtoms);
            atoms.Add(PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)));
            atoms.AddRange(postHandshakeAtoms);

            if (httpChunkSize is { } chunkSize)
            {
                var bytes = new ArrayBufferWriter<byte>();
                bytes.Write(System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.0 200 OK\r\nContent-Type: application/x-peercast-pcp\r\n\r\n"));
                foreach (var atom in atoms)
                    PcpAtomTreeCodec.Write(bytes, atom);
                for (var offset = 0; offset < bytes.WrittenCount; offset += chunkSize)
                    await connection.WriteAsync(bytes.WrittenMemory.Slice(offset,
                        Math.Min(chunkSize, bytes.WrittenCount - offset)), cancellationToken);
                await ReadAndAssertHeloAsync(pipeReader, expectedSessionId, cancellationToken);
            }
            else
            {
                await WriteHttpResponseAsync(connection, 200, "OK", cancellationToken);
                await ReadAndAssertHeloAsync(pipeReader, expectedSessionId, cancellationToken);
                await WriteAtomsAsync(connection, atoms, cancellationToken);
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunHeldAcceptedPeerAsync(
        TcpTransportListener listener,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        await ReadAndAssertRequestAsync(
            pipeReader,
            expectedPosition: 0,
            cancellationToken);
        await WriteHttpResponseAsync(
            connection,
            statusCode: 200,
            "OK",
            cancellationToken);
        await ReadAndAssertHeloAsync(
            pipeReader,
            expectedSessionId,
            cancellationToken);
        await WriteAtomsAsync(
            connection,
            [
                CreateOlehAtom(),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Okay,
                    PcpWireValueCodec.EncodeUInt32(1)),
            ],
            cancellationToken);
        await pipeReader.CompleteAsync();

        try
        {
            var buffer = new byte[1];
            _ = await connection.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException)
        {
            // Session shutdown may reset rather than gracefully close TCP.
        }
        catch (SocketException)
        {
            // Session shutdown may reset rather than gracefully close TCP.
        }
    }

    private static async Task RunPacketThenHoldPeerAsync(
        TcpTransportListener listener,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        await ReadAndAssertRequestAsync(
            pipeReader,
            expectedPosition: 0,
            cancellationToken);
        await WriteHttpResponseAsync(
            connection,
            statusCode: 200,
            "OK",
            cancellationToken);
        await ReadAndAssertHeloAsync(
            pipeReader,
            expectedSessionId,
            cancellationToken);
        await WriteAtomsAsync(
            connection,
            [
                CreateOlehAtom(),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Okay,
                    PcpWireValueCodec.EncodeUInt32(1)),
                CreatePacketAtom(100, [0x01]),
            ],
            cancellationToken);
        await pipeReader.CompleteAsync();

        try
        {
            var buffer = new byte[1];
            _ = await connection.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException)
        {
            // Session shutdown may reset rather than gracefully close TCP.
        }
        catch (SocketException)
        {
            // Session shutdown may reset rather than gracefully close TCP.
        }
    }

    private static async Task RunRelayFullPeerAsync(
        TcpTransportListener listener,
        Guid expectedSessionId,
        CancellationToken cancellationToken,
        uint quitCode = 1003)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(
                connection,
                statusCode: 503,
                "Service Unavailable",
                cancellationToken);
            await ReadAndAssertHeloAsync(
                pipeReader,
                expectedSessionId,
                cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    CreateQuitAtom(quitCode),
                ],
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task ReadAndAssertRequestAsync(
        PipeReader pipeReader,
        long expectedPosition,
        CancellationToken cancellationToken)
    {
        var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
            pipeReader,
            32 * 1024,
            cancellationToken);

        Assert.Equal("GET", request.Method);
        Assert.Equal(
            expectedPosition.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            request.Headers["x-peercast-pos"]);
    }

    private static async Task ReadAndAssertHeloAsync(
        PipeReader pipeReader,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
        var helo = await atomReader.ReadNextAsync(cancellationToken);
        var sessionAtom = helo.FindFirst(PcpKnownAtomNames.HeloSessionId);

        Assert.Equal(PcpKnownAtomNames.Helo, helo.Name);
        Assert.NotNull(sessionAtom);
        Assert.Equal(
            expectedSessionId,
            PcpWireValueCodec.DecodeGuid(sessionAtom!.Content.Span));
    }

    private static async Task WriteHttpResponseAsync(
        TcpTransportConnection connection,
        int statusCode,
        string reasonPhrase,
        CancellationToken cancellationToken)
    {
        var response = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.0 {statusCode} {reasonPhrase}\r\n" +
            "Server: PeerCastStation/Test\r\n" +
            "Content-Type:application/x-peercast-pcp\r\n\r\n");

        await connection.WriteAsync(response, cancellationToken);
    }

    private static async Task WriteAtomsAsync(
        TcpTransportConnection connection,
        IReadOnlyList<PcpAtom> atoms,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();

        foreach (var atom in atoms)
        {
            PcpAtomTreeCodec.Write(writer, atom);
        }

        await connection.WriteAsync(
            writer.WrittenMemory,
            cancellationToken);
    }

    private static PcpAtom CreateOlehAtom()
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Oleh,
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(
                    "PeerCastStation/Test")),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(
                    Guid.ParseExact(
                        "102132435465768798A9BABBDCDDEDEF",
                        "N"))),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(1218)));
    }

    private static PcpAtom CreatePacketAtom(
        uint position,
        byte[] payload)
    {
        return CreatePacketAtom(ChannelGuid, position, payload);
    }

    private static PcpAtom CreatePacketAtom(
        Guid channelId,
        uint position,
        byte[] payload)
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(channelId)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Type,
                    PcpWireValueCodec.EncodeUInt32(1)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Position,
                    PcpWireValueCodec.EncodeUInt32(position)),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, payload)));
    }

    private static PcpAtom CreateQuitAtom(uint code = 0)
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.Quit,
            PcpWireValueCodec.EncodeUInt32(code));
    }

    private static (TcpTransportListener Listener, TcpTransportConnector Connector)
        CreateTransportPair()
    {
        var options = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
        var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            options);
        listener.Start();
        return (listener, new TcpTransportConnector(options));
    }

    private static PcpChannelSessionOptions CreateOptions(
        int maxReconnectAttempts)
    {
        return new PcpChannelSessionOptions
        {
            HandshakeTimeout = TimeSpan.FromSeconds(2),
            MaxReconnectAttempts = maxReconnectAttempts,
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            Stream = new PcpChannelStreamOptions
            {
                ReadIdleTimeout = TimeSpan.FromSeconds(2),
            },
        };
    }

    private static ChannelStatusTracker CreateTracker()
    {
        return new ChannelStatusTracker(
            new ChannelId(ChannelGuid),
            SessionId);
    }

    private static async Task WaitForStateAsync(
        ChannelStatusTracker tracker,
        ChannelSessionState expected,
        CancellationToken cancellationToken)
    {
        while (tracker.Snapshot.State != expected)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(5),
                cancellationToken);
        }
    }

    private static void AssertFault(
        ChannelStatusTracker tracker,
        ChannelFaultCategory category,
        ChannelFaultStage stage,
        int attemptCount,
        int reconnectCount)
    {
        var fault = tracker.Snapshot.Fault;

        Assert.NotNull(fault);
        Assert.Equal(category, fault!.Category);
        Assert.Equal(stage, fault.Stage);
        Assert.Equal(attemptCount, fault.AttemptCount);
        Assert.Equal(reconnectCount, fault.ReconnectCount);
    }

    private sealed class CollectingPacketConsumer : IPcpChannelPacketConsumer
    {
        public List<ObservedPacket> Packets { get; } = [];

        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Packets.Add(new ObservedPacket(
                packet.Sequence,
                packet.WirePosition,
                packet.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPacketConsumer : IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            _ = packet.Payload.Length;
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Synthetic consumer failure.");
        }
    }

    private sealed class StoppingPacketConsumer : IPcpChannelPacketConsumer
    {
        public Action? StopRequest { get; set; }

        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            _ = packet.Payload.Length;
            cancellationToken.ThrowIfCancellationRequested();
            StopRequest?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ObservedPacket(
        long Sequence,
        uint WirePosition,
        byte[] Payload);
}
