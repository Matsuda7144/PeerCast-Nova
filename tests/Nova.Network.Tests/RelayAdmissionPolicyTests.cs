using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.Network.Tests;

public sealed class RelayAdmissionPolicyTests
{
    [Fact]
    public void UnlimitedPolicyDoesNotIntroduceTheFanoutResourceLimit()
    {
        using var policy = new RelayAdmissionPolicy();
        using var channel = Open(policy, RelayChannelKind.Viewing);
        var reservations = Enumerable.Range(1, 64)
            .Select(id => Reserve(channel, id)).ToArray();
        Assert.Equal(64, policy.ReservedCount);
        foreach (var reservation in reservations) reservation.Dispose();
        Assert.Equal(0, policy.ReservedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitZeroRejectsOnlyTheSpecifiedAdmissionLimit(int limit)
    {
        using var policy = new RelayAdmissionPolicy(limit switch
        {
            0 => new(MaxRelaysTotal: 0),
            1 => new(MaxRelaysPerBroadcastChannel: 0),
            _ => new(MaxRelaysPerViewingChannel: 0),
        });
        using var channel = Open(policy, limit == 1 ? RelayChannelKind.Broadcast : RelayChannelKind.Viewing);
        var denied = channel.TryReserve(1);
        Assert.Equal(limit == 0 ? RelayAdmissionDecision.TotalLimitReached :
            RelayAdmissionDecision.ChannelLimitReached, denied.Decision);
        Assert.Null(denied.Reservation);
        Assert.Equal(0, policy.ReservedCount);
        if (limit != 0)
        {
            using var other = Open(policy, limit == 1 ? RelayChannelKind.Viewing : RelayChannelKind.Broadcast);
            using var accepted = Reserve(other, 2);
            Assert.Equal(1, policy.ReservedCount);
        }
    }

    [Fact]
    public void TotalAndChannelLimitsKeepOtherChannelsAndExistingReservationsIntact()
    {
        using var policy = new RelayAdmissionPolicy(new(3, 2, 1));
        using var broadcast = Open(policy, RelayChannelKind.Broadcast);
        using var viewing = Open(policy, RelayChannelKind.Viewing);
        using var other = Open(policy, RelayChannelKind.Viewing);
        using var first = Reserve(broadcast, 1);
        using var second = Reserve(broadcast, 2);
        Assert.Equal(RelayAdmissionDecision.ChannelLimitReached, broadcast.TryReserve(3).Decision);
        using var third = Reserve(viewing, 4);
        Assert.Equal(RelayAdmissionDecision.TotalLimitReached, other.TryReserve(5).Decision);
        Assert.Equal(3, policy.ReservedCount);
        first.Dispose();
        Assert.Equal(RelayAdmissionDecision.ChannelLimitReached, viewing.TryReserve(6).Decision);
        using var replacement = Reserve(other, 7);
        Assert.Equal(1, broadcast.ReservedCount);
        Assert.Equal(1, viewing.ReservedCount);
        Assert.Equal(1, other.ReservedCount);
    }

    [Fact]
    public void DuplicateNotificationDoesNotCreateAnAliasThatCanReleaseTheOriginal()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 2));
        using var channel = Open(policy, RelayChannelKind.Viewing);
        using var other = Open(policy, RelayChannelKind.Broadcast);
        using var original = Reserve(channel, 1);
        foreach (var owner in new[] { channel, other })
        {
            var duplicate = owner.TryReserve(1);
            Assert.Equal(RelayAdmissionDecision.DuplicateRelay, duplicate.Decision);
            Assert.Null(duplicate.Reservation);
        }
        Assert.Equal(1, policy.ReservedCount);
        original.Dispose();
        using var reused = Reserve(other, 1);
        original.Dispose();
        Assert.Equal(1, policy.ReservedCount);
    }

    [Fact]
    public void NewGenerationRetainsOldUsageAndOldCleanupCannotReleaseNewReservations()
    {
        using var policy = new RelayAdmissionPolicy(new(2, 1, 1));
        var channelId = new ChannelId(Guid.NewGuid());
        using var old = policy.OpenChannel(channelId, new(Guid.NewGuid()), RelayChannelKind.Viewing);
        using var oldReservation = Reserve(old, 1);
        using var current = policy.OpenChannel(channelId, new(Guid.NewGuid()), RelayChannelKind.Broadcast);
        Assert.Equal(RelayAdmissionDecision.OwnerRetired, old.TryReserve(2).Decision);
        Assert.Equal(RelayAdmissionDecision.ChannelLimitReached, current.TryReserve(2).Decision);
        old.Dispose();
        Assert.Equal(1, current.ReservedCount);
        oldReservation.Dispose();
        using var currentReservation = Reserve(current, 1);
        oldReservation.Dispose();
        old.Dispose();
        Assert.Equal(1, current.ReservedCount);
        Assert.Equal(1, policy.ReservedCount);
        Assert.Equal(current.SessionId, currentReservation.SessionId);
    }

    [Fact]
    public void DuplicateOwnerGenerationIsRejectedWithoutRetiringCurrentOwner()
    {
        using var policy = new RelayAdmissionPolicy();
        var channelId = new ChannelId(Guid.NewGuid());
        var sessionId = new ChannelSessionId(Guid.NewGuid());
        using var channel = policy.OpenChannel(channelId, sessionId, RelayChannelKind.Viewing);
        Assert.Throws<InvalidOperationException>(() =>
            policy.OpenChannel(channelId, sessionId, RelayChannelKind.Broadcast));
        using var reservation = Reserve(channel, 1);
        Assert.Equal(1, policy.ReservedCount);
    }

    [Fact]
    public void RetiringOwnerOrPolicyDoesNotPretendExistingConnectionHasEnded()
    {
        using var policy = new RelayAdmissionPolicy();
        using var channel = Open(policy, RelayChannelKind.Viewing);
        using var reservation = Reserve(channel, 1);
        channel.Dispose();
        Assert.Equal(1, policy.ReservedCount);
        Assert.Equal(RelayAdmissionDecision.OwnerRetired, channel.TryReserve(2).Decision);
        policy.Dispose();
        Assert.Equal(1, policy.ReservedCount);
        Assert.Throws<ObjectDisposedException>(() => Open(policy, RelayChannelKind.Viewing));
        reservation.Dispose();
        reservation.Dispose();
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public async Task ConcurrentRequestsAtomicallyRespectTotalAndBothChannelLimits()
    {
        using var policy = new RelayAdmissionPolicy(new(2, 1, 1));
        using var broadcast = Open(policy, RelayChannelKind.Broadcast);
        using var viewing = Open(policy, RelayChannelKind.Viewing);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrants = 0;
        var tasks = Enumerable.Range(1, 32).Select(id => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref entrants) == 32) ready.SetResult();
            await start.Task;
            return (id % 2 == 0 ? broadcast : viewing).TryReserve(id);
        })).ToArray();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, results.Count(result => result.Decision == RelayAdmissionDecision.Admitted));
        Assert.Equal(2, policy.ReservedCount);
        Assert.Equal(1, broadcast.ReservedCount);
        Assert.Equal(1, viewing.ReservedCount);
        foreach (var result in results) result.Reservation?.Dispose();
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public async Task TwoChannelsRaceForOneGlobalSlotAndCanReenterAfterRelease()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var first = Open(policy, RelayChannelKind.Viewing);
        using var second = Open(policy, RelayChannelKind.Broadcast);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RelayAdmissionResult> RequestAsync(RelayAdmissionPolicy.Channel channel, long id)
        {
            await start.Task;
            return channel.TryReserve(id);
        }
        var tasks = new[] { RequestAsync(first, 1), RequestAsync(second, 2) };
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, result => result.Decision == RelayAdmissionDecision.Admitted);
        Assert.Single(results, result => result.Decision == RelayAdmissionDecision.TotalLimitReached);
        foreach (var result in results) result.Reservation?.Dispose();
        using var next = Reserve(first, 3);
        Assert.Equal(1, policy.ReservedCount);
    }

    [Fact]
    public async Task FailedEstablishmentReleasesItsReservationBeforeNextAdmission()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var channel = Open(policy, RelayChannelKind.Viewing);
        await Assert.ThrowsAsync<IOException>(() => RunFakeConnectionAsync(channel, 1,
            _ => Task.FromException(new IOException("Establishment failed.")), CancellationToken.None));
        Assert.Equal(0, policy.ReservedCount);
        using var next = Reserve(channel, 2);
    }

    [Fact]
    public async Task CancellationKeepsSlotUntilConnectionCleanupActuallyCompletes()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var channel = Open(policy, RelayChannelKind.Viewing);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = RunFakeConnectionAsync(channel, 1, async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleanup.SetResult(); await releaseCleanup.Task; }
        }, cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();
        await cleanup.Task;
        Assert.Equal(1, policy.ReservedCount);
        Assert.Equal(RelayAdmissionDecision.TotalLimitReached, channel.TryReserve(2).Decision);
        releaseCleanup.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, policy.ReservedCount);
        using var next = Reserve(channel, 3);
    }

    [Fact]
    public async Task CancellationCleanupAndDisposeRaceOnlyReleaseOnce()
    {
        using var policy = new RelayAdmissionPolicy(new(MaxRelaysTotal: 1));
        using var channel = Open(policy, RelayChannelKind.Viewing);
        using var reservation = Reserve(channel, 1);
        using var cancellation = new CancellationTokenSource();
        var cleanup = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token); }
            catch (OperationCanceledException) { reservation.Dispose(); }
        });
        var disposal = Task.Run(reservation.Dispose);
        await cancellation.CancelAsync();
        await Task.WhenAll(cleanup, disposal);
        Assert.Equal(0, policy.ReservedCount);
        using var next = Reserve(channel, 1);
        reservation.Dispose();
        Assert.Equal(1, policy.ReservedCount);
    }

    [Fact]
    public void CanceledAdmissionAndInvalidInputsDoNotMutateUsage()
    {
        using var policy = new RelayAdmissionPolicy();
        using var channel = Open(policy, RelayChannelKind.Viewing);
        Assert.Throws<OperationCanceledException>(() => channel.TryReserve(1, new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.TryReserve(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.TryReserve(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), (RelayChannelKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelayAdmissionPolicy(new(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelayAdmissionPolicy(new(null, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelayAdmissionPolicy(new(null, null, -1)));
        Assert.Throws<ArgumentNullException>(() => policy.UpdateLimits(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.UpdateLimits(new(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.UpdateLimits(new(null, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.UpdateLimits(new(null, null, -1)));
        Assert.Equal(0, policy.ReservedCount);
    }

    [Fact]
    public void UpdateLimits_UpdatesRelayAndBandwidthLimitsWhileKeepingActiveReservations()
    {
        var bandwidth = new UpstreamBandwidthPolicy(new(1000));
        using var policy = new RelayAdmissionPolicy(new(1, 1, 1), bandwidth);
        using var channel = policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), RelayChannelKind.Viewing, () => 400);
        using var first = Reserve(channel, 1);
        Assert.Equal(RelayAdmissionDecision.TotalLimitReached, channel.TryReserve(2).Decision);

        policy.UpdateLimits(new(2, 2, 2), new(900));
        Assert.Equal(1, policy.ReservedCount);
        Assert.Equal(2, policy.Snapshot.RelayLimit);
        Assert.Equal(900, policy.Snapshot.Ipv4BudgetKbps);
        using var second = Reserve(channel, 2);

        policy.UpdateLimits(new(5, 5, 5), new(700));
        Assert.Equal(RelayAdmissionDecision.BandwidthLimitReached, channel.TryReserve(3).Decision);
        Assert.Equal(2, policy.ReservedCount);
    }

    private static RelayAdmissionPolicy.Channel Open(RelayAdmissionPolicy policy, RelayChannelKind kind) =>
        policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), kind);

    private static RelayAdmissionPolicy.Reservation Reserve(RelayAdmissionPolicy.Channel channel, long id)
    {
        var result = channel.TryReserve(id);
        Assert.Equal(RelayAdmissionDecision.Admitted, result.Decision);
        return Assert.IsType<RelayAdmissionPolicy.Reservation>(result.Reservation);
    }

    private static async Task RunFakeConnectionAsync(RelayAdmissionPolicy.Channel channel,
        long id, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var reservation = Reserve(channel, id);
        await operation(cancellationToken);
    }
}
