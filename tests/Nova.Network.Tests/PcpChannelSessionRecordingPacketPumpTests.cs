using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpChannelSessionRecordingPacketPumpTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public async Task RunAsync_ResolvesEndpointAndRunsSession()
    {
        var endpoint = new PcpRecordingSourceEndpoint(
            "127.0.0.1",
            7144,
            initialStreamPosition: 123);
        var endpointProvider =
            new TestEndpointProvider(endpoint);
        var sessionRunner = new TestSessionRunner(
            CreateResult(
                PcpChannelSessionEndReason.ConnectionEnded));
        var pump = new PcpChannelSessionRecordingPacketPump(
            endpointProvider,
            sessionRunner);
        var consumer = new TestConsumer();
        var request = CreateRequest();

        await pump.RunAsync(
            request,
            consumer,
            CancellationToken.None);

        Assert.Same(
            request,
            endpointProvider.LastRequest);
        Assert.Same(
            endpoint,
            sessionRunner.LastEndpoint);
        Assert.Same(
            request,
            sessionRunner.LastRequest);
        Assert.Same(
            consumer,
            sessionRunner.LastConsumer);
    }

    [Fact]
    public async Task RunAsync_MissingEndpoint_FailsBeforeSessionRun()
    {
        var endpointProvider =
            new TestEndpointProvider(endpoint: null);
        var sessionRunner = new TestSessionRunner(
            CreateResult(
                PcpChannelSessionEndReason.ConnectionEnded));
        var pump = new PcpChannelSessionRecordingPacketPump(
            endpointProvider,
            sessionRunner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pump.RunAsync(
                CreateRequest(),
                new TestConsumer(),
                CancellationToken.None));

        Assert.Null(sessionRunner.LastEndpoint);
    }

    [Theory]
    [InlineData(PcpChannelSessionEndReason.Stopped)]
    [InlineData(PcpChannelSessionEndReason.QuitReceived)]
    [InlineData(PcpChannelSessionEndReason.ConnectionEnded)]
    public async Task RunAsync_NormalTerminalReasons_Complete(
        PcpChannelSessionEndReason endReason)
    {
        var pump = CreatePump(endReason);

        await pump.RunAsync(
            CreateRequest(),
            new TestConsumer(),
            CancellationToken.None);
    }

    [Theory]
    [InlineData(PcpChannelSessionEndReason.RelayFull)]
    [InlineData(PcpChannelSessionEndReason.HttpRejected)]
    public async Task RunAsync_RemoteRejection_IsIoFailure(
        PcpChannelSessionEndReason endReason)
    {
        var pump = CreatePump(endReason);

        await Assert.ThrowsAsync<IOException>(
            async () => await pump.RunAsync(
                CreateRequest(),
                new TestConsumer(),
                CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_ProtocolRejected_IsInvalidData()
    {
        var pump = CreatePump(
            PcpChannelSessionEndReason.ProtocolRejected);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await pump.RunAsync(
                CreateRequest(),
                new TestConsumer(),
                CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_PreCancelled_DoesNotResolveEndpoint()
    {
        var endpointProvider =
            new TestEndpointProvider(
                new PcpRecordingSourceEndpoint(
                    "127.0.0.1",
                    7144));
        var sessionRunner = new TestSessionRunner(
            CreateResult(
                PcpChannelSessionEndReason.ConnectionEnded));
        var pump = new PcpChannelSessionRecordingPacketPump(
            endpointProvider,
            sessionRunner);

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pump.RunAsync(
                CreateRequest(),
                new TestConsumer(),
                source.Token));

        Assert.Null(endpointProvider.LastRequest);
        Assert.Null(sessionRunner.LastEndpoint);
    }

    [Fact]
    public void Endpoint_InvalidValues_AreRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new PcpRecordingSourceEndpoint(
                " ",
                7144));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRecordingSourceEndpoint(
                "127.0.0.1",
                0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRecordingSourceEndpoint(
                "127.0.0.1",
                7144,
                initialStreamPosition: -1));
    }

    private static PcpChannelSessionRecordingPacketPump CreatePump(
        PcpChannelSessionEndReason endReason)
    {
        return new PcpChannelSessionRecordingPacketPump(
            new TestEndpointProvider(
                new PcpRecordingSourceEndpoint(
                    "127.0.0.1",
                    7144)),
            new TestSessionRunner(
                CreateResult(endReason)));
    }

    private static RecordingSinkStartRequest CreateRequest()
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                TestChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));
    }

    private static PcpChannelSessionResult CreateResult(
        PcpChannelSessionEndReason endReason)
    {
        return new PcpChannelSessionResult(
            endReason,
            AttemptCount: 1,
            ReconnectCount: 0,
            HttpStatusCode: 200,
            ChannelMessageCount: 0,
            PacketCount: 0,
            PayloadByteCount: 0);
    }

    private sealed class TestEndpointProvider :
        IPcpRecordingSourceEndpointProvider
    {
        private readonly PcpRecordingSourceEndpoint? endpoint;

        public TestEndpointProvider(
            PcpRecordingSourceEndpoint? endpoint)
        {
            this.endpoint = endpoint;
        }

        public RecordingSinkStartRequest? LastRequest { get; private set; }

        public ValueTask<PcpRecordingSourceEndpoint?> ResolveAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return ValueTask.FromResult(endpoint);
        }
    }

    private sealed class TestSessionRunner :
        IPcpRecordingChannelSessionRunner
    {
        private readonly PcpChannelSessionResult result;

        public TestSessionRunner(
            PcpChannelSessionResult result)
        {
            this.result = result;
        }

        public PcpRecordingSourceEndpoint? LastEndpoint { get; private set; }

        public RecordingSinkStartRequest? LastRequest { get; private set; }

        public IPcpChannelPacketConsumer? LastConsumer { get; private set; }

        public Task<PcpChannelSessionResult> RunAsync(
            PcpRecordingSourceEndpoint endpoint,
            RecordingSinkStartRequest request,
            IPcpChannelPacketConsumer consumer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEndpoint = endpoint;
            LastRequest = request;
            LastConsumer = consumer;
            return Task.FromResult(result);
        }
    }

    private sealed class TestConsumer : IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
