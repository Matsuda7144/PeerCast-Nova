using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531dc8dfc7fb42928ac2c0a626517a87",
        "N");

    private static readonly ChannelSessionId LocalSessionId = new(
        Guid.ParseExact("3f2504e04f8911d39a0c0305e82c3301", "N"));

    private static readonly Guid RemoteSessionId = Guid.ParseExact(
        "102132435465768798a9babbdcddedef",
        "N");

    [Fact]
    public async Task Coordinator_RelaysContinuousWirePacketsAndDrainsBeforeStop()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePackets = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunAcceptedUpstreamAttemptAsync(
            upstreamListener,
            expectedPosition: 0,
            releasePackets.Task,
            [
                CreatePacketAtom(position: 100, [0x10, 0x20]),
                CreatePacketAtom(position: 102, [0x30]),
                CreateQuitAtom(),
            ],
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);
        var readiness = await coordinator.WaitForReadinessAsync(
            timeoutSource.Token);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePackets.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 2,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(
            new byte[][] { [0x10, 0x20], [0x30] },
            payloads);
        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Ready,
            readiness);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamEnded,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(2, result.Ingress.AcceptedPacketCount);
        Assert.Equal(3, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.Upstream?.EndReason);
        Assert.Equal(2, result.Upstream?.PacketCount);
        Assert.Equal(3, result.Upstream?.PayloadByteCount);
        Assert.Equal(1, result.Downstream?.Snapshot.AcceptedHandshakeCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(3, result.Downstream?.Snapshot.DeliveredByteCount);
        Assert.Equal(0, result.Downstream?.Snapshot.DiscardedPacketCount);
        Assert.Equal(0, result.Downstream?.Snapshot.CleanupIncompleteCount);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
        Assert.Equal(102, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task Coordinator_PreservesDownstreamChildAcrossUpstreamReconnect()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePackets = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunTwoUpstreamAttemptsAsync(
            upstreamListener,
            releasePackets.Task,
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 1);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePackets.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 2,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(new byte[][] { [0x41], [0x42] }, payloads);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamEnded,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.Equal(2, result.Ingress.AcceptedPacketCount);
        Assert.Equal(2, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(2, result.Upstream?.AttemptCount);
        Assert.Equal(1, result.Upstream?.ReconnectCount);
        Assert.Equal(2, result.Upstream?.PacketCount);
        Assert.Equal(1, result.Downstream?.Snapshot.AcceptedConnectionCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(200, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task Coordinator_CallerCancellationStopsOwnedBoundaries()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var callerSource = new CancellationTokenSource();
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunHeldUpstreamAsync(
            upstreamListener,
            accepted,
            timeoutSource.Token);
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);
        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: callerSource.Token);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = coordinator.RunAsync(
                    upstreamListener.LocalEndPoint.Address.ToString(),
                    upstreamListener.LocalEndPoint.Port);
            });

        await accepted.Task.WaitAsync(timeoutSource.Token);
        callerSource.Cancel();
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(
            PcpRelayCoordinatorEndReason.CallerCanceled,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.NotRequired, result.DrainOutcome);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(0, result.Ingress.AcceptedPacketCount);
        Assert.Equal(0, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.Upstream?.EndReason);
        Assert.Equal(
            PcpDownstreamRelayServerEndReason.Stopped,
            result.Downstream?.EndReason);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
    }

    [Fact]
    public async Task Coordinator_ConvertsUpstreamFailureToFixedResult()
    {
        var transportOptions = CreateTransportOptions();
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        upstreamListener.Start();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var releasePacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunPacketThenTruncatedUpstreamAsync(
            upstreamListener,
            releasePacket.Task,
            timeoutSource.Token);
        var tracker = CreateTracker();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            tracker,
            maxReconnectAttempts: 0);

        var runTask = coordinator.RunAsync(
            upstreamListener.LocalEndPoint.Address.ToString(),
            upstreamListener.LocalEndPoint.Port,
            cancellationToken: timeoutSource.Token);

        await using var downstream = await ConnectDownstreamAsync(
            coordinator.LocalEndPoint,
            transportOptions,
            timeoutSource.Token);
        releasePacket.TrySetResult();
        var payloads = await downstream.ReadPayloadsAsync(
            count: 1,
            timeoutSource.Token);
        var result = await runTask.WaitAsync(timeoutSource.Token);
        await upstreamTask;

        Assert.Equal(new byte[][] { [0x51, 0x52] }, payloads);
        Assert.Equal(
            PcpRelayCoordinatorEndReason.UpstreamFailed,
            result.EndReason);
        Assert.Equal(PcpRelayDrainOutcome.Completed, result.DrainOutcome);
        Assert.Null(result.Upstream);
        Assert.Equal(1, result.Ingress.AcceptedPacketCount);
        Assert.Equal(2, result.Ingress.AcceptedPayloadByteCount);
        Assert.Equal(1, coordinator.IngressSnapshot.AcceptedPacketCount);
        Assert.Equal(1, result.Downstream?.Snapshot.DeliveredPacketCount);
        Assert.Equal(2, result.Downstream?.Snapshot.DeliveredByteCount);
        Assert.True(result.ShutdownCompleted);
        Assert.Equal(
            ChannelFaultCategory.ConnectionFailed,
            tracker.Snapshot.Fault?.Category);

        var properties = typeof(PcpRelayCoordinatorResult).GetProperties();
        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(IPEndPoint));

        var ingressProperties = typeof(PcpRelayIngressSnapshot).GetProperties();
        Assert.All(
            ingressProperties,
            property => Assert.Equal(typeof(long), property.PropertyType));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRelayCoordinatorOptions
            {
                DrainTimeout = TimeSpan.Zero,
            }.Validate());
    }

    [Fact]
    public async Task Coordinator_ReadinessReportsOccupiedListenerFailure()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();

        try
        {
            var transportOptions = CreateTransportOptions();
            var occupiedEndPoint = (IPEndPoint)occupied.LocalEndpoint;
            using var timeoutSource = new CancellationTokenSource(TestTimeout);
            await using var coordinator = CreateCoordinator(
                transportOptions,
                CreateTracker(),
                maxReconnectAttempts: 0,
                occupiedEndPoint);

            var runTask = coordinator.RunAsync(
                IPAddress.Loopback.ToString(),
                upstreamPort: 1,
                cancellationToken: timeoutSource.Token);
            var readiness = await coordinator.WaitForReadinessAsync(
                timeoutSource.Token);
            var result = await runTask.WaitAsync(timeoutSource.Token);

            Assert.Equal(
                PcpRelayCoordinatorReadinessOutcome.Failed,
                readiness);
            Assert.Equal(
                PcpRelayCoordinatorEndReason.DownstreamFailed,
                result.EndReason);
            Assert.Throws<InvalidOperationException>(
                () => coordinator.LocalEndPoint);
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public async Task Coordinator_UnstartedStopCompletesReadiness()
    {
        var transportOptions = CreateTransportOptions();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);

        var readinessTask = coordinator.WaitForReadinessAsync();
        await coordinator.StopAsync();
        var readiness = await readinessTask;

        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Stopped,
            readiness);
        Assert.Equal(PcpRelayCoordinatorState.Stopped, coordinator.State);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RunAsync(
                IPAddress.Loopback.ToString(),
                upstreamPort: 1));
    }

    [Fact]
    public async Task Coordinator_ReadinessWaitCancellationDoesNotStopOwner()
    {
        var transportOptions = CreateTransportOptions();
        await using var coordinator = CreateCoordinator(
            transportOptions,
            CreateTracker(),
            maxReconnectAttempts: 0);
        using var waitSource = new CancellationTokenSource();

        var canceledWait = coordinator.WaitForReadinessAsync(
            waitSource.Token);
        waitSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledWait);
        Assert.Equal(PcpRelayCoordinatorState.Created, coordinator.State);

        var terminalWait = coordinator.WaitForReadinessAsync();
        await coordinator.StopAsync();

        Assert.Equal(
            PcpRelayCoordinatorReadinessOutcome.Stopped,
            await terminalWait);
    }

    private static PcpRelayCoordinator CreateCoordinator(
        TcpTransportOptions transportOptions,
        ChannelStatusTracker tracker,
        int maxReconnectAttempts,
        IPEndPoint? downstreamEndPoint = null)
    {
        return new PcpRelayCoordinator(
            new TcpTransportConnector(transportOptions),
            tracker,
            downstreamEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0),
            new PcpRelayCoordinatorOptions
            {
                DrainTimeout = TimeSpan.FromSeconds(2),
                UpstreamSession = new PcpChannelSessionOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(2),
                    MaxReconnectAttempts = maxReconnectAttempts,
                    ReconnectDelay = TimeSpan.FromMilliseconds(10),
                    Stream = new PcpChannelStreamOptions
                    {
                        ReadIdleTimeout = TimeSpan.FromSeconds(2),
                    },
                },
                DownstreamTransport = transportOptions,
                Fanout = new PcpRelayFanoutOptions
                {
                    MaxChildren = 4,
                    MaxPendingPacketsPerChild = 8,
                    MaxPendingBytesPerChild = 1024,
                    ChildStopTimeout = TimeSpan.FromSeconds(1),
                },
                DownstreamChild = new PcpDownstreamChildOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(2),
                    WriteTimeout = TimeSpan.FromSeconds(2),
                },
            });
    }

    private static ChannelStatusTracker CreateTracker()
    {
        return new ChannelStatusTracker(
            new ChannelId(ChannelGuid),
            LocalSessionId);
    }

    private static TcpTransportOptions CreateTransportOptions()
    {
        return new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };
    }

    private static async Task RunTwoUpstreamAttemptsAsync(
        TcpTransportListener listener,
        Task releasePackets,
        CancellationToken cancellationToken)
    {
        await RunAcceptedUpstreamAttemptAsync(
            listener,
            expectedPosition: 0,
            releasePackets,
            [CreatePacketAtom(position: 100, [0x41])],
            cancellationToken);
        await RunAcceptedUpstreamAttemptAsync(
            listener,
            expectedPosition: 100,
            Task.CompletedTask,
            [
                CreatePacketAtom(position: 200, [0x42]),
                CreateQuitAtom(),
            ],
            cancellationToken);
    }

    private static async Task RunAcceptedUpstreamAttemptAsync(
        TcpTransportListener listener,
        long expectedPosition,
        Task releasePackets,
        IReadOnlyList<PcpAtom> postHandshakeAtoms,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            await releasePackets.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                postHandshakeAtoms,
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunHeldUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            accepted.TrySetResult();

            try
            {
                var buffer = new byte[1];
                _ = await connection.ReadAsync(buffer, cancellationToken);
            }
            catch (IOException)
            {
                // Coordinator cancellation may close or reset the connection.
            }
            catch (SocketException)
            {
                // Coordinator cancellation may close or reset the connection.
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunPacketThenTruncatedUpstreamAsync(
        TcpTransportListener listener,
        Task releasePacket,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            await ReadAndAssertUpstreamRequestAsync(
                pipeReader,
                expectedPosition: 0,
                cancellationToken);
            await WriteHttpResponseAsync(connection, cancellationToken);
            await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
            await WriteAtomsAsync(
                connection,
                [
                    CreateOlehAtom(),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Okay,
                        PcpWireValueCodec.EncodeUInt32(1)),
                ],
                cancellationToken);
            await releasePacket.WaitAsync(cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(position: 100, [0x51, 0x52])],
                cancellationToken);

            // Leave an incomplete next atom on the wire. The complete packet
            // above must remain visible in coordinator diagnostics even though
            // the upstream session cannot return a terminal summary.
            await connection.WriteAsync(
                new byte[] { 0x63, 0x68, 0x61 },
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task<DownstreamPeer> ConnectDownstreamAsync(
        IPEndPoint endPoint,
        TcpTransportOptions transportOptions,
        CancellationToken cancellationToken)
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
                ChannelGuid,
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
            Assert.Equal(PcpCompatibilityOutcome.Accepted, handshake.Outcome);
            return new DownstreamPeer(connection, pipeReader);
        }
        catch
        {
            await pipeReader.CompleteAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ReadAndAssertUpstreamRequestAsync(
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
            $"/channel/{ChannelGuid:N}".ToUpperInvariant(),
            request.RequestTarget.ToUpperInvariant());
        Assert.Equal(
            expectedPosition.ToString(CultureInfo.InvariantCulture),
            request.Headers["x-peercast-pos"]);
    }

    private static async Task ReadAndAssertHeloAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken)
    {
        await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
        var helo = await atomReader.ReadNextAsync(cancellationToken);
        var sessionAtom = helo.FindFirst(PcpKnownAtomNames.HeloSessionId);

        Assert.Equal(PcpKnownAtomNames.Helo, helo.Name);
        Assert.NotNull(sessionAtom);
        Assert.Equal(
            LocalSessionId.Value,
            PcpWireValueCodec.DecodeGuid(sessionAtom!.Content.Span));
    }

    private static async Task WriteHttpResponseAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.0 200 OK\r\n" +
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

        await connection.WriteAsync(writer.WrittenMemory, cancellationToken);
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
                PcpWireValueCodec.EncodeGuid(RemoteSessionId)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(
                    PcpHeloCodec.LegacyServantVersion)));
    }

    private static PcpAtom CreatePacketAtom(uint position, byte[] payload)
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelGuid)),
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

    private static PcpAtom CreateQuitAtom()
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.Quit,
            PcpWireValueCodec.EncodeUInt32(0));
    }

    private sealed class DownstreamPeer : IAsyncDisposable
    {
        private readonly TcpTransportConnection _connection;
        private readonly PipeReader _pipeReader;

        public DownstreamPeer(
            TcpTransportConnection connection,
            PipeReader pipeReader)
        {
            _connection = connection;
            _pipeReader = pipeReader;
        }

        public async Task<byte[][]> ReadPayloadsAsync(
            int count,
            CancellationToken cancellationToken)
        {
            var payloads = new List<byte[]>(count);
            await using var atomReader = new PcpAtomPipeMessageReader(
                _pipeReader,
                maxBufferedBytes: 1024 * 1024);

            while (payloads.Count < count)
            {
                var channel = await atomReader.ReadNextAsync(
                    cancellationToken);
                var packet = channel.FindFirst(
                    PcpKnownAtomNames.ChannelPacket);
                var data = packet?.FindFirst(PcpKnownAtomNames.Data);

                Assert.Equal(PcpKnownAtomNames.Channel, channel.Name);
                Assert.NotNull(data);
                payloads.Add(data!.Content.ToArray());
            }

            return payloads.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await _pipeReader.CompleteAsync();
            await _connection.DisposeAsync();
        }
    }
}
