using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpSmartRelayRecoveryTests
{
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(false, 2, 4)]
    [InlineData(false, 3, 8)]
    [InlineData(false, 4, 16)]
    [InlineData(false, 5, 30)]
    [InlineData(true, 0, 60)]
    [InlineData(true, 1, 120)]
    [InlineData(true, 2, 300)]
    [InlineData(true, 99, 300)]
    public void BackoffHasBoundedJitterAndLowFrequencyCap(bool low, int step, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds * .8), PcpSmartRelayOptions.Backoff(low, step, 0));
        Assert.Equal(TimeSpan.FromSeconds(seconds), PcpSmartRelayOptions.Backoff(low, step, .5));
        Assert.Equal(TimeSpan.FromSeconds(seconds * 1.2), PcpSmartRelayOptions.Backoff(low, step, 1));
    }

    [Fact]
    public async Task PersistentModeCrossesFiveMinutesUsesOneAttemptAndStopsWithoutLateRetry()
    {
        using var clock = new AnnounceManualTimeProvider();
        var connector = Failing();
        await using var session = Session(connector, clock);
        var run = session.RunAsync("explicit-bootstrap", 7144);
        await Backoff(session, clock, 1);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Backoff(session, clock, 2);
        Assert.True(session.SmartRelaySnapshot.LowFrequency);
        Assert.Equal(TimeSpan.FromSeconds(60), session.SmartRelaySnapshot.RetryDelay);
        clock.Advance(TimeSpan.FromSeconds(60));
        await Backoff(session, clock, 3);
        Assert.Equal(TimeSpan.FromSeconds(120), session.SmartRelaySnapshot.RetryDelay);
        clock.Advance(TimeSpan.FromSeconds(120));
        await Backoff(session, clock, 4);
        Assert.Equal(TimeSpan.FromSeconds(300), session.SmartRelaySnapshot.RetryDelay);
        clock.Advance(TimeSpan.FromHours(10));
        await Backoff(session, clock, 5);
        Assert.Equal(1, session.SmartRelaySnapshot.WaveAttemptCount);
        await session.StopAsync();
        Assert.Equal(PcpChannelSessionEndReason.Stopped, (await run).EndReason);
        clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal(5, connector.Requests.Count);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task LimitedModeEndsAtMonotonicRecoveryDeadline()
    {
        using var clock = new AnnounceManualTimeProvider();
        await using var session = Session(Failing(), clock, PcpSmartRelayMode.Limited);
        var run = session.RunAsync("explicit-bootstrap", 7144);
        await Backoff(session, clock, 1);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(PcpChannelSessionEndReason.RecoveryExhausted, (await run.WaitAsync(TimeSpan.FromSeconds(5))).EndReason);
        Assert.Equal(PcpSmartRelayState.Failed, session.SmartRelaySnapshot.State);
    }

    [Fact]
    public async Task WaveIncludesBootstrapAndAtMostSevenUniqueCandidates()
    {
        using var clock = new AnnounceManualTimeProvider();
        var tracker = Tracker();
        using var peers = new PcpPeerDirectory(tracker.Snapshot.ChannelId.Value, tracker.Snapshot.SessionId.Value, clock);
        for (var i = 0; i < 12; i++) peers.Observe(PcpPeerDirectoryTests.Host(7144 + i) with
        { ChannelId = tracker.Snapshot.ChannelId.Value }, new());
        var connector = Failing();
        await using var session = new PcpChannelSession(connector, tracker, new Sink(), Options(clock), peers);
        var run = session.RunAsync("explicit-bootstrap", 7144);
        await Backoff(session, clock, 8);
        Assert.Equal(8, connector.Requests.Count);
        Assert.Equal(7, connector.Requests.Where(e => e is not null).Distinct().Count());
        Assert.Equal(1, connector.Requests.Count(e => e is null));
        await session.StopAsync(); await run;
    }

    [Fact]
    public async Task WaitingConnectUsesThirtySecondWaveBudget()
    {
        using var clock = new AnnounceManualTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new Connector(async (_, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); });
        await using var session = Session(connector, clock);
        var run = session.RunAsync("explicit-bootstrap", 7144);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(30));
        await Backoff(session, clock, 1);
        Assert.Single(connector.Requests);
        await session.StopAsync(); await run;
    }

    [Fact]
    public async Task RetiredDirectoryCannotStartAnotherConnectionAfterBackoff()
    {
        using var clock = new AnnounceManualTimeProvider();
        var tracker = Tracker();
        using var peers = new PcpPeerDirectory(tracker.Snapshot.ChannelId.Value, tracker.Snapshot.SessionId.Value, clock);
        var connector = Failing();
        await using var session = new PcpChannelSession(connector, tracker, new Sink(), Options(clock), peers);
        var run = session.RunAsync("explicit-bootstrap", 7144);
        await Backoff(session, clock, 1);
        peers.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PcpChannelSessionEndReason.Stopped, (await run.WaitAsync(TimeSpan.FromSeconds(5))).EndReason);
        Assert.Single(connector.Requests);
    }

    [Fact]
    public async Task MultipleOwnersShareFourNegotiationSlotsAndCancelWaiters()
    {
        var active = 0; var maximum = 0;
        var connector = new Connector(async (_, token) =>
        {
            var now = Interlocked.Increment(ref active);
            int prior;
            do { prior = maximum; } while (now > prior && Interlocked.CompareExchange(ref maximum, now, prior) != prior);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); }
            finally { Interlocked.Decrement(ref active); }
        });
        var sessions = Enumerable.Range(0, 8).Select(_ => Session(connector, TimeProvider.System)).ToArray();
        var runs = sessions.Select(s => s.RunAsync("explicit-bootstrap", 7144)).ToArray();
        try
        {
            await Until(() => Volatile.Read(ref active) == 4);
            Assert.Equal(4, connector.Requests.Count);
            Assert.Equal(4, maximum);
        }
        finally { await Task.WhenAll(sessions.Select(s => s.StopAsync())); foreach (var s in sessions) await s.DisposeAsync(); }
        await Task.WhenAll(runs);
        Assert.Equal(0, active);
    }

    internal static async Task Until(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(1, deadline.Token);
    }
    private static Task Backoff(PcpChannelSession session, AnnounceManualTimeProvider clock, int attempts) =>
        Until(() => session.SmartRelaySnapshot is { State: PcpSmartRelayState.Backoff } s && s.AttemptCount == attempts && clock.ActiveTimerCount > 0);
    private static ChannelStatusTracker Tracker() => new(new(Guid.NewGuid()), new(Guid.NewGuid()));
    private static PcpChannelSessionOptions Options(TimeProvider clock, PcpSmartRelayMode mode = PcpSmartRelayMode.Persistent) => new()
    { SmartRelay = new() { Mode = mode, TimeProvider = clock, JitterSample = () => .5 } };
    private static PcpChannelSession Session(Connector connector, TimeProvider clock, PcpSmartRelayMode mode = PcpSmartRelayMode.Persistent) =>
        new(connector, Tracker(), new Sink(), Options(clock, mode));
    private static Connector Failing() => new((_, _) => ValueTask.FromException<TcpTransportConnection>(new SocketException((int)SocketError.ConnectionRefused)));
    private sealed class Sink : IPcpChannelPacketConsumer
    { public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken token) => ValueTask.CompletedTask; }
    private sealed class Connector(Func<IPEndPoint?, CancellationToken, ValueTask<TcpTransportConnection>> connect) : IPcpChannelTransportConnector
    {
        internal ConcurrentQueue<IPEndPoint?> Requests { get; } = new();
        public ValueTask<TcpTransportConnection> ConnectOriginalAsync(string host, int port, CancellationToken token)
        { Requests.Enqueue(null); return connect(null, token); }
        public ValueTask<TcpTransportConnection> ConnectRedirectAsync(IPEndPoint endpoint, CancellationToken token)
        { Requests.Enqueue(endpoint); return connect(endpoint, token); }
    }
}
