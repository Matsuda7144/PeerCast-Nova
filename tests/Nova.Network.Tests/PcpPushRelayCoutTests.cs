using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.Network.Tests.AnnounceTestData;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpPushRelayCoutTests
{
    private static readonly Guid ReceiverSessionId = Guid.Parse("22334455-6677-8899-aabb-ccddeeff0011");

    [Fact]
    public async Task TrackerPushAndCoutAtomsDispatchValidatedRequestsAndRejectSsrfAndForeignChannel()
    {
        var connector = new ProductionConnector();
        var oleh = PcpHeloCodec.CreateOleh("tracker", Guid.NewGuid());
        var advertised = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 17144);
        var tracker = new IPEndPoint(IPAddress.Parse("198.41.0.5"), 7144);
        connector.Connection.ReplaceHandshake(PcpAtom.Parent(oleh.Name,
        [
            .. oleh.Children,
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(17144)),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloRemoteIp, PcpWireValueCodec.EncodeLegacyIpv4(advertised.Address)),
        ]));

        await using var owner = new PcpAnnounceOwner(connector);
        var sid = Guid.NewGuid();
        var identity = new PcpAnnounceIdentity(1, Guid.NewGuid(), sid, sid, owner.BroadcastId);
        var capability = new PublishingAnnounceCapability(
            identity, Guid.NewGuid(), new(IPAddress.Parse("192.168.1.2"), 7144), advertised, tracker, () => true);

        var accepted = new List<PcpPushRelayRequest>();
        var secondAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = owner.Start(
            1,
            identity.ChannelId,
            sid,
            sid,
            PcpAnnounceEndpoint.Parse("pcp://198.41.0.5:7144"),
            new(new ChannelMetadataSnapshot("Pushテスト", "", "", "https://example.com/", "FLV", 128), advertised),
            PcpAnnounceAccess.ForProduction(tracker),
            capability: capability,
            onPushRelayRequest: request =>
            {
                accepted.Add(request);
                if (accepted.Count == 2)
                {
                    secondAccepted.TrySetResult();
                }
            });

        _ = await connector.Connection.NextWriteAsync(); // HELO
        _ = await connector.Connection.NextWriteAsync(); // Initial BCST
        await connector.Connection.IdleReadStarted.Task.WaitAsync(Deadline);

        // ループバックIP・プライベートIP・別チャンネルは SSRF/境界防御により拒否されること
        connector.Connection.Receive(PcpAnnounceCodec.CreatePushRequest(IPAddress.Loopback, 17144, identity.ChannelId, useCoutName: true));
        connector.Connection.Receive(PcpAnnounceCodec.CreatePushRequest(IPAddress.Parse("192.168.10.5"), 17144, identity.ChannelId, useCoutName: true));
        connector.Connection.Receive(PcpAnnounceCodec.CreatePushRequest(IPAddress.Parse("198.41.0.20"), 17144, Guid.NewGuid(), useCoutName: true));

        // 公開IP + 自チャンネルの push / cout は両方受理されること
        var firstTarget = IPAddress.Parse("198.41.0.21");
        var secondTarget = IPAddress.Parse("198.41.0.22");
        connector.Connection.Receive(PcpAnnounceCodec.CreatePushRequest(firstTarget, 17144, identity.ChannelId, useCoutName: false));
        connector.Connection.Receive(PcpAnnounceCodec.CreatePushRequest(secondTarget, 17145, identity.ChannelId, isRelay: false, useCoutName: true));

        await secondAccepted.Task.WaitAsync(Deadline);
        Assert.Equal(2, accepted.Count);
        Assert.Equal(2, worker.PushRequestsAccepted);
        Assert.Equal(new PcpPushRelayRequest(firstTarget, 17144, identity.ChannelId, true), accepted[0]);
        Assert.Equal(new PcpPushRelayRequest(secondTarget, 17145, identity.ChannelId, false), accepted[1]);

        var stopResult = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
        Assert.True(stopResult.CleanupProven);
        Assert.Equal(PcpAnnounceFailure.None, stopResult.Failure);
    }

    [Fact]
    public async Task TrackerRootControlAdjustsUpdateIntervalAndTriggersImmediateBroadcast()
    {
        using var clock = new AnnounceManualTimeProvider();
        var connector = new AnnounceFakeConnector();
        await using var owner = new PcpAnnounceOwner(connector, timeProvider: clock);
        var worker = Start(owner);

        _ = await connector.Connection.NextWriteAsync(); // HELO
        _ = await connector.Connection.NextWriteAsync(); // Initial BCST
        await clock.TimerCreated.Task.WaitAsync(Deadline);
        await connector.Connection.IdleReadStarted.Task.WaitAsync(Deadline);

        Assert.Equal(PcpAnnounceOptions.UpdateInterval, worker.CurrentUpdateInterval);

        // root(uint=60, upd) により更新間隔が60秒へ変更され、即座にBCSTが送信されること
        connector.Connection.Receive(PcpAtom.Parent(
            PcpKnownAtomNames.Root,
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdateInterval, PcpWireValueCodec.EncodeUInt32(60)),
            PcpAtom.Leaf(PcpKnownAtomNames.RootUpdate, [])));

        var immediateBcst = Decode(await connector.Connection.NextWriteAsync());
        Assert.Equal("Motion + tone", Name(immediateBcst));
        Assert.Equal(TimeSpan.FromSeconds(60), worker.CurrentUpdateInterval);

        // 30秒経過だけでは再送されず、60秒到達で送信されること
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(connector.Connection.TryWrite(out _));
        clock.Advance(TimeSpan.FromSeconds(30));
        var periodicBcst = Decode(await connector.Connection.NextWriteAsync());
        Assert.Equal("Motion + tone", Name(periodicBcst));

        var result = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
        Assert.True(result.CleanupProven);
    }

    [Fact]
    public async Task DownstreamRelayServerExecutesPushRelayOverHttpGivAndStreamsMetadataAndPackets()
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var localSessionId = Guid.NewGuid();
        var metadata = new ObsPublishChannelOptions { ChannelName = "HTTP-GIV Pushテスト", BitrateKbps = 800 }.CreateMetadata();

        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            channelId,
            localSessionId,
            initialMetadata: metadata);
        await using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();

        using var timeout = new CancellationTokenSource(Deadline);
        var runTask = server.RunAsync(timeout.Token);

        byte[] head = [0x46, 0x4c, 0x56, 0x01];
        await fanout.ConsumeAsync(new(channelId, new(Guid.NewGuid()), 0, 0x64616568, 0, 0, head, null), timeout.Token);

        var started = await server.TryStartPushRelaySessionAsync(listener.LocalEndPoint, cancellationToken: timeout.Token);
        Assert.True(started);

        await using var receiverConnection = await listener.AcceptAsync(timeout.Token);
        var pipe = PipeReader.Create(receiverConnection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await using var atomReader = new PcpAtomPipeMessageReader(pipe);
        try
        {
            // 1. GIV リクエストを受信して検証
            var giv = await ReadAsciiHeaderAsync(pipe, timeout.Token);
            Assert.Equal($"GIV /{channelId.Value:N}\r\n\r\n", giv);

            // 2. 受信ノード側から通常 HTTP リレー要求 + HELO を送信
            var getRequest = Encoding.ASCII.GetBytes(
                $"GET /channel/{channelId.Value:N} HTTP/1.0\r\nx-peercast-pcp:1\r\nx-peercast-pos:0\r\n\r\n");
            await receiverConnection.WriteAsync(getRequest, timeout.Token);

            var helo = PcpHeloCodec.CreateHelo("Receiver/HTTP-GIV", ReceiverSessionId);
            var heloBytes = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(heloBytes, helo);
            await receiverConnection.WriteAsync(heloBytes.WrittenMemory, timeout.Token);

            // 3. HTTP 200 OK と PCP OLEH / OK / CHAN(info) / CHAN(pkt) が届くこと
            var httpResponse = await ReadAsciiHeaderAsync(pipe, timeout.Token);
            Assert.StartsWith("HTTP/1.0 200 OK\r\n", httpResponse, StringComparison.Ordinal);

            var oleh = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(localSessionId, PcpHeloCodec.ParseOleh(oleh).SessionId);

            var ok = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(PcpKnownAtomNames.Okay, ok.Name);

            var chanInfo = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(PcpKnownAtomNames.Channel, chanInfo.Name);
            Assert.NotNull(chanInfo.FindFirst(PcpKnownAtomNames.ChannelInfo));

            var chanPacket = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(head, chanPacket.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());

            var quitWriter = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(quitWriter, PcpQuitCodec.Create(1000));
            await receiverConnection.WriteAsync(quitWriter.WrittenMemory, timeout.Token);
        }
        finally
        {
            await pipe.CompleteAsync();
        }

        await server.StopAsync(timeout.Token);
        var result = await runTask.WaitAsync(timeout.Token);
        Assert.Equal(1, result.Snapshot.PushRelayRequestedCount);
        Assert.Equal(1, result.Snapshot.PushRelayAcceptedCount);
        Assert.Equal(0, result.Snapshot.PushRelayFailedCount);
        Assert.Equal(0, result.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task DownstreamRelayServerExecutesPushRelayOverDirectPcpGivAndDeduplicatesTarget()
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var localSessionId = Guid.NewGuid();
        var metadata = new ObsPublishChannelOptions { ChannelName = "Direct-PCP-GIV Pushテスト", BitrateKbps = 1000 }.CreateMetadata();

        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            channelId,
            localSessionId,
            initialMetadata: metadata);
        await using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();

        using var timeout = new CancellationTokenSource(Deadline);
        var runTask = server.RunAsync(timeout.Token);

        byte[] head = [0x46, 0x4c, 0x56, 0x02];
        await fanout.ConsumeAsync(new(channelId, new(Guid.NewGuid()), 0, 0x64616568, 0, 0, head, null), timeout.Token);

        Assert.True(await server.TryStartPushRelaySessionAsync(listener.LocalEndPoint, cancellationToken: timeout.Token));
        // 同一宛先への重複 Push は抑止されること
        Assert.False(await server.TryStartPushRelaySessionAsync(listener.LocalEndPoint, cancellationToken: timeout.Token));

        await using var receiverConnection = await listener.AcceptAsync(timeout.Token);
        var pipe = PipeReader.Create(receiverConnection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await using var atomReader = new PcpAtomPipeMessageReader(pipe);
        try
        {
            var giv = await ReadAsciiHeaderAsync(pipe, timeout.Token);
            Assert.Equal($"GIV /{channelId.Value:N}\r\n\r\n", giv);

            // HTTP を介さず pcp\n プレアンブル + HELO を直接送信
            var preamble = new byte[PcpPreamble.Size];
            PcpPreamble.Write(preamble);
            var helo = PcpHeloCodec.CreateHelo("Receiver/Direct-PCP-GIV", ReceiverSessionId);
            var heloWriter = new ArrayBufferWriter<byte>();
            heloWriter.Write(preamble);
            PcpAtomTreeCodec.Write(heloWriter, helo);
            await receiverConnection.WriteAsync(heloWriter.WrittenMemory, timeout.Token);

            var oleh = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(localSessionId, PcpHeloCodec.ParseOleh(oleh).SessionId);

            var ok = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(PcpKnownAtomNames.Okay, ok.Name);

            var chanInfo = await atomReader.ReadNextAsync(timeout.Token);
            Assert.NotNull(chanInfo.FindFirst(PcpKnownAtomNames.ChannelInfo));

            var chanPacket = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(head, chanPacket.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());

            var quitWriter = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(quitWriter, PcpQuitCodec.Create(1000));
            await receiverConnection.WriteAsync(quitWriter.WrittenMemory, timeout.Token);
        }
        finally
        {
            await pipe.CompleteAsync();
        }

        await server.StopAsync(timeout.Token);
        var result = await runTask.WaitAsync(timeout.Token);
        Assert.Equal(1, result.Snapshot.PushRelayRequestedCount);
        Assert.Equal(1, result.Snapshot.PushRelayAcceptedCount);
        Assert.Equal(0, result.Snapshot.PushRelayFailedCount);
        Assert.Equal(0, result.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, result.Snapshot.InternalFailureCount);
    }

    [Fact]
    public async Task TrackerCoutCommandTriggersEndToEndPushRelayStreamFromServer()
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var localSessionId = Guid.NewGuid();
        var metadata = new ObsPublishChannelOptions { ChannelName = "Tracker COUT E2E", BitrateKbps = 512 }.CreateMetadata();

        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            channelId,
            localSessionId,
            initialMetadata: metadata);
        await using var receiverListener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        receiverListener.Start();
        await using var tracker = new AnnounceLoopbackTracker();

        using var timeout = new CancellationTokenSource(Deadline);
        var runTask = server.RunAsync(timeout.Token);
        byte[] head = [0x46, 0x4c, 0x56, 0x09];
        await fanout.ConsumeAsync(new(channelId, new(Guid.NewGuid()), 0, 0x64616568, 0, 0, head, null), timeout.Token);

        await using var announceOwner = new PcpAnnounceOwner();
        var worker = announceOwner.Start(
            1,
            channelId.Value,
            localSessionId,
            localSessionId,
            PcpAnnounceEndpoint.Parse($"pcp://127.0.0.1:{tracker.Endpoint.Port}"),
            new(new ChannelMetadataSnapshot("Tracker COUT E2E", "", "", "https://example.com/", "FLV", 512), server.LocalEndPoint),
            PcpAnnounceAccess.ForLoopbackFixture(tracker.Endpoint),
            onPushRelayRequest: request =>
                _ = server.TryStartPushRelaySessionAsync(new IPEndPoint(request.Address, request.Port), cancellationToken: timeout.Token));

        _ = await tracker.AcceptHeloAsync();
        await tracker.WriteAsync(PcpHeloCodec.CreateOleh("tracker", Guid.NewGuid()));
        _ = await tracker.ReadAsync(); // Initial BCST

        // Tracker から COUT アトムを送信
        await tracker.WriteAsync(PcpAnnounceCodec.CreatePushRequest(
            receiverListener.LocalEndPoint.Address,
            checked((ushort)receiverListener.LocalEndPoint.Port),
            channelId.Value,
            useCoutName: true));

        await using var receiverConnection = await receiverListener.AcceptAsync(timeout.Token);
        var pipe = PipeReader.Create(receiverConnection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        await using var atomReader = new PcpAtomPipeMessageReader(pipe);
        try
        {
            var giv = await ReadAsciiHeaderAsync(pipe, timeout.Token);
            Assert.Equal($"GIV /{channelId.Value:N}\r\n\r\n", giv);

            var preamble = new byte[PcpPreamble.Size];
            PcpPreamble.Write(preamble);
            var helo = PcpHeloCodec.CreateHelo("Receiver/Tracker-COUT", ReceiverSessionId);
            var heloWriter = new ArrayBufferWriter<byte>();
            heloWriter.Write(preamble);
            PcpAtomTreeCodec.Write(heloWriter, helo);
            await receiverConnection.WriteAsync(heloWriter.WrittenMemory, timeout.Token);

            Assert.Equal(PcpKnownAtomNames.Oleh, (await atomReader.ReadNextAsync(timeout.Token)).Name);
            Assert.Equal(PcpKnownAtomNames.Okay, (await atomReader.ReadNextAsync(timeout.Token)).Name);
            Assert.Equal(PcpKnownAtomNames.Channel, (await atomReader.ReadNextAsync(timeout.Token)).Name);
            var packet = await atomReader.ReadNextAsync(timeout.Token);
            Assert.Equal(head, packet.FindFirst(PcpKnownAtomNames.ChannelPacket)!.FindFirst(PcpKnownAtomNames.Data)!.Content.ToArray());

            var quitWriter = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(quitWriter, PcpQuitCodec.Create(1000));
            await receiverConnection.WriteAsync(quitWriter.WrittenMemory, timeout.Token);
        }
        finally
        {
            await pipe.CompleteAsync();
        }

        Assert.Equal(1, worker.PushRequestsAccepted);
        var announceResult = await worker.StopAsync(PcpAnnounceStopReason.Finish).WaitAsync(Deadline);
        Assert.True(announceResult.CleanupProven);

        await server.StopAsync(timeout.Token);
        var serverResult = await runTask.WaitAsync(timeout.Token);
        Assert.Equal(1, serverResult.Snapshot.PushRelayAcceptedCount);
        Assert.Equal(0, serverResult.Snapshot.CleanupIncompleteCount);
    }

    private static async Task<string> ReadAsciiHeaderAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;
            var array = buffer.ToArray();
            var idx = FindHeaderEnd(array);
            if (idx >= 0)
            {
                var length = idx + 4;
                var text = Encoding.ASCII.GetString(array, 0, length);
                reader.AdvanceTo(buffer.GetPosition(length));
                return text;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Expected CRLFCRLF-terminated header.");
            }
        }
    }

    private static int FindHeaderEnd(byte[] bytes) => bytes.AsSpan().IndexOf("\r\n\r\n"u8);

    private sealed class ProductionConnector : IPcpAnnounceConnector
    {
        internal AnnounceFakeConnection Connection { get; } = new();

        public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Assert.Equal("198.41.0.5", endpoint.Address.ToString());
            return Task.FromResult<IPcpAnnounceConnection>(Connection);
        }
    }
}
