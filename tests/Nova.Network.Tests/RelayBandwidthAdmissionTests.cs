using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.Network.Tests;

public sealed class RelayBandwidthAdmissionTests
{
    [Theory]
    [InlineData(null, null, RelayAdmissionDecision.Admitted)]
    [InlineData(null, 0, RelayAdmissionDecision.Admitted)]
    [InlineData(0, null, RelayAdmissionDecision.BandwidthLimitReached)]
    [InlineData(0, 500, RelayAdmissionDecision.BandwidthLimitReached)]
    [InlineData(500, null, RelayAdmissionDecision.BitrateUnavailable)]
    [InlineData(500, 0, RelayAdmissionDecision.BitrateUnavailable)]
    [InlineData(499, 500, RelayAdmissionDecision.BandwidthLimitReached)]
    [InlineData(500, 500, RelayAdmissionDecision.Admitted)]
    [InlineData(501, 500, RelayAdmissionDecision.Admitted)]
    public void NullZeroUnknownAndExactBoundaryHaveDistinctMeanings(int? budget, int? bitrate,
        RelayAdmissionDecision expected)
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(budget)));
        using var owner = Open(policy, () => bitrate);
        var result = owner.TryReserve(1);
        using var reservation = result.Reservation;
        Assert.Equal(expected, result.Decision);
        Assert.Equal(expected == RelayAdmissionDecision.Admitted ? 1 : 0, policy.ReservedCount);
        Assert.Equal(budget, owner.Snapshot.Ipv4BudgetKbps);
        if (reservation is null) Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
        else Assert.Equal(bitrate is > 0 ? bitrate : null, policy.Snapshot.ReservedIpv4Kbps);
        reservation?.Dispose();
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public void Ipv6BudgetDoesNotBecomeAnIpv4LimitAndUnknownRatesAreNotReportedAsZero()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(null, 0)));
        using var owner = Open(policy);
        using var reservation = Reserve(owner, 1);
        Assert.Null(policy.Snapshot.Ipv4BudgetKbps);
        Assert.Null(policy.Snapshot.ReservedIpv4Kbps);
        Assert.True(owner.CanReserve);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CountRejectionNeverCommitsBandwidthAndBandwidthRejectionNeverCommitsCount(int field)
    {
        var limits = field switch
        {
            0 => new RelayAdmissionLimits(MaxRelaysTotal: 0),
            1 => new RelayAdmissionLimits(MaxRelaysPerBroadcastChannel: 0),
            _ => new RelayAdmissionLimits(MaxRelaysPerViewingChannel: 0),
        };
        using var policy = new RelayAdmissionPolicy(limits, new(new(500)));
        using var owner = Open(policy, () => 500, field == 1 ? RelayChannelKind.Broadcast : RelayChannelKind.Viewing);
        Assert.Null(owner.TryReserve(1).Reservation);
        Assert.Equal(0, policy.Snapshot.ReservedRelays);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
        if (field == 0) return;
        using var other = Open(policy, () => 500, field == 1 ? RelayChannelKind.Viewing : RelayChannelKind.Broadcast);
        using var allowed = Reserve(other, 2);
        Assert.Equal(RelayAdmissionDecision.BandwidthLimitReached, other.TryReserve(3).Decision);
        Assert.Equal(1, policy.Snapshot.ReservedRelays);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Theory]
    [InlineData(1, 1000, 1)]
    [InlineData(10, 999, 1)]
    [InlineData(2, 1000, 2)]
    public async Task ConcurrentChannelsCommitBothResourcesUnderOneGate(int total, int budget, int expected)
    {
        using var policy = new RelayAdmissionPolicy(new(total, 1, 1), new(new(budget)));
        using var broadcast = Open(policy, () => 500, RelayChannelKind.Broadcast);
        using var viewing = Open(policy, () => 500);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(1, 32).Select(id => Task.Run(async () =>
        {
            await start.Task;
            return (id % 2 == 0 ? broadcast : viewing).TryReserve(id);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(expected, results.Count(r => r.Reservation is not null));
        Assert.Equal(expected, policy.Snapshot.ReservedRelays);
        Assert.Equal(expected * 500, policy.Snapshot.ReservedIpv4Kbps);
        foreach (var result in results) result.Reservation?.Dispose();
        Assert.Equal(0, policy.Snapshot.ReservedRelays);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public void RetiredGenerationAndDuplicateCleanupCannotSpendOrReleaseNewOwnersBudget()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(600)));
        var channel = new ChannelId(Guid.NewGuid());
        using var old = policy.OpenChannel(channel, new(Guid.NewGuid()), RelayChannelKind.Viewing, () => 500);
        using var oldLease = Reserve(old, 1);
        using var current = policy.OpenChannel(channel, new(Guid.NewGuid()), RelayChannelKind.Broadcast, () => 200);
        old.Dispose();
        Assert.False(old.CanReserve);
        Assert.Equal(RelayAdmissionDecision.OwnerRetired, old.TryReserve(2).Decision);
        Assert.Equal(RelayAdmissionDecision.BandwidthLimitReached, current.TryReserve(2).Decision);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
        oldLease.Dispose();
        using var next = Reserve(current, 1);
        oldLease.Dispose();
        old.Dispose();
        Assert.Equal(1, current.Snapshot.ChannelReservedRelays);
        Assert.Equal(200, policy.Snapshot.ReservedIpv4Kbps);
        policy.Dispose();
        Assert.Equal(200, policy.Snapshot.ReservedIpv4Kbps);
        next.Dispose();
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public void ChangedOrUnknownDeclarationCannotUnderstateAnExistingReservation()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(1000)));
        int? bitrate = 400;
        using var first = Open(policy, () => bitrate);
        using var second = Open(policy, () => 350);
        using var lease = Reserve(first, 1);
        bitrate = 800;
        Assert.Equal(800, policy.Snapshot.ReservedIpv4Kbps);
        Assert.Equal(RelayAdmissionDecision.BandwidthLimitReached, second.TryReserve(2).Decision);
        bitrate = null;
        Assert.Null(policy.Snapshot.ReservedIpv4Kbps);
        Assert.False(second.CanReserve);
        Assert.Equal(RelayAdmissionDecision.BandwidthAccountingUnavailable, second.TryReserve(2).Decision);
        bitrate = 100;
        Assert.Equal(400, policy.Snapshot.ReservedIpv4Kbps);
        using var next = Reserve(second, 2);
        Assert.Equal(750, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public void CancellationAndDuplicateNotificationsCannotChangeEitherReservation()
    {
        using var policy = new RelayAdmissionPolicy(bandwidth: new(new(1000)));
        using var owner = Open(policy, () => 500);
        Assert.Throws<OperationCanceledException>(() => owner.TryReserve(1, new(true)));
        Assert.Equal(0, policy.Snapshot.ReservedRelays);
        using var lease = Reserve(owner, 1);
        Assert.Equal(RelayAdmissionDecision.DuplicateRelay, owner.TryReserve(1).Decision);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
        Assert.Equal(1, policy.Snapshot.ReservedRelays);
    }

    private static RelayAdmissionPolicy.Channel Open(RelayAdmissionPolicy policy, Func<int?>? bitrate = null,
        RelayChannelKind kind = RelayChannelKind.Viewing) =>
        policy.OpenChannel(new(Guid.NewGuid()), new(Guid.NewGuid()), kind, bitrate);

    private static RelayAdmissionPolicy.Reservation Reserve(RelayAdmissionPolicy.Channel owner, long id)
    {
        var result = owner.TryReserve(id);
        Assert.Equal(RelayAdmissionDecision.Admitted, result.Decision);
        return Assert.IsType<RelayAdmissionPolicy.Reservation>(result.Reservation);
    }
}
