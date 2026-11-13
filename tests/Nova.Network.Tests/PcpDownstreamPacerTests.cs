using System.Diagnostics;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamPacerTests
{
    [Fact]
    public async Task UnlimitedZeroAndSeparateFiniteControlAllowanceAreExplicit()
    {
        var clock = new ManualNatTimeProvider();
        var unlimited = new PcpDownstreamPacer(null, clock);
        await unlimited.AcquireMediaAsync(int.MaxValue, default);
        unlimited.ConsumeControl(int.MaxValue);
        var zero = new PcpDownstreamPacer(0, clock);
        await Assert.ThrowsAsync<PcpPacingLimitException>(() => zero.AcquireMediaAsync(1, default).AsTask());
        zero.ConsumeControl(PcpDownstreamPacer.ControlBurstBytes);
        Assert.Throws<PcpPacingLimitException>(() => zero.ConsumeControl(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        zero.ConsumeControl(1000);
        Assert.Throws<PcpPacingLimitException>(() => zero.ConsumeControl(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public async Task BurstAndLongTermGrantBoundUseMonotonicDecimalRate(int kbps)
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(kbps, clock);
        long granted = 0;
        for (var i = 0; i < 20; i++)
        {
            var grant = pacing.AcquireMediaAsync(pacing.BurstBytes, default).AsTask();
            if (!grant.IsCompleted)
            {
                clock.ShiftWallClock(TimeSpan.FromDays(-100));
                clock.Advance(TimeSpan.FromMilliseconds(Math.Max(1,
                    Math.Ceiling(pacing.BurstBytes / ((double)kbps * 125) * 1000))));
            }
            await grant.WaitAsync(TimeSpan.FromSeconds(5));
            granted += pacing.BurstBytes;
            Assert.True(granted <= pacing.BurstBytes + (double)kbps * 125 * clock.GetTimestamp() / clock.TimestampFrequency);
        }
        Assert.Equal(0, pacing.WaitingRequests);
    }

    [Fact]
    public async Task FifoTurnsAndCancellationCannotStealOrLeaveOldGenerationWaiters()
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(8, clock);
        await pacing.AcquireMediaAsync(pacing.BurstBytes, default);
        using var oldOwner = new CancellationTokenSource();
        var first = pacing.AcquireMediaAsync(pacing.BurstBytes, oldOwner.Token).AsTask();
        var second = pacing.AcquireMediaAsync(pacing.BurstBytes, default).AsTask();
        var third = pacing.AcquireMediaAsync(pacing.BurstBytes, default).AsTask();
        Assert.Equal(3, pacing.WaitingRequests);
        oldOwner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Until(() => clock.ActiveTimerCount == 1);
        clock.Advance(TimeSpan.FromSeconds(0.25));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(third.IsCompleted);
        await Until(() => clock.ActiveTimerCount == 1);
        clock.Advance(TimeSpan.FromSeconds(0.25));
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, pacing.WaitingRequests);
    }

    [Fact]
    public async Task WaitingQueueIsBoundedAndGrantedCreditIsNotRefundedOnUnknownWriteFailure()
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(8, clock);
        await pacing.AcquireMediaAsync(pacing.BurstBytes, default); // Issued credit stays spent even without confirmed I/O.
        using var stop = new CancellationTokenSource();
        var waiters = Enumerable.Range(0, PcpDownstreamPacer.MaxWaitingRequests)
            .Select(_ => pacing.AcquireMediaAsync(pacing.BurstBytes, stop.Token).AsTask()).ToArray();
        Assert.All(waiters, task => Assert.False(task.IsCompleted));
        await Assert.ThrowsAsync<PcpPacingLimitException>(() => pacing.AcquireMediaAsync(1, default).AsTask());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(waiters));
        Assert.Equal(0, pacing.WaitingRequests);
        var replacement = pacing.AcquireMediaAsync(1, stop.Token).AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement);
        Assert.Equal(0, pacing.WaitingRequests);
    }

    internal static async Task Until(Func<bool> predicate)
    {
        var started = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), "Controlled async condition did not settle.");
            await Task.Yield();
        }
    }
}
