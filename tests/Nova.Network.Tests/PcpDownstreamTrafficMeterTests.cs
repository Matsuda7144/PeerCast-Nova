using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamTrafficMeterTests
{
    [Fact]
    public void UnobservedWarmupAndObservedIdleAreDistinctAndKbpsIsDecimal()
    {
        var clock = new Clock();
        var meter = new PcpDownstreamTrafficMeter(clock);
        Assert.False(meter.Snapshot.HasObservation);
        Assert.Null(meter.Snapshot.KilobitsPerSecond);
        meter.Observe();
        Assert.True(meter.Snapshot.HasObservation);
        Assert.Null(meter.Snapshot.KilobitsPerSecond);
        clock.Advance(1);
        Assert.Equal(0, meter.Snapshot.KilobitsPerSecond);
        meter.RecordCompletedWrite(125000);
        clock.Advance(1);
        Assert.Equal(500, meter.Snapshot.KilobitsPerSecond); // Two completed seconds, one active.
        Assert.Equal(125000, meter.Snapshot.CompletedWriteBytes);
        clock.Advance(6);
        Assert.Equal(0, meter.Snapshot.KilobitsPerSecond);
        Assert.Equal(125000, meter.Snapshot.CompletedWriteBytes);
    }

    [Fact]
    public void RollingWindowExpiresBurstAndIgnoresWallClockChanges()
    {
        var clock = new Clock();
        var meter = new PcpDownstreamTrafficMeter(clock);
        meter.RecordCompletedWrite(125000);
        clock.Advance(1);
        Assert.Equal(1000, meter.Snapshot.KilobitsPerSecond);
        clock.Wall = DateTimeOffset.UnixEpoch.AddYears(50);
        Assert.Equal(1000, meter.Snapshot.KilobitsPerSecond);
        clock.Advance(4);
        Assert.Equal(200, meter.Snapshot.KilobitsPerSecond);
        clock.Advance(1);
        Assert.Equal(0, meter.Snapshot.KilobitsPerSecond);
    }

    [Fact]
    public void ContinuousSendingPauseAndResumeUseOnlyCompletedIntervals()
    {
        var clock = new Clock();
        var meter = new PcpDownstreamTrafficMeter(clock);
        for (var second = 0; second < 5; second++)
        {
            meter.RecordCompletedWrite(125000);
            clock.Advance(1);
            Assert.Equal(1000, meter.Snapshot.KilobitsPerSecond);
        }
        clock.Advance(2);
        Assert.Equal(600, meter.Snapshot.KilobitsPerSecond);
        clock.Advance(4);
        Assert.Equal(0, meter.Snapshot.KilobitsPerSecond);
        meter.RecordCompletedWrite(125000);
        Assert.Equal(0, meter.Snapshot.KilobitsPerSecond); // Current partial second is excluded.
        clock.Advance(1);
        Assert.Equal(200, meter.Snapshot.KilobitsPerSecond);
        Assert.Equal(750000, meter.Snapshot.CompletedWriteBytes);
    }

    [Fact]
    public async Task ConcurrentConnectionsAndChannelsAggregateOnceAndKeepClosedHistory()
    {
        var clock = new Clock();
        var total = new PcpDownstreamTrafficMeter(clock);
        var first = total.CreateChild();
        var second = total.CreateChild();
        var stats = new PcpTransportStatistics(clock);
        var connections = Enumerable.Range(1, 16).Select(id =>
        {
            var counter = stats.CreateCounter(id);
            var meter = counter.ObserveDownstream(id % 2 == 0 ? first : second);
            return (counter, meter);
        }).ToArray();
        await Task.WhenAll(connections.Select(pair => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++) pair.counter.AddSent(10);
            pair.counter.Close();
        })));
        Assert.Equal(160000, total.Snapshot.CompletedWriteBytes);
        Assert.Equal(80000, first.Snapshot.CompletedWriteBytes);
        Assert.Equal(80000, second.Snapshot.CompletedWriteBytes);
        Assert.All(connections, pair => Assert.Equal(10000, pair.meter.Snapshot.CompletedWriteBytes));
        Assert.Equal(0, stats.Capture().SentBytes); // Old active-only counter contract is unchanged.
        clock.Advance(1);
        Assert.Equal(1280, total.Snapshot.KilobitsPerSecond);
        clock.Advance(6);
        Assert.Equal(0, total.Snapshot.KilobitsPerSecond);
    }

    [Fact]
    public async Task PendingWriteIsNotSentAndPartialFailureDoesNotInventUnknownBytes()
    {
        var clock = new Clock();
        var total = new PcpDownstreamTrafficMeter(clock);
        var counter = new PcpTransportStatistics(clock).CreateCounter(1);
        var connection = counter.ObserveDownstream(total);
        using var inner = new ControlledStream();
        using var stream = new TransportObservationStream(inner, counter);
        var write = stream.WriteAsync(new byte[72]).AsTask();
        await inner.Entered.Task;
        Assert.Equal(0, total.Snapshot.CompletedWriteBytes);
        inner.Release.SetResult();
        await write;
        Assert.Equal(72, total.Snapshot.CompletedWriteBytes);
        inner.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => stream.WriteAsync(new byte[123]).AsTask());
        Assert.Equal(74, inner.SimulatedSent); // The failing API cannot return that partial count.
        Assert.Equal(72, connection.Snapshot.CompletedWriteBytes);
        Assert.Equal(72, total.Snapshot.CompletedWriteBytes);
        Assert.Equal(1, total.Snapshot.UnconfirmedWrites);
        Assert.False(total.Snapshot.CumulativeComplete);
        clock.Advance(1);
        Assert.Null(total.Snapshot.KilobitsPerSecond);
        clock.Advance(6);
        Assert.Equal(0, total.Snapshot.KilobitsPerSecond);
        Assert.False(total.Snapshot.CumulativeComplete);
    }

    [Fact]
    public async Task CancelledWritesAreUncertainButCancelledReadsDoNotInvalidateTxEvidence()
    {
        var clock = new Clock();
        var meter = new PcpDownstreamTrafficMeter(clock);
        var counter = new PcpTransportStatistics(clock).CreateCounter(1);
        counter.ObserveDownstream(meter);
        using var stream = new TransportObservationStream(new CancelStream(), counter);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[2]).AsTask());
        await stream.WriteAsync(new byte[10]);
        Assert.True(meter.Snapshot.CumulativeComplete);
        Assert.Equal(10, meter.Snapshot.CompletedWriteBytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.WriteAsync(new byte[20], new(true)).AsTask());
        Assert.Equal(10, meter.Snapshot.CompletedWriteBytes);
        Assert.Equal(1, meter.Snapshot.UnconfirmedWrites);
    }

    [Fact]
    public void OverflowDoesNotWrapOrThrowAndDuplicateBindingCannotDoubleCount()
    {
        var clock = new Clock();
        var meter = new PcpDownstreamTrafficMeter(clock);
        var counter = new PcpTransportStatistics(clock).CreateCounter(1);
        counter.ObserveDownstream(meter);
        Assert.Throws<InvalidOperationException>(() => counter.ObserveDownstream(meter));
        counter.AddSent(long.MaxValue);
        counter.AddSent(1);
        Assert.Equal(long.MaxValue, meter.Snapshot.CompletedWriteBytes);
        Assert.True(meter.Snapshot.Overflowed);
        Assert.False(meter.Snapshot.CumulativeComplete);
        clock.Advance(1);
        Assert.Null(meter.Snapshot.KilobitsPerSecond);
    }

    [Fact]
    public async Task LateOldOwnerWritesStayWithItsGenerationAndCannotReleaseFailedCleanupReservations()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(1000)));
        var id = new ChannelId(Guid.NewGuid());
        using var old = policy.OpenChannel(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, () => 500);
        using var lease = old.TryReserve(1).Reservation!;
        Assert.NotNull(lease);
        var stats = new PcpTransportStatistics();
        var previous = stats.CreateCounter(1);
        var connection = previous.ObserveDownstream(old.Traffic);
        previous.AddSent(100);
        using var current = policy.OpenChannel(id, new(Guid.NewGuid()), RelayChannelKind.Viewing, () => 500);
        old.Dispose();
        Assert.False(current.Traffic.Snapshot.HasObservation);
        Assert.False(await PcpDownstreamChildSession.ReleaseAdmissionAfterCleanupAsync(lease,
            Task.FromException(new IOException("Unconfirmed cleanup")), Task.CompletedTask));
        previous.AddSent(50); // A non-cooperative write can finish after the session returns.
        previous.Close();
        Assert.Equal(150, old.Traffic.Snapshot.CompletedWriteBytes);
        Assert.Equal(150, connection.Snapshot.CompletedWriteBytes);
        Assert.Equal(0, current.Traffic.Snapshot.CompletedWriteBytes);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
        var next = stats.CreateCounter(2);
        next.ObserveDownstream(current.Traffic);
        next.AddSent(200);
        Assert.Equal(200, current.Traffic.Snapshot.CompletedWriteBytes);
        Assert.Equal(350, policy.Traffic.Snapshot.CompletedWriteBytes);
        Assert.Equal(1, policy.ReservedCount);
        old.Dispose();
        Assert.True(current.IsCurrent);
        Assert.Equal(350, policy.Traffic.Snapshot.CompletedWriteBytes);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        internal DateTimeOffset Wall = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => Wall;
        internal void Advance(int seconds) => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }

    private sealed class ControlledStream : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Fail;
        internal long SimulatedSent;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Fail) { SimulatedSent += 2; throw new IOException("Controlled partial write"); }
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            SimulatedSent += buffer.Length;
        }
    }

    private sealed class CancelStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<int>(new(true));
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled(cancellationToken) : ValueTask.CompletedTask;
    }
}
