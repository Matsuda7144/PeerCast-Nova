using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayFanoutTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task Consume_CopiesPayloadBeforeIngressCallbackReturns()
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new GatedRelaySink();
        await using var child = fanout.AddChild(sink);
        byte[] source = [0x10, 0x20, 0x30];

        await fanout.ConsumeAsync(
            CreatePacket(0, source),
            CancellationToken.None);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        source.AsSpan().Fill(0xFF);
        sink.Release.TrySetResult();
        await sink.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([0x10, 0x20, 0x30], sink.ReceivedPayload);
        await WaitForStateAsync(child, PcpRelayChildState.Accepting);
        Assert.Equal(1, child.Snapshot.DeliveredPacketCount);
        Assert.Equal(0, child.Snapshot.PendingPacketCount);
    }

    [Fact]
    public async Task AddChild_ReplaysOnlyLatestHeadBeforeFutureLiveData()
    {
        await using var fanout = new PcpRelayFanout(
            replayHeadForNewChildren: true);
        await fanout.ConsumeAsync(
            CreatePacket(0, [0x10], type: 0x64616568),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(1, [0x20], type: 0x61746164),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(2, [0x11], type: 0x64616568),
            CancellationToken.None);

        var sink = new CollectingRelaySink();
        await using var child = fanout.AddChild(sink);
        await sink.WaitForCountAsync(1);

        await fanout.ConsumeAsync(
            CreatePacket(3, [0x30], type: 0x61746164),
            CancellationToken.None);
        await sink.WaitForCountAsync(2);

        Assert.Collection(
            sink.Packets,
            head =>
            {
                Assert.Equal(0x64616568U, head.Type);
                Assert.Equal([0x11], head.Payload.ToArray());
                Assert.True(head.IsRetainedHeadReplay);
            },
            data =>
            {
                Assert.Equal(0x61746164U, data.Type);
                Assert.Equal([0x30], data.Payload.ToArray());
                Assert.False(data.IsRetainedHeadReplay);
            });
    }

    [Fact]
    public async Task Consume_RejectsHeadLargerThanBoundedReplayBudget()
    {
        var options = new PcpRelayFanoutOptions
        {
            MaxPendingBytesPerChild = 8,
        };
        var fanout = new PcpRelayFanout(
            options,
            replayHeadForNewChildren: true);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fanout.ConsumeAsync(
                CreatePacket(0, new byte[9], type: 0x64616568),
                CancellationToken.None));

        Assert.Contains("bounded 8-byte relay bootstrap limit", exception.Message, StringComparison.Ordinal);
        await fanout.DisposeAsync();
    }

    [Fact]
    public async Task Consume_SlowChildIsStoppedWithoutBlockingFastChild()
    {
        var options = new PcpRelayFanoutOptions
        {
            MaxPendingPacketsPerChild = 1,
            MaxPendingBytesPerChild = 64,
        };
        await using var fanout = new PcpRelayFanout(options);
        var slowSink = new GatedRelaySink();
        var fastSink = new CollectingRelaySink();
        await using var slowChild = fanout.AddChild(slowSink);
        await using var fastChild = fanout.AddChild(fastSink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x01]),
            CancellationToken.None);
        await slowSink.Entered.Task;
        await fastSink.WaitForCountAsync(1);

        await fanout.ConsumeAsync(
            CreatePacket(1, [0x02]),
            CancellationToken.None);
        await fastSink.WaitForCountAsync(2);
        await slowChild.Completion;

        Assert.Equal(1, fanout.ActiveChildCount);
        Assert.Equal(
            PcpRelayChildEndReason.SlowConsumer,
            slowChild.Snapshot.EndReason);
        Assert.Equal(0, slowChild.Snapshot.DeliveredPacketCount);
        Assert.Equal(2, slowChild.Snapshot.DiscardedPacketCount);
        Assert.Equal(2, fastChild.Snapshot.DeliveredPacketCount);
        Assert.Equal([0x01], fastSink.Packets[0].Payload);
        Assert.Equal([0x02], fastSink.Packets[1].Payload);
    }

    [Fact]
    public async Task Consume_FailingSinkIsIsolatedFromAnotherChild()
    {
        await using var fanout = new PcpRelayFanout();
        var healthySink = new CollectingRelaySink();
        await using var failingChild = fanout.AddChild(new FailingRelaySink());
        await using var healthyChild = fanout.AddChild(healthySink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x41]),
            CancellationToken.None);
        await healthySink.WaitForCountAsync(1);
        await WaitForStateAsync(failingChild, PcpRelayChildState.Stopped);

        Assert.Equal(
            PcpRelayChildEndReason.SinkFailed,
            failingChild.Snapshot.EndReason);
        Assert.Equal(1, failingChild.Snapshot.DiscardedPacketCount);
        Assert.Equal(1, healthyChild.Snapshot.DeliveredPacketCount);
        Assert.Equal(1, fanout.ActiveChildCount);
    }

    [Fact]
    public async Task Consume_PacketLargerThanChildByteLimitStopsBeforeSend()
    {
        var options = new PcpRelayFanoutOptions
        {
            MaxPendingBytesPerChild = 2,
        };
        await using var fanout = new PcpRelayFanout(options);
        var sink = new CountingRelaySink();
        await using var child = fanout.AddChild(sink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x01, 0x02, 0x03]),
            CancellationToken.None);
        await WaitForStateAsync(child, PcpRelayChildState.Stopped);

        Assert.Equal(0, sink.CallCount);
        Assert.Equal(PcpRelayChildEndReason.SlowConsumer, child.Snapshot.EndReason);
        Assert.Equal(1, child.Snapshot.DiscardedPacketCount);
        Assert.Equal(3, child.Snapshot.DiscardedByteCount);
    }

    [Fact]
    public async Task AddChild_EnforcesFiniteLimitAndAllowsCompletedReplacement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRelayFanout(
                new PcpRelayFanoutOptions { MaxChildren = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRelayFanout(
                new PcpRelayFanoutOptions
                {
                    MaxPendingPacketsPerChild = 0,
                }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcpRelayFanout(
                new PcpRelayFanoutOptions
                {
                    MaxPendingBytesPerChild = 0,
                }));

        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions { MaxChildren = 1 });
        var first = fanout.AddChild(new CountingRelaySink());

        Assert.Throws<InvalidOperationException>(
            () => fanout.AddChild(new CountingRelaySink()));
        Assert.True(await first.StopAsync());

        await using var replacement = fanout.AddChild(new CountingRelaySink());
        Assert.Equal(1, fanout.ActiveChildCount);
        await first.DisposeAsync();
    }

    [Fact]
    public async Task TryAddChild_ReportsFullAndCompletionReleasesSlot()
    {
        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions { MaxChildren = 1 });
        Assert.True(fanout.TryAddChild(
            new CountingRelaySink(),
            out var first));
        Assert.False(fanout.TryAddChild(
            new CountingRelaySink(),
            out _));

        Assert.True(await first.StopAsync());
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fanout.TryAddChild(
            new CountingRelaySink(),
            out var replacement));
        await replacement.DisposeAsync();
        await first.DisposeAsync();
    }

    [Fact]
    public async Task StopChild_DoesNotStopAnotherRegistration()
    {
        await using var fanout = new PcpRelayFanout();
        var firstSink = new CollectingRelaySink();
        var secondSink = new CollectingRelaySink();
        var first = fanout.AddChild(firstSink);
        await using var second = fanout.AddChild(secondSink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x11]),
            CancellationToken.None);
        await firstSink.WaitForCountAsync(1);
        await secondSink.WaitForCountAsync(1);
        Assert.True(await first.StopAsync());

        await fanout.ConsumeAsync(
            CreatePacket(1, [0x22]),
            CancellationToken.None);
        await secondSink.WaitForCountAsync(2);

        Assert.Equal(PcpRelayChildEndReason.OwnerRemoved, first.Snapshot.EndReason);
        Assert.Single(firstSink.Packets);
        Assert.Equal(2, secondSink.Packets.Length);
        Assert.Equal(1, fanout.ActiveChildCount);
        await first.DisposeAsync();
    }

    [Fact]
    public async Task DefaultOptions_AbsorbsInitialBurstWithoutSlowConsumerTermination()
    {
        var defaultFanout = new PcpRelayFanoutOptions();
        var defaultHttpFlv = new PcpHttpFlvPlaybackOptions();
        Assert.True(defaultFanout.MaxPendingPacketsPerChild >= 64);
        Assert.True(defaultHttpFlv.Fanout.MaxPendingPacketsPerChild >= 64);

        await using var fanout = new PcpRelayFanout();
        var sink = new CollectingRelaySink();
        await using var child = fanout.AddChild(sink);

        const int burstCount = 64;
        var payload = new byte[4096];
        for (var i = 0; i < burstCount; i++)
        {
            await fanout.ConsumeAsync(CreatePacket(i, payload), CancellationToken.None);
        }

        await sink.WaitForCountAsync(burstCount);
        Assert.Equal(burstCount, sink.Packets.Length);
        Assert.Null(child.Snapshot.EndReason);
        Assert.Equal(0, child.Snapshot.DiscardedPacketCount);
    }

    [Fact]
    public async Task StopChild_ReturnsAtDeadlineAndRetainsBoundedSlotUntilCleanup()
    {
        var options = new PcpRelayFanoutOptions
        {
            MaxChildren = 1,
            ChildStopTimeout = TimeSpan.FromMilliseconds(50),
        };
        await using var fanout = new PcpRelayFanout(options);
        var sink = new UncooperativeRelaySink();
        var child = fanout.AddChild(sink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x33]),
            CancellationToken.None);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(await child.StopAsync());
        Assert.Equal(PcpRelayChildState.Stopping, child.Snapshot.State);
        Assert.Throws<InvalidOperationException>(
            () => fanout.AddChild(new CountingRelaySink()));

        sink.Release.TrySetResult();
        await WaitForStateAsync(child, PcpRelayChildState.Stopped);
        await using var replacement = fanout.AddChild(new CountingRelaySink());
        await child.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_StopsBlockedChildAndDiscardsPendingPacket()
    {
        var fanout = new PcpRelayFanout();
        var sink = new GatedRelaySink();
        var child = fanout.AddChild(sink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x7A]),
            CancellationToken.None);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fanout.DisposeAsync();

        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(PcpRelayChildState.Stopped, child.Snapshot.State);
        Assert.Equal(PcpRelayChildEndReason.FanoutStopped, child.Snapshot.EndReason);
        Assert.Equal(1, child.Snapshot.DiscardedPacketCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => fanout.ConsumeAsync(
                CreatePacket(1, [0x7B]),
                CancellationToken.None).AsTask());

        await child.DisposeAsync();
    }

    [Fact]
    public async Task Consume_PreservesExplicitDiscontinuityForChild()
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new CollectingRelaySink();
        await using var child = fanout.AddChild(sink);
        var discontinuity = new PcpStreamDiscontinuity(1000, 900, 1000);

        await fanout.ConsumeAsync(
            CreatePacket(4, [0x55], discontinuity),
            CancellationToken.None);
        await sink.WaitForCountAsync(1);

        var packet = Assert.Single(sink.Packets);
        Assert.Equal(4, packet.Sequence);
        Assert.Null(packet.StreamPosition);
        Assert.Equal(1000U, packet.Discontinuity?.PreviousWirePosition);
        Assert.Equal(900U, packet.Discontinuity?.ObservedWirePosition);
        Assert.Equal(1000, packet.Discontinuity?.AbsolutePositionBeforeObservation);
    }

    [Fact]
    public async Task WaitForIdle_IsFiniteAndIncludesInFlightPacket()
    {
        await using var fanout = new PcpRelayFanout();
        var sink = new GatedRelaySink();
        await using var child = fanout.AddChild(sink);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x61, 0x62]),
            CancellationToken.None);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(await fanout.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(50)));
        Assert.Equal(1, child.Snapshot.PendingPacketCount);

        sink.Release.TrySetResult();

        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));
        Assert.Equal(0, child.Snapshot.PendingPacketCount);
        Assert.Equal(1, child.Snapshot.DeliveredPacketCount);
    }

    [Fact]
    public void StatusSnapshot_HasOnlyFixedEnumsAndNumericCounters()
    {
        var properties = typeof(PcpRelayChildStatusSnapshot).GetProperties();

        Assert.DoesNotContain(
            properties,
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload,
        PcpStreamDiscontinuity? discontinuity = null,
        uint type = 1)
    {
        var wirePosition = checked((uint)(100 + sequence));
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type,
            wirePosition,
            discontinuity is null ? wirePosition : null,
            payload,
            discontinuity);
    }

    private static async Task WaitForStateAsync(
        PcpRelayChildRegistration child,
        PcpRelayChildState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (child.Snapshot.State != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class GatedRelaySink : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[]? ReceivedPayload { get; private set; }

        public async ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();

            try
            {
                await Release.Task.WaitAsync(
                    cancellationToken).ConfigureAwait(false);
                ReceivedPayload = packet.Payload.ToArray();
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class CollectingRelaySink : IPcpRelayChildSink
    {
        private readonly object _gate = new();
        private readonly List<ObservedRelayPacket> _packets = [];
        private TaskCompletionSource _packetAdded = CreatePacketAddedSource();

        public ObservedRelayPacket[] Packets
        {
            get
            {
                lock (_gate)
                {
                    return _packets.ToArray();
                }
            }
        }

        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource packetAdded;

            lock (_gate)
            {
                _packets.Add(
                    new ObservedRelayPacket(
                        packet.Sequence,
                        packet.Type,
                        packet.StreamPosition,
                        packet.Payload.ToArray(),
                        packet.Discontinuity,
                        packet.IsRetainedHeadReplay));
                packetAdded = _packetAdded;
                _packetAdded = CreatePacketAddedSource();
            }

            packetAdded.TrySetResult();

            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int expected)
        {
            while (true)
            {
                Task packetAdded;

                lock (_gate)
                {
                    if (_packets.Count >= expected)
                    {
                        return;
                    }

                    packetAdded = _packetAdded.Task;
                }

                await packetAdded.ConfigureAwait(false);
            }
        }

        private static TaskCompletionSource CreatePacketAddedSource()
        {
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class UncooperativeRelaySink : IPcpRelayChildSink
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }

    private sealed class FailingRelaySink : IPcpRelayChildSink
    {
        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Synthetic relay-child failure.");
        }
    }

    private sealed class CountingRelaySink : IPcpRelayChildSink
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ObservedRelayPacket(
        long Sequence,
        uint Type,
        long? StreamPosition,
        byte[] Payload,
        PcpStreamDiscontinuity? Discontinuity,
        bool IsRetainedHeadReplay);
}
