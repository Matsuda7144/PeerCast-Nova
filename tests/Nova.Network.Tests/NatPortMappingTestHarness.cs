using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

internal sealed class NatPortMappingTestHarness : IAsyncDisposable
{
    public static NatNetworkIdentity Home { get; } = new(
        new NatDeviceIdentity("192.168.1.1", "test-interface"), "192.168.1.2", 1);

    public NatPortMappingTestHarness(bool enabled = true)
    {
        Manager = new NatPortMappingManager(Router,
            new NatPortMappingOptions { Enabled = enabled }, Clock,
            new NatDeviceOperationSerializer(), static () => TimeSpan.Zero);
        Manager.UpdateNetwork(Home);
    }

    public ManualNatTimeProvider Clock { get; } = new();
    public FakeNatRouter Router { get; } = new();
    public NatPortMappingManager Manager { get; }

    public IDisposable Acquire(int port = 7144) =>
        Manager.AcquirePublicTcpListener(new IPEndPoint(IPAddress.Any, port));

    public async Task MapAsync(int externalPort = 7144, uint lifetime = 120)
    {
        var turn = Manager.ReconcileAsync();
        var call = await Router.NextAsync();
        Assert.Equal(NatMappingOperation.Map, call.Request.Operation);
        call.Succeed(externalPort, lifetime);
        await turn.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        Router.AutoComplete = true;
        Router.CompleteOutstanding();
        var shutdown = Manager.ShutdownAsync();
        Clock.Advance(TimeSpan.FromSeconds(10));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

internal sealed class FakeNatRouter : INatMappingBackend
{
    private readonly Channel<Call> calls = Channel.CreateUnbounded<Call>();
    private readonly ConcurrentQueue<Call> history = new();
    private readonly ConcurrentDictionary<RouterKey, (int Port, uint Lifetime)> mappings = new();
    private int active;
    private int maximumActive;

    public bool AutoComplete { get; set; }
    public int Count => history.Count;
    public int MaximumActive => Volatile.Read(ref maximumActive);
    public IReadOnlyList<NatMappingRequest> History => history.Select(call => call.Request).ToArray();

    public async Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken cancellationToken)
    {
        // Deliberately ignores cancellation: the router can finish an already
        // submitted operation after the host's timeout or generation change.
        var concurrency = Interlocked.Increment(ref active);
        InterlockedExtensionsSetMaximum(ref maximumActive, concurrency);
        var call = new Call(this, request);
        history.Enqueue(call);
        calls.Writer.TryWrite(call);
        if (AutoComplete)
        {
            call.Succeed(request.SuggestedExternalPort, request.RequestedLifetimeSeconds);
        }

        try
        {
            return await call.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref active);
        }
    }

    public Task<Call> NextAsync() => calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    public bool Contains(NatMappingIdentity identity) => mappings.ContainsKey(Key(identity));

    public int? ExternalPort(NatMappingIdentity identity) =>
        mappings.TryGetValue(Key(identity), out var value) ? value.Port : null;

    public void DeleteWithoutResponse(NatMappingIdentity identity) => mappings.TryRemove(Key(identity), out _);

    public void CompleteOutstanding()
    {
        foreach (var call in history.Where(call => !call.Completion.Task.IsCompleted))
        {
            call.Succeed(call.Request.SuggestedExternalPort, call.Request.RequestedLifetimeSeconds);
        }
    }

    private static RouterKey Key(NatMappingIdentity identity) =>
        new(identity.Network.Device, identity.Network.InternalAddress, identity.InternalPort);

    private static void InterlockedExtensionsSetMaximum(ref int location, int value)
    {
        int previous;
        do
        {
            previous = Volatile.Read(ref location);
            if (previous >= value) { return; }
        }
        while (Interlocked.CompareExchange(ref location, value, previous) != previous);
    }

    private sealed record RouterKey(NatDeviceIdentity Device, string Address, int InternalPort);

    internal sealed class Call(FakeNatRouter router, NatMappingRequest request)
    {
        public NatMappingRequest Request { get; } = request;
        public TaskCompletionSource<NatBackendResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ApplyRouterSide(int externalPort = 7144, uint lifetime = 120)
        {
            if (Request.Operation == NatMappingOperation.Unmap)
            {
                router.mappings.TryRemove(Key(Request.Identity), out _);
            }
            else
            {
                router.mappings[Key(Request.Identity)] = (externalPort, lifetime);
            }
        }

        public void Succeed(int externalPort = 7144, uint lifetime = 120, uint routerEpoch = 100)
        {
            ApplyRouterSide(externalPort, lifetime);
            Completion.TrySetResult(Request.Operation == NatMappingOperation.Unmap
                ? new NatBackendResult(NatBackendOutcome.Success, 0, 0, routerEpoch)
                : new NatBackendResult(NatBackendOutcome.Success, externalPort, lifetime, routerEpoch));
        }

        public void Unknown() => Completion.TrySetResult(new NatBackendResult(NatBackendOutcome.ResultUnknown));
    }
}

// No delays, sleeps or polling determine race order. Advance fires registered
// timeout callbacks, while backend completion is controlled separately.
internal sealed class ManualNatTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long timestamp;

    public override DateTimeOffset GetUtcNow() { lock (gate) { return now; } }
    public override long GetTimestamp() { lock (gate) { return timestamp; } }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public int ActiveTimerCount { get { lock (gate) { return timers.Count(timer => timer.DueTimestamp != long.MaxValue); } } }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
            timer.Change(dueTime, period);
        }

        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<ManualTimer> due;
        lock (gate)
        {
            now += duration;
            timestamp += duration.Ticks;
            due = timers.Where(timer => timer.DueTimestamp <= timestamp).ToList();
            foreach (var timer in due) { timer.PrepareFire(); }
        }

        foreach (var timer in due) { timer.Fire(); }
    }

    public void ShiftWallClock(TimeSpan duration)
    {
        lock (gate) { now += duration; }
    }

    private sealed class ManualTimer(ManualNatTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan period;
        private bool disposed;
        public long DueTimestamp { get; private set; } = long.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan nextPeriod)
        {
            lock (owner.gate)
            {
                if (disposed) { return false; }
                period = nextPeriod;
                DueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.timestamp + dueTime.Ticks;
                return true;
            }
        }

        public void PrepareFire() => DueTimestamp = period > TimeSpan.Zero ? owner.timestamp + period.Ticks : long.MaxValue;
        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner.gate)
            {
                disposed = true;
                DueTimestamp = long.MaxValue;
            }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
