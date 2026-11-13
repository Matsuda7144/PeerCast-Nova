using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionRedirectTests
{
    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531DC8DFC7FB42928AC2C0A626517A87",
        "N");

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RelayFull_GlobalRedirectIsAcceptedAndDeliversPacket()
    {
        await using var transport = new FakeTransport();
        var redirectEndpoint = Endpoint("1.1.1.1", 7144);
        var redirectListener = transport.AddRedirect(redirectEndpoint);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        var consumer = new CollectingPacketConsumer();
        await using var session = CreateSession(
            transport,
            tracker,
            consumer,
            reconnectDelay: TimeSpan.FromSeconds(2));

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 77,
            [CreateHost(("1.1.1.1", 7144))],
            timeoutSource.Token);
        var redirectPeer = RunAcceptedPeerAsync(
            redirectListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 77,
            [CreatePacketAtom(100, [0x10, 0x20]), CreateQuitAtom()],
            timeoutSource.Token);

        var runTask = session.RunAsync(
            "original.example",
            7144,
            initialStreamPosition: 77,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        var result = await runTask;
        await originalPeer;
        await redirectPeer;

        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(0, result.ReconnectCount);
        var packet = Assert.Single(consumer.Packets);
        Assert.Equal([0x10, 0x20], packet.Payload);
        var requested = Assert.Single(transport.LogicalRedirectEndpoints);
        Assert.Equal(redirectEndpoint, requested);
    }

    [Fact]
    public async Task RelayFull_DoesNotCompleteNegotiationRejectedBeforeRedirectExhaustion()
    {
        await using var transport = new FakeTransport();
        var redirectEndpoint = Endpoint("1.1.1.1", 7144);
        var redirectListener = transport.AddRedirect(redirectEndpoint);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var release = new CancellationTokenSource();
        var redirectRequestSeen = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = CreateTracker();
        await using var session = CreateSession(
            transport,
            tracker,
            new CollectingPacketConsumer(),
            reconnectDelay: TimeSpan.FromSeconds(2));

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144))],
            timeoutSource.Token);
        var redirectPeer = RunHeldAcceptedPeerAsync(
            redirectListener,
            expectedHost: "1.1.1.1:7144",
            requestSeen: redirectRequestSeen,
            release.Token,
            timeoutSource.Token);

        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await redirectRequestSeen.Task.WaitAsync(timeoutSource.Token);

        Assert.False(session.Negotiation.IsCompleted);

        release.Cancel();
        session.RequestStop();
        var result = await runTask;
        await originalPeer;
        await redirectPeer;

        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.EndReason);
    }

    [Fact]
    public async Task RelayFull_DuplicateCandidatesAreAttemptedOnce()
    {
        await using var transport = new FakeTransport();
        var redirectEndpoint = Endpoint("1.1.1.1", 7144);
        var redirectListener = transport.AddRedirect(redirectEndpoint);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [
                CreateHost(("1.1.1.1", 7144)),
                CreateHost(("1.1.1.1", 7144)),
            ],
            timeoutSource.Token);
        var redirectPeer = RunAcceptedPeerAsync(
            redirectListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await originalPeer;
        await redirectPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(2, result.AttemptCount);
        Assert.Single(transport.RedirectRequests);
    }

    [Fact]
    public async Task RelayFull_IneligibleCandidatesAreSkippedWithoutRedirectRequests()
    {
        await using var transport = new FakeTransport();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        await using var session = CreateSession(
            transport,
            tracker,
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [
                CreateHost(("192.168.1.10", 7144)),
                CreateHost(("192.0.2.10", 7144)),
                CreateHost(("224.0.0.1", 7144)),
            ],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;

        Assert.Equal(PcpChannelSessionEndReason.RelayFull, result.EndReason);
        Assert.Equal(PcpChannelSessionNegotiationOutcome.Rejected, negotiation.Outcome);
        Assert.Equal(1, result.AttemptCount);
        Assert.Empty(transport.RedirectRequests);
    }

    [Fact]
    public async Task RelayFull_FailedFirstRedirectMovesToSecondEligibleCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var secondListener = transport.AddRedirect(second);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var secondPeer = RunAcceptedPeerAsync(
            secondListener,
            expectedHost: "8.8.8.8:7145",
            expectedPosition: 0,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await originalPeer;
        await secondPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(0, result.ReconnectCount);
        Assert.Equal([first, second], transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_TruncatedFirstRedirectMovesToSecondEligibleCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        var secondListener = transport.AddRedirect(second);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunTruncatedPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            timeoutSource.Token);
        var secondPeer = RunAcceptedPeerAsync(
            secondListener,
            expectedHost: "8.8.8.8:7145",
            expectedPosition: 0,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;
        await secondPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal([first, second], transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_RejectedFirstRedirectMovesToSecondAndDoesNotRecurse()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var nested = Endpoint("9.9.9.9", 7146);
        var firstListener = transport.AddRedirect(first);
        var secondListener = transport.AddRedirect(second);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunRelayFullPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            [CreateHost(("9.9.9.9", 7146))],
            timeoutSource.Token);
        var secondPeer = RunAcceptedPeerAsync(
            secondListener,
            expectedHost: "8.8.8.8:7145",
            expectedPosition: 0,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;
        await secondPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal([first, second], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(nested, transport.LogicalRedirectEndpoints);
    }

    [Theory]
    [InlineData(503, 1000U)]
    [InlineData(503, 1005U)]
    [InlineData(503, 1007U)]
    [InlineData(503, 1234U)]
    [InlineData(401, 0U)]
    [InlineData(403, 0U)]
    public async Task RelayFull_RedirectExplicitRejectionIsNotMaskedAsOriginalCapacity(int status, uint code)
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var listener = transport.AddRedirect(first);
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(transport, CreateTracker(),
            new CollectingPacketConsumer(), maxReconnectAttempts: 0);
        var original = RunRelayFullPeerAsync(transport.OriginalListener, "original.example:7144", 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))], timeout.Token);
        var rejected = status == 503
            ? RunRelayFullPeerAsync(listener, "1.1.1.1:7144", 0, [], timeout.Token, code)
            : RunHttpRejectedPeerAsync(listener, "1.1.1.1:7144", status, timeout.Token);
        var result = await session.RunAsync("original.example", 7144, timeout.Token);
        await original;
        await rejected;
        Assert.Equal(status == 503 ? PcpChannelSessionEndReason.ProtocolRejected :
            PcpChannelSessionEndReason.HttpRejected, result.EndReason);
        Assert.Equal(status, result.HttpStatusCode);
        Assert.Equal(status == 503 ? new PcpQuitInfo(code) : (PcpQuitInfo?)null, result.Quit);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(new[] { first }, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_AllRedirectsFailPreservesOriginalEvidence()
    {
        await using var transport = new FakeTransport();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var tracker = CreateTracker();
        await using var session = CreateSession(
            transport,
            tracker,
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token,
            terminalCode: 1003);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;

        Assert.Equal(PcpChannelSessionEndReason.RelayFull, result.EndReason);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal(PcpChannelSessionNegotiationOutcome.Rejected, negotiation.Outcome);
        Assert.Equal(503, negotiation.HttpStatusCode);
        Assert.Equal(
            [Endpoint("1.1.1.1", 7144), Endpoint("8.8.8.8", 7145)],
            transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_EightEligibleRedirectsConsumeOnlyEightAttempts()
    {
        await using var transport = new FakeTransport();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);
        var endpoints = Enumerable.Range(0, 8)
            .Select(index => Endpoint(
                "1.1.1." + (index + 1),
                7144 + index))
            .ToArray();

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            endpoints.Select(endpoint => CreateHost(
                (endpoint.Address.ToString(), endpoint.Port))).ToArray(),
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await originalPeer;

        Assert.Equal(PcpChannelSessionEndReason.RelayFull, result.EndReason);
        Assert.Equal(9, result.AttemptCount);
        Assert.Equal(endpoints, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_CancellationDuringRedirectDoesNotTryNextCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        transport.BlockRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);
        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);

        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await transport.FirstRedirectRequest.Task.WaitAsync(timeoutSource.Token);
        session.RequestStop();
        var result = await runTask;
        await originalPeer;

        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.EndReason);
        Assert.Single(transport.RedirectRequests);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_StopDuringRedirectHandshakeDoesNotTryNextCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var release = new CancellationTokenSource();
        var requestSeen = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = CreateTracker();
        await using var session = CreateSession(
            transport,
            tracker,
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunHeldAcceptedPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            requestSeen,
            release.Token,
            timeoutSource.Token);

        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await requestSeen.Task.WaitAsync(timeoutSource.Token);
        await session.StopAsync(timeoutSource.Token);
        release.Cancel();
        var result = await runTask;
        await originalPeer;
        await firstPeer;

        Assert.Equal(PcpChannelSessionEndReason.Stopped, result.EndReason);
        Assert.Single(transport.RedirectRequests);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_NonRelayFullRejectionRemainsTerminal()
    {
        await using var transport = new FakeTransport();
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);
        var originalPeer = RunHttpRejectedPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            statusCode: 403,
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;

        Assert.Equal(PcpChannelSessionEndReason.HttpRejected, result.EndReason);
        Assert.Equal(PcpChannelSessionNegotiationOutcome.Rejected, negotiation.Outcome);
        Assert.Empty(transport.RedirectRequests);
        Assert.Equal(1, result.AttemptCount);
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectTruncatedReceivingFailureDoesNotTryPendingCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedThenTruncatedStreamPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            timeoutSource.Token);

        var failure = await Assert.ThrowsAsync<PcpProtocolException>(() =>
            session.RunAsync(
                "original.example",
                7144,
                timeoutSource.Token));
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;

        Assert.Equal(
            PcpParseError.UnexpectedEndOfStream,
            failure.Error);
        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectIOExceptionDoesNotTryPendingCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var resetSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new CollectingPacketConsumer();
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            consumer,
            maxReconnectAttempts: 0);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedThenResetPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            resetSignal,
            timeoutSource.Token);

        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await consumer.FirstPacketObserved.Task.WaitAsync(
            timeoutSource.Token);
        resetSignal.TrySetResult(true);

        var failure = await Assert.ThrowsAsync<IOException>(() => runTask);
        await originalPeer;
        await firstPeer;

        Assert.IsAssignableFrom<IOException>(failure);
        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_PreAcceptRedirectHandoffDoesNotUseReconnectDelay()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        var secondListener = transport.AddRedirect(second);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var handoffDeadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));
        var reconnectDelay = TimeSpan.FromSeconds(5);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0,
            reconnectDelay: reconnectDelay);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunTruncatedPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            timeoutSource.Token);
        var secondPeer = RunAcceptedPeerAsync(
            secondListener,
            expectedHost: "8.8.8.8:7145",
            expectedPosition: 0,
            [CreateQuitAtom()],
            timeoutSource.Token);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await transport.SecondRedirectRequest.Task.WaitAsync(
            handoffDeadline.Token);
        var handoffElapsed = stopwatch.Elapsed;
        var result = await runTask;
        await originalPeer;
        await firstPeer;
        await secondPeer;

        Assert.True(handoffElapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(0, result.ReconnectCount);
        Assert.Equal([first, second], transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectReceivingTimeoutDoesNotTryPendingCandidate()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        using var release = new CancellationTokenSource();
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 0,
            readIdleTimeout: TimeSpan.FromMilliseconds(50));

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedAndHeldStreamPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            release.Token,
            timeoutSource.Token);

        try
        {
            var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
                session.RunAsync(
                    "original.example",
                    7144,
                    timeoutSource.Token));
            Assert.NotNull(failure);
        }
        finally
        {
            release.Cancel();
        }

        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;

        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectConnectionEndedReconnectsSameTarget()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var reconnectDelay = TimeSpan.FromMilliseconds(200);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 1,
            reconnectDelay: reconnectDelay);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedThenAcceptedPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            firstPostHandshakeAtoms: [],
            secondPostHandshakeAtoms: [CreateQuitAtom()],
            timeoutSource.Token);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var runTask = session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        await transport.FirstRedirectRequest.Task.WaitAsync(
            timeoutSource.Token);
        var firstRequestElapsed = stopwatch.Elapsed;
        await transport.SecondRedirectRequest.Task.WaitAsync(
            timeoutSource.Token);
        var secondRequestElapsed = stopwatch.Elapsed;
        var result = await runTask;
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(1, result.ReconnectCount);
        Assert.True(
            secondRequestElapsed - firstRequestElapsed >=
                reconnectDelay - TimeSpan.FromMilliseconds(50));
        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first, first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectRetryableFailureReconnectsSameTarget()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var consumer = new CollectingPacketConsumer();
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            consumer,
            maxReconnectAttempts: 1);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedThenTruncatedThenAcceptedPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            secondExpectedPosition: 99,
            secondPostHandshakeAtoms: [
                CreatePacketAtom(100, [0x30, 0x40]),
                CreateQuitAtom(),
            ],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;

        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(1, result.ReconnectCount);
        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first, first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
        Assert.Collection(
            consumer.Packets,
            firstPacket => Assert.Equal(0, firstPacket.Sequence),
            secondPacket => Assert.Equal(1, secondPacket.Sequence));
    }

    [Fact]
    public async Task RelayFull_AcceptedRedirectReconnectRelayFullDoesNotReenterStartupPlan()
    {
        await using var transport = new FakeTransport();
        var first = Endpoint("1.1.1.1", 7144);
        var second = Endpoint("8.8.8.8", 7145);
        var nested = Endpoint("9.9.9.9", 7146);
        var firstListener = transport.AddRedirect(first);
        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(
            transport,
            CreateTracker(),
            new CollectingPacketConsumer(),
            maxReconnectAttempts: 1);

        var originalPeer = RunRelayFullPeerAsync(
            transport.OriginalListener,
            expectedHost: "original.example:7144",
            expectedPosition: 0,
            [CreateHost(("1.1.1.1", 7144)), CreateHost(("8.8.8.8", 7145))],
            timeoutSource.Token);
        var firstPeer = RunAcceptedThenRelayFullPeerAsync(
            firstListener,
            expectedHost: "1.1.1.1:7144",
            expectedPosition: 0,
            [CreateHost(("9.9.9.9", 7146))],
            timeoutSource.Token);

        var result = await session.RunAsync(
            "original.example",
            7144,
            timeoutSource.Token);
        var negotiation = await session.Negotiation.WaitAsync(
            timeoutSource.Token);
        await originalPeer;
        await firstPeer;

        Assert.Equal(PcpChannelSessionEndReason.RelayFull, result.EndReason);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(1, result.ReconnectCount);
        Assert.Equal(
            PcpChannelSessionNegotiationOutcome.Accepted,
            negotiation.Outcome);
        Assert.Equal([first, first], transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(second, transport.LogicalRedirectEndpoints);
        Assert.DoesNotContain(nested, transport.LogicalRedirectEndpoints);
    }

    private static PcpChannelSession CreateSession(
        IPcpChannelTransportConnector connector,
        ChannelStatusTracker tracker,
        IPcpChannelPacketConsumer consumer,
        int maxReconnectAttempts = 0,
        TimeSpan? reconnectDelay = null,
        TimeSpan? readIdleTimeout = null)
    {
        return new PcpChannelSession(
            connector,
            tracker,
            consumer,
            new PcpChannelSessionOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(2),
                MaxReconnectAttempts = maxReconnectAttempts,
                ReconnectDelay = reconnectDelay ?? TimeSpan.FromMilliseconds(10),
                Stream = new PcpChannelStreamOptions
                {
                    ReadIdleTimeout = readIdleTimeout ?? TimeSpan.FromSeconds(2),
                },
            });
    }

    private static async Task RunRelayFullPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        IReadOnlyList<PcpAtom> hostAtoms,
        CancellationToken cancellationToken,
        uint terminalCode = 1003)
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
                expectedHost,
                expectedPosition,
                cancellationToken);
            await WriteHttpResponseAsync(
                connection,
                503,
                "Service Unavailable",
                cancellationToken);
            await ReadAndAssertHeloAsync(
                pipeReader,
                cancellationToken);
            var atoms = new List<PcpAtom> { CreateOlehAtom() };
            atoms.AddRange(hostAtoms);
            atoms.Add(CreateQuitAtom(terminalCode));
            await WriteAtomsAsync(connection, atoms, cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunAcceptedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
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
            await ReadAndAssertRequestAsync(
                pipeReader,
                expectedHost,
                expectedPosition,
                cancellationToken);
            await WriteAcceptedHandshakeAsync(
                connection,
                pipeReader,
                cancellationToken);
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

    private static async Task RunAcceptedThenTruncatedStreamPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        CancellationToken cancellationToken)
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
                expectedHost,
                expectedPosition,
                cancellationToken);
            await WriteAcceptedHandshakeAsync(
                connection,
                pipeReader,
                cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(99, [0x20, 0x21])],
                cancellationToken);

            var encodedPacket = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(
                encodedPacket,
                CreatePacketAtom(100, [0x01]));
            await connection.WriteAsync(
                encodedPacket.WrittenMemory[..1],
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunAcceptedAndHeldStreamPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        CancellationToken releaseToken,
        CancellationToken cancellationToken)
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
                expectedHost,
                expectedPosition,
                cancellationToken);
            await WriteAcceptedHandshakeAsync(
                connection,
                pipeReader,
                cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, releaseToken);
        }
        catch (OperationCanceledException) when (
            releaseToken.IsCancellationRequested)
        {
            // The test intentionally releases the held stream after timeout.
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunAcceptedThenResetPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        TaskCompletionSource<bool> resetSignal,
        CancellationToken cancellationToken)
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
                expectedHost,
                expectedPosition,
                cancellationToken);
            await WriteAcceptedHandshakeAsync(
                connection,
                pipeReader,
                cancellationToken);
            await WriteAtomsAsync(
                connection,
                [CreatePacketAtom(99, [0x50, 0x51])],
                cancellationToken);
            await resetSignal.Task.WaitAsync(cancellationToken);

            connection.TransportSocket.LingerState =
                new LingerOption(enable: true, seconds: 0);
            connection.TransportSocket.Close();
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunAcceptedThenAcceptedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        IReadOnlyList<PcpAtom> firstPostHandshakeAtoms,
        IReadOnlyList<PcpAtom> secondPostHandshakeAtoms,
        CancellationToken cancellationToken)
    {
        await RunAcceptedPeerAsync(
            listener,
            expectedHost,
            expectedPosition,
            firstPostHandshakeAtoms,
            cancellationToken);
        await RunAcceptedPeerAsync(
            listener,
            expectedHost,
            expectedPosition,
            secondPostHandshakeAtoms,
            cancellationToken);
    }

    private static async Task RunAcceptedThenTruncatedThenAcceptedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        long secondExpectedPosition,
        IReadOnlyList<PcpAtom> secondPostHandshakeAtoms,
        CancellationToken cancellationToken)
    {
        await RunAcceptedThenTruncatedStreamPeerAsync(
            listener,
            expectedHost,
            expectedPosition,
            cancellationToken);
        await RunAcceptedPeerAsync(
            listener,
            expectedHost,
            secondExpectedPosition,
            secondPostHandshakeAtoms,
            cancellationToken);
    }

    private static async Task RunAcceptedThenRelayFullPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        long expectedPosition,
        IReadOnlyList<PcpAtom> hostAtoms,
        CancellationToken cancellationToken)
    {
        await RunAcceptedPeerAsync(
            listener,
            expectedHost,
            expectedPosition,
            [],
            cancellationToken);
        await RunRelayFullPeerAsync(
            listener,
            expectedHost,
            expectedPosition,
            hostAtoms,
            cancellationToken);
    }

    private static async Task WriteAcceptedHandshakeAsync(
        TcpTransportConnection connection,
        PipeReader pipeReader,
        CancellationToken cancellationToken)
    {
        await WriteHttpResponseAsync(
            connection,
            200,
            "OK",
            cancellationToken);
        await ReadAndAssertHeloAsync(pipeReader, cancellationToken);
        await WriteAtomsAsync(
            connection,
            [CreateOlehAtom(), CreateOkayAtom()],
            cancellationToken);
    }

    private static async Task RunHeldAcceptedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        TaskCompletionSource<bool> requestSeen,
        CancellationToken releaseToken,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await ReadAndAssertRequestAsync(
                pipeReader,
                expectedHost,
                0,
                cancellationToken);
            requestSeen.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, releaseToken);
        }
        catch (OperationCanceledException) when (releaseToken.IsCancellationRequested)
        {
            // The test intentionally cancels the pending redirect handshake.
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunTruncatedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        CancellationToken cancellationToken)
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
                expectedHost,
                0,
                cancellationToken);
            await WriteHttpResponseAsync(
                connection,
                503,
                "Service Unavailable",
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunHttpRejectedPeerAsync(
        TcpTransportListener listener,
        string expectedHost,
        int statusCode,
        CancellationToken cancellationToken)
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
                expectedHost,
                0,
                cancellationToken);
            await WriteHttpResponseAsync(
                connection,
                statusCode,
                "Rejected",
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task ReadAndAssertRequestAsync(
        PipeReader pipeReader,
        string expectedHost,
        long expectedPosition,
        CancellationToken cancellationToken)
    {
        var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
            pipeReader,
            32 * 1024,
            cancellationToken);
        Assert.Equal("GET", request.Method);
        Assert.Equal(expectedHost, request.Headers["Host"]);
        Assert.Equal(
            expectedPosition.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            request.Headers["x-peercast-pos"]);
    }

    private static async Task ReadAndAssertHeloAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken)
    {
        await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
        var helo = await atomReader.ReadNextAsync(cancellationToken);
        Assert.Equal(PcpKnownAtomNames.Helo, helo.Name);
        var session = helo.FindFirst(PcpKnownAtomNames.HeloSessionId);
        Assert.NotNull(session);
        Assert.Equal(
            SessionId.Value,
            PcpWireValueCodec.DecodeGuid(session!.Content.Span));
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

        await connection.WriteAsync(writer.WrittenMemory, cancellationToken);
    }

    private static PcpAtom CreateHost(params (string Address, int Port)[] endpoints)
    {
        var children = new List<PcpAtom>();
        foreach (var (address, port) in endpoints)
        {
            children.Add(PcpAtom.Leaf(
                PcpAtomName.FromAscii("ip"),
                PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse(address))));
            children.Add(PcpAtom.Leaf(
                PcpKnownAtomNames.HeloPort,
                PcpWireValueCodec.EncodeUInt16(checked((ushort)port))));
        }

        return PcpAtom.Parent(PcpKnownAtomNames.Host, children.ToArray());
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

    private static PcpAtom CreateQuitAtom(uint code = 0)
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.Quit,
            PcpWireValueCodec.EncodeUInt32(code));
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

    private static PcpAtom CreateOkayAtom()
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.Okay,
            PcpWireValueCodec.EncodeUInt32(1));
    }

    private static PcpRelayHostEndpoint Endpoint(string address, int port)
    {
        return new(IPAddress.Parse(address), port);
    }

    private static ChannelStatusTracker CreateTracker()
    {
        return new ChannelStatusTracker(
            new ChannelId(ChannelGuid),
            SessionId);
    }

    private sealed class CollectingPacketConsumer : IPcpChannelPacketConsumer
    {
        public List<ObservedPacket> Packets { get; } = [];

        public TaskCompletionSource<bool> FirstPacketObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Packets.Add(new ObservedPacket(
                packet.Sequence,
                packet.WirePosition,
                packet.Payload.ToArray()));
            FirstPacketObserved.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ObservedPacket(
        long Sequence,
        uint WirePosition,
        byte[] Payload);

    private sealed class FakeTransport : IPcpChannelTransportConnector, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly TcpTransportConnector _connector;
        private readonly List<TcpTransportListener> _listeners = [];
        private readonly Dictionary<PcpRelayHostEndpoint, TcpTransportListener>
            _redirectListeners = [];
        private readonly HashSet<PcpRelayHostEndpoint> _blockedRedirects = [];

        public FakeTransport()
        {
            var options = new TcpTransportOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                MaxConcurrentConnections = 32,
            };
            _connector = new TcpTransportConnector(options);
            OriginalListener = CreateListener(options);
        }

        public TcpTransportListener OriginalListener { get; }

        public List<IPEndPoint> RedirectRequests { get; } = [];

        public IReadOnlyList<PcpRelayHostEndpoint> LogicalRedirectEndpoints =>
            RedirectRequests
                .Select(endpoint => new PcpRelayHostEndpoint(
                    endpoint.Address,
                    endpoint.Port))
                .ToArray();

        public TaskCompletionSource<IPEndPoint> FirstRedirectRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IPEndPoint> SecondRedirectRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TcpTransportListener AddRedirect(PcpRelayHostEndpoint endpoint)
        {
            var listener = CreateListener(new TcpTransportOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                MaxConcurrentConnections = 32,
            });
            lock (_gate)
            {
                _redirectListeners.Add(endpoint, listener);
            }

            return listener;
        }

        public void BlockRedirect(PcpRelayHostEndpoint endpoint)
        {
            lock (_gate)
            {
                _blockedRedirects.Add(endpoint);
            }
        }

        public ValueTask<TcpTransportConnection> ConnectOriginalAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            return _connector.ConnectAsync(
                OriginalListener.LocalEndPoint,
                cancellationToken);
        }

        public async ValueTask<TcpTransportConnection> ConnectRedirectAsync(
            IPEndPoint endpoint,
            CancellationToken cancellationToken)
        {
            var logicalEndpoint = new IPEndPoint(
                endpoint.Address,
                endpoint.Port);
            RedirectRequests.Add(logicalEndpoint);
            FirstRedirectRequest.TrySetResult(logicalEndpoint);
            if (RedirectRequests.Count == 2)
            {
                SecondRedirectRequest.TrySetResult(logicalEndpoint);
            }

            TcpTransportListener? listener;
            var blocked = false;
            lock (_gate)
            {
                blocked = _blockedRedirects.Contains(
                    new PcpRelayHostEndpoint(endpoint.Address, endpoint.Port));
                _redirectListeners.TryGetValue(
                    new PcpRelayHostEndpoint(endpoint.Address, endpoint.Port),
                    out listener);
            }

            if (blocked)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }

            if (listener is null)
            {
                throw new SocketException((int)SocketError.ConnectionRefused);
            }

            return await _connector.ConnectAsync(
                listener.LocalEndPoint,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            List<TcpTransportListener> listeners;
            lock (_gate)
            {
                listeners = _listeners.ToList();
            }

            for (var index = listeners.Count - 1; index >= 0; index--)
            {
                await listeners[index].DisposeAsync();
            }
        }

        private TcpTransportListener CreateListener(TcpTransportOptions options)
        {
            var listener = new TcpTransportListener(
                new IPEndPoint(IPAddress.Loopback, 0),
                options);
            listener.Start();
            lock (_gate)
            {
                _listeners.Add(listener);
            }

            return listener;
        }
    }
}
